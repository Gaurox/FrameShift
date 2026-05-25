using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RemoveBackground = FrameShift.Core.AI.RemoveBackground;
using RemoveNoise = FrameShift.Core.AI.RemoveNoise;
using SeparateAudio = FrameShift.Core.AI.SeparateAudio;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Core.Progress;
using FrameShift.Windows.AI;
using FrameShift.Windows.Batch;
using FrameShift.Windows.Forms;
using FrameShift.Windows.ProgressUI;

namespace FrameShift;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        AppLogger.LogStatic($"DIAG BUILD ACTIVE - log path = {AppLogger.LogPath}");
        AppLogger.LogStatic($"Program: Main entered. baseDirectory={AppContext.BaseDirectory}, processPath={Environment.ProcessPath ?? "<null>"}, argsCount={args.Length}.");
        var logger = new AppLogger();
        var registry = ActionRegistry.CreateDefault();

        ApplicationConfiguration.Initialize();

        if (args.Length > 0)
        {
            return RunCli(args, registry, logger);
        }

        Application.Run(new MainForm());
        return 0;
    }

    private static int RunCli(string[] args, ActionRegistry registry, AppLogger logger)
    {
        if (!TryParseArguments(args, out var actionId, out var inputPaths, out var options, out var error))
        {
            ShowCliError(error ?? MediaActionMessages.UnsupportedCommandLine("FrameShift.exe --action <id> [--target <format>] [--profile <name>] <input-paths...>"));
            return 1;
        }

        if (!registry.TryGet(actionId, out var action))
        {
            ShowCliError(MediaActionMessages.UnknownAction(actionId));
            return 1;
        }

        if (inputPaths.Count == 0)
        {
            ShowCliError(MediaActionMessages.AtLeastOneInputPathRequired());
            return 1;
        }

        if (actionId.Equals("remove-background", StringComparison.OrdinalIgnoreCase))
        {
            return RunRemoveBackground(registry, actionId, logger, inputPaths, options);
        }

        if (actionId.Equals("separate-audio", StringComparison.OrdinalIgnoreCase))
        {
            return RunSeparateAudio(registry, actionId, logger, inputPaths, options);
        }

        if (actionId.Equals("remove-noise", StringComparison.OrdinalIgnoreCase))
        {
            return RunRemoveNoise(registry, actionId, logger, inputPaths, options);
        }

        if (actionId.Equals("remove-noise-video", StringComparison.OrdinalIgnoreCase))
        {
            return RunRemoveNoiseVideo(registry, actionId, logger, inputPaths, options);
        }

        if (ShouldRunConversionBatch(actionId, options))
        {
            return RunConversionBatch(registry, actionId, logger, inputPaths);
        }

        if (actionId.Equals("media-info", StringComparison.OrdinalIgnoreCase))
        {
            return RunMediaInfo(inputPaths[0], logger);
        }

        if (actionId.Equals("convert-video", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureConvertVideoOptions(inputPaths, options))
            {
                return 0;
            }
        }

        if (actionId.Equals("convert-audio", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureConvertAudioOptions(inputPaths, options))
            {
                return 0;
            }
        }

        if (actionId.Equals("extract-audio", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureExtractAudioOptions(inputPaths, options))
            {
                return 0;
            }
        }

        if (actionId.Equals("cut-audio", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureCutAudioOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("cut-video", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureCutVideoOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("create-gif", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureCreateGifOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("convert-image", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureConvertImageOptions(inputPaths, options))
            {
                return 0;
            }
        }

        if (actionId.Equals("change-pitch", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureChangePitchOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("change-audio-speed", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureChangeAudioSpeedOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("change-video-speed", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureChangeVideoSpeedOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("convert-to-icon", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureConvertToIconOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("crop-video", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureCropVideoOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("crop-image", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureCropImageOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("rotate-flip-image", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureRotateFlipImageOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("rotate-flip-video", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureRotateFlipVideoOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("interpolate-video", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureInterpolateVideoOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("resize-video", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureResizeVideoOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("compress-video", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureCompressVideoOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("compress-audio", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureCompressAudioOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("compress-image", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureCompressImageOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("resize-image", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureResizeImageOptions(inputPaths, options, logger))
            {
                return 0;
            }
        }

        if (actionId.Equals("image-to-pdf", StringComparison.OrdinalIgnoreCase))
        {
            return RunImageToPdf(action, inputPaths, options, logger);
        }

        return RunQueuedActionWithProgressForm(action, inputPaths, options, logger, "single-run");
    }

    private static int RunRemoveBackground(
        ActionRegistry registry,
        string actionId,
        AppLogger logger,
        IReadOnlyList<string> inputPaths,
        IReadOnlyDictionary<string, string> options)
    {
        return RunConversionBatch(registry, actionId, logger, inputPaths, options);
    }

    private static int RunSeparateAudio(
        ActionRegistry registry,
        string actionId,
        AppLogger logger,
        IReadOnlyList<string> inputPaths,
        IReadOnlyDictionary<string, string> options)
    {
        return RunConversionBatch(registry, actionId, logger, inputPaths, options);
    }

    private static int RunRemoveNoise(
        ActionRegistry registry,
        string actionId,
        AppLogger logger,
        IReadOnlyList<string> inputPaths,
        IReadOnlyDictionary<string, string> options)
    {
        return RunConversionBatch(registry, actionId, logger, inputPaths, options);
    }

    private static int RunRemoveNoiseVideo(
        ActionRegistry registry,
        string actionId,
        AppLogger logger,
        IReadOnlyList<string> inputPaths,
        IReadOnlyDictionary<string, string> options)
    {
        return RunConversionBatch(registry, actionId, logger, inputPaths, options);
    }

    private const string ImageToPdfMutexName = @"Local\FrameShift_ImageToPdf";
    private const string ImageToPdfPipeName = "FrameShift_ImageToPdfQueue";

    private static int RunImageToPdf(
        IFrameShiftAction action,
        IReadOnlyList<string> inputPaths,
        Dictionary<string, string> options,
        AppLogger logger)
    {
        if (options.ContainsKey(ActionOptionKeys.ImageToPdfSettings))
        {
            return RunImmediateAction(action, inputPaths[0], options, logger);
        }

        if (inputPaths.Count == 0)
        {
            ShowCliError(MediaActionMessages.InputPathRequired());
            return 1;
        }

        foreach (var inputPath in inputPaths)
        {
            var ext = Path.GetExtension(inputPath).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp"))
            {
                ShowCliError(MediaActionMessages.UnsupportedSourceFormat(ext, ImageCropSupport.GetSupportedExtensionsText()));
                return 1;
            }
        }

        using var mutex = new Mutex(true, ImageToPdfMutexName, out var isPrimary);

        if (!isPrimary)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", ImageToPdfPipeName, PipeDirection.Out);
                pipe.Connect(5000);
                using var writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };
                foreach (var path in inputPaths)
                    writer.WriteLine(path);
                return 0;
            }
            catch (Exception ex)
            {
                logger.Log($"Program: RunImageToPdf failed to send to primary instance: {ex}");
                ShowCliError("FrameShift is already running, but the new image could not be added.");
                return 1;
            }
        }

        using var cts = new CancellationTokenSource();
        ImageToPdfForm? form = null;
        try
        {
            var toolLocator = new ToolLocator();
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            form = new ImageToPdfForm(inputPaths, ffmpegPath, new FfmpegRunner(logger));

            var listenerThread = new Thread(() => RunImageToPdfPipeListener(form, cts.Token))
            {
                IsBackground = true
            };
            listenerThread.Start();

            if (form.ShowDialog() != DialogResult.OK || form.Settings is null)
            {
                return 0;
            }

            options[ActionOptionKeys.ImageToPdfSettings] = form.Settings.ToOptionPayload();
            return RunImmediateAction(action, inputPaths[0], options, logger);
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Image to PDF")));
            return 0;
        }
        finally
        {
            cts.Cancel();
            form?.Dispose();
        }
    }

    private static void RunImageToPdfPipeListener(ImageToPdfForm form, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    ImageToPdfPipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                pipe.WaitForConnectionAsync(cancellationToken).GetAwaiter().GetResult();

                if (cancellationToken.IsCancellationRequested)
                {
                    pipe.Dispose();
                    break;
                }

                using (pipe)
                {
                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    var paths = new List<string>();
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            paths.Add(line);
                    }

                    if (paths.Count > 0 && !form.IsDisposed)
                        form.AddPathsThreadSafe(paths);
                }
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                break;
            }
            catch
            {
                pipe?.Dispose();
            }
        }
    }

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
            RemoveNoise.DeepFilterNetModelDownloader.ModelDisplayName,
            RemoveNoise.DeepFilterNetModelDownloader.ModelLicense,
            RemoveNoise.DeepFilterNetModelDownloader.TotalSizeBytes,
            async (progress, ct) =>
            {
                RemoveNoise.DeepFilterNetModelLocator.EnsureDirectoryExists();
                await RemoveNoise.DeepFilterNetModelDownloader.DownloadAllAsync(
                    System.IO.Path.GetDirectoryName(RemoveNoise.DeepFilterNetModelLocator.EncPath)!,
                    progress,
                    ct).ConfigureAwait(false);
            });

        var dialogResult = downloadForm.ShowDialog();
        var ready = dialogResult == DialogResult.OK && RemoveNoise.DeepFilterNetModelLocator.AllModelsExist();
        logger.Log($"Program: Remove Noise preflight completed. dialogResult={dialogResult}, ready={ready}.");
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
            return true;

        if (inputPaths.Count == 0)
        {
            ShowCliError(MediaActionMessages.InputPathRequired());
            return false;
        }

        var sourceIsStereo = false;
        string? ffmpegPath = null;
        try
        {
            var toolLocator   = new ToolLocator();
            var ffprobeRunner = new FfprobeRunner(logger);
            var ffprobePath   = toolLocator.ResolveFfprobePath();
            ffmpegPath = toolLocator.ResolveFfmpegPath();
            var probeAttempt = ffprobeRunner
                .TryProbeMediaAsync(ffprobePath, inputPaths[0], CancellationToken.None)
                .GetAwaiter().GetResult();

            if (probeAttempt.Probe is not null)
                sourceIsStereo = probeAttempt.Probe.PrimaryAudioChannels >= 2;
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
                return false;

            options[ActionOptionKeys.NoiseStrength]      = picker.SelectedStrength;
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
            return true;

        if (inputPaths.Count == 0)
        {
            ShowCliError(MediaActionMessages.InputPathRequired());
            return false;
        }

        var sourceIsStereo = false;
        string? ffmpegPath = null;
        try
        {
            var toolLocator   = new ToolLocator();
            var ffprobeRunner = new FfprobeRunner(logger);
            var ffprobePath   = toolLocator.ResolveFfprobePath();
            ffmpegPath = toolLocator.ResolveFfmpegPath();
            var probeAttempt = ffprobeRunner
                .TryProbeMediaAsync(ffprobePath, inputPaths[0], CancellationToken.None)
                .GetAwaiter().GetResult();

            if (probeAttempt.Probe is not null)
                sourceIsStereo = probeAttempt.Probe.PrimaryAudioChannels >= 2;
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
                return false;

            options[ActionOptionKeys.NoiseStrength]      = picker.SelectedStrength;
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

    private static int RunMediaInfo(string inputPath, AppLogger logger)
    {
        if (!File.Exists(inputPath))
        {
            ShowCliError(MediaActionMessages.InputFileNotFound(inputPath));
            return 1;
        }

        var toolLocator = new ToolLocator();
        var ffprobePath = toolLocator.ResolveFfprobePath();

        if (!File.Exists(ffprobePath))
        {
            ShowCliError("ffprobe introuvable. Vérifiez l'installation de FrameShift.");
            return 1;
        }

        var fileInfo = new FileInfo(inputPath);
        var probeRunner = new FfprobeRunner(logger);
        var (data, error) = probeRunner
            .TryProbeMediaInfoAsync(ffprobePath, inputPath, fileInfo.Length, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        if (data is null)
        {
            ShowCliError(error ?? MediaActionMessages.ProbeFailed());
            return 1;
        }

        var report = MediaInfoFormatter.Build(data, fileInfo);

        using var form = new MediaInfoForm(report.Text, report.Kind, fileInfo.Name);
        form.ShowDialog();
        return 0;
    }

    private static int RunConversionBatch(
        ActionRegistry registry,
        string actionId,
        AppLogger logger,
        IReadOnlyList<string> inputPaths,
        IReadOnlyDictionary<string, string>? options = null)
    {
        logger.Log($"Program: RunConversionBatch entered. actionId={actionId}, inputPathCount={inputPaths.Count}, logPath={AppLogger.LogPath}, baseDirectory={AppContext.BaseDirectory}, processPath={Environment.ProcessPath ?? "<null>"}.");
        var effectiveOptions = options is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase);

        var definition = GetConversionBatchDefinition(actionId);
        if (definition is null)
        {
            ShowCliError(MediaActionMessages.UnknownAction(actionId));
            return 1;
        }

        using var batchMutex = new Mutex(true, definition.MutexName, out var isPrimaryInstance);
        if (!isPrimaryInstance)
        {
            try
            {
                ConversionBatchSession.SendPathsToPrimaryInstance(definition, inputPaths);
                return 0;
            }
            catch (Exception ex)
            {
                logger.Log(ex.ToString());
                ShowCliError($"FrameShift is already running, but the new {definition.DisplayName.ToLowerInvariant()} file could not be queued.");
                return 1;
            }
        }

        if (!registry.TryGet(actionId, out var action))
        {
            ShowCliError(MediaActionMessages.UnknownAction(actionId));
            return 1;
        }

        if (actionId.Equals("remove-background", StringComparison.OrdinalIgnoreCase) &&
            !EnsureRemoveBackgroundModelReady(logger))
        {
            logger.Log("Program: Remove Background preflight exited before progress window opened.");
            return 0;
        }

        if (actionId.Equals("separate-audio", StringComparison.OrdinalIgnoreCase) &&
            !EnsureSeparateAudioOptions(inputPaths, effectiveOptions, logger))
        {
            logger.Log("Program: Separate Audio options prompt exited before progress window opened.");
            return 0;
        }

        if (actionId.Equals("separate-audio", StringComparison.OrdinalIgnoreCase) &&
            !EnsureSeparateAudioModelReady(logger, effectiveOptions))
        {
            logger.Log("Program: Separate Audio preflight exited before progress window opened.");
            return 0;
        }

        if (actionId.Equals("remove-noise", StringComparison.OrdinalIgnoreCase) &&
            !ValidateRemoveNoiseInputs(inputPaths))
        {
            logger.Log("Program: Remove Noise input validation failed before progress window opened.");
            return 0;
        }

        if (actionId.Equals("remove-noise", StringComparison.OrdinalIgnoreCase) &&
            !EnsureRemoveNoiseAudioOptions(inputPaths, effectiveOptions, logger))
        {
            logger.Log("Program: Remove Noise options prompt exited before progress window opened.");
            return 0;
        }

        if (actionId.Equals("remove-noise", StringComparison.OrdinalIgnoreCase) &&
            !EnsureRemoveNoiseModelReady(logger))
        {
            logger.Log("Program: Remove Noise preflight exited before progress window opened.");
            return 0;
        }

        if (actionId.Equals("remove-noise-video", StringComparison.OrdinalIgnoreCase) &&
            !ValidateRemoveNoiseVideoInputs(inputPaths))
        {
            logger.Log("Program: Remove Noise (Video) input validation failed before progress window opened.");
            return 0;
        }

        if (actionId.Equals("remove-noise-video", StringComparison.OrdinalIgnoreCase) &&
            !EnsureRemoveNoiseVideoOptions(inputPaths, effectiveOptions, logger))
        {
            logger.Log("Program: Remove Noise (Video) options prompt exited before progress window opened.");
            return 0;
        }

        if (actionId.Equals("remove-noise-video", StringComparison.OrdinalIgnoreCase) &&
            !EnsureRemoveNoiseModelReady(logger))
        {
            logger.Log("Program: Remove Noise (Video) preflight exited before progress window opened.");
            return 0;
        }

        if (actionId.Equals("convert-video", StringComparison.OrdinalIgnoreCase) ||
            actionId.Equals("convert-audio", StringComparison.OrdinalIgnoreCase) ||
            actionId.Equals("convert-image", StringComparison.OrdinalIgnoreCase) ||
            actionId.Equals("extract-audio", StringComparison.OrdinalIgnoreCase))
        {
            return RunDeferredProgressConversionBatch(definition, action, logger, inputPaths);
        }

        using var progressForm = new ProgressForm();
        using var cancellationSource = new CancellationTokenSource();
        progressForm.CancelRequested += (_, _) =>
        {
            logger.Log("Program: RunConversionBatch CancelRequested event received.");
            RequestCancellationAsync(cancellationSource, logger, "batch");
        };

        var session = new ConversionBatchSession(definition, action, logger, progressForm);
        if (effectiveOptions.Count > 0)
        {
            session.SetSharedOptions(effectiveOptions);
        }
        progressForm.Shown += (_, _) => session.Start(inputPaths.ToArray(), cancellationSource.Token);

        logger.Log("Program: before Application.Run (batch progress form).");
        Application.Run(progressForm);
        logger.Log("Program: after Application.Run returns (batch progress form).");
        return session.ExitCode;
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

    private static int RunDeferredProgressConversionBatch(
        ConversionBatchSession.BatchDefinition definition,
        IFrameShiftAction action,
        AppLogger logger,
        IReadOnlyList<string> inputPaths)
    {
        logger.Log($"Program: RunDeferredProgressConversionBatch entered. actionId={definition.ActionId}, inputPathCount={inputPaths.Count}.");

        using var cancellationSource = new CancellationTokenSource();
        var session = new ConversionBatchSession(definition, action, logger);
        session.Initialize(inputPaths.ToArray(), cancellationSource.Token);

        try
        {
            session.WaitForPickerDebounce(cancellationSource.Token);
            var optionResult = session.PromptForSharedOptions();
            if (!optionResult.Success || optionResult.Options is null)
            {
                logger.Log("Program: RunDeferredProgressConversionBatch exited before showing progress because picker was canceled or failed.");
                return session.ExitCode;
            }

            session.SetSharedOptions(optionResult.Options);
        }
        catch (OperationCanceledException)
        {
            logger.Log("Program: RunDeferredProgressConversionBatch canceled before progress window opened.");
            return session.ExitCode;
        }
        finally
        {
            if (session.ExitCode != 0 && cancellationSource.IsCancellationRequested)
            {
                logger.Log("Program: RunDeferredProgressConversionBatch cancellation detected during picker phase.");
            }
        }

        using var progressForm = new ProgressForm();
        session.AttachProgressForm(progressForm);
        progressForm.CancelRequested += (_, _) =>
        {
            logger.Log("Program: RunDeferredProgressConversionBatch CancelRequested event received.");
            RequestCancellationAsync(cancellationSource, logger, "batch-deferred-progress");
        };

        progressForm.Shown += (_, _) => session.StartProcessing(cancellationSource.Token);

        logger.Log("Program: before Application.Run (deferred batch progress form).");
        Application.Run(progressForm);
        logger.Log("Program: after Application.Run returns (deferred batch progress form).");
        return session.ExitCode;
    }

    private static int RunImmediateAction(
        IFrameShiftAction action,
        string inputPath,
        IReadOnlyDictionary<string, string> options,
        AppLogger logger)
    {
        try
        {
            var result = action.ExecuteAsync(
                new ActionRequest(inputPath, logger, null, options),
                CancellationToken.None).GetAwaiter().GetResult();

            if (!result.Success && !result.Canceled)
            {
                logger.Log($"ACTION FAILED ({action.Descriptor.Id}): {result.Message}");
                ShowCliError(result.Message);
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            logger.Log(ex.ToString());
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.UnhandledError("An unexpected error occurred.")));
            return 1;
        }
    }

    private static async Task<RunActionResult> ExecuteQueuedActionAsync(
        IFrameShiftAction action,
        IReadOnlyList<string> inputPaths,
        IReadOnlyDictionary<string, string> options,
        AppLogger logger,
        IProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        var queueRunner = new ActionQueueRunner();
        var requests = inputPaths
            .Select(path => new ActionRequest(path, logger, progressReporter, options))
            .ToArray();

        var results = await queueRunner.RunAsync(action, requests, cancellationToken).ConfigureAwait(false);
        var anyFailure = results.Any(result => !result.Success && !result.Canceled);
        var failureMessage = results.FirstOrDefault(result => !result.Success && !result.Canceled)?.Message;
        var anySuccess = results.Any(result => result.Success);
        var allCanceled = results.Any() && results.All(result => result.Canceled);
        var anyGlobalCancellation = results.Any(result => result.CancellationScope == CancellationScope.All);
        var anyCurrentItemCancellation = results.Any(result => result.CancellationScope == CancellationScope.CurrentItem);
        var shouldCloseWindow =
            !anyFailure &&
            (anyGlobalCancellation || anySuccess || !(allCanceled && anyCurrentItemCancellation));

        return new RunActionResult(anyFailure ? 1 : 0, failureMessage, shouldCloseWindow);
    }

    private static int RunQueuedActionWithProgressForm(
        IFrameShiftAction action,
        IReadOnlyList<string> inputPaths,
        IReadOnlyDictionary<string, string> options,
        AppLogger logger,
        string cancellationSourceName)
    {
        using var progressForm = new ProgressForm();
        using var cancellationSource = new CancellationTokenSource();
        progressForm.CancelRequested += (_, _) =>
        {
            logger.Log($"Program: {cancellationSourceName} CancelRequested event received.");
            RequestCancellationAsync(cancellationSource, logger, cancellationSourceName);
        };

        var exitCode = 1;
        string? failureMessage = null;
        var shouldCloseWindow = true;
        progressForm.Shown += async (_, _) =>
        {
            try
            {
                var runResult = await ExecuteQueuedActionAsync(action, inputPaths, options, logger, progressForm, cancellationSource.Token).ConfigureAwait(true);
                exitCode = runResult.ExitCode;
                failureMessage = runResult.FailureMessage;
                shouldCloseWindow = runResult.ShouldCloseWindow;
                if (exitCode != 0 && !string.IsNullOrWhiteSpace(failureMessage))
                {
                    logger.Log($"ACTION FAILED ({action.Descriptor.Id}): {failureMessage}");
                    ShowCliError(failureMessage);
                }
            }
            catch (Exception ex)
            {
                logger.Log(ex.ToString());
                ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.UnhandledError("An unexpected error occurred.")));
                exitCode = 1;
            }
            finally
            {
                if (!progressForm.IsDisposed)
                {
                    if (shouldCloseWindow || cancellationSource.IsCancellationRequested)
                    {
                        progressForm.CloseSafely();
                    }
                    else
                    {
                        progressForm.EnableCloseMode(failureMessage);
                    }
                }
            }
        };

        logger.Log($"Program: before Application.Run ({cancellationSourceName} progress form).");
        Application.Run(progressForm);
        logger.Log($"Program: after Application.Run returns ({cancellationSourceName} progress form).");
        return exitCode;
    }

    private static bool TryParseArguments(
        string[] args,
        out string actionId,
        out List<string> inputPaths,
        out Dictionary<string, string> options,
        out string? error)
    {
        actionId = string.Empty;
        inputPaths = new List<string>();
        options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error = null;

        var actionIndex = Array.IndexOf(args, "--action");
        if (actionIndex < 0 || actionIndex + 1 >= args.Length)
        {
            error = MediaActionMessages.UnsupportedCommandLine("FrameShift.exe --action <id> [--target <format>] [--profile <name>] <input-paths...>");
            return false;
        }

        actionId = args[actionIndex + 1];

        for (var index = actionIndex + 2; index < args.Length; index++)
        {
            var token = args[index];
            if (string.Equals(token, "--target", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.Target] = args[++index];
                continue;
            }

            if (string.Equals(token, "--profile", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.Profile] = args[++index];
                continue;
            }

            if (string.Equals(token, "--stems", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.Stems] = args[++index];
                continue;
            }

            if ((string.Equals(token, "--separate-engine", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token, "--engine", StringComparison.OrdinalIgnoreCase)) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.SeparateEngine] = args[++index];
                continue;
            }

            if (string.Equals(token, "--noise-strength", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.NoiseStrength] = args[++index];
                continue;
            }

            if (string.Equals(token, "--stereo", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.ProcessStereoAudio] = args[++index];
                continue;
            }

            if ((string.Equals(token, "--target-size-bytes", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token, "--size-bytes", StringComparison.OrdinalIgnoreCase)) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.TargetSizeBytes] = args[++index];
                continue;
            }

            if (string.Equals(token, "--start", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.StartTime] = args[++index];
                continue;
            }

            if (string.Equals(token, "--end", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.EndTime] = args[++index];
                continue;
            }

            if (string.Equals(token, "--duration", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.Duration] = args[++index];
                continue;
            }

            if ((string.Equals(token, "--gif-resolution", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token, "--resolution", StringComparison.OrdinalIgnoreCase)) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.GifResolution] = args[++index];
                continue;
            }

            if ((string.Equals(token, "--gif-fps", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token, "--fps", StringComparison.OrdinalIgnoreCase)) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.GifFps] = args[++index];
                continue;
            }

            if ((string.Equals(token, "--gif-quality", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token, "--quality", StringComparison.OrdinalIgnoreCase)) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.GifQuality] = args[++index];
                continue;
            }

            if (string.Equals(token, "--icon-sizes", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.IconSizes] = args[++index];
                continue;
            }

            if (string.Equals(token, "--icon-fit-mode", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.IconFitMode] = args[++index];
                continue;
            }

            if (string.Equals(token, "--icon-background", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.IconBackground] = args[++index];
                continue;
            }

            if (string.Equals(token, "--semitones", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.PitchSemitones] = args[++index];
                continue;
            }

            if (string.Equals(token, "--keep-duration", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.PitchKeepDuration] = args[++index];
                continue;
            }

            if (string.Equals(token, "--start-frame", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.StartFrame] = args[++index];
                continue;
            }

            if (string.Equals(token, "--end-frame", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.EndFrame] = args[++index];
                continue;
            }

            if (string.Equals(token, "--resize-width", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.ResizeWidth] = args[++index];
                continue;
            }

            if (string.Equals(token, "--resize-height", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.ResizeHeight] = args[++index];
                continue;
            }

            if (token.StartsWith("--", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            inputPaths.Add(token);
        }

        NormalizeSplitInputPaths(inputPaths);
        return true;
    }

    private static void NormalizeSplitInputPaths(List<string> inputPaths)
    {
        if (inputPaths.Count <= 1)
        {
            return;
        }

        if (inputPaths.Any(File.Exists))
        {
            return;
        }

        var combinedPath = string.Join(" ", inputPaths);
        if (!File.Exists(combinedPath) && !Directory.Exists(combinedPath))
        {
            return;
        }

        inputPaths.Clear();
        inputPaths.Add(combinedPath);
    }

    private static bool ShouldRunConversionBatch(string actionId, IReadOnlyDictionary<string, string> options)
    {
        if (!options.Any())
        {
            return actionId.Equals("convert-video", StringComparison.OrdinalIgnoreCase) ||
                   actionId.Equals("convert-audio", StringComparison.OrdinalIgnoreCase) ||
                   actionId.Equals("convert-image", StringComparison.OrdinalIgnoreCase) ||
                   actionId.Equals("extract-audio", StringComparison.OrdinalIgnoreCase) ||
                   actionId.Equals("extract-frames", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static ConversionBatchSession.BatchDefinition? GetConversionBatchDefinition(string actionId)
    {
        if (actionId.Equals("convert-video", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateVideoDefinition();
        }

        if (actionId.Equals("convert-audio", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateAudioDefinition();
        }

        if (actionId.Equals("convert-image", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateImageDefinition();
        }

        if (actionId.Equals("extract-audio", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateExtractAudioDefinition();
        }

        if (actionId.Equals("extract-frames", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateExtractFramesDefinition();
        }

        if (actionId.Equals("remove-background", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateRemoveBackgroundDefinition();
        }

        if (actionId.Equals("separate-audio", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateSeparateAudioDefinition();
        }

        if (actionId.Equals("remove-noise", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateRemoveNoiseDefinition();
        }

        if (actionId.Equals("remove-noise-video", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionBatchSession.CreateRemoveNoiseVideoDefinition();
        }

        return null;
    }

    private static bool EnsureConvertVideoOptions(List<string> inputPaths, Dictionary<string, string> options)
    {
        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        return EnsureConversionOptions(
            inputPaths,
            options,
            "FrameShift - Convert Video",
            "Select a target container and a profile.",
            VideoConversionCatalog.GetTargets(),
            VideoConversionCatalog.GetProfiles(),
            true);
    }

    private static bool EnsureConvertAudioOptions(List<string> inputPaths, Dictionary<string, string> options)
    {
        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!AudioConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, AudioConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        return EnsureConversionOptions(
            inputPaths,
            options,
            "FrameShift - Convert Audio",
            "Select an output format and a profile.",
            AudioConversionCatalog.GetTargets(),
            AudioConversionCatalog.GetProfiles(),
            true);
    }

    private static bool EnsureConvertImageOptions(List<string> inputPaths, Dictionary<string, string> options)
    {
        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!ImageConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        return EnsureConversionOptions(
            inputPaths,
            options,
            "FrameShift - Convert Image",
            "Select an output format and a profile.",
            ImageConversionCatalog.GetTargets(),
            ImageConversionCatalog.GetProfiles(),
            true);
    }

    private static bool EnsureExtractAudioOptions(List<string> inputPaths, Dictionary<string, string> options)
    {
        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        return EnsureConversionOptions(
            inputPaths,
            options,
            "FrameShift - Extract Audio",
            "Choose the output audio format.",
            ExtractAudioCatalog.GetTargets(),
            [],
            false,
            "Extract");
    }

    private static bool EnsureCutAudioOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Cut Audio"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
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

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
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

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
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

    private static bool EnsureConversionOptions(
        List<string> inputPaths,
        Dictionary<string, string> options,
        string title,
        string description,
        IReadOnlyList<IConversionChoice> targets,
        IReadOnlyList<IConversionChoice> profiles,
        bool showProfiles,
        string primaryButtonText = "Convert")
    {
        if (options.ContainsKey(ActionOptionKeys.Target) && (!showProfiles || options.ContainsKey(ActionOptionKeys.Profile)))
        {
            return true;
        }

        if (targets.Count == 0)
        {
            ShowCliError(MediaActionMessages.NoAlternativeTargetFormatsAvailable());
            return false;
        }

        var sourceLabel = inputPaths.Count == 1
            ? FormatSelectionLabel(Path.GetFileName(inputPaths[0]))
            : $"{inputPaths.Count} selected files";

        var initialTargetId = options.TryGetValue(ActionOptionKeys.Target, out var targetId) ? targetId : targets[0].Id;
        var initialProfileId = showProfiles && options.TryGetValue(ActionOptionKeys.Profile, out var profileId) ? profileId : profiles.FirstOrDefault()?.Id;

        using var picker = new ConversionPickerForm(
            title,
            sourceLabel,
            description,
            targets,
            profiles,
            initialTargetId,
            initialProfileId,
            primaryButtonText);

        if (picker.ShowDialog() != DialogResult.OK || picker.Selection is null)
        {
            return false;
        }

        options[ActionOptionKeys.Target] = picker.Selection.TargetId;
        if (showProfiles && !string.IsNullOrWhiteSpace(picker.Selection.ProfileId))
        {
            options[ActionOptionKeys.Profile] = picker.Selection.ProfileId!;
        }

        return true;
    }

    private static bool EnsureConvertToIconOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Convert to Icon"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!ImageCropSupport.IsSupportedExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageCropSupport.GetSupportedExtensionsText()));
            return false;
        }

        if (ConvertToIconSettings.ContainsAnyOption(options))
        {
            if (!ConvertToIconSettings.TryFromOptions(options, out _, out var settingsError))
            {
                ShowCliError(settingsError ?? MediaActionMessages.ConvertToIconSettingsInvalid());
                return false;
            }

            return true;
        }

        var toolLocator = new ToolLocator();
        var ffmpegRunner = new FfmpegRunner(logger);
        try
        {
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            string? temporaryPreviewPath = null;
            var previewPath = inputPaths[0];

            if (string.Equals(sourceExtension, ".webp", StringComparison.OrdinalIgnoreCase))
            {
                temporaryPreviewPath = CreateTemporaryIconPreviewPng(ffmpegPath, ffmpegRunner, inputPaths[0]);
                previewPath = temporaryPreviewPath;
            }

            try
            {
                using var picker = new ConvertToIconForm(inputPaths[0], previewPath);
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
            finally
            {
                if (!string.IsNullOrWhiteSpace(temporaryPreviewPath) && File.Exists(temporaryPreviewPath))
                {
                    File.Delete(temporaryPreviewPath);
                }
            }
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Convert to Icon")));
            return false;
        }
    }

    private static bool EnsureCropVideoOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Crop Video"));
            return false;
        }

        if (options.ContainsKey(ActionOptionKeys.CropX) &&
            options.ContainsKey(ActionOptionKeys.CropY) &&
            options.ContainsKey(ActionOptionKeys.CropWidth) &&
            options.ContainsKey(ActionOptionKeys.CropHeight))
        {
            return true;
        }

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

            using var picker = new CropVideoForm(inputPaths[0], ffmpegPath, probe, ffmpegRunner);
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
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Crop Video")));
            return false;
        }
    }

    private static bool EnsureCropImageOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Crop Image"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!ImageCropSupport.IsSupportedExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageCropSupport.GetSupportedExtensionsText()));
            return false;
        }

        if (options.ContainsKey(ActionOptionKeys.CropX) &&
            options.ContainsKey(ActionOptionKeys.CropY) &&
            options.ContainsKey(ActionOptionKeys.CropWidth) &&
            options.ContainsKey(ActionOptionKeys.CropHeight))
        {
            return true;
        }

        var toolLocator = new ToolLocator();
        var ffmpegRunner = new FfmpegRunner(logger);
        try
        {
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            using var picker = new CropImageForm(inputPaths[0], ffmpegPath, ffmpegRunner);
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
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Crop Image")));
            return false;
        }
    }

    private static bool EnsureRotateFlipVideoOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Rotate / Flip Video"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        if (RotateFlipSettings.ContainsAnyOption(options))
        {
            if (!RotateFlipSettings.TryFromOptions(options, out _, out var settingsError))
            {
                ShowCliError(settingsError ?? MediaActionMessages.RotateFlipSettingsMissing());
                return false;
            }

            return true;
        }

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

            using var picker = new RotateFlipVideoForm(inputPaths[0], ffmpegPath, probe, ffmpegRunner);
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
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Rotate / Flip Video")));
            return false;
        }
    }

    private static bool EnsureRotateFlipImageOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Rotate / Flip Image"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!ImageCropSupport.IsSupportedExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageCropSupport.GetSupportedExtensionsText()));
            return false;
        }

        if (RotateFlipSettings.ContainsAnyOption(options))
        {
            if (!RotateFlipSettings.TryFromOptions(options, out _, out var settingsError))
            {
                ShowCliError(settingsError ?? MediaActionMessages.RotateFlipSettingsMissing());
                return false;
            }

            return true;
        }

        var toolLocator = new ToolLocator();
        var ffmpegRunner = new FfmpegRunner(logger);
        try
        {
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            using var picker = new RotateFlipImageForm(inputPaths[0], ffmpegPath, ffmpegRunner);
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
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Rotate / Flip Image")));
            return false;
        }
    }

    private static bool EnsureResizeVideoOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Resize Video"));
            return false;
        }

        if (options.ContainsKey(ActionOptionKeys.ResizeWidth) &&
            options.ContainsKey(ActionOptionKeys.ResizeHeight))
        {
            return true;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
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

            if (probe.VideoWidth <= 0 || probe.VideoHeight <= 0)
            {
                ShowCliError(MediaActionMessages.VideoResolutionUnavailable());
                return false;
            }

            using var picker = new ResizeVideoForm(inputPaths[0], probe.VideoWidth, probe.VideoHeight);
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
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Resize Video")));
            return false;
        }
    }

    private static bool EnsureCompressVideoOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Compress Video"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!VideoCompressionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoCompressionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        if (options.TryGetValue(ActionOptionKeys.Profile, out var profileId))
        {
            if (!VideoCompressionCatalog.IsSupportedProfileId(profileId))
            {
                ShowCliError(MediaActionMessages.UnsupportedCompressionProfile(profileId));
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

            var sourceBytes = new FileInfo(inputPaths[0]).Length;
            var resolutionText = probe.VideoWidth > 0 && probe.VideoHeight > 0
                ? $"{probe.VideoWidth}x{probe.VideoHeight}"
                : "unknown";

            using var picker = new CompressVideoForm(inputPaths[0], sourceExtension, sourceBytes, resolutionText);
            if (picker.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(picker.SelectedProfileId))
            {
                return false;
            }

            options[ActionOptionKeys.Profile] = picker.SelectedProfileId!;
            if (picker.TargetBytes is long targetBytes && targetBytes > 0)
            {
                options[ActionOptionKeys.TargetSizeBytes] = targetBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Compress Video")));
            return false;
        }
    }

    private static bool EnsureCompressAudioOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Compress Audio"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!AudioConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, AudioConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        if (options.TryGetValue(ActionOptionKeys.Profile, out var profileId))
        {
            if (profileId is not (CompressAudioSettings.ProfileHigh or CompressAudioSettings.ProfileBalanced or CompressAudioSettings.ProfileSmall))
            {
                ShowCliError(MediaActionMessages.UnsupportedCompressionProfile(profileId));
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
            if (!probe.HasAudio)
            {
                ShowCliError(MediaActionMessages.MissingAudioTrack());
                return false;
            }

            var sourceBytes = new FileInfo(inputPaths[0]).Length;

            using var picker = new CompressAudioForm(inputPaths[0], sourceExtension, sourceBytes, probe.PrimaryAudioSampleRate, probe.PrimaryAudioChannels);
            if (picker.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(picker.SelectedProfileId))
            {
                return false;
            }

            options[ActionOptionKeys.Profile] = picker.SelectedProfileId!;
            if (picker.TargetBytes is long targetBytes && targetBytes > 0)
            {
                options[ActionOptionKeys.TargetSizeBytes] = targetBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Compress Audio")));
            return false;
        }
    }

    private static bool EnsureCompressImageOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Compress Image"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!ImageConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        if (options.ContainsKey(ActionOptionKeys.Profile) ||
            options.ContainsKey(ActionOptionKeys.Target) ||
            options.ContainsKey(ActionOptionKeys.TargetSizeBytes))
        {
            if (!CompressImageSettings.TryFromOptions(options, out _, out var settingsError))
            {
                ShowCliError(settingsError ?? MediaActionMessages.CompressImageSettingsInvalid());
                return false;
            }

            return true;
        }

        try
        {
            var sourceBytes = new FileInfo(inputPaths[0]).Length;
            using var picker = new CompressImageForm(inputPaths[0], sourceExtension, sourceBytes);
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
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Compress Image")));
            return false;
        }
    }

    private static bool EnsureResizeImageOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Resize Image"));
            return false;
        }

        if (options.ContainsKey(ActionOptionKeys.ResizeWidth) &&
            options.ContainsKey(ActionOptionKeys.ResizeHeight))
        {
            return true;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!ImageCropSupport.IsSupportedExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageCropSupport.GetSupportedExtensionsText()));
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

            if (probe.VideoWidth <= 0 || probe.VideoHeight <= 0)
            {
                ShowCliError(MediaActionMessages.VideoResolutionUnavailable());
                return false;
            }

            using var picker = new ResizeImageForm(inputPaths[0], probe.VideoWidth, probe.VideoHeight);
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
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Resize Image")));
            return false;
        }
    }

    private static bool EnsureChangePitchOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Change Pitch"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
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

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
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

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
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

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
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

    private static bool EnsureImageToPdfOptions(List<string> inputPaths, Dictionary<string, string> options)
    {
        if (inputPaths.Count == 0)
        {
            ShowCliError(MediaActionMessages.InputPathRequired());
            return false;
        }

        foreach (var inputPath in inputPaths)
        {
            var sourceExtension = Path.GetExtension(inputPath).ToLowerInvariant();
            if (sourceExtension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp"))
            {
                ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageCropSupport.GetSupportedExtensionsText()));
                return false;
            }
        }

        if (options.ContainsKey(ActionOptionKeys.ImageToPdfSettings))
        {
            return true;
        }

        try
        {
            var toolLocator = new ToolLocator();
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            using var form = new ImageToPdfForm(inputPaths, ffmpegPath, new FfmpegRunner(new AppLogger()));
            if (form.ShowDialog() != DialogResult.OK || form.Settings is null)
            {
                return false;
            }

            options[ActionOptionKeys.ImageToPdfSettings] = form.Settings.ToOptionPayload();
            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Image to PDF")));
            return false;
        }
    }

    private static string CreateTemporaryIconPreviewPng(string ffmpegPath, FfmpegRunner ffmpegRunner, string inputPath)
    {
        var previewPath = Path.Combine(Path.GetTempPath(), $"frameshift_icon_preview_{Guid.NewGuid():N}.png");
        var result = ffmpegRunner.RunCaptureAsync(
            ffmpegPath,
            [
                "-hide_banner",
                "-loglevel", "error",
                "-y",
                "-i", inputPath,
                "-frames:v", "1",
                "-update", "1",
                previewPath
            ],
            CancellationToken.None).GetAwaiter().GetResult();

        if (!result.Success || !File.Exists(previewPath))
        {
            if (File.Exists(previewPath))
            {
                File.Delete(previewPath);
            }

            throw new InvalidOperationException(ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, MediaActionMessages.ConvertToIconFailed()));
        }

        return previewPath;
    }

    private static void ShowCliError(string message)
    {
        MessageBox.Show(
            message,
            "FrameShift",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static string FormatSelectionLabel(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "selected file";
        }

        const int maxLength = 72;
        if (fileName.Length <= maxLength)
        {
            return fileName;
        }

        var prefixLength = 34;
        var suffixLength = 30;
        return $"{fileName[..prefixLength]}...{fileName[^suffixLength..]}";
    }

    private static void RequestCancellationAsync(CancellationTokenSource cancellationSource, AppLogger logger, string source)
    {
        _ = Task.Run(() =>
        {
            try
            {
                logger.Log($"Program: cancellationSource.Cancel started ({source}).");
                cancellationSource.Cancel();
                logger.Log($"Program: cancellationSource.Cancel finished ({source}).");
            }
            catch (ObjectDisposedException)
            {
                logger.Log($"Program: cancellationSource.Cancel hit disposed source ({source}).");
            }
            catch (Exception ex)
            {
                logger.Log($"Program: cancellationSource.Cancel failed ({source}): {ex}");
            }
        });
    }

    private sealed record RunActionResult(int ExitCode, string? FailureMessage, bool ShouldCloseWindow);
}
