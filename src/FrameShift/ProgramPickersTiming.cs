using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Windows.AI;
using FrameShift.Windows.Forms;

namespace FrameShift;

internal static partial class Program
{
    private static bool EnsureChangePitchOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Change Pitch"));
            return false;
        }

        var sourceExtension = System.IO.Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (sourceExtension is not (".wav" or ".wave" or ".mp3" or ".flac" or ".m4a" or ".ogg"))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ".wav, .mp3, .flac, .m4a, .ogg"));
            return false;
        }

        if (ChangePitchSettings.ContainsAnyOption(options))
        {
            if (!ChangePitchSettings.TryFromOptions(options, out _, out var settingsError))
            {
                ShowCliError(settingsError ?? MediaActionMessages.ChangePitchSettingsInvalid());
                return false;
            }

            return true;
        }

        var toolLocator = new ToolLocator();
        try
        {
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            using var picker = new ChangePitchForm(inputPaths[0], ffmpegPath, new FfmpegRunner(logger));
            if (picker.ShowDialog() != DialogResult.OK || picker.Selection is null)
            {
                return false;
            }

            foreach (var item in picker.Selection.ToOptions())
            {
                options[item.Key] = item.Value;
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Change Pitch")));
            return false;
        }
    }

    private static bool EnsureChangeAudioSpeedOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Change Audio Speed"));
            return false;
        }

        var sourceExtension = System.IO.Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (sourceExtension is not (".wav" or ".wave" or ".mp3" or ".flac" or ".m4a" or ".ogg"))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ".wav, .mp3, .flac, .m4a, .ogg"));
            return false;
        }

        if (ChangeSpeedSettings.ContainsAnyOption(options))
        {
            if (!ChangeSpeedSettings.TryFromOptions(options, out _, out var settingsError))
            {
                ShowCliError(settingsError ?? MediaActionMessages.ChangeSpeedSettingsInvalid());
                return false;
            }

            return true;
        }

        const int fallbackSampleRate = 44100;
        var toolLocator = new ToolLocator();
        var ffmpegRunner = new FfmpegRunner(logger);
        var ffprobeRunner = new FfprobeRunner(logger);
        try
        {
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            var ffprobePath = toolLocator.ResolveFfprobePath();
            var probeAttempt = ffprobeRunner.TryProbeMediaAsync(ffprobePath, inputPaths[0], CancellationToken.None).GetAwaiter().GetResult();

            if (probeAttempt.Probe is null)
            {
                ShowCliError(probeAttempt.ErrorMessage ?? MediaActionMessages.ProbeFailed());
                return false;
            }

            var probe = probeAttempt.Probe;
            if (!probe.HasAudio)
            {
                ShowCliError(MediaActionMessages.MissingAudioTrack());
                return false;
            }

            if (probe.Duration is null || probe.Duration.Value.TotalSeconds <= 0)
            {
                ShowCliError(MediaActionMessages.DurationUnavailable());
                return false;
            }

            var sourceDurationSeconds = probe.Duration.Value.TotalSeconds;
            var sampleRate = probe.PrimaryAudioSampleRate > 0 ? probe.PrimaryAudioSampleRate : fallbackSampleRate;

            using var picker = new ChangeSpeedForm(inputPaths[0], ffmpegPath, ffmpegRunner, ChangeSpeedMediaKind.Audio, sourceDurationSeconds, false, sampleRate);
            if (picker.ShowDialog() != DialogResult.OK || picker.Selection is null)
            {
                return false;
            }

            foreach (var item in picker.Selection.ToOptions())
            {
                options[item.Key] = item.Value;
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Change Audio Speed")));
            return false;
        }
    }

    private static bool EnsureChangeVideoSpeedOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Change Video Speed"));
            return false;
        }

        var sourceExtension = System.IO.Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (sourceExtension is not (".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".m4v"))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ".mp4, .mkv, .avi, .mov, .webm, .m4v"));
            return false;
        }

        if (ChangeSpeedSettings.ContainsAnyOption(options))
        {
            if (!ChangeSpeedSettings.TryFromOptions(options, out _, out var settingsError))
            {
                ShowCliError(settingsError ?? MediaActionMessages.ChangeSpeedSettingsInvalid());
                return false;
            }

            return true;
        }

        const int fallbackSampleRate = 44100;
        var toolLocator = new ToolLocator();
        var ffmpegRunner = new FfmpegRunner(logger);
        var ffprobeRunner = new FfprobeRunner(logger);
        try
        {
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            var ffprobePath = toolLocator.ResolveFfprobePath();
            var probeAttempt = ffprobeRunner.TryProbeMediaAsync(ffprobePath, inputPaths[0], CancellationToken.None).GetAwaiter().GetResult();

            if (probeAttempt.Probe is null)
            {
                ShowCliError(probeAttempt.ErrorMessage ?? MediaActionMessages.ProbeFailed());
                return false;
            }

            var probe = probeAttempt.Probe;
            if (!probe.HasVideo)
            {
                ShowCliError(MediaActionMessages.MissingVideoTrack());
                return false;
            }

            if (probe.Duration is null || probe.Duration.Value.TotalSeconds <= 0)
            {
                ShowCliError(MediaActionMessages.DurationUnavailable());
                return false;
            }

            var sourceDurationSeconds = probe.Duration.Value.TotalSeconds;
            var hasAudio = probe.HasAudio;
            var sampleRate = probe.PrimaryAudioSampleRate > 0 ? probe.PrimaryAudioSampleRate : fallbackSampleRate;

            using var picker = new ChangeSpeedForm(inputPaths[0], ffmpegPath, ffmpegRunner, ChangeSpeedMediaKind.Video, sourceDurationSeconds, hasAudio, sampleRate);
            if (picker.ShowDialog() != DialogResult.OK || picker.Selection is null)
            {
                return false;
            }

            foreach (var item in picker.Selection.ToOptions())
            {
                options[item.Key] = item.Value;
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Change Video Speed")));
            return false;
        }
    }

    private static bool EnsureInterpolateVideoOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Interpolate Video"));
            return false;
        }

        var sourceExtension = System.IO.Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (sourceExtension is not (".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".m4v"))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ".mp4, .mkv, .avi, .mov, .webm, .m4v"));
            return false;
        }

        if (InterpolateVideoSettings.ContainsAnyOption(options))
        {
            if (!InterpolateVideoSettings.TryFromOptions(options, out _, out var settingsError))
            {
                ShowCliError(settingsError ?? MediaActionMessages.InterpolateVideoSettingsInvalid());
                return false;
            }

            return true;
        }

        var toolLocator = new ToolLocator();
        var ffprobeRunner = new FfprobeRunner(logger);
        try
        {
            var ffprobePath = toolLocator.ResolveFfprobePath();
            var probeAttempt = ffprobeRunner.TryProbeMediaAsync(ffprobePath, inputPaths[0], CancellationToken.None).GetAwaiter().GetResult();

            if (probeAttempt.Probe is null)
            {
                ShowCliError(probeAttempt.ErrorMessage ?? MediaActionMessages.ProbeFailed());
                return false;
            }

            var probe = probeAttempt.Probe;
            if (!probe.HasVideo)
            {
                ShowCliError(MediaActionMessages.MissingVideoTrack());
                return false;
            }

            if (probe.VideoFrameRate is null || probe.VideoFrameRate <= 0)
            {
                ShowCliError(MediaActionMessages.InterpolateVideoFrameRateUnavailable());
                return false;
            }

            using var picker = new InterpolateVideoForm(inputPaths[0], probe.VideoFrameRate.Value);
            if (picker.ShowDialog() != DialogResult.OK || picker.Selection is null)
            {
                return false;
            }

            foreach (var item in picker.Selection.ToOptions())
            {
                options[item.Key] = item.Value;
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Interpolate Video")));
            return false;
        }
    }

    private static bool EnsureRifeInterpolateVideoOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Interpolate Video (RIFE)"));
            return false;
        }

        var sourceExtension = System.IO.Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (sourceExtension is not (".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".m4v"))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ".mp4, .mkv, .avi, .mov, .webm, .m4v"));
            return false;
        }

        if (RifeInterpolateVideoSettings.ContainsAnyOption(options))
        {
            if (!RifeInterpolateVideoSettings.TryFromOptions(options, out _, out var settingsError))
            {
                ShowCliError(settingsError ?? MediaActionMessages.RifeInterpolateVideoSettingsInvalid());
                return false;
            }

            return true;
        }

        var toolLocator = new ToolLocator();
        var ffprobeRunner = new FfprobeRunner(logger);
        try
        {
            var ffprobePath = toolLocator.ResolveFfprobePath();
            var probeAttempt = ffprobeRunner.TryProbeMediaAsync(ffprobePath, inputPaths[0], CancellationToken.None).GetAwaiter().GetResult();

            if (probeAttempt.Probe is null)
            {
                ShowCliError(probeAttempt.ErrorMessage ?? MediaActionMessages.ProbeFailed());
                return false;
            }

            var probe = probeAttempt.Probe;
            if (!probe.HasVideo)
            {
                ShowCliError(MediaActionMessages.MissingVideoTrack());
                return false;
            }

            if (probe.VideoFrameRate is null || probe.VideoFrameRate <= 0)
            {
                ShowCliError(MediaActionMessages.InterpolateVideoFrameRateUnavailable());
                return false;
            }

            using var picker = new RifeInterpolateVideoPickerForm(inputPaths[0], probe.VideoFrameRate.Value);
            if (picker.ShowDialog() != DialogResult.OK || picker.Selection is null)
            {
                return false;
            }

            foreach (var item in picker.Selection.ToOptions())
            {
                options[item.Key] = item.Value;
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Interpolate Video (RIFE)")));
            return false;
        }
    }
}
