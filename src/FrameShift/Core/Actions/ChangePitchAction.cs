using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class ChangePitchAction : IFrameShiftAction
{
    private const int FallbackSampleRate = 44100;

    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public ChangePitchAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "change-pitch",
        "Change Pitch",
        "Changes the pitch of an audio file.");

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

        if (!ChangePitchSettings.TryFromOptions(request.Options, out var settings, out var settingsError) || settings is null)
        {
            return new ActionExecutionResult(false, settingsError ?? MediaActionMessages.ChangePitchSettingsMissing());
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

        var sourceDurationSeconds = probe.Duration.Value.TotalSeconds;
        var sampleRate = probe.PrimaryAudioSampleRate > 0 ? probe.PrimaryAudioSampleRate : FallbackSampleRate;
        var targetDurationSeconds = settings.EstimatedOutputDuration(sourceDurationSeconds);
        var outputExtension = sourceExtension == ".wave" ? ".wav" : sourceExtension;
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, settings.OutputSuffix, outputExtension);

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.Logger.Log($"Pitch: {settings.Semitones:0.##} semitones, factor={settings.PitchFactor:0.######}, keepDuration={settings.KeepDuration}, sampleRate={sampleRate}");
        request.ProgressReporter?.ReportState("processing", "Changing audio pitch...");

        try
        {
            var arguments = BuildArguments(request.InputPath, outputPath, outputExtension, settings, sampleRate);
            var result = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                TimeSpan.FromSeconds(targetDurationSeconds),
                request.ProgressReporter,
                request.InputPath,
                Descriptor.DisplayName,
                "Audio",
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
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, "FFmpeg failed during pitch change.");
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
        ChangePitchSettings settings,
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
            "-i", inputPath,
            "-vn"
        };

        if (settings.KeepDuration)
        {
            args.Add("-af");
            args.Add($"rubberband=pitch={settings.PitchFactor.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}");
        }
        else
        {
            var targetRate = Math.Max(1000, (int)Math.Round(sampleRate * settings.PitchFactor));
            args.Add("-filter:a");
            args.Add($"asetrate={targetRate},aresample={sampleRate}");
        }

        var encodingPlan = GetEncodingPlan(extension);
        args.Add("-c:a");
        args.Add(encodingPlan.Codec);
        args.AddRange(encodingPlan.Args);

        args.Add(outputPath);
        return args;
    }

    private static AudioEncodingPlan GetEncodingPlan(string extension)
    {
        return extension switch
        {
            ".mp3"  => new AudioEncodingPlan("Audio", "libmp3lame", ["-b:a", "320k"]),
            ".wav"  => new AudioEncodingPlan("Audio", "pcm_s16le", []),
            ".flac" => new AudioEncodingPlan("Audio", "flac", ["-compression_level", "5"]),
            ".m4a"  => new AudioEncodingPlan("Audio", "aac", ["-b:a", "256k"]),
            ".ogg"  => new AudioEncodingPlan("Audio", "libvorbis", ["-q:a", "6"]),
            _       => throw new NotSupportedException($"Unsupported audio extension '{extension}'.")
        };
    }

    private static string NormalizeExtension(string extension)
    {
        return string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.Trim().ToLowerInvariant();
    }

    private static bool IsSupportedExtension(string extension)
    {
        return extension is ".wav" or ".wave" or ".mp3" or ".flac" or ".m4a" or ".ogg";
    }

    private static string GetSupportedFormatsText()
    {
        return ".wav, .mp3, .flac, .m4a, .ogg";
    }
}
