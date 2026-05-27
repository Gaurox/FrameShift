using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Windows.Forms;

namespace FrameShift;

internal static partial class Program
{
    private static bool EnsureCutAudioOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Cut Audio"));
            return false;
        }

        var sourceExtension = System.IO.Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!AudioConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, AudioConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        var hasCliCutOptions = CutAudioSettings.ContainsAnyOption(options);
        var cliSettingsValid = CutAudioSettings.TryFromOptions(options, out _, out var settingsError);
        if (hasCliCutOptions && cliSettingsValid)
        {
            return true;
        }

        if (hasCliCutOptions)
        {
            ShowCliError(settingsError ?? MediaActionMessages.CutAudioSettingsInvalid());
            return false;
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

            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            using var picker = new CutAudioForm(
                inputPaths[0],
                ffmpegPath,
                ffprobePath,
                probe.Duration.Value.TotalSeconds,
                new FfmpegRunner(logger),
                ffprobeRunner);

            if (picker.ShowDialog() != DialogResult.OK || picker.Selection is null)
            {
                return false;
            }

            foreach (var item in picker.Selection.ToOptions())
            {
                options[item.Key] = item.Value;
            }

            if (!string.IsNullOrWhiteSpace(picker.WorkingFilePath))
            {
                options[ActionOptionKeys.WorkingFile] = picker.WorkingFilePath!;
            }

            if (!string.IsNullOrWhiteSpace(picker.TemporaryRootPath))
            {
                options[ActionOptionKeys.WorkingRoot] = picker.TemporaryRootPath!;
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Cut Audio")));
            return false;
        }
    }

    private static bool EnsureCutVideoOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Cut Video"));
            return false;
        }

        var sourceExtension = System.IO.Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        var hasCliCutOptions = CutVideoSettings.ContainsAnyOption(options);
        if (hasCliCutOptions)
        {
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

                var fps = probe.VideoFrameRate ?? 0d;
                var totalFrames = probe.EstimatedVideoFrameCount is long count && count > 0
                    ? (int)Math.Min(int.MaxValue, count)
                    : CutVideoMath.EstimateFrameCount(probe.Duration, fps);

                if (!CutVideoSettings.TryFromOptions(options, fps, totalFrames, out _, out var settingsError))
                {
                    ShowCliError(settingsError ?? MediaActionMessages.CutVideoSettingsInvalid());
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Cut Video")));
                return false;
            }
        }

        var toolLocatorUi = new ToolLocator();
        var ffprobeRunnerUi = new FfprobeRunner(logger);
        try
        {
            var ffmpegPath = toolLocatorUi.ResolveFfmpegPath();
            var ffprobePath = toolLocatorUi.ResolveFfprobePath();
            var probeAttempt = ffprobeRunnerUi.TryProbeMediaAsync(ffprobePath, inputPaths[0], CancellationToken.None).GetAwaiter().GetResult();

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
                ShowCliError(MediaActionMessages.VideoFrameRateUnavailable());
                return false;
            }

            var totalFrames = probe.EstimatedVideoFrameCount is long count && count > 0
                ? (int)Math.Min(int.MaxValue, count)
                : CutVideoMath.EstimateFrameCount(probe.Duration, probe.VideoFrameRate.Value);

            if (totalFrames <= 0)
            {
                ShowCliError(MediaActionMessages.VideoFrameCountUnavailable());
                return false;
            }

            using var picker = new CutVideoForm(inputPaths[0], ffmpegPath, probe, new FfmpegRunner(logger));
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
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Cut Video")));
            return false;
        }
    }

    private static bool EnsureCreateGifOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Create GIF"));
            return false;
        }

        var sourceExtension = System.IO.Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
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

            if (probe.Duration is null || probe.Duration.Value.TotalSeconds <= 0)
            {
                ShowCliError(MediaActionMessages.DurationUnavailable());
                return false;
            }

            if (CreateGifSettings.ContainsAnyOption(options))
            {
                if (!CreateGifSettings.TryFromOptions(options, probe.Duration.Value.TotalSeconds, out _, out var settingsError))
                {
                    ShowCliError(settingsError ?? MediaActionMessages.CreateGifSettingsInvalid());
                    return false;
                }

                return true;
            }

            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            logger.Log("Program: EnsureCreateGifOptions before CreateGifForm constructor.");
            using var picker = new CreateGifForm(inputPaths[0], ffmpegPath, probe, new FfmpegRunner(logger));
            logger.Log("Program: EnsureCreateGifOptions before picker.ShowDialog().");
            if (picker.ShowDialog() != DialogResult.OK || picker.Selection is null)
            {
                logger.Log("Program: EnsureCreateGifOptions picker canceled or no selection.");
                return false;
            }

            logger.Log("Program: EnsureCreateGifOptions picker returned OK.");
            foreach (var item in picker.Selection.ToOptions())
            {
                options[item.Key] = item.Value;
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Create GIF")));
            return false;
        }
    }
}
