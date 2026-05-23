using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class RotateFlipVideoAction : IFrameShiftAction
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public RotateFlipVideoAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "rotate-flip-video",
        "Rotate / Flip Video",
        "Rotates or flips a video.");

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

        if (!RotateFlipSettings.TryFromOptions(request.Options, out var settings, out var settingsError))
        {
            return new ActionExecutionResult(false, settingsError ?? MediaActionMessages.RotateFlipSettingsMissing());
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

        var sourceExtension = Path.GetExtension(request.InputPath).ToLowerInvariant();
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
        }

        var nvencAvailable = await _ffmpegRunner.IsEncoderAvailableAsync(ffmpegPath, "h264_nvenc", cancellationToken).ConfigureAwait(false);
        var targetId = sourceExtension.TrimStart('.');
        var target = VideoConversionCatalog.GetTargetById(targetId);
        var profile = VideoConversionCatalog.GetProfileById("universal");
        var plan = VideoConversionPlanner.BuildPlan(target, profile, probe, nvencAvailable);
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, "_rotated");

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.ProgressReporter?.ReportState("processing", $"Applying transform on {plan.ModeLabel}...");

        try
        {
            var arguments = BuildArguments(request.InputPath, outputPath, plan, settings, probe.HasAudio);
            var result = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                probe.Duration,
                probe.EstimatedVideoFrameCount,
                request.ProgressReporter,
                request.InputPath,
                Descriptor.DisplayName,
                plan.ModeLabel,
                cancellationToken).ConfigureAwait(false);

            if (result.Canceled)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, result.CancellationScope);
            }

            if (result.ExitCode != 0)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, MediaActionMessages.FfmpegFailure("video"));
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
        RotateFlipSettings settings,
        bool hasAudio)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-i", inputPath,
            "-map", "0:v:0?",
            "-map", "0:a?",
            "-sn",
            "-dn"
        };

        var filter = settings.BuildFilterString();
        if (filter is not null)
        {
            args.Add("-vf");
            args.Add(filter);
        }

        args.Add("-c:v");
        args.Add(plan.VideoCodec);
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

        if (plan.SubtitleCodecArgs.Count > 0)
        {
            args.AddRange(plan.SubtitleCodecArgs);
        }

        if (plan.GlobalArgs.Count > 0)
        {
            args.AddRange(plan.GlobalArgs);
        }

        args.Add(outputPath);
        return args;
    }

}
