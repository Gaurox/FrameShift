using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Core.Progress;

namespace FrameShift.Core.Actions;

public sealed class ExtractFramesAction : IFrameShiftAction
{
    private static readonly (TimeSpan? SearchWindow, int ProgressValue, string Message)[] s_lastFrameAttempts =
    [
        (TimeSpan.FromSeconds(10), 200, "Searching near the end..."),
        (TimeSpan.FromSeconds(60), 450, "Expanding the end search..."),
        (TimeSpan.FromSeconds(300), 600, "Expanding the end search..."),
        (null, 700, "Decoding from the beginning...")
    ];

    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public ExtractFramesAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "extract-frames",
        "Extract Frames",
        "Extracts all video frames into a new adjacent PNG folder.");

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

        var sourceExtension = Path.GetExtension(request.InputPath).ToLowerInvariant();
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
        }

        if (!ExtractFramesSettings.TryFromOptions(request.Options, out var settings, out var settingsError))
        {
            request.ProgressReporter?.ReportState("failed", settingsError);
            return new ActionExecutionResult(false, settingsError!);
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

        return settings!.Mode switch
        {
            ExtractFramesMode.All => await ExecuteAllAsync(request, ffmpegPath, probe, cancellationToken).ConfigureAwait(false),
            ExtractFramesMode.First => await ExecuteFirstFrameAsync(request, ffmpegPath, probe, cancellationToken).ConfigureAwait(false),
            ExtractFramesMode.Last => await ExecuteLastFrameAsync(request, ffmpegPath, cancellationToken).ConfigureAwait(false),
            ExtractFramesMode.Keyframes => await ExecuteKeyframesAsync(request, ffmpegPath, probe, cancellationToken).ConfigureAwait(false),
            _ => new ActionExecutionResult(false, MediaActionMessages.FrameExtractionModeInvalid())
        };
    }

    private async Task<ActionExecutionResult> ExecuteAllAsync(
        ActionRequest request,
        string ffmpegPath,
        MediaProbeResult probe,
        CancellationToken cancellationToken)
    {
        var outputDirectory = OutputPathHelper.CreateUniqueOutputDirectoryPath(request.InputPath, "_frames");
        var outputDirectoryCreated = false;
        var inputBaseName = Path.GetFileNameWithoutExtension(request.InputPath);
        var outputPattern = Path.Combine(outputDirectory, $"{inputBaseName}_%06d.png");

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.Logger.Log($"Extract frames output directory: {outputDirectory}");
        request.ProgressReporter?.ReportState("processing", "Preparing frame extraction on CPU...");

        try
        {
            Directory.CreateDirectory(outputDirectory);
            outputDirectoryCreated = true;

            var arguments = BuildAllArguments(request.InputPath, outputPattern);
            var runResult = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                probe.Duration,
                probe.EstimatedVideoFrameCount,
                request.ProgressReporter,
                request.InputPath,
                Descriptor.DisplayName,
                "CPU",
                cancellationToken).ConfigureAwait(false);

            if (runResult.Canceled)
            {
                CleanupOutputDirectory(outputDirectory, outputDirectoryCreated, request.Logger);
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, runResult.CancellationScope);
            }

            var extractedFrameCount = CountExtractedPngFiles(outputDirectory);
            if (runResult.ExitCode != 0 || extractedFrameCount == 0)
            {
                CleanupOutputDirectory(outputDirectory, outputDirectoryCreated, request.Logger);
                var failureMessage = runResult.ExitCode != 0
                    ? ConversionActionHelper.GetFriendlyFfmpegError(runResult.StandardError, MediaActionMessages.FrameExtractionFailure())
                    : MediaActionMessages.FrameExtractionProducedNoFiles();
                request.ProgressReporter?.ReportState("failed", failureMessage);
                return new ActionExecutionResult(false, failureMessage, null, false);
            }

            request.Logger.Log($"Extract frames completed. frameCount={extractedFrameCount}, outputDirectory={outputDirectory}");
            request.ProgressReporter?.ReportProgress(1000, request.InputPath, Descriptor.DisplayName, "Completed.");
            request.ProgressReporter?.ReportState("done", "Completed.");
            return new ActionExecutionResult(true, MediaActionMessages.Completed(Descriptor.DisplayName), outputDirectory);
        }
        catch (OperationCanceledException)
        {
            CleanupOutputDirectory(outputDirectory, outputDirectoryCreated, request.Logger);
            request.ProgressReporter?.ReportState("canceled", "Canceled.");
            return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, CancellationScope.All);
        }
        catch (Exception ex)
        {
            CleanupOutputDirectory(outputDirectory, outputDirectoryCreated, request.Logger);
            var failureMessage = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed(Descriptor.DisplayName));
            request.ProgressReporter?.ReportState("failed", failureMessage);
            return new ActionExecutionResult(false, failureMessage);
        }
    }

    private async Task<ActionExecutionResult> ExecuteFirstFrameAsync(
        ActionRequest request,
        string ffmpegPath,
        MediaProbeResult probe,
        CancellationToken cancellationToken)
    {
        const string actionName = "Extract First Frame";
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, "_first_frame", ".png");

        request.Logger.Log($"Extract first frame output path: {outputPath}");
        request.ProgressReporter?.ReportState("processing", "Extracting the first frame...");

        try
        {
            var runResult = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                BuildFirstFrameArguments(request.InputPath, outputPath),
                probe.Duration,
                null,
                request.ProgressReporter,
                request.InputPath,
                actionName,
                "CPU",
                cancellationToken).ConfigureAwait(false);

            if (runResult.Canceled)
            {
                CleanupOutputFile(outputPath, request.Logger);
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(actionName), null, true, runResult.CancellationScope);
            }

            if (runResult.ExitCode != 0 || !HasOutputFile(outputPath))
            {
                CleanupOutputFile(outputPath, request.Logger);
                var failureMessage = runResult.ExitCode != 0
                    ? ConversionActionHelper.GetFriendlyFfmpegError(runResult.StandardError, MediaActionMessages.FrameExtractionFailure())
                    : MediaActionMessages.FrameExtractionProducedNoVideoFrame();
                request.ProgressReporter?.ReportState("failed", failureMessage);
                return new ActionExecutionResult(false, failureMessage);
            }

            request.ProgressReporter?.ReportProgress(1000, request.InputPath, actionName, "Completed.");
            request.ProgressReporter?.ReportState("done", "Completed.");
            return new ActionExecutionResult(true, MediaActionMessages.Completed(actionName), outputPath);
        }
        catch (OperationCanceledException)
        {
            CleanupOutputFile(outputPath, request.Logger);
            request.ProgressReporter?.ReportState("canceled", "Canceled.");
            return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(actionName), null, true, CancellationScope.All);
        }
        catch (Exception ex)
        {
            CleanupOutputFile(outputPath, request.Logger);
            var failureMessage = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed(actionName));
            request.ProgressReporter?.ReportState("failed", failureMessage);
            return new ActionExecutionResult(false, failureMessage);
        }
    }

    private async Task<ActionExecutionResult> ExecuteLastFrameAsync(
        ActionRequest request,
        string ffmpegPath,
        CancellationToken cancellationToken)
    {
        const string actionName = "Extract Last Frame";
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, "_last_frame", ".png");
        var temporaryOutputPath = CreateTemporaryOutputPath(outputPath);
        var progressReporter = request.ProgressReporter is null ? null : new LastFrameProgressReporter(request.ProgressReporter);

        request.Logger.Log($"Extract last frame output path: {outputPath}");
        request.ProgressReporter?.ReportState("processing", "Searching for the last frame...");
        request.ProgressReporter?.ReportProgress(50, request.InputPath, actionName, "Preparing last-frame extraction...");

        try
        {
            FfmpegRunResult? lastRunResult = null;

            foreach (var attempt in s_lastFrameAttempts)
            {
                request.ProgressReporter?.ReportProgress(attempt.ProgressValue, request.InputPath, actionName, attempt.Message);
                lastRunResult = await _ffmpegRunner.RunAsync(
                    ffmpegPath,
                    BuildLastFrameArguments(request.InputPath, temporaryOutputPath, attempt.SearchWindow),
                    null,
                    null,
                    progressReporter,
                    request.InputPath,
                    actionName,
                    "CPU",
                    cancellationToken).ConfigureAwait(false);

                if (lastRunResult.Canceled)
                {
                    CleanupOutputFile(temporaryOutputPath, request.Logger);
                    request.ProgressReporter?.ReportState("canceled", "Canceled.");
                    return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(actionName), null, true, lastRunResult.CancellationScope);
                }

                if (lastRunResult.ExitCode == 0 && HasOutputFile(temporaryOutputPath))
                {
                    File.Move(temporaryOutputPath, outputPath);
                    request.ProgressReporter?.ReportProgress(1000, request.InputPath, actionName, "Completed.");
                    request.ProgressReporter?.ReportState("done", "Completed.");
                    return new ActionExecutionResult(true, MediaActionMessages.Completed(actionName), outputPath);
                }

                CleanupOutputFile(temporaryOutputPath, request.Logger);
            }

            var failureMessage = lastRunResult is not null && lastRunResult.ExitCode != 0
                ? ConversionActionHelper.GetFriendlyFfmpegError(lastRunResult.StandardError, MediaActionMessages.FrameExtractionFailure())
                : MediaActionMessages.FrameExtractionProducedNoVideoFrame();
            request.ProgressReporter?.ReportState("failed", failureMessage);
            return new ActionExecutionResult(false, failureMessage);
        }
        catch (OperationCanceledException)
        {
            CleanupOutputFile(temporaryOutputPath, request.Logger);
            request.ProgressReporter?.ReportState("canceled", "Canceled.");
            return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(actionName), null, true, CancellationScope.All);
        }
        catch (Exception ex)
        {
            CleanupOutputFile(temporaryOutputPath, request.Logger);
            var failureMessage = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed(actionName));
            request.ProgressReporter?.ReportState("failed", failureMessage);
            return new ActionExecutionResult(false, failureMessage);
        }
        finally
        {
            CleanupOutputFile(temporaryOutputPath, request.Logger);
        }
    }

    private async Task<ActionExecutionResult> ExecuteKeyframesAsync(
        ActionRequest request,
        string ffmpegPath,
        MediaProbeResult probe,
        CancellationToken cancellationToken)
    {
        const string actionName = "Extract Keyframes";
        var outputDirectory = OutputPathHelper.CreateUniqueOutputDirectoryPath(request.InputPath, "_keyframes");
        var outputDirectoryCreated = false;
        var outputPattern = Path.Combine(outputDirectory, "keyframe_%06d.png");

        request.Logger.Log($"Extract keyframes output directory: {outputDirectory}");
        request.ProgressReporter?.ReportState("processing", "Extracting keyframes...");

        try
        {
            Directory.CreateDirectory(outputDirectory);
            outputDirectoryCreated = true;

            var runResult = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                BuildKeyframesArguments(request.InputPath, outputPattern),
                probe.Duration,
                null,
                request.ProgressReporter,
                request.InputPath,
                actionName,
                "CPU",
                cancellationToken).ConfigureAwait(false);

            if (runResult.Canceled)
            {
                CleanupOutputDirectory(outputDirectory, outputDirectoryCreated, request.Logger);
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(actionName), null, true, runResult.CancellationScope);
            }

            var extractedFrameCount = CountExtractedPngFiles(outputDirectory);
            if (runResult.ExitCode != 0 || extractedFrameCount == 0)
            {
                CleanupOutputDirectory(outputDirectory, outputDirectoryCreated, request.Logger);
                var failureMessage = runResult.ExitCode != 0
                    ? ConversionActionHelper.GetFriendlyFfmpegError(runResult.StandardError, MediaActionMessages.FrameExtractionFailure())
                    : MediaActionMessages.FrameExtractionProducedNoKeyframes();
                request.ProgressReporter?.ReportState("failed", failureMessage);
                return new ActionExecutionResult(false, failureMessage);
            }

            request.ProgressReporter?.ReportProgress(1000, request.InputPath, actionName, "Completed.");
            request.ProgressReporter?.ReportState("done", "Completed.");
            return new ActionExecutionResult(true, MediaActionMessages.Completed(actionName), outputDirectory);
        }
        catch (OperationCanceledException)
        {
            CleanupOutputDirectory(outputDirectory, outputDirectoryCreated, request.Logger);
            request.ProgressReporter?.ReportState("canceled", "Canceled.");
            return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(actionName), null, true, CancellationScope.All);
        }
        catch (Exception ex)
        {
            CleanupOutputDirectory(outputDirectory, outputDirectoryCreated, request.Logger);
            var failureMessage = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed(actionName));
            request.ProgressReporter?.ReportState("failed", failureMessage);
            return new ActionExecutionResult(false, failureMessage);
        }
    }

    // Keep every decoded frame without relying on the removed FFmpeg -vsync option.
    internal static IReadOnlyList<string> BuildAllArguments(string inputPath, string outputPattern)
    {
        return
        [
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-i", inputPath,
            "-map", "0:v:0?",
            "-an",
            "-sn",
            "-dn",
            "-fps_mode", "passthrough",
            "-c:v", "png",
            "-compression_level", "6",
            outputPattern
        ];
    }

    internal static IReadOnlyList<string> BuildFirstFrameArguments(string inputPath, string outputPath)
    {
        return
        [
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-i", inputPath,
            "-map", "0:v:0",
            "-an",
            "-sn",
            "-dn",
            "-frames:v", "1",
            "-c:v", "png",
            "-compression_level", "6",
            outputPath
        ];
    }

    internal static IReadOnlyList<string> BuildLastFrameArguments(string inputPath, string temporaryOutputPath, TimeSpan? searchWindow)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y"
        };

        if (searchWindow is not null)
        {
            arguments.Add("-sseof");
            arguments.Add($"-{searchWindow.Value.TotalSeconds:0.###}");
        }

        arguments.AddRange(
        [
            "-i", inputPath,
            "-map", "0:v:0",
            "-an",
            "-sn",
            "-dn",
            "-fps_mode", "passthrough",
            "-c:v", "png",
            "-compression_level", "6",
            "-f", "image2",
            "-update", "1",
            temporaryOutputPath
        ]);

        return arguments;
    }

    internal static IReadOnlyList<string> BuildKeyframesArguments(string inputPath, string outputPattern)
    {
        return
        [
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-skip_frame", "nokey",
            "-i", inputPath,
            "-map", "0:v:0",
            "-an",
            "-sn",
            "-dn",
            "-vf", "select=eq(key\\,1)",
            "-fps_mode", "passthrough",
            "-c:v", "png",
            "-compression_level", "6",
            outputPattern
        ];
    }

    private static string CreateTemporaryOutputPath(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp.png");
    }

    private static bool HasOutputFile(string outputPath)
    {
        return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
    }

    private static int CountExtractedPngFiles(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return 0;
        }

        return Directory.GetFiles(outputDirectory, "*.png", SearchOption.TopDirectoryOnly).Length;
    }

    private static void CleanupOutputFile(string outputPath, AppLogger logger)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
        {
            return;
        }

        try
        {
            File.Delete(outputPath);
            logger.Log($"Extract frames cleanup removed file: {outputPath}");
        }
        catch (Exception ex)
        {
            logger.Log($"Extract frames cleanup could not remove file '{outputPath}': {ex}");
        }
    }

    private static void CleanupOutputDirectory(string outputDirectory, bool outputDirectoryCreated, AppLogger logger)
    {
        if (!outputDirectoryCreated || string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(outputDirectory, recursive: true);
            logger.Log($"Extract frames cleanup removed directory: {outputDirectory}");
        }
        catch (Exception ex)
        {
            logger.Log($"Extract frames cleanup could not remove directory '{outputDirectory}': {ex}");
        }
    }

    private sealed class LastFrameProgressReporter : IProgressReporter
    {
        private readonly IProgressReporter _inner;

        public LastFrameProgressReporter(IProgressReporter inner)
        {
            _inner = inner;
        }

        public bool IsCancellationRequested => _inner.IsCancellationRequested;

        public bool IsQueueItemRemovalRequested(string inputPath) => _inner.IsQueueItemRemovalRequested(inputPath);

        public bool IsQueueItemCancellationRequested(string inputPath) => _inner.IsQueueItemCancellationRequested(inputPath);

        public void ReportQueue(IReadOnlyList<string> items) => _inner.ReportQueue(items);

        public void ReportProgress(int progressValue, string? currentFile, string? currentAction, string? message, string? etaText = null)
        {
        }

        public void ReportState(string state, string? message = null)
        {
        }

        public void ReportQueueItem(string currentFile, string state, string? message = null) => _inner.ReportQueueItem(currentFile, state, message);
    }
}
