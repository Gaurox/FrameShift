using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.AI.VideoInterpolation;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class RifeInterpolateVideoAction : IFrameShiftAction
{
    private const string TempFrameExtension = ".bmp";
    private const string TempFramePattern = "%08d.bmp";
    private const int FallbackSampleRate = 44100;

    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public RifeInterpolateVideoAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "interpolate-video-rife",
        "Interpolate Video (RIFE)",
        "Interpolates video frames with the RIFE AI model.");

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

        if (!RifeInterpolateVideoSettings.TryFromOptions(request.Options, out var settings, out var settingsError) || settings is null)
        {
            return new ActionExecutionResult(false, settingsError ?? MediaActionMessages.RifeInterpolateVideoSettingsMissing());
        }

        var model = RifeModelCatalog.GetById(settings.ModelId);
        if (model is null)
        {
            return new ActionExecutionResult(false, MediaActionMessages.RifeInterpolateVideoModelMissing());
        }

        if (Array.IndexOf(model.SupportedMultipliers, settings.TargetMultiplier) < 0)
        {
            return new ActionExecutionResult(false, MediaActionMessages.RifeInterpolateVideoSettingsInvalid());
        }

        var modelPath = RifeModelLocator.GetModelPath(model);
        if (!RifeModelDownloader.IsModelFileValid(modelPath, model))
        {
            return new ActionExecutionResult(
                false,
                $"AI model not found or invalid. Run FrameShift.exe --action {Descriptor.Id} from Explorer to verify and download it first.");
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

        if (probe.VideoFrameRate is null || probe.VideoFrameRate <= 0)
        {
            request.ProgressReporter?.ReportState("failed", MediaActionMessages.InterpolateVideoFrameRateUnavailable());
            return new ActionExecutionResult(false, MediaActionMessages.InterpolateVideoFrameRateUnavailable());
        }

        if (probe.Duration is null || probe.Duration.Value.TotalSeconds <= 0)
        {
            request.ProgressReporter?.ReportState("failed", MediaActionMessages.DurationUnavailable());
            return new ActionExecutionResult(false, MediaActionMessages.DurationUnavailable());
        }

        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, settings.OutputSuffix, sourceExtension);
        var requestedPipeline = ConversionActionHelper.GetOption(request, ActionOptionKeys.InterpolatePipeline, "auto");
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "FrameShift",
            "RifeInterpolation",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        var extractedFramesDirectory = Path.Combine(tempRoot, "input");
        var interpolationWorkingDirectory = Path.Combine(tempRoot, "passes");
        var metrics = new RifePerformanceMetrics
        {
            TempFrameFormat = "BMP",
            CropMode = "Implicit crop during tensor -> image conversion",
            TempFrameExtension = TempFrameExtension,
            PipelineDescription = "Video -> FFmpeg BMP extraction on disk -> ImageSharp frame reads -> float tensors -> ONNX -> ImageSharp output image -> BMP output frames on disk -> FFmpeg reconstruction"
        };
        var totalStopwatch = Stopwatch.StartNew();

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.Logger.Log($"RIFE model: {model.DisplayName}");
        request.Logger.Log($"Target multiplier: x{settings.TargetMultiplier}");
        request.Logger.Log($"Playback mode divisor: x{settings.PlaybackDivisor}");
        request.Logger.Log($"Slow motion audio: preserveAudio={settings.PreservesAudio}, keepPitch={settings.KeepPitch}");
        request.Logger.Log($"Requested pipeline: {requestedPipeline}");

        try
        {
            var nvencAvailable = await _ffmpegRunner.IsEncoderAvailableAsync(ffmpegPath, "h264_nvenc", cancellationToken).ConfigureAwait(false);
            var encodePlan = BuildEncodePlan(sourceExtension, nvencAvailable);

            var pipelineMode = ResolvePipelineMode(requestedPipeline, settings.TargetMultiplier);
            if (pipelineMode == "rawvideo")
            {
                try
                {
                    request.ProgressReporter?.ReportProgress(100, request.InputPath, Descriptor.DisplayName, "Starting rawvideo RIFE pipeline...");
                    request.ProgressReporter?.ReportState("processing", "Starting rawvideo RIFE pipeline...");

                    var rawProgress = new Progress<(int Processed, int Total)>(p =>
                    {
                        var fraction = Math.Clamp(p.Processed / (double)Math.Max(1, p.Total), 0d, 1d);
                        var permille = 100 + (int)Math.Round(fraction * 900d);
                        request.ProgressReporter?.ReportProgress(permille, request.InputPath, Descriptor.DisplayName,
                            $"Interpolating frames... {p.Processed}/{p.Total}");
                        request.ProgressReporter?.ReportState("processing", $"Interpolating frames... {p.Processed}/{p.Total}");
                    });

                    var rawPipeline = new RifeRawVideoPipeline(_ffmpegRunner, request.Logger);
                    var rawResult = await rawPipeline.RunAsync(
                        ffmpegPath,
                        ffprobePath,
                        request.InputPath,
                        outputPath,
                        modelPath,
                        probe,
                        settings,
                        probe.HasAudio,
                        probe.PrimaryAudioSampleRate > 0 ? probe.PrimaryAudioSampleRate : FallbackSampleRate,
                        encodePlan.ModeLabel,
                        encodePlan.VideoCodec,
                        encodePlan.VideoArgs,
                        rawProgress,
                        cancellationToken).ConfigureAwait(false);

                    request.Logger.Log(rawResult.Report);
                    WritePerformanceReport(rawResult.Report, "RifeRawVideoPerformanceReport_latest.txt");
                    request.ProgressReporter?.ReportProgress(1000, request.InputPath, Descriptor.DisplayName, "Completed.");
                    request.ProgressReporter?.ReportState("done", "Completed.");
                    return new ActionExecutionResult(true, MediaActionMessages.Completed(Descriptor.DisplayName), outputPath);
                }
                catch (Exception ex) when (!string.Equals(requestedPipeline, "rawvideo", StringComparison.OrdinalIgnoreCase))
                {
                    ConversionActionHelper.DeleteIfExists(outputPath);
                    request.Logger.Log($"Rawvideo pipeline failed in auto mode. Falling back to BMP pipeline. {ex.Message}");
                    request.ProgressReporter?.ReportState("processing", "Rawvideo pipeline unavailable. Falling back to BMP...");
                }
            }

            Directory.CreateDirectory(extractedFramesDirectory);
            Directory.CreateDirectory(interpolationWorkingDirectory);

            request.ProgressReporter?.ReportProgress(20, request.InputPath, Descriptor.DisplayName, "Extracting source frames...");
            request.ProgressReporter?.ReportState("processing", "Extracting source frames...");

            var extractArguments = BuildExtractArguments(request.InputPath, Path.Combine(extractedFramesDirectory, TempFramePattern));
            var extractionStopwatch = Stopwatch.StartNew();
            var extractionResult = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                extractArguments,
                probe.Duration,
                probe.EstimatedVideoFrameCount,
                null,
                request.InputPath,
                Descriptor.DisplayName,
                "CPU",
                cancellationToken).ConfigureAwait(false);
            extractionStopwatch.Stop();
            metrics.AddExtraction(extractionStopwatch.Elapsed);

            if (extractionResult.Canceled)
            {
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, extractionResult.CancellationScope);
            }

            var extractedFrameCount = Directory.GetFiles(extractedFramesDirectory, $"*{TempFrameExtension}", SearchOption.TopDirectoryOnly).Length;
            if (extractionResult.ExitCode != 0 || extractedFrameCount == 0)
            {
                var failureMessage = extractionResult.ExitCode != 0
                    ? ConversionActionHelper.GetFriendlyFfmpegError(extractionResult.StandardError, MediaActionMessages.FrameExtractionFailure())
                    : MediaActionMessages.FrameExtractionProducedNoFiles();
                request.ProgressReporter?.ReportState("failed", failureMessage);
                return new ActionExecutionResult(false, failureMessage);
            }

            request.ProgressReporter?.ReportProgress(120, request.InputPath, Descriptor.DisplayName, "Loading AI model...");
            request.ProgressReporter?.ReportState("processing", "Loading AI model...");
            metrics.SetExtractedFrames(extractedFramesDirectory);

            int encodedFrameCount;
            string finalFramesDirectory;
            string provider;
            using (var engine = new RifeFrameInterpolationEngine(modelPath, metrics))
            {
                encodedFrameCount = await Task.Run(() =>
                {
                    return engine.InterpolateMultiplePassesAsync(
                        extractedFramesDirectory,
                        interpolationWorkingDirectory,
                        settings.TargetMultiplier,
                        new Progress<RifeInterpolationProgress>(progress =>
                        {
                            var totalPairs = Math.Max(1, progress.TotalPairs);
                            var totalPasses = Math.Max(1, progress.PassCount);
                            var completedPasses = Math.Clamp(progress.PassIndex - 1, 0, totalPasses - 1);
                            var passFraction = Math.Clamp(progress.ProcessedPairs / (double)totalPairs, 0d, 1d);
                            var overallFraction = Math.Clamp((completedPasses + passFraction) / totalPasses, 0d, 1d);
                            var permille = 150 + (int)Math.Round(overallFraction * 700d, MidpointRounding.AwayFromZero);
                            request.ProgressReporter?.ReportProgress(
                                permille,
                                request.InputPath,
                                Descriptor.DisplayName,
                                progress.Status);
                            request.ProgressReporter?.ReportState("processing", progress.Status);
                        }),
                        cancellationToken);
                }, cancellationToken).ConfigureAwait(false);

                provider = engine.Provider;
                finalFramesDirectory = Path.Combine(
                    interpolationWorkingDirectory,
                    $"pass_{(int)Math.Round(Math.Log2(settings.TargetMultiplier)):00}");
            }
            metrics.SetEncodedFrames(finalFramesDirectory);
            if (metrics.PaddedWidth > 0 && metrics.PaddedHeight > 0)
            {
                var approxTensorBytes = 3L * 3L * metrics.PaddedWidth * metrics.PaddedHeight * sizeof(float);
                metrics.VramApproximationText = $"{(approxTensorBytes / (1024d * 1024d)).ToString("0.00", CultureInfo.InvariantCulture)} MB minimum tensor footprint";
            }

            request.Logger.Log($"RIFE interpolation completed. provider={provider}, extractedFrameCount={extractedFrameCount}, outputFrameCount={encodedFrameCount}.");

            var targetFps = settings.GetOutputFrameRate(probe.VideoFrameRate.Value);
            request.Logger.Log($"Output FPS: {targetFps.ToString("0.###", CultureInfo.InvariantCulture)}");
            request.Logger.Log(settings.PreservesAudio
                ? "Audio mode: copy original audio."
                : "Audio mode: remove audio for slow motion output.");
            var fallbackPlan = encodePlan.ModeLabel.Equals("GPU", StringComparison.OrdinalIgnoreCase)
                ? BuildEncodePlan(sourceExtension, false)
                : null;

            request.ProgressReporter?.ReportProgress(900, request.InputPath, Descriptor.DisplayName, $"Encoding output on {encodePlan.ModeLabel}...");
            request.ProgressReporter?.ReportState("processing", $"Encoding output on {encodePlan.ModeLabel}...");

            var encodeArguments = BuildEncodeArguments(
                finalFramesDirectory,
                request.InputPath,
                outputPath,
                targetFps,
                encodePlan,
                probe.HasAudio,
                settings,
                probe.PrimaryAudioSampleRate > 0 ? probe.PrimaryAudioSampleRate : FallbackSampleRate);
            var reconstructionStopwatch = Stopwatch.StartNew();
            var encodeResult = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                encodeArguments,
                probe.Duration,
                encodedFrameCount,
                null,
                request.InputPath,
                Descriptor.DisplayName,
                encodePlan.ModeLabel,
                cancellationToken).ConfigureAwait(false);
            reconstructionStopwatch.Stop();
            metrics.AddReconstruction(reconstructionStopwatch.Elapsed);

            if (!encodeResult.Canceled && encodeResult.ExitCode != 0 && fallbackPlan is not null)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.Logger.Log("RIFE encode path failed on GPU. Retrying with CPU fallback.");
                request.ProgressReporter?.ReportState("processing", "GPU unavailable. Retrying encode on CPU...");

                var fallbackArguments = BuildEncodeArguments(
                    finalFramesDirectory,
                    request.InputPath,
                    outputPath,
                    targetFps,
                    fallbackPlan,
                    probe.HasAudio,
                    settings,
                    probe.PrimaryAudioSampleRate > 0 ? probe.PrimaryAudioSampleRate : FallbackSampleRate);
                reconstructionStopwatch = Stopwatch.StartNew();
                encodeResult = await _ffmpegRunner.RunAsync(
                    ffmpegPath,
                    fallbackArguments,
                    probe.Duration,
                    encodedFrameCount,
                    null,
                    request.InputPath,
                    Descriptor.DisplayName,
                    fallbackPlan.ModeLabel,
                    cancellationToken).ConfigureAwait(false);
                reconstructionStopwatch.Stop();
                metrics.AddReconstruction(reconstructionStopwatch.Elapsed);
            }

            if (encodeResult.Canceled)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, encodeResult.CancellationScope);
            }

            if (encodeResult.ExitCode != 0 || !File.Exists(outputPath))
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(encodeResult.StandardError, "FFmpeg failed while encoding the RIFE output video.");
                request.ProgressReporter?.ReportState("failed", failureMessage);
                return new ActionExecutionResult(false, failureMessage);
            }

            totalStopwatch.Stop();
            metrics.CompleteTotal(totalStopwatch);
            var report = metrics.BuildReport();
            request.Logger.Log(report);
            WritePerformanceReport(report, "RifePerformanceReport_latest.txt");
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
            if (totalStopwatch.IsRunning)
            {
                totalStopwatch.Stop();
                metrics.CompleteTotal(totalStopwatch);
                var report = metrics.BuildReport();
                request.Logger.Log(report);
                WritePerformanceReport(report, "RifePerformanceReport_latest.txt");
            }
            var failureMessage = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed(Descriptor.DisplayName));
            request.ProgressReporter?.ReportState("failed", failureMessage);
            return new ActionExecutionResult(false, failureMessage);
        }
        finally
        {
            TryDeleteDirectory(tempRoot, request.Logger);
        }
    }

    private static void WritePerformanceReport(string report, string fileName)
    {
        try
        {
            var logDirectory = Path.GetDirectoryName(Core.Logging.AppLogger.LogPath);
            if (string.IsNullOrWhiteSpace(logDirectory))
            {
                return;
            }

            Directory.CreateDirectory(logDirectory);
            var reportPath = Path.Combine(logDirectory, fileName);
            File.WriteAllText(reportPath, report);
        }
        catch
        {
        }
    }

    internal static IReadOnlyList<string> BuildExtractArguments(string inputPath, string outputPattern)
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
            "-vsync", "0",
            "-c:v", "bmp",
            outputPattern
        ];
    }

    internal static IReadOnlyList<string> BuildEncodeArguments(
        string interpolatedFramesDirectory,
        string sourceVideoPath,
        string outputPath,
        double targetFps,
        RifeEncodePlan plan,
        bool hasAudio,
        RifeInterpolateVideoSettings settings,
        int sampleRate)
    {
        var fpsText = targetFps.ToString("0.###", CultureInfo.InvariantCulture);
        if (settings.PreservesAudio && hasAudio)
        {
            if (settings.IsSlowMotion)
            {
                var audioFilter = BuildSlowMotionAudioFilter(settings, sampleRate);
                return
                [
                    "-hide_banner",
                    "-loglevel", "error",
                    "-stats_period", "0.25",
                    "-progress", "pipe:1",
                    "-nostats",
                    "-y",
                    "-framerate", fpsText,
                    "-start_number", "1",
                    "-i", Path.Combine(interpolatedFramesDirectory, TempFramePattern),
                    "-i", sourceVideoPath,
                    "-filter_complex", $"[1:a]{audioFilter}[a]",
                    "-map", "0:v:0",
                    "-map", "[a]",
                    "-c:v", plan.VideoCodec,
                    .. plan.VideoArgs,
                    .. GetAudioCodecArgs(Path.GetExtension(outputPath)),
                    "-shortest",
                    outputPath
                ];
            }

            return
            [
                "-hide_banner",
                "-loglevel", "error",
                "-stats_period", "0.25",
                "-progress", "pipe:1",
                "-nostats",
                "-y",
                "-framerate", fpsText,
                "-start_number", "1",
                "-i", Path.Combine(interpolatedFramesDirectory, TempFramePattern),
                "-i", sourceVideoPath,
                "-map", "0:v:0",
                "-map", "1:a?",
                "-c:v", plan.VideoCodec,
                .. plan.VideoArgs,
                "-c:a", "copy",
                "-shortest",
                outputPath
            ];
        }

        return
        [
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-framerate", fpsText,
            "-start_number", "1",
            "-i", Path.Combine(interpolatedFramesDirectory, TempFramePattern),
            "-map", "0:v:0",
            "-c:v", plan.VideoCodec,
            .. plan.VideoArgs,
            outputPath
        ];
    }

    internal static string BuildSlowMotionAudioFilter(RifeInterpolateVideoSettings settings, int sampleRate)
    {
        var speedFactor = 1d / settings.PlaybackDivisor;
        if (settings.KeepPitch)
        {
            return ChangeAudioSpeedAction.BuildAtempoChain(speedFactor);
        }

        var targetRate = Math.Max(1000, (int)Math.Round(sampleRate * speedFactor));
        return $"asetrate={targetRate},aresample={sampleRate}";
    }

    internal static IReadOnlyList<string> GetAudioCodecArgs(string extension)
    {
        return NormalizeExtension(extension) switch
        {
            ".webm" => ["-c:a", "libopus", "-b:a", "160k"],
            ".avi" => ["-c:a", "libmp3lame", "-b:a", "192k"],
            _ => ["-c:a", "aac", "-b:a", "192k"]
        };
    }

    private static RifeEncodePlan BuildEncodePlan(string extension, bool nvencAvailable)
    {
        return extension switch
        {
            ".avi" => new RifeEncodePlan("CPU", "mpeg4", ["-q:v", "2"]),
            ".webm" => new RifeEncodePlan("CPU", "libvpx-vp9", ["-crf", "31", "-b:v", "0", "-deadline", "good", "-cpu-used", "2", "-row-mt", "1"]),
            _ when nvencAvailable => new RifeEncodePlan("GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p"]),
            _ => new RifeEncodePlan("CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p"])
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
        return extension is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".m4v";
    }

    private static string GetSupportedFormatsText()
    {
        return ".mp4, .mkv, .avi, .mov, .webm, .m4v";
    }

    private static void TryDeleteDirectory(string path, Core.Logging.AppLogger logger)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            logger.Log($"RIFE interpolation cleanup could not remove directory '{path}': {ex}");
        }
    }

    internal sealed record RifeEncodePlan(string ModeLabel, string VideoCodec, IReadOnlyList<string> VideoArgs);

    internal static string ResolvePipelineMode(string requestedPipeline, int targetMultiplier)
    {
        if (string.Equals(requestedPipeline, "bmp", StringComparison.OrdinalIgnoreCase))
        {
            return "bmp";
        }

        if (string.Equals(requestedPipeline, "rawvideo", StringComparison.OrdinalIgnoreCase))
        {
            return targetMultiplier == 2 ? "rawvideo" : "bmp";
        }

        return targetMultiplier == 2 ? "rawvideo" : "bmp";
    }
}
