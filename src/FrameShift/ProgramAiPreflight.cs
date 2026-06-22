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
using CreateSubtitles = FrameShift.Core.AI.CreateSubtitles;
using RemoveBackground = FrameShift.Core.AI.RemoveBackground;
using RemoveNoise = FrameShift.Core.AI.RemoveNoise;
using SeparateAudio = FrameShift.Core.AI.SeparateAudio;
using Upscale = FrameShift.Core.AI.Upscale;
using VideoInterpolation = FrameShift.Core.AI.VideoInterpolation;

namespace FrameShift;

internal static partial class Program
{
    private static bool EnsureRemoveBackgroundModelReady(
        AppLogger logger,
        IReadOnlyDictionary<string, string>? options = null)
    {
        var model = ResolveBackgroundRemovalModel(options, logger);
        if (model is null)
        {
            return false;
        }

        if (model.ManualOnly)
        {
            return EnsureBriaManualModelReady(model, logger);
        }

        var modelPath = RemoveBackground.ModelLocator.GetModelPath(model);
        if (File.Exists(modelPath) &&
            RemoveBackground.ModelDownloader.IsModelFileValid(modelPath, model, logger))
        {
            logger.Log($"Program: Remove Background model already present and verified. modelId={model.Id}.");
            return true;
        }

        if (File.Exists(modelPath))
        {
            logger.Log($"Program: Remove Background model failed verification. Removing invalid file. modelId={model.Id}.");
            try
            {
                File.Delete(modelPath);
            }
            catch (Exception ex)
            {
                logger.Log($"Program: failed to delete invalid Remove Background model. {ex}");
            }
        }

        logger.Log($"Program: Remove Background model missing. Opening DownloadModelForm. modelId={model.Id}.");
        using var downloadForm = new DownloadModelForm(
            "FrameShift AI - Remove Background",
            "Download the AI model to enable background removal",
            IconPaths.RemoveBackgroundAiIcon,
            model.DisplayName,
            model.License,
            model.ExpectedSizeBytes,
            async (progress, ct) =>
            {
                RemoveBackground.ModelLocator.EnsureDirectoryExists(model);
                await RemoveBackground.ModelDownloader.DownloadAsync(
                    model,
                    modelPath,
                    progress,
                    ct).ConfigureAwait(false);
            });
        var dialogResult = downloadForm.ShowDialog();
        var modelReady = dialogResult == DialogResult.OK &&
            File.Exists(modelPath) &&
            RemoveBackground.ModelDownloader.IsModelFileValid(modelPath, model, logger);
        logger.Log($"Program: Remove Background preflight completed. dialogResult={dialogResult}, modelReady={modelReady}, modelId={model.Id}.");
        return modelReady;
    }

    // BRIA RMBG-2.0 models are user-supplied. FrameShift never downloads them: it only
    // verifies the file the user placed in the model folder and, when missing or invalid,
    // points to the official BRIA page (strict SHA256 pin, warn-only fallback on mismatch).
    private static bool EnsureBriaManualModelReady(
        RemoveBackground.BackgroundRemovalModelDefinition model,
        AppLogger logger)
    {
        var modelFolder = RemoveBackground.ModelLocator.GetModelDirectory(model);
        var modelPath = RemoveBackground.ModelLocator.GetModelPath(model);
        var infoPageUrl = model.InfoPageUrl ?? "https://huggingface.co/briaai/RMBG-2.0/tree/main";
        var approxMb = model.ExpectedSizeBytes / (1024L * 1024L);

        try
        {
            RemoveBackground.ModelLocator.EnsureDirectoryExists(model);
        }
        catch (Exception ex)
        {
            logger.Log($"Program: failed to create BRIA model folder. {ex.Message}");
        }

        FrameShift.Windows.AI.BriaModelStatus CheckStatus()
        {
            if (!File.Exists(modelPath))
            {
                return FrameShift.Windows.AI.BriaModelStatus.Missing;
            }

            return RemoveBackground.ModelDownloader.IsModelFileValid(modelPath, model, logger)
                ? FrameShift.Windows.AI.BriaModelStatus.Valid
                : FrameShift.Windows.AI.BriaModelStatus.Mismatch;
        }

        var status = CheckStatus();
        if (status == FrameShift.Windows.AI.BriaModelStatus.Valid)
        {
            logger.Log($"Program: BRIA model present and verified. modelId={model.Id}.");
            return true;
        }

        logger.Log($"Program: BRIA model not ready. status={status}, modelId={model.Id}, path={modelPath}.");
        using var notice = new FrameShift.Windows.AI.BriaModelNoticeForm(
            model.FileName,
            modelFolder,
            infoPageUrl,
            $"~{approxMb} MB",
            status,
            CheckStatus);
        var dialogResult = notice.ShowDialog();
        var proceed = dialogResult == DialogResult.OK && notice.Proceed;
        logger.Log($"Program: BRIA notice dialog completed. proceed={proceed}, modelId={model.Id}.");
        return proceed;
    }

