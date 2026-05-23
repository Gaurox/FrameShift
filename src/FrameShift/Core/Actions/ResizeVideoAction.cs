using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class ResizeVideoAction : IFrameShiftAction
{
    private const int MinDimension = 1;

    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public ResizeVideoAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "resize-video",
        "Resize Video",
        "Resizes a video to a new width and height.");

    public async Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath))
        {
            return new ActionExecutionResult(false, MediaActionMessages.InputPathRequired());
        }

        if (!File.Exists(request.InputPath))
        {
            return new ActionExecutionResult(false, MediaActionMessages.InputFileNotFound(request.InputPath));
        }

        var sourceExtension = Path.GetExtension(request.InputPath).ToLowerInvariant();
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
        }

        if (!ResizeSettings.TryFromOptions(request.Options, out var resize, out var resizeError))
        {
            return new ActionExecutionResult(false, resizeError ?? MediaActionMessages.ResizeSettingsMissing());
        }

        var ffmpegPath = _toolLocator.ResolveFfmpegPath();
        var ffprobePath = _toolLocator.ResolveFfprobePath();
        var probeAttempt = await _ffprobeRunner.TryProbeMediaAsync(ffprobePath, request.InputPath, cancellationToken).ConfigureAwait(false);
        if (probeAttempt.Probe is null)
        {
            var probeError = probeAttempt.ErrorMessage ?? MediaActionMessages.ProbeFailed();
            request.ProgressReporter?.ReportState("failed", probeError);
            return new ActionExecutionResult(false, probeError);
        }

        var probe = probeAttempt.Probe;
        if (!probe.HasVideo)
        {
            request.ProgressReporter?.ReportState("failed", MediaActionMessages.MissingVideoTrack());
            return new ActionExecutionResult(false, MediaActionMessages.MissingVideoTrack());
        }

        if (probe.VideoWidth <= 0 || probe.VideoHeight <= 0)
        {
            return new ActionExecutionResult(false, MediaActionMessages.VideoResolutionUnavailable());
        }

        resize = NormalizeTargetSize(resize.Width, resize.Height);

        var targetId = sourceExtension.TrimStart('.');
        var target = VideoConversionCatalog.GetTargetById(targetId);
        var profile = VideoConversionCatalog.GetProfileById("universal");
        var plan = VideoConversionPlanner.BuildPlan(target, profile, probe, await _ffmpegRunner.IsEncoderAvailableAsync(ffmpegPath, "h264_nvenc", cancellationToken).ConfigureAwait(false));
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, $"_resize_{resize.Width}x{resize.Height}");

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.ProgressReporter?.ReportState("processing", $"Preparing video resize on {plan.ModeLabel}...");

        try
        {
            var arguments = BuildArguments(request.InputPath, outputPath, plan, resize, probe.HasAudio);
            var runResult = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                probe.Duration,
                probe.EstimatedVideoFrameCount,
                request.ProgressReporter,
                request.InputPath,
                Descriptor.DisplayName,
                plan.ModeLabel,
                cancellationToken).ConfigureAwait(false);

            if (runResult.Canceled)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, runResult.CancellationScope);
            }

            if (runResult.ExitCode != 0)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(runResult.StandardError, MediaActionMessages.FfmpegFailure("video"));
                request.ProgressReporter?.ReportState("failed", failureMessage);
                return new ActionExecutionResult(false, failureMessage, null, false);
            }

            request.ProgressReporter?.ReportProgress(1000, request.InputPath, Descriptor.DisplayName, "Completed.");
            request.ProgressReporter?.ReportState("done", "Completed.");
            return new ActionExecutionResult(true, MediaActionMessages.Completed(Descriptor.DisplayName), outputPath);
        }
        catch (OperationCanceledException)
        {
            ConversionActionHelper.DeleteIfExists(outputPath);
            request.ProgressReporter?.ReportState("canceled", "Canceled.");
            return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, CancellationScope.All);
        }
        catch (Exception ex)
        {
            ConversionActionHelper.DeleteIfExists(outputPath);
            var failureMessage = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed(Descriptor.DisplayName));
            request.ProgressReporter?.ReportState("failed", failureMessage);
            return new ActionExecutionResult(false, failureMessage);
        }
    }

    private static IReadOnlyList<string> BuildArguments(
        string inputPath,
        string outputPath,
        VideoConversionPlan plan,
        ResizeSettings resize,
        bool hasAudio)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-i", inputPath,
            "-map", "0:v:0?",
            "-map", "0:a?",
            "-sn",
            "-dn",
            "-vf", $"scale={resize.Width}:{resize.Height}",
            "-c:v", plan.VideoCodec
        };

        args.AddRange(plan.VideoArgs);

        if (hasAudio && !string.IsNullOrWhiteSpace(plan.AudioCodec))
        {
            args.Add("-c:a");
            args.Add(plan.AudioCodec!);
            args.AddRange(plan.AudioArgs);
        }
        else
        {
            args.Add("-an");
        }

        if (plan.GlobalArgs.Count > 0)
        {
            args.AddRange(plan.GlobalArgs);
        }

        args.Add(outputPath);
        return args;
    }

    private static ResizeSettings NormalizeTargetSize(int width, int height)
    {
        if (width < MinDimension)
        {
            width = MinDimension;
        }

        if (height < MinDimension)
        {
            height = MinDimension;
        }

        if (width > 1 && width % 2 != 0)
        {
            width--;
        }

        if (height > 1 && height % 2 != 0)
        {
            height--;
        }

        if (width < MinDimension)
        {
            width = MinDimension;
        }

        if (height < MinDimension)
        {
            height = MinDimension;
        }

        return new ResizeSettings(width, height);
    }

}
