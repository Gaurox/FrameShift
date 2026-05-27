using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Windows.AI;
using FrameShift.Windows.Helpers;
using RemoveBackground = FrameShift.Core.AI.RemoveBackground;
using RemoveNoise = FrameShift.Core.AI.RemoveNoise;
using SeparateAudio = FrameShift.Core.AI.SeparateAudio;
using VideoInterpolation = FrameShift.Core.AI.VideoInterpolation;

namespace FrameShift;

internal static partial class Program
{
    private static bool EnsureRemoveBackgroundModelReady(AppLogger logger)
    {
        if (RemoveBackground.ModelLocator.ModelExists())
        {
            logger.Log("Program: Remove Background model already present.");
            return true;
        }

        logger.Log("Program: Remove Background model missing. Opening DownloadModelForm.");
        using var downloadForm = new DownloadModelForm(
            "FrameShift AI - Remove Background",
            "Download the AI model to enable background removal",
            IconPaths.RemoveBackgroundAiIcon,
            RemoveBackground.ModelDownloader.ModelDisplayName,
            RemoveBackground.ModelDownloader.ModelLicense,
            RemoveBackground.ModelDownloader.ExpectedSizeBytes,
            async (progress, ct) =>
            {
                RemoveBackground.ModelLocator.EnsureDirectoryExists();
                await RemoveBackground.ModelDownloader.DownloadAsync(
                    RemoveBackground.ModelLocator.ModelPath,
                    new Progress<RemoveBackground.ModelDownloadProgress>(p =>
                        progress.Report(new FrameShift.Core.AI.AiModelDownloadProgress(p.Percent, p.Status))),
                    ct).ConfigureAwait(false);
            });
        var dialogResult = downloadForm.ShowDialog();
        var modelReady = dialogResult == DialogResult.OK && RemoveBackground.ModelLocator.ModelExists();
        logger.Log($"Program: Remove Background preflight completed. dialogResult={dialogResult}, modelReady={modelReady}.");
        return modelReady;
    }

    private static bool EnsureSeparateAudioModelReady(
        AppLogger logger,
        IReadOnlyDictionary<string, string>? options = null)
    {
        var preferGpu = ShouldPreferGpuForSeparateAudio(options);
        var modelExists = preferGpu
            ? SeparateAudio.ModelLocator.GpuModelExists()
            : SeparateAudio.ModelLocator.CpuModelExists();

        if (modelExists)
        {
            logger.Log($"Program: Separate Audio model already present. preferGpu={preferGpu}.");
            return true;
        }

        var displayName = preferGpu
            ? SeparateAudio.ModelDownloader.GpuModelDisplayName
            : SeparateAudio.ModelDownloader.CpuModelDisplayName;
        var sizeBytes = preferGpu
            ? SeparateAudio.ModelDownloader.GpuExpectedSizeBytes
            : SeparateAudio.ModelDownloader.CpuExpectedSizeBytes;
        var destPath = preferGpu
            ? SeparateAudio.ModelLocator.GpuModelPath
            : SeparateAudio.ModelLocator.CpuModelPath;

        logger.Log($"Program: Separate Audio model missing. Opening DownloadModelForm. preferGpu={preferGpu}.");
        using var downloadForm = new DownloadModelForm(
            "FrameShift AI - Audio Separation",
            "Download the AI model to enable audio stem separation",
            IconPaths.SeparateAudioAiIcon,
            displayName,
            SeparateAudio.ModelDownloader.ModelLicense,
            sizeBytes,
            async (progress, ct) =>
            {
                SeparateAudio.ModelLocator.EnsureDirectoryExists();
                await SeparateAudio.ModelDownloader.DownloadAsync(
                    preferGpu,
                    destPath,
                    new Progress<SeparateAudio.ModelDownloadProgress>(p =>
                        progress.Report(new FrameShift.Core.AI.AiModelDownloadProgress(p.Percent, p.Status))),
                    ct).ConfigureAwait(false);
            });
        var dialogResult = downloadForm.ShowDialog();
        var ready = dialogResult == DialogResult.OK &&
            (preferGpu ? SeparateAudio.ModelLocator.GpuModelExists() : SeparateAudio.ModelLocator.CpuModelExists());
        logger.Log($"Program: Separate Audio preflight completed. dialogResult={dialogResult}, ready={ready}.");
        return ready;
    }

    private static bool EnsureRemoveNoiseModelReady(AppLogger logger)
    {
        if (RemoveNoise.DeepFilterNetModelLocator.AllModelsExist())
        {
            logger.Log("Program: Remove Noise models already present.");
            return true;
        }

        logger.Log("Program: Remove Noise models missing. Opening DownloadModelForm.");
        using var downloadForm = new DownloadModelForm(
            "FrameShift AI - Remove Noise",
            "Download the AI model to enable noise removal",
            IconPaths.RemoveNoiseAiIcon,
            RemoveNoise.DeepFilterNetModelDownloader.ModelDisplayName,
            RemoveNoise.DeepFilterNetModelDownloader.ModelLicense,
            RemoveNoise.DeepFilterNetModelDownloader.TotalSizeBytes,
            async (progress, ct) =>
            {
                RemoveNoise.DeepFilterNetModelLocator.EnsureDirectoryExists();
                await RemoveNoise.DeepFilterNetModelDownloader.DownloadAllAsync(
                    Path.GetDirectoryName(RemoveNoise.DeepFilterNetModelLocator.EncPath)!,
                    progress,
                    ct).ConfigureAwait(false);
            });

        var dialogResult = downloadForm.ShowDialog();
        var ready = dialogResult == DialogResult.OK && RemoveNoise.DeepFilterNetModelLocator.AllModelsExist();
        logger.Log($"Program: Remove Noise preflight completed. dialogResult={dialogResult}, ready={ready}.");
        return ready;
    }

