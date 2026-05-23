using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Progress;

namespace FrameShift.Core.Actions;

public sealed class CutVideoAction : IFrameShiftAction
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public CutVideoAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "cut-video",
        "Cut Video",
        "Cuts a video between two frame boundaries.");

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

        var fps = probe.VideoFrameRate;
        if (fps is null || fps <= 0)
        {
            return new ActionExecutionResult(false, MediaActionMessages.VideoFrameRateUnavailable());
        }

        var totalFrames = probe.EstimatedVideoFrameCount is long frameCount && frameCount > 0
            ? (int)Math.Min(int.MaxValue, frameCount)
            : CutVideoMath.EstimateFrameCount(probe.Duration, fps.Value);

        if (totalFrames <= 0)
        {
            return new ActionExecutionResult(false, MediaActionMessages.VideoFrameCountUnavailable());
        }

        if (!CutVideoSettings.TryFromOptions(request.Options, fps.Value, totalFrames, out var settings, out var settingsError) || settings is null)
        {
            return new ActionExecutionResult(false, settingsError ?? MediaActionMessages.CutVideoSettingsMissing());
        }

        var outputPath = OutputPathHelper.CreateUniqueOutputPath(
            request.InputPath,
            $"__frame_{settings.StartFrame}_to_{settings.EndFrame}",
            sourceExtension);

        var encodingPlan = await GetEncodingPlanAsync(sourceExtension, cancellationToken).ConfigureAwait(false);
        var selectedDurationSeconds = settings.DurationSeconds;
        if (selectedDurationSeconds <= 0)
        {
            return new ActionExecutionResult(false, MediaActionMessages.CutVideoEndInvalid());
        }

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.Logger.Log($"Cut video strategy: re-encode to {sourceExtension} for frame-accurate trimming.");
        request.ProgressReporter?.ReportState("processing", $"Preparing cut on {encodingPlan.Primary.ModeLabel}...");

        try
        {
            var primaryResult = await RunEncodingAttemptAsync(
                ffmpegPath,
                request.InputPath,
                outputPath,
                settings,
                encodingPlan.Primary,
                probe.HasAudio,
                selectedDurationSeconds,
                request.ProgressReporter,
                cancellationToken).ConfigureAwait(false);

            if (primaryResult.Canceled)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, primaryResult.CancellationScope);
            }

            if (primaryResult.ExitCode == 0 && File.Exists(outputPath))
            {
                request.ProgressReporter?.ReportProgress(1000, request.InputPath, Descriptor.DisplayName, "Completed.");
                request.ProgressReporter?.ReportState("done", "Completed.");
                return new ActionExecutionResult(true, MediaActionMessages.Completed(Descriptor.DisplayName), outputPath);
            }

            if (encodingPlan.Fallback is not null)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.ProgressReporter?.ReportState("processing", $"Retrying cut on {encodingPlan.Fallback.ModeLabel}...");

                var fallbackResult = await RunEncodingAttemptAsync(
                    ffmpegPath,
                    request.InputPath,
                    outputPath,
                    settings,
                    encodingPlan.Fallback,
                    probe.HasAudio,
                    selectedDurationSeconds,
                    request.ProgressReporter,
                    cancellationToken).ConfigureAwait(false);

                if (fallbackResult.Canceled)
                {
                    ConversionActionHelper.DeleteIfExists(outputPath);
                    request.ProgressReporter?.ReportState("canceled", "Canceled.");
                    return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, fallbackResult.CancellationScope);
                }

                if (fallbackResult.ExitCode == 0 && File.Exists(outputPath))
                {
                    request.ProgressReporter?.ReportProgress(1000, request.InputPath, Descriptor.DisplayName, "Completed.");
                    request.ProgressReporter?.ReportState("done", "Completed.");
                    return new ActionExecutionResult(true, MediaActionMessages.Completed(Descriptor.DisplayName), outputPath);
                }

                ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(
                    fallbackResult.StandardError,
                    "FFmpeg failed during Cut Video processing.");
                request.ProgressReporter?.ReportState("failed", failureMessage);
                return new ActionExecutionResult(false, failureMessage, null, false);
            }

            ConversionActionHelper.DeleteIfExists(outputPath);
            var primaryFailureMessage = ConversionActionHelper.GetFriendlyFfmpegError(
                primaryResult.StandardError,
                "FFmpeg failed during Cut Video processing.");
            request.ProgressReporter?.ReportState("failed", primaryFailureMessage);
            return new ActionExecutionResult(false, primaryFailureMessage, null, false);
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

    private async Task<FfmpegRunResult> RunEncodingAttemptAsync(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        CutVideoSettings settings,
        EncodingPlan plan,
        bool hasAudio,
        double selectedDurationSeconds,
        IProgressReporter? progressReporter,
        CancellationToken cancellationToken)
    {
        var arguments = BuildArguments(inputPath, outputPath, settings, plan, hasAudio);
        return await _ffmpegRunner.RunAsync(
            ffmpegPath,
            arguments,
            TimeSpan.FromSeconds(selectedDurationSeconds),
            EstimateOutputFrameCount(settings),
            progressReporter,
            inputPath,
            Descriptor.DisplayName,
            plan.ModeLabel,
            cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> BuildArguments(
        string inputPath,
        string outputPath,
        CutVideoSettings settings,
        EncodingPlan plan,
        bool hasAudio)
    {
        var videoFilter = $"select='between(n,{settings.StartFrameZeroBased},{settings.EndFrame - 1})',setpts=PTS-STARTPTS";
        var audioFilter = $"atrim=start={settings.StartTimeSeconds.ToString("0.###", CultureInfo.InvariantCulture)}:end={settings.EndTimeSeconds.ToString("0.###", CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS";

        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-i", inputPath,
            "-vf", videoFilter
        };

        if (hasAudio)
        {
            args.Add("-af");
            args.Add(audioFilter);
        }

        args.Add("-c:v");
        args.Add(plan.VideoCodec);
        args.AddRange(plan.VideoArgs);

        if (hasAudio)
        {
            args.Add("-c:a");
            args.Add(plan.AudioCodec);
            args.AddRange(plan.AudioArgs);
        }
        else
        {
            args.Add("-an");
        }

        args.Add(outputPath);
        return args;
    }

    private async Task<EncodingPlanBundle> GetEncodingPlanAsync(string extension, CancellationToken cancellationToken)
    {
        return extension.ToLowerInvariant() switch
        {
            ".mp4" => await GetNvencPlanAsync(
                cancellationToken,
                new EncodingPlan("CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart"], "aac", ["-b:a", "320k"]),
                new EncodingPlan("NVIDIA GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p", "-movflags", "+faststart"], "aac", ["-b:a", "320k"])).ConfigureAwait(false),
            ".mkv" => await GetNvencPlanAsync(
                cancellationToken,
                new EncodingPlan("CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p"], "aac", ["-b:a", "320k"]),
                new EncodingPlan("NVIDIA GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p"], "aac", ["-b:a", "320k"])).ConfigureAwait(false),
            ".avi" => new EncodingPlanBundle(
                new EncodingPlan("CPU", "mpeg4", ["-q:v", "2"], "libmp3lame", ["-b:a", "320k"]),
                null),
            ".mov" => await GetNvencPlanAsync(
                cancellationToken,
                new EncodingPlan("CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart"], "aac", ["-b:a", "320k"]),
                new EncodingPlan("NVIDIA GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p", "-movflags", "+faststart"], "aac", ["-b:a", "320k"])).ConfigureAwait(false),
            ".webm" => new EncodingPlanBundle(
                new EncodingPlan("CPU", "libvpx-vp9", ["-crf", "31", "-b:v", "0", "-deadline", "good", "-cpu-used", "2", "-row-mt", "1"], "libopus", ["-b:a", "192k"]),
                null),
            ".m4v" => await GetNvencPlanAsync(
                cancellationToken,
                new EncodingPlan("CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart"], "aac", ["-b:a", "320k"]),
                new EncodingPlan("NVIDIA GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p", "-movflags", "+faststart"], "aac", ["-b:a", "320k"])).ConfigureAwait(false),
            _ => throw new NotSupportedException("Unsupported input format. Only .mp4, .mkv, .avi, .mov, .webm and .m4v are supported.")
        };
    }

    private async Task<EncodingPlanBundle> GetNvencPlanAsync(CancellationToken cancellationToken, EncodingPlan cpuPlan, EncodingPlan gpuPlan)
    {
        var ffmpegPath = _toolLocator.ResolveFfmpegPath();
        var nvencAvailable = await _ffmpegRunner.IsEncoderAvailableAsync(ffmpegPath, "h264_nvenc", cancellationToken).ConfigureAwait(false);
        return nvencAvailable
            ? new EncodingPlanBundle(gpuPlan, cpuPlan)
            : new EncodingPlanBundle(cpuPlan, null);
    }

    private static long? EstimateOutputFrameCount(CutVideoSettings settings)
    {
        var frames = (long)Math.Max(1, settings.EndFrame - settings.StartFrame + 1);
        return frames;
    }

    private sealed record EncodingPlan(
        string ModeLabel,
        string VideoCodec,
        IReadOnlyList<string> VideoArgs,
        string AudioCodec,
        IReadOnlyList<string> AudioArgs);

    private sealed record EncodingPlanBundle(EncodingPlan Primary, EncodingPlan? Fallback);
}
