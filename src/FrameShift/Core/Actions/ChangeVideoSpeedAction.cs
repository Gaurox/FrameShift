using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class ChangeVideoSpeedAction : IFrameShiftAction
{
    private const int FallbackSampleRate = 44100;

    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public ChangeVideoSpeedAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "change-video-speed",
        "Change Video Speed",
        "Changes the playback speed of a video file.");

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

        var sourceExtension = NormalizeExtension(Path.GetExtension(request.InputPath));
        if (!IsSupportedExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(
                sourceExtension, GetSupportedFormatsText()));
        }

        if (!ChangeSpeedSettings.TryFromOptions(request.Options, out var settings, out var settingsError) || settings is null)
        {
            return new ActionExecutionResult(false, settingsError ?? MediaActionMessages.ChangeSpeedSettingsMissing());
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

        if (probe.Duration is null || probe.Duration.Value.TotalSeconds <= 0)
        {
            request.ProgressReporter?.ReportState("failed", MediaActionMessages.DurationUnavailable());
            return new ActionExecutionResult(false, MediaActionMessages.DurationUnavailable());
        }

        var sourceDurationSeconds = probe.Duration.Value.TotalSeconds;
        var hasAudio = probe.HasAudio;
        var sampleRate = probe.PrimaryAudioSampleRate > 0 ? probe.PrimaryAudioSampleRate : FallbackSampleRate;
        var targetDurationSeconds = settings.EstimatedOutputDuration(sourceDurationSeconds);
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, settings.OutputSuffix, sourceExtension);

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.Logger.Log($"SpeedFactor: {settings.SpeedFactor:0.######}, keepPitch={settings.KeepPitch}, hasAudio={hasAudio}, sampleRate={sampleRate}");
        request.ProgressReporter?.ReportState("processing", "Changing video speed...");

        try
        {
            var arguments = BuildArguments(request.InputPath, outputPath, sourceExtension, settings, hasAudio, sampleRate);
            var result = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                TimeSpan.FromSeconds(targetDurationSeconds),
                request.ProgressReporter,
                request.InputPath,
                Descriptor.DisplayName,
                "Video",
                cancellationToken).ConfigureAwait(false);

            if (result.Canceled)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, result.CancellationScope);
            }

            if (result.ExitCode != 0 || !File.Exists(outputPath))
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, "FFmpeg failed during video speed change.");
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
        string extension,
        ChangeSpeedSettings settings,
        bool hasAudio,
        int sampleRate)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-i", inputPath
        };

        var setPtsFactor = (1.0 / settings.SpeedFactor).ToString("0.######", CultureInfo.InvariantCulture);
        var videoFilter = $"setpts={setPtsFactor}*PTS";

        if (hasAudio)
        {
            string audioFilter;
            if (settings.KeepPitch)
            {
                audioFilter = ChangeAudioSpeedAction.BuildAtempoChain(settings.SpeedFactor);
            }
            else
            {
                var targetRate = Math.Max(1000, (int)Math.Round(sampleRate * settings.SpeedFactor));
                audioFilter = $"asetrate={targetRate},aresample={sampleRate}";
            }

            args.Add("-filter_complex");
            args.Add($"[0:v]{videoFilter}[v];[0:a]{audioFilter}[a]");
            args.Add("-map"); args.Add("[v]");
            args.Add("-map"); args.Add("[a]");
        }
        else
        {
            args.Add("-filter:v"); args.Add(videoFilter);
            args.Add("-an");
        }

        var codecArgs = GetVideoCodecArgs(extension, hasAudio);
        args.AddRange(codecArgs);

        args.Add(outputPath);
        return args;
    }

    private static IReadOnlyList<string> GetVideoCodecArgs(string extension, bool hasAudio)
    {
        var args = new List<string>();
        switch (extension)
        {
            case ".webm":
                args.AddRange(["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0"]);
                if (hasAudio) args.AddRange(["-c:a", "libopus", "-b:a", "160k"]);
                break;
            case ".avi":
                args.AddRange(["-c:v", "mpeg4", "-q:v", "3"]);
                if (hasAudio) args.AddRange(["-c:a", "libmp3lame", "-b:a", "192k"]);
                break;
            default:
                args.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p"]);
                if (hasAudio) args.AddRange(["-c:a", "aac", "-b:a", "192k"]);
                break;
        }
        return args;
    }

    private static string NormalizeExtension(string extension)
    {
        return string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.Trim().ToLowerInvariant();
    }

    private static bool IsSupportedExtension(string extension)
    {
        return extension is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".m4v";
    }

    private static string GetSupportedFormatsText()
    {
        return ".mp4, .mkv, .avi, .mov, .webm, .m4v";
    }
}
