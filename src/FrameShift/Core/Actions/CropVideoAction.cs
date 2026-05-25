using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class CropVideoAction : IFrameShiftAction
{
    private const int MinSourceCropDimension = 146;
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public CropVideoAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "crop-video",
        "Crop Video",
        "Crops a video using a fixed rectangle selected in the crop window.");

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

        if (!VideoCropSettings.TryFromOptions(request.Options, out var crop, out var cropError))
        {
            return new ActionExecutionResult(false, cropError ?? MediaActionMessages.CropSettingsMissing());
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

        crop = NormalizeCropRect(crop.X, crop.Y, crop.Width, crop.Height, probe.VideoWidth, probe.VideoHeight);

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
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, "_crop");

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.ProgressReporter?.ReportState("processing", $"Preparing video crop on {plan.ModeLabel}...");

        try
        {
            var arguments = BuildArguments(request.InputPath, outputPath, plan, crop, probe.HasAudio);
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
        VideoCropSettings crop,
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
            "-dn",
            "-vf", $"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}",
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

    private static VideoCropSettings NormalizeCropRect(int x, int y, int width, int height, int sourceWidth, int sourceHeight)
    {
        var minWidth = sourceWidth >= MinSourceCropDimension ? MinSourceCropDimension : 1;
        var minHeight = sourceHeight >= MinSourceCropDimension ? MinSourceCropDimension : 1;

        if (x < 0)
        {
            x = 0;
        }

        if (y < 0)
        {
            y = 0;
        }

        if (width < minWidth)
        {
            width = minWidth;
        }

        if (height < minHeight)
        {
            height = minHeight;
        }

        if (x + width > sourceWidth)
        {
            width = sourceWidth - x;
        }

        if (y + height > sourceHeight)
        {
            height = sourceHeight - y;
        }

        if (x > 0 && x % 2 != 0 && width > minWidth)
        {
            x++;
            width--;
        }

        if (y > 0 && y % 2 != 0 && height > minHeight)
        {
            y++;
            height--;
        }

        if (width > 1 && width % 2 != 0)
        {
            if (width > minWidth)
            {
                width--;
            }
            else if (x + width < sourceWidth)
            {
                width++;
            }
        }

        if (height > 1 && height % 2 != 0)
        {
            if (height > minHeight)
            {
                height--;
            }
            else if (y + height < sourceHeight)
            {
                height++;
            }
        }

        if (x > 0 && x % 2 != 0)
        {
            x--;
        }

        if (y > 0 && y % 2 != 0)
        {
            y--;
        }

        if (x + width > sourceWidth)
        {
            width = sourceWidth - x;
        }

        if (y + height > sourceHeight)
        {
            height = sourceHeight - y;
        }

        if (width < minWidth)
        {
            width = minWidth;
        }

        if (height < minHeight)
        {
            height = minHeight;
        }

        return new VideoCropSettings(x, y, width, height);
    }
}