    private static bool EnsureUpscaleOptions(
        IReadOnlyList<string> inputPaths,
        Dictionary<string, string> options,
        AppLogger logger)
    {
        // A model supplied via CLI (--upscale-model) skips the picker (headless override).
        if (options.ContainsKey(ActionOptionKeys.UpscaleModel))
        {
            return true;
        }

        if (inputPaths.Count == 0)
        {
            ShowCliError(MediaActionMessages.InputPathRequired());
            return false;
        }

        var ext = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (ext is not ".png" and not ".jpg" and not ".jpeg" and not ".webp" and not ".bmp")
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(ext, ".png, .jpg, .jpeg, .webp, .bmp"));
            return false;
        }

        try
        {
            // Custom target size needs the source dimensions and a single file; presets (x2/x3/x4)
            // work for any selection.
            int sourceWidth = 0, sourceHeight = 0;
            bool allowCustom = inputPaths.Count == 1;
            if (allowCustom)
            {
                try
                {
                    var info = SixLabors.ImageSharp.Image.Identify(inputPaths[0]);
                    sourceWidth = info.Width;
                    sourceHeight = info.Height;
                }
                catch (Exception ex)
                {
                    allowCustom = false;
                    logger.Log($"Program: could not read image size for upscale custom target (non-fatal): {ex.Message}");
                }
            }

            using var picker = new UpscaleImagePickerForm(
                UpscaleImagePickerForm.BuildSourceLabel(inputPaths),
                sourceWidth,
                sourceHeight,
                allowCustom);
            if (picker.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(picker.SelectedModelId))
            {
                return false;
            }

            options[ActionOptionKeys.UpscaleModel] = picker.SelectedModelId!;

            if (picker.SelectedScale is { } scale)
            {
                options[ActionOptionKeys.UpscaleScale] = scale;
            }
            else if (picker.CustomTarget is { } target)
            {
                options[ActionOptionKeys.UpscaleTargetWidth] = target.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
                options[ActionOptionKeys.UpscaleTargetHeight] = target.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            logger.Log($"Program: Upscale Image selected via picker. modelId={picker.SelectedModelId}, scale={picker.SelectedScale ?? "custom"}.");
            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Upscale Image")));
            return false;
        }
    }

    private static bool EnsureUpscaleModelReady(
        AppLogger logger,
        IReadOnlyDictionary<string, string>? options = null,
        string featureName = "Upscale Image",
        bool videoMode = false,
        IReadOnlyList<string>? inputPaths = null)
    {
        var model = ResolveUpscaleModel(options, logger, videoMode);
        if (videoMode)
        {
            model = ResolveUpscaleVideoExecutionModel(model, options, inputPaths, logger);
        }

        var modelPath = Upscale.ModelLocator.GetModelPath(model);
        if (File.Exists(modelPath) &&
            Upscale.ModelDownloader.IsModelFileValid(modelPath, model, logger))
        {
            logger.Log($"Program: {featureName} model already present and verified. modelId={model.Id}.");
            return true;
        }

        if (File.Exists(modelPath))
        {
            logger.Log($"Program: {featureName} model failed verification. Removing invalid file. modelId={model.Id}.");
            try
            {
                File.Delete(modelPath);
            }
            catch (Exception ex)
            {
                logger.Log($"Program: failed to delete invalid {featureName} model. {ex}");
            }
        }

        logger.Log($"Program: {featureName} model missing. Opening DownloadModelForm. modelId={model.Id}.");
        using var downloadForm = new DownloadModelForm(
            $"FrameShift AI - {featureName}",
            $"Download the AI model to enable {(videoMode ? "video" : "image")} upscaling",
            videoMode ? IconPaths.UpscaleVideoAiIcon : IconPaths.UpscaleImageAiIcon,
            model.DisplayName,
            model.License,
            model.ExpectedSizeBytes,
            async (progress, ct) =>
            {
                Upscale.ModelLocator.EnsureDirectoryExists(model);
                await Upscale.ModelDownloader.DownloadAsync(
                    model,
                    modelPath,
                    progress,
                    ct).ConfigureAwait(false);
            });
        var dialogResult = downloadForm.ShowDialog();
        var modelReady = dialogResult == DialogResult.OK &&
            File.Exists(modelPath) &&
            Upscale.ModelDownloader.IsModelFileValid(modelPath, model, logger);
        logger.Log($"Program: {featureName} preflight completed. dialogResult={dialogResult}, modelReady={modelReady}, modelId={model.Id}.");
        return modelReady;
    }

    private static bool EnsureUpscaleVideoOptions(
        IReadOnlyList<string> inputPaths,
        Dictionary<string, string> options,
        AppLogger logger)
    {
        // Supplying the model explicitly is the headless override, matching Upscale Image.
        if (options.ContainsKey(ActionOptionKeys.UpscaleModel))
        {
            options.TryGetValue(ActionOptionKeys.UpscaleModel, out var modelId);
            var model = Upscale.UpscaleModelCatalog.GetById(modelId);
            if (model is null || !model.RecommendedForVideo)
            {
                ShowCliError($"Unknown or unsupported video upscale model: {modelId}.");
                return false;
            }

            return true;
        }
        if (inputPaths.Count == 0)
        {
            ShowCliError(MediaActionMessages.InputPathRequired());
            return false;
        }

        const string supported = ".mp4, .mkv, .avi, .mov, .webm, .m4v";
        foreach (var inputPath in inputPaths)
        {
            var extension = Path.GetExtension(inputPath).ToLowerInvariant();
            if (extension is not ".mp4" and not ".mkv" and not ".avi" and not ".mov" and not ".webm" and not ".m4v")
            {
                ShowCliError(MediaActionMessages.UnsupportedSourceFormat(extension, supported));
                return false;
            }
        }

        try
        {
            int sourceWidth = 0;
            int sourceHeight = 0;
            bool allowCustom = inputPaths.Count == 1;
            if (allowCustom)
            {
                var locator = new ToolLocator();
                var runner = new FfprobeRunner(logger);
                var probeAttempt = runner.TryProbeMediaAsync(
                        locator.ResolveFfprobePath(),
                        inputPaths[0],
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                if (probeAttempt.Probe is { HasVideo: true, VideoWidth: > 0, VideoHeight: > 0 } probe)
                {
                    sourceWidth = probe.VideoWidth;
                    sourceHeight = probe.VideoHeight;
                }
                else
                {
                    allowCustom = false;
                    logger.Log($"Program: video dimensions unavailable for upscale custom target: {probeAttempt.ErrorMessage}");
                }
            }

            using var picker = new UpscaleVideoPickerForm(
                UpscaleImagePickerForm.BuildSourceLabel(inputPaths),
                sourceWidth,
                sourceHeight,
                allowCustom);
            if (picker.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(picker.SelectedModelId))
                return false;

            options[ActionOptionKeys.UpscaleModel] = picker.SelectedModelId!;
            if (picker.SelectedScale is { } scale)
            {
                options[ActionOptionKeys.UpscaleScale] = scale;
            }
            else if (picker.CustomTarget is { } target)
            {
                options[ActionOptionKeys.UpscaleTargetWidth] = target.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
                options[ActionOptionKeys.UpscaleTargetHeight] = target.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            logger.Log($"Program: Upscale Video selected via picker. modelId={picker.SelectedModelId}, scale={picker.SelectedScale ?? "custom"}.");
            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Upscale Video")));
            return false;
        }
    }

    private static Upscale.UpscaleModelDefinition ResolveUpscaleModel(
        IReadOnlyDictionary<string, string>? options,
        AppLogger logger,
        bool videoMode = false)
    {
        string? modelId = null;
        if (options is not null &&
            options.TryGetValue(ActionOptionKeys.UpscaleModel, out var optionValue) &&
            !string.IsNullOrWhiteSpace(optionValue))
        {
            modelId = optionValue;
        }

        var model = Upscale.UpscaleModelCatalog.GetById(modelId) ??
            (videoMode
                ? Upscale.UpscaleModelCatalog.GetDefaultVideo()
                : Upscale.UpscaleModelCatalog.GetDefault());

        if (!string.IsNullOrWhiteSpace(modelId) &&
            !string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase))
        {
            logger.Log($"Program: upscale model option not found. requested={modelId}, fallback={model.Id}.");
        }

        return model;
    }

    private static Upscale.UpscaleModelDefinition ResolveUpscaleVideoExecutionModel(
        Upscale.UpscaleModelDefinition selectedModel,
        IReadOnlyDictionary<string, string>? options,
        IReadOnlyList<string>? inputPaths,
        AppLogger logger)
    {
        if (!string.Equals(selectedModel.Id, "realesr-animevideov3", StringComparison.OrdinalIgnoreCase))
            return selectedModel;
        if (!UpscaleVideoSettings.TryFromOptions(options, out var settings, out _) || settings is null)
            return selectedModel;

        int sourceWidth = 1;
        int sourceHeight = 1;
        if (settings.TargetWidth.HasValue || settings.TargetHeight.HasValue)
        {
            if (inputPaths is null || inputPaths.Count != 1)
                return selectedModel;

            try
            {
                var locator = new ToolLocator();
                var runner = new FfprobeRunner(logger);
                var probeAttempt = runner.TryProbeMediaAsync(
                        locator.ResolveFfprobePath(),
                        inputPaths[0],
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                if (probeAttempt.Probe is not { HasVideo: true, VideoWidth: > 0, VideoHeight: > 0 } probe)
                    return selectedModel;

                sourceWidth = probe.VideoWidth;
                sourceHeight = probe.VideoHeight;
            }
            catch (Exception ex)
            {
                logger.Log($"Program: failed to resolve anime upscale execution model via ffprobe. {ex.Message}");
                return selectedModel;
            }
        }

        return Upscale.UpscaleModelCatalog.ResolveVideoExecutionModel(
            selectedModel,
            settings.Request,
            sourceWidth,
            sourceHeight);
    }

    private static bool EnsureCreateSubtitlesOptions(
        IReadOnlyList<string> inputPaths,
        Dictionary<string, string> options,
        AppLogger logger,
        bool videoMode)
    {
        if (options.ContainsKey(ActionOptionKeys.SubtitlesModel))
        {
            return true;
        }

        if (inputPaths.Count == 0)
        {
            ShowCliError(MediaActionMessages.InputPathRequired());
            return false;
        }

        var supported = videoMode
            ? CreateSubtitles.CreateSubtitlesAction.GetSupportedVideoExtensionsText()
            : CreateSubtitles.CreateSubtitlesAction.GetSupportedAudioExtensionsText();

        foreach (var inputPath in inputPaths)
        {
            var extension = Path.GetExtension(inputPath).ToLowerInvariant();
            var isSupported = videoMode
                ? CreateSubtitles.CreateSubtitlesAction.IsSupportedVideoExtension(extension)
                : CreateSubtitles.CreateSubtitlesAction.IsSupportedAudioExtension(extension);
            if (!isSupported)
            {
                ShowCliError(MediaActionMessages.UnsupportedSourceFormat(extension, supported));
                return false;
            }
        }

        try
        {
            var initialOutputFormat = CreateSubtitles.CreateSubtitlesOutputFormats.Default;
            if (options.TryGetValue(ActionOptionKeys.SubtitlesOutputFormat, out var requestedOutputFormat) &&
                CreateSubtitles.CreateSubtitlesOutputFormats.TryParse(requestedOutputFormat, out var parsedOutputFormat))
            {
                initialOutputFormat = parsedOutputFormat;
            }

            var initialAssPreset = CreateSubtitles.CreateSubtitlesAssPresets.Default;
            if (options.TryGetValue(ActionOptionKeys.SubtitlesAssPreset, out var requestedAssPreset) &&
                CreateSubtitles.CreateSubtitlesAssPresets.TryParse(requestedAssPreset, out var parsedAssPreset))
            {
                initialAssPreset = parsedAssPreset;
            }

            using var picker = new CreateSubtitlesPickerForm(
                "Create Subtitle File",
                CreateSubtitlesPickerForm.BuildSourceLabel(inputPaths),
                initialOutputFormat,
                initialAssPreset);
            if (picker.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(picker.SelectedModelId))
            {
                return false;
            }

            options[ActionOptionKeys.SubtitlesModel] = picker.SelectedModelId;
            var outputFormatId = CreateSubtitles.CreateSubtitlesOutputFormats.ToOptionValue(picker.SelectedOutputFormat);
            var assPresetId = CreateSubtitles.CreateSubtitlesAssPresets.ToOptionValue(picker.SelectedAssPreset);
            options[ActionOptionKeys.SubtitlesOutputFormat] = outputFormatId;
            options[ActionOptionKeys.SubtitlesAssPreset] = assPresetId;
            logger.Log($"Program: Create Subtitles options selected via picker. modelId={picker.SelectedModelId}, outputFormat={outputFormatId}, assPreset={assPresetId}, videoMode={videoMode}.");
            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Create Subtitles")));
            return false;
        }
    }

    private static bool EnsureCreateSubtitlesModelReady(
        AppLogger logger,
        IReadOnlyDictionary<string, string>? options = null)
    {
        string? requestedModelId = null;
        if (options is not null &&
            options.TryGetValue(ActionOptionKeys.SubtitlesModel, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            requestedModelId = value;
        }

        var model = CreateSubtitles.CreateSubtitlesModelCatalog.GetById(requestedModelId) ??
            CreateSubtitles.CreateSubtitlesModelCatalog.GetDefault();
        var modelFiles = CreateSubtitles.CreateSubtitlesModelLocator.GetModelFiles(model);

        if (CreateSubtitles.CreateSubtitlesModelDownloader.IsModelReady(model, logger))
        {
            logger.Log($"Program: Create Subtitles model already present and verified. modelId={model.Id}.");
            return true;
        }

        CreateSubtitles.CreateSubtitlesModelDownloader.RemoveInvalidFiles(model, logger);

        logger.Log($"Program: Create Subtitles model missing. Opening DownloadModelForm. modelId={model.Id}.");
        using var downloadForm = new DownloadModelForm(
            "FrameShift AI - Create Subtitles",
            $"Download {model.DisplayName} to enable local subtitle creation",
            IconPaths.CreateSubtitlesAiIcon,
            model.DisplayName,
            model.License,
            model.ExpectedTotalSizeBytes,
            async (progress, ct) =>
            {
                CreateSubtitles.CreateSubtitlesModelLocator.EnsureDirectoryExists(model);
                await CreateSubtitles.CreateSubtitlesModelDownloader.DownloadAsync(model, progress, ct).ConfigureAwait(false);
            });

        var dialogResult = downloadForm.ShowDialog();
        var ready = dialogResult == DialogResult.OK &&
            File.Exists(modelFiles.EncoderPath) &&
            File.Exists(modelFiles.DecoderPath) &&
            File.Exists(modelFiles.TokensPath) &&
            CreateSubtitles.CreateSubtitlesModelDownloader.IsModelReady(model, logger);
        logger.Log($"Program: Create Subtitles preflight completed. dialogResult={dialogResult}, ready={ready}, modelId={model.Id}.");
        return ready;
    }

    private static RemoveBackground.BackgroundRemovalModelDefinition? ResolveBackgroundRemovalModel(
        IReadOnlyDictionary<string, string>? options,
        AppLogger logger)
    {
        string? modelId = null;
        if (options is not null &&
            options.TryGetValue(ActionOptionKeys.BackgroundRemovalModel, out var optionValue) &&
            !string.IsNullOrWhiteSpace(optionValue))
        {
            modelId = optionValue;
        }

        var model = RemoveBackground.BackgroundRemovalModelCatalog.GetById(modelId) ??
            RemoveBackground.BackgroundRemovalModelCatalog.GetDefault();

        if (!string.IsNullOrWhiteSpace(modelId) &&
            !string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase))
        {
            logger.Log($"Program: Remove Background model option not found. requested={modelId}, fallback={model.Id}.");
        }

        return model;
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
