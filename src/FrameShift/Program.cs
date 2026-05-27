using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Windows.Forms;

namespace FrameShift;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        AppLogger.LogStatic($"DIAG BUILD ACTIVE - log path = {AppLogger.LogPath}");
        AppLogger.LogStatic($"Program: Main entered. baseDirectory={AppContext.BaseDirectory}, processPath={Environment.ProcessPath ?? "<null>"}, argsCount={args.Length}.");
        var logger = new AppLogger();
        var registry = ActionRegistry.CreateDefault();

        ApplicationConfiguration.Initialize();

        if (TryGetUiStartupPaths(args, out var startupPaths))
        {
            Application.Run(new MainForm(startupPaths));
            return 0;
        }

        if (args.Length > 0)
        {
            return RunCli(args, registry, logger);
        }

        Application.Run(new MainForm());
        return 0;
    }

    private static bool TryGetUiStartupPaths(string[] args, out List<string> startupPaths)
    {
        startupPaths = new List<string>();
        if (args.Length == 0 || args.Any(arg => arg.StartsWith("--", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        startupPaths.AddRange(args.Where(arg => !string.IsNullOrWhiteSpace(arg)));
        NormalizeSplitInputPaths(startupPaths);
        if (startupPaths.Count == 0)
        {
            return false;
        }

        return startupPaths.All(path => File.Exists(path) || Directory.Exists(path));
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

        if (actionId.Equals("interpolate-video-rife", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnsureRifeInterpolateVideoOptions(inputPaths, options, logger))
            {
                return 0;
            }

            if (!EnsureRifeInterpolateModelReady(logger, options))
            {
                logger.Log("Program: RIFE Interpolate Video preflight exited before progress window opened.");
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
}
