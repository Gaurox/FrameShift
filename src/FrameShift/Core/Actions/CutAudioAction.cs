using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class CutAudioAction : IFrameShiftAction
{
    private const double DurationToleranceSeconds = 0.01d;
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public CutAudioAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "cut-audio",
        "Cut Audio",
        "Cuts an audio file between a start time and an end time.");

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

        var sourceExtension = NormalizeSourceExtension(Path.GetExtension(request.InputPath));
        if (!AudioConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(sourceExtension, AudioConversionCatalog.GetSupportedSourceFormatsText()));
        }

        var processingInputPath = ConversionActionHelper.GetOption(request, ActionOptionKeys.WorkingFile, request.InputPath);
        var workingRoot = ConversionActionHelper.GetOption(request, ActionOptionKeys.WorkingRoot, string.Empty);
        if (!File.Exists(processingInputPath))
        {
            return new ActionExecutionResult(false, MediaActionMessages.InputFileNotFound(processingInputPath));
        }

        if (!CutAudioSettings.TryFromOptions(request.Options, out var settings, out var settingsError) || settings is null)
        {
            return new ActionExecutionResult(false, settingsError ?? MediaActionMessages.CutAudioSettingsMissing());
        }

        var ffmpegPath = _toolLocator.ResolveFfmpegPath();
        var ffprobePath = _toolLocator.ResolveFfprobePath();
        var probeAttempt = await _ffprobeRunner.TryProbeMediaAsync(ffprobePath, processingInputPath, cancellationToken).ConfigureAwait(false);
        if (probeAttempt.Probe is null)
        {
            var probeError = probeAttempt.ErrorMessage ?? MediaActionMessages.ProbeFailed();
            request.ProgressReporter?.ReportState("failed", probeError);
            return new ActionExecutionResult(false, probeError);
        }

        var probe = probeAttempt.Probe;
        if (!probe.HasAudio)
        {
            request.ProgressReporter?.ReportState("failed", MediaActionMessages.MissingAudioTrack());
            return new ActionExecutionResult(false, MediaActionMessages.MissingAudioTrack());
        }

        if (probe.Duration is null || probe.Duration.Value.TotalSeconds <= 0)
        {
            request.ProgressReporter?.ReportState("failed", MediaActionMessages.DurationUnavailable());
            return new ActionExecutionResult(false, MediaActionMessages.DurationUnavailable());
        }

        var sourceDurationSeconds = probe.Duration.Value.TotalSeconds;
        if (settings.StartSeconds < 0)
        {
            return new ActionExecutionResult(false, MediaActionMessages.CutAudioStartInvalid());
        }

        var effectiveEndSeconds = settings.EndSeconds;
        if (effectiveEndSeconds > sourceDurationSeconds &&
            effectiveEndSeconds <= sourceDurationSeconds + DurationToleranceSeconds)
        {
            effectiveEndSeconds = sourceDurationSeconds;
        }

        if (effectiveEndSeconds > sourceDurationSeconds)
        {
            return new ActionExecutionResult(false, MediaActionMessages.CutAudioEndInvalid());
        }

        if (effectiveEndSeconds <= settings.StartSeconds)
        {
            return new ActionExecutionResult(false, MediaActionMessages.CutAudioEndInvalid());
        }

        settings = settings with { EndSeconds = effectiveEndSeconds };

        var outputExtension = sourceExtension == ".wave" ? ".wav" : sourceExtension;
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, "_cut", outputExtension);
        var plan = GetEncodingPlan(outputExtension);

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.Logger.Log($"Cut audio strategy: re-encode to {outputExtension} for sample-accurate trimming.");
        request.ProgressReporter?.ReportState("processing", "Preparing audio cut on CPU...");

        try
        {
            var arguments = BuildArguments(processingInputPath, outputPath, settings, plan);
            var result = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                TimeSpan.FromSeconds(settings.DurationSeconds),
                request.ProgressReporter,
                request.InputPath,
                Descriptor.DisplayName,
                "CPU",
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
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, "FFmpeg failed during audio cut.");
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
        finally
        {
            CleanupWorkingArtifacts(processingInputPath, request.InputPath, workingRoot, request.Logger);
        }
    }

    private static IReadOnlyList<string> BuildArguments(
        string inputPath,
        string outputPath,
        CutAudioSettings settings,
        AudioEncodingPlan plan)
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
            "-ss", settings.StartSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            "-t", settings.DurationSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            "-vn",
            "-sn",
            "-dn",
            "-map", "0:a:0?",
            "-c:a", plan.Codec
        };

        args.AddRange(plan.Args);
        args.Add(outputPath);
        return args;
    }

    private static AudioEncodingPlan GetEncodingPlan(string sourceExtension)
    {
        return sourceExtension switch
        {
            ".mp3" => new AudioEncodingPlan("Audio", "libmp3lame", ["-b:a", "320k"]),
            ".wav" => new AudioEncodingPlan("Audio", "pcm_s16le", []),
            ".flac" => new AudioEncodingPlan("Audio", "flac", ["-compression_level", "5"]),
            ".m4a" => new AudioEncodingPlan("Audio", "aac", ["-b:a", "256k"]),
            ".ogg" => new AudioEncodingPlan("Audio", "libvorbis", ["-q:a", "6"]),
            _ => throw new NotSupportedException($"Unsupported audio source '{sourceExtension}'.")
        };
    }

    private static string NormalizeSourceExtension(string extension)
    {
        return string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.Trim().ToLowerInvariant();
    }

    private static void CleanupWorkingArtifacts(string processingInputPath, string originalInputPath, string workingRoot, Logging.AppLogger logger)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(workingRoot) && Directory.Exists(workingRoot))
            {
                Directory.Delete(workingRoot, recursive: true);
                logger.Log($"Cut audio cleanup removed temporary root: {workingRoot}");
                return;
            }

            if (!string.Equals(processingInputPath, originalInputPath, StringComparison.OrdinalIgnoreCase))
            {
                ConversionActionHelper.DeleteIfExists(processingInputPath);
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Cut audio cleanup could not remove temporary artifacts: {ex}");
        }
    }
}
