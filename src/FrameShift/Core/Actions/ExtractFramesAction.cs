using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;

namespace FrameShift.Core.Actions;

public sealed class ExtractFramesAction : IFrameShiftAction
{
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

            var arguments = BuildArguments(request.InputPath, outputPattern);
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

    private static IReadOnlyList<string> BuildArguments(string inputPath, string outputPattern)
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
            "-vsync", "0",
            "-c:v", "png",
            "-compression_level", "6",
            outputPattern
        ];
    }

    private static int CountExtractedPngFiles(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return 0;
        }

        return Directory.GetFiles(outputDirectory, "*.png", SearchOption.TopDirectoryOnly).Length;
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
}