    private static bool EnsureRifeInterpolateModelReady(
        AppLogger logger,
        IReadOnlyDictionary<string, string>? options)
    {
        if (!RifeInterpolateVideoSettings.TryFromOptions(options, out var settings, out var errorMessage) || settings is null)
        {
            logger.Log($"Program: RIFE Interpolate Video settings invalid during preflight. error={errorMessage ?? "<none>"}.");
            return false;
        }

        var model = VideoInterpolation.RifeModelCatalog.GetById(settings.ModelId);
        if (model is null)
        {
            logger.Log($"Program: RIFE Interpolate Video model not found in catalog. modelId={settings.ModelId}.");
            return false;
        }

        var modelPath = VideoInterpolation.RifeModelLocator.GetModelPath(model);
        if (File.Exists(modelPath) &&
            VideoInterpolation.RifeModelDownloader.IsModelFileValid(modelPath, model, logger))
        {
            logger.Log($"Program: RIFE model already present and verified. modelId={model.Id}.");
            return true;
        }

        if (File.Exists(modelPath))
        {
            logger.Log($"Program: RIFE model failed verification. Removing invalid file. modelId={model.Id}.");
            try
            {
                File.Delete(modelPath);
            }
            catch (Exception ex)
            {
                logger.Log($"Program: failed to delete invalid RIFE model. {ex}");
            }
        }

        logger.Log($"Program: RIFE model missing. Opening DownloadModelForm. modelId={model.Id}.");
        using var downloadForm = new DownloadModelForm(
            "FrameShift AI - Interpolate Video",
            "Download the AI model to enable RIFE video interpolation",
            IconPaths.InterpolateAiIcon,
            model.DisplayName,
            model.License,
            model.ExpectedSizeBytes,
            async (progress, ct) =>
            {
                VideoInterpolation.RifeModelLocator.EnsureDirectoryExists();
                await VideoInterpolation.RifeModelDownloader.DownloadAsync(
                    model,
                    modelPath,
                    progress,
                    ct).ConfigureAwait(false);
            });

        var dialogResult = downloadForm.ShowDialog();
        var ready = dialogResult == DialogResult.OK &&
            File.Exists(modelPath) &&
            VideoInterpolation.RifeModelDownloader.IsModelFileValid(modelPath, model, logger);
        logger.Log($"Program: RIFE Interpolate Video preflight completed. dialogResult={dialogResult}, ready={ready}, modelId={model.Id}.");
        return ready;
    }

    private static bool ValidateRemoveNoiseInputs(
        IReadOnlyList<string> inputPaths)
    {
        if (inputPaths.Count == 0)
        {
            ShowCliError(MediaActionMessages.InputPathRequired());
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!RemoveNoise.RemoveNoiseAction.IsAudioExtensionSupported(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(
                sourceExtension,
                RemoveNoise.RemoveNoiseAction.GetSupportedExtensionsText()));
            return false;
        }

        return true;
    }

