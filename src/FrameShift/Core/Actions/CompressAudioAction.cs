using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class CompressAudioAction : IFrameShiftAction
{
    private const int FallbackSampleRate = 44100;
    private const int FallbackChannels = 2;
    private const int MinTargetKbps = 48;

    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public CompressAudioAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "compress-audio",
        "Compress Audio",
        "Compresses an audio file using a preset profile or a target file size.");

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
        if (!AudioConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(sourceExtension, AudioConversionCatalog.GetSupportedSourceFormatsText()));
        }

        if (!CompressAudioSettings.TryFromOptions(request.Options, out var settings, out var settingsError) || settings is null)
        {
            return new ActionExecutionResult(false, settingsError ?? MediaActionMessages.CompressAudioSettingsMissing());
        }

        if (settings.TargetBytes.HasValue && !CompressAudioSettings.IsTargetSizeSupportedForExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.CompressAudioTargetUnsupportedFormat(sourceExtension));
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

        var sampleRate = probe.PrimaryAudioSampleRate > 0 ? probe.PrimaryAudioSampleRate : FallbackSampleRate;
        var channels = probe.PrimaryAudioChannels > 0 ? probe.PrimaryAudioChannels : FallbackChannels;
        var durationSeconds = probe.Duration.Value.TotalSeconds;

        AudioEncodingPlan plan;
        try
        {
            plan = settings.TargetBytes.HasValue
                ? BuildTargetSizePlan(sourceExtension, settings.TargetBytes.Value, durationSeconds, sampleRate, channels)
                : BuildPresetPlan(sourceExtension, settings.ProfileId, sampleRate, channels);
        }
        catch (InvalidOperationException ex)
        {
            var message = string.IsNullOrWhiteSpace(ex.Message)
                ? MediaActionMessages.Failed(Descriptor.DisplayName)
                : ex.Message;
            request.ProgressReporter?.ReportState("failed", message);
            return new ActionExecutionResult(false, message);
        }

        var outputExtension = sourceExtension == ".wave" ? ".wav" : sourceExtension;
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, settings.OutputSuffix, outputExtension);

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.ProgressReporter?.ReportState("processing", "Preparing audio compression on CPU...");

        try
        {
            var arguments = BuildArguments(request.InputPath, outputPath, plan);
            var result = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                probe.Duration,
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

            if (result.ExitCode != 0)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, MediaActionMessages.FfmpegFailure("audio"));
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

    private static AudioEncodingPlan BuildPresetPlan(string extension, string profileId, int sampleRate, int channels)
    {
        var maxRate = profileId switch
        {
            CompressAudioSettings.ProfileHigh => 48000,
            CompressAudioSettings.ProfileBalanced => 44100,
            _ => 32000
        };
        var targetRate = Math.Min(sampleRate, maxRate);
        if (targetRate < 16000)
        {
            targetRate = sampleRate;
        }

        var targetChannels = profileId == CompressAudioSettings.ProfileSmall && channels > 1 ? 1 : channels;
        if (targetChannels < 1)
        {
            targetChannels = 1;
        }

        var rateArg = targetRate.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var chArg = targetChannels.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return extension switch
        {
            ".mp3" => new AudioEncodingPlan("Audio", "libmp3lame", new[]
            {
                "-b:a", profileId == CompressAudioSettings.ProfileHigh ? "192k" : profileId == CompressAudioSettings.ProfileBalanced ? "128k" : "96k",
                "-ar", rateArg, "-ac", chArg
            }),
            ".m4a" => new AudioEncodingPlan("Audio", "aac", new[]
            {
                "-b:a", profileId == CompressAudioSettings.ProfileHigh ? "160k" : profileId == CompressAudioSettings.ProfileBalanced ? "128k" : "96k",
                "-ar", rateArg, "-ac", chArg
            }),
            ".ogg" => new AudioEncodingPlan("Audio", "libvorbis", new[]
            {
                "-q:a", profileId == CompressAudioSettings.ProfileHigh ? "5" : profileId == CompressAudioSettings.ProfileBalanced ? "4" : "3",
                "-ar", rateArg, "-ac", chArg
            }),
            ".flac" => new AudioEncodingPlan("Audio", "flac", new[]
            {
                "-compression_level", profileId == CompressAudioSettings.ProfileHigh ? "5" : "8",
                "-ar", rateArg, "-ac", chArg
            }),
            ".wav" or ".wave" => new AudioEncodingPlan("Audio",
                profileId == CompressAudioSettings.ProfileHigh ? "adpcm_ms" : "adpcm_ima_wav",
                new[] { "-ar", rateArg, "-ac", chArg }),
            _ => throw new NotSupportedException($"Unsupported audio format '{extension}' for compression.")
        };
    }

    private static AudioEncodingPlan BuildTargetSizePlan(string extension, long targetBytes, double durationSeconds, int sampleRate, int channels)
    {
        var totalKbps = (int)Math.Floor(targetBytes * 8.0 / durationSeconds / 1000.0 * 0.97);
        if (totalKbps < MinTargetKbps)
        {
            throw new InvalidOperationException(MediaActionMessages.CompressAudioTargetTooSmall());
        }

        var targetChannels = totalKbps < 80 && channels > 1 ? 1 : channels;
        if (targetChannels < 1)
        {
            targetChannels = 1;
        }

        var maxRate = totalKbps < 80 ? 32000 : 44100;
        var targetRate = Math.Min(sampleRate, maxRate);
        if (targetRate < 16000)
        {
            targetRate = sampleRate;
        }

        var bitrateArg = $"{totalKbps}k";
        var rateArg = targetRate.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var chArg = targetChannels.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return extension switch
        {
            ".mp3" => new AudioEncodingPlan("Audio", "libmp3lame",
                new[] { "-b:a", bitrateArg, "-ar", rateArg, "-ac", chArg }),
            ".m4a" => new AudioEncodingPlan("Audio", "aac",
                new[] { "-b:a", bitrateArg, "-ar", rateArg, "-ac", chArg }),
            ".ogg" => new AudioEncodingPlan("Audio", "libvorbis",
                new[] { "-b:a", bitrateArg, "-ar", rateArg, "-ac", chArg }),
            _ => throw new InvalidOperationException(MediaActionMessages.CompressAudioTargetUnsupportedFormat(extension))
        };
    }

    private static IReadOnlyList<string> BuildArguments(string inputPath, string outputPath, AudioEncodingPlan plan)
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

    private static string NormalizeExtension(string extension) =>
        string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.Trim().ToLowerInvariant();
}