    private static bool ValidateRemoveNoiseVideoInputs(IReadOnlyList<string> inputPaths)
    {
        if (inputPaths.Count == 0)
        {
            ShowCliError(MediaActionMessages.InputPathRequired());
            return false;
        }

        var ext = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!RemoveNoise.RemoveNoiseVideoSettings.IsSupportedVideoExtension(ext))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(
                ext,
                RemoveNoise.RemoveNoiseVideoSettings.GetSupportedVideoExtensionsText()));
            return false;
        }

        return true;
    }

    private static bool EnsureRemoveNoiseVideoOptions(
        IReadOnlyList<string> inputPaths,
        Dictionary<string, string> options,
        AppLogger logger)
    {
        if (options.ContainsKey(ActionOptionKeys.NoiseStrength))
        {
            return true;
        }

        if (inputPaths.Count == 0)
        {
            ShowCliError(MediaActionMessages.InputPathRequired());
            return false;
        }

        var sourceIsStereo = false;
        string? ffmpegPath = null;
        try
        {
            var toolLocator = new ToolLocator();
            var ffprobeRunner = new FfprobeRunner(logger);
            var ffprobePath = toolLocator.ResolveFfprobePath();
            ffmpegPath = toolLocator.ResolveFfmpegPath();
            var probeAttempt = ffprobeRunner
                .TryProbeMediaAsync(ffprobePath, inputPaths[0], CancellationToken.None)
                .GetAwaiter().GetResult();

            if (probeAttempt.Probe is not null)
            {
                sourceIsStereo = probeAttempt.Probe.PrimaryAudioChannels >= 2;
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Program: EnsureRemoveNoiseVideoOptions probe failed (non-fatal): {ex.Message}");
        }

        var previewSourcePath = inputPaths.Count == 1 ? inputPaths[0] : null;

        try
        {
            using var picker = new RemoveNoiseVideoPickerForm(
                RemoveNoiseVideoPickerForm.BuildSourceLabel(inputPaths),
                sourceIsStereo,
                previewSourcePath,
                ffmpegPath);
            if (picker.ShowDialog() != DialogResult.OK || picker.SelectedStrength is null)
            {
                return false;
            }

            options[ActionOptionKeys.NoiseStrength] = picker.SelectedStrength;
            options[ActionOptionKeys.ProcessStereoAudio] = picker.ProcessStereo ? "true" : "false";
            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Remove Noise (Video)")));
            return false;
        }
    }

    private static bool EnsureRemoveNoiseAudioOptions(
        IReadOnlyList<string> inputPaths,
        Dictionary<string, string> options,
        AppLogger logger)
    {
        if (options.ContainsKey(ActionOptionKeys.NoiseStrength))
        {
            return true;
        }

        if (inputPaths.Count == 0)
        {
            ShowCliError(MediaActionMessages.InputPathRequired());
            return false;
        }

        var sourceIsStereo = false;
        string? ffmpegPath = null;
        try
        {
            var toolLocator = new ToolLocator();
            var ffprobeRunner = new FfprobeRunner(logger);
            var ffprobePath = toolLocator.ResolveFfprobePath();
            ffmpegPath = toolLocator.ResolveFfmpegPath();
            var probeAttempt = ffprobeRunner
                .TryProbeMediaAsync(ffprobePath, inputPaths[0], CancellationToken.None)
                .GetAwaiter().GetResult();

            if (probeAttempt.Probe is not null)
            {
                sourceIsStereo = probeAttempt.Probe.PrimaryAudioChannels >= 2;
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Program: EnsureRemoveNoiseAudioOptions probe failed (non-fatal): {ex.Message}");
        }

        var previewSourcePath = inputPaths.Count == 1 ? inputPaths[0] : null;

        try
        {
            using var picker = new RemoveNoiseAudioPickerForm(
                RemoveNoiseAudioPickerForm.BuildSourceLabel(inputPaths),
                sourceIsStereo,
                previewSourcePath,
                ffmpegPath);
            if (picker.ShowDialog() != DialogResult.OK || picker.SelectedStrength is null)
            {
                return false;
            }

            options[ActionOptionKeys.NoiseStrength] = picker.SelectedStrength;
            options[ActionOptionKeys.ProcessStereoAudio] = picker.ProcessStereo ? "true" : "false";
            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Remove Noise")));
            return false;
        }
    }

    private static bool ShouldPreferGpuForSeparateAudio(IReadOnlyDictionary<string, string>? options)
    {
        if (options is not null &&
            options.TryGetValue(ActionOptionKeys.SeparateEngine, out var engine) &&
            !string.IsNullOrWhiteSpace(engine))
        {
            if (string.Equals(engine, "cpu", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(engine, "gpu", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return SeparateAudio.ModelLocator.IsDmlAvailable();
    }

    private static bool EnsureSeparateAudioOptions(
        IReadOnlyList<string> inputPaths,
        Dictionary<string, string> options,
        AppLogger logger)
    {
        if (options.ContainsKey(ActionOptionKeys.Stems))
        {
            return true;
        }

        if (inputPaths.Count == 0)
        {
            ShowCliError(MediaActionMessages.InputPathRequired());
            return false;
        }

        var firstInputPath = inputPaths[0];
        var sourceExtension = Path.GetExtension(firstInputPath).ToLowerInvariant();
        if (sourceExtension is not (".wav" or ".mp3" or ".flac" or ".m4a" or ".ogg" or ".aac" or ".wma"))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(
                sourceExtension,
                SeparateAudio.SeparateAudioAction.GetSupportedExtensionsText()));
            return false;
        }

        var toolLocator = new ToolLocator();
        var ffprobeRunner = new FfprobeRunner(logger);
        try
        {
            var ffprobePath = toolLocator.ResolveFfprobePath();
            var probeAttempt = ffprobeRunner.TryProbeMediaAsync(ffprobePath, firstInputPath, CancellationToken.None).GetAwaiter().GetResult();

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

            using var picker = new SeparateAudioPickerForm(
                SeparateAudioPickerForm.BuildSourceLabel(inputPaths),
                SeparateAudioPickerForm.BuildDurationLabel(probe, inputPaths.Count),
                SeparateAudio.ModelLocator.IsDmlAvailable(),
                options);

            if (picker.ShowDialog() != DialogResult.OK || picker.SelectionOptions is null)
            {
                return false;
            }

            foreach (var item in picker.SelectionOptions)
            {
                options[item.Key] = item.Value;
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Audio Separation")));
            return false;
        }
    }
}
