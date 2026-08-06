using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.AI;
using FrameShift.Core.AI.Upscale;
using FrameShift.Core.AI.VideoUpscale;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Progress;

namespace FrameShift.Core.Actions;

public sealed class UpscaleVideoAction : IFrameShiftAction
{
    private const string TempFramePattern = "%08d.bmp";
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public UpscaleVideoAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "upscale-video",
        "Upscale Video",
        "Upscales every video frame with a local AI model while preserving frame rate and audio.");

    public async Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath))
            return new ActionExecutionResult(false, MediaActionMessages.InputPathRequired());
        if (!File.Exists(request.InputPath))
            return new ActionExecutionResult(false, MediaActionMessages.InputFileNotFound(request.InputPath));

        string extension = NormalizeExtension(Path.GetExtension(request.InputPath));
        if (!IsSupportedExtension(extension))
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(extension, SupportedFormatsText));

        if (!UpscaleVideoSettings.TryFromOptions(request.Options, out var settings, out var settingsError) || settings is null)
            return new ActionExecutionResult(false, settingsError ?? "Invalid Upscale Video settings.");

        var selectedModel = UpscaleModelCatalog.GetById(settings.ModelId);
        if (selectedModel is null || !selectedModel.RecommendedForVideo)
            return new ActionExecutionResult(false, $"Unknown or unsupported video upscale model: {settings.ModelId}.");

        string ffmpegPath = _toolLocator.ResolveFfmpegPath();
        string ffprobePath = _toolLocator.ResolveFfprobePath();
        var probeAttempt = await _ffprobeRunner.TryProbeMediaAsync(
            ffprobePath,
            request.InputPath,
            cancellationToken).ConfigureAwait(false);
        if (probeAttempt.Probe is null)
            return new ActionExecutionResult(false, probeAttempt.ErrorMessage ?? MediaActionMessages.ProbeFailed());

        var probe = probeAttempt.Probe;
        if (!probe.HasVideo)
            return new ActionExecutionResult(false, MediaActionMessages.MissingVideoTrack());
        if (probe.VideoFrameRate is null || probe.VideoFrameRate <= 0)
            return new ActionExecutionResult(false, MediaActionMessages.InterpolateVideoFrameRateUnavailable());
        if (probe.Duration is null || probe.Duration.Value.TotalSeconds <= 0)
            return new ActionExecutionResult(false, MediaActionMessages.DurationUnavailable());
        var sourceWidth = probe.DisplayVideoWidth;
        var sourceHeight = probe.DisplayVideoHeight;
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return new ActionExecutionResult(false, "Video dimensions are unavailable.");
        if ((long)sourceWidth * sourceHeight > UpscaleFrameProcessor.MaxInputPixels)
        {
            return new ActionExecutionResult(
                false,
                $"Video frame size {sourceWidth}x{sourceHeight} exceeds the {UpscaleFrameProcessor.MaxInputPixels:N0}-pixel safety limit.");
        }

        var target = UpscaleFrameProcessor.ResolveFinalSize(
            sourceWidth,
            sourceHeight,
            settings.Request,
            selectedModel.ScaleFactor);
        var executionModel = UpscaleModelCatalog.ResolveVideoExecutionModel(
            selectedModel,
            settings.Request,
            sourceWidth,
            sourceHeight);
        string modelPath = ModelLocator.GetModelPath(executionModel);
        if (!ModelDownloader.IsModelFileValid(modelPath, executionModel, request.Logger))
        {
            return new ActionExecutionResult(
                false,
                "AI model not found or invalid. Run FrameShift.exe --action upscale-video from Explorer to verify and download it first.");
        }
        if (target.Width > 8192)
        {
            string warning = $"Large output ({target.Width}x{target.Height}); processing and encoding may take a long time.";
            request.Logger.Log($"UpscaleVideoAction: {warning}");
            request.ProgressReporter?.ReportState("processing", warning);
        }

        string outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, target.Suffix, extension);
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "FrameShift",
            "UpscaleVideo",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        string inputFrames = Path.Combine(tempRoot, "input");
        string outputFrames = Path.Combine(tempRoot, "output");

        using var itemCancellationSource = new CancellationTokenSource();
        using var linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, itemCancellationSource.Token);
        using var monitorStopSource = new CancellationTokenSource();
        var monitorTask = MonitorCurrentItemCancellationAsync(
            request.ProgressReporter,
            request.InputPath,
            request.Logger,
            itemCancellationSource,
            monitorStopSource.Token);

        request.Logger.Log(
            $"UpscaleVideoAction: input={request.InputPath}, model={selectedModel.Id}, executionModel={executionModel.Id}, target={target.Width}x{target.Height}, fps={probe.VideoFrameRate.Value.ToString("0.###", CultureInfo.InvariantCulture)}.");

        try
        {
            bool nvencAvailable = await _ffmpegRunner.IsEncoderAvailableAsync(
                ffmpegPath,
                "h264_nvenc",
                linkedCancellationSource.Token).ConfigureAwait(false);
            var preferredPlan = BuildEncodePlan(extension, nvencAvailable);
            var cpuPlan = BuildEncodePlan(extension, false);
            var attempts = BuildEncodeAttempts(preferredPlan, cpuPlan, probe.HasAudio);

            string requestedPipeline = ConversionActionHelper.GetOption(request, ActionOptionKeys.UpscalePipeline, "auto");
            string pipelineMode = ResolvePipelineMode(requestedPipeline);
            if (pipelineMode == "rawvideo")
            {
                try
                {
                    Exception? lastRawPipelineError = null;
                    for (int attemptIndex = 0; attemptIndex < attempts.Count; attemptIndex++)
                    {
                        var attempt = attempts[attemptIndex];
                        if (attemptIndex > 0)
                        {
                            ConversionActionHelper.DeleteIfExists(outputPath);
                            request.Logger.Log($"UpscaleVideoAction: retrying rawvideo pipeline. mode={attempt.Plan.ModeLabel}, transcodeAudio={attempt.TranscodeAudio}.");
                        }

                        string status = attempt.TranscodeAudio
                            ? "Upscaling video with compatible audio..."
                            : $"Upscaling video on {attempt.Plan.ModeLabel}...";
                        request.ProgressReporter?.ReportState("processing", status);

                        var frameProgress = new Progress<(int Processed, int Total)>(value =>
                        {
                            double fraction = value.Processed / (double)Math.Max(1, value.Total);
                            int permille = 100 + (int)Math.Round(fraction * 850d, MidpointRounding.AwayFromZero);
                            string frameStatus = $"Upscaling frames... {value.Processed}/{value.Total}";
                            request.ProgressReporter?.ReportProgress(permille, request.InputPath, Descriptor.DisplayName, frameStatus);
                            request.ProgressReporter?.ReportState("processing", frameStatus);
                        });

                        try
                        {
                            var rawPipeline = new UpscaleRawVideoPipeline(_ffmpegRunner, request.Logger);
                            var rawResult = await rawPipeline.RunAsync(
                                ffmpegPath,
                                request.InputPath,
                                outputPath,
                                executionModel,
                                probe,
                                settings.Request,
                                attempt.Plan.VideoCodec,
                                attempt.Plan.VideoArgs,
                                attempt.TranscodeAudio,
                                frameProgress,
                                linkedCancellationSource.Token).ConfigureAwait(false);

                            request.Logger.Log(
                                $"UpscaleVideoAction: rawvideo complete. sourceFrames={rawResult.SourceFrameCount}, outputFrames={rawResult.OutputFrameCount}, provider={rawResult.Provider}.");
                            request.ProgressReporter?.ReportProgress(1000, request.InputPath, Descriptor.DisplayName, "Completed.");
                            request.ProgressReporter?.ReportState("done", "Completed.");
                            return new ActionExecutionResult(true, MediaActionMessages.Completed(Descriptor.DisplayName), outputPath);
                        }
                        catch (OperationCanceledException)
                        {
                            ConversionActionHelper.DeleteIfExists(outputPath);
                            return CanceledResult(request, cancellationToken, itemCancellationSource, CancellationScope.All);
                        }
                        catch (Exception ex)
                        {
                            ConversionActionHelper.DeleteIfExists(outputPath);
                            lastRawPipelineError = ex;
                            request.Logger.Log($"UpscaleVideoAction: rawvideo attempt failed. mode={attempt.Plan.ModeLabel}, transcodeAudio={attempt.TranscodeAudio}. {ex.Message}");
                        }
                    }

                    throw lastRawPipelineError ?? new InvalidOperationException("Rawvideo upscale pipeline failed.");
                }
                catch (Exception ex) when (!string.Equals(requestedPipeline, "rawvideo", StringComparison.OrdinalIgnoreCase))
                {
                    ConversionActionHelper.DeleteIfExists(outputPath);
                    request.Logger.Log($"UpscaleVideoAction: rawvideo pipeline failed in auto mode. Falling back to BMP pipeline. {ex.Message}");
                    request.ProgressReporter?.ReportState("processing", "Rawvideo pipeline unavailable. Falling back to frame files...");
                }
            }

            Directory.CreateDirectory(inputFrames);
            Directory.CreateDirectory(outputFrames);
            request.ProgressReporter?.ReportState("processing", "Extracting source frames...");

            var extractionReporter = new MappedProgressReporter(
                request.ProgressReporter,
                20,
                150,
                "Extracting source frames...");
            var extractionResult = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                BuildExtractArguments(request.InputPath, Path.Combine(inputFrames, TempFramePattern)),
                probe.Duration,
                probe.EstimatedVideoFrameCount,
                extractionReporter,
                request.InputPath,
                Descriptor.DisplayName,
                "CPU",
                linkedCancellationSource.Token).ConfigureAwait(false);

            if (extractionResult.Canceled)
                return CanceledResult(request, cancellationToken, itemCancellationSource, extractionResult.CancellationScope);

            int extractedFrameCount = Directory.GetFiles(inputFrames, "*.bmp", SearchOption.TopDirectoryOnly).Length;
            if (extractionResult.ExitCode != 0 || extractedFrameCount == 0)
            {
                string failure = extractionResult.ExitCode != 0
                    ? ConversionActionHelper.GetFriendlyFfmpegError(extractionResult.StandardError, MediaActionMessages.FrameExtractionFailure())
                    : MediaActionMessages.FrameExtractionProducedNoFiles();
                return FailedResult(request, failure);
            }

            VideoUpscaleResult upscaleResult;
            using (var engine = new VideoUpscaleEngine(executionModel))
            {
                var frameProgress = new Progress<(int Processed, int Total)>(value =>
                {
                    double fraction = value.Processed / (double)Math.Max(1, value.Total);
                    int permille = 150 + (int)Math.Round(fraction * 750d, MidpointRounding.AwayFromZero);
                    string status = $"Upscaling frames... {value.Processed}/{value.Total}";
                    request.ProgressReporter?.ReportProgress(permille, request.InputPath, Descriptor.DisplayName, status);
                    request.ProgressReporter?.ReportState("processing", status);
                });
                upscaleResult = await engine.ProcessAsync(
                    inputFrames,
                    outputFrames,
                    settings.Request,
                    frameProgress,
                    linkedCancellationSource.Token).ConfigureAwait(false);
            }

            request.Logger.Log(
                $"UpscaleVideoAction: frames complete. count={upscaleResult.ProcessedFrameCount}, provider={upscaleResult.Provider}.");

            FfmpegRunResult? encodeResult = null;

            for (int attemptIndex = 0; attemptIndex < attempts.Count; attemptIndex++)
            {
                var attempt = attempts[attemptIndex];
                if (attemptIndex > 0)
                {
                    ConversionActionHelper.DeleteIfExists(outputPath);
                    request.Logger.Log($"UpscaleVideoAction: retrying encode. mode={attempt.Plan.ModeLabel}, transcodeAudio={attempt.TranscodeAudio}.");
                }

                string status = attempt.TranscodeAudio
                    ? "Encoding output with compatible audio..."
                    : $"Encoding output on {attempt.Plan.ModeLabel}...";
                request.ProgressReporter?.ReportState("processing", status);
                var encodeReporter = new MappedProgressReporter(request.ProgressReporter, 900, 1000, status);
                encodeResult = await _ffmpegRunner.RunAsync(
                    ffmpegPath,
                    BuildEncodeArguments(
                        outputFrames,
                        request.InputPath,
                        outputPath,
                        probe.VideoFrameRate.Value,
                        attempt.Plan,
                        probe.HasAudio,
                        attempt.TranscodeAudio),
                    probe.Duration,
                    upscaleResult.ProcessedFrameCount,
                    encodeReporter,
                    request.InputPath,
                    Descriptor.DisplayName,
                    attempt.Plan.ModeLabel,
                    linkedCancellationSource.Token).ConfigureAwait(false);

                if (encodeResult.Canceled)
                {
                    ConversionActionHelper.DeleteIfExists(outputPath);
                    return CanceledResult(request, cancellationToken, itemCancellationSource, encodeResult.CancellationScope);
                }
                if (encodeResult.ExitCode == 0 && File.Exists(outputPath))
                    break;
            }

            if (encodeResult is null || encodeResult.ExitCode != 0 || !File.Exists(outputPath))
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                string failure = ConversionActionHelper.GetFriendlyFfmpegError(
                    encodeResult?.StandardError ?? string.Empty,
                    "FFmpeg failed while encoding the upscaled video.");
                return FailedResult(request, failure);
            }

            request.ProgressReporter?.ReportProgress(1000, request.InputPath, Descriptor.DisplayName, "Completed.");
            request.ProgressReporter?.ReportState("done", "Completed.");
            return new ActionExecutionResult(true, MediaActionMessages.Completed(Descriptor.DisplayName), outputPath);
        }
        catch (OperationCanceledException)
        {
            ConversionActionHelper.DeleteIfExists(outputPath);
            return CanceledResult(request, cancellationToken, itemCancellationSource, CancellationScope.All);
        }
        catch (Exception ex)
        {
            ConversionActionHelper.DeleteIfExists(outputPath);
            request.Logger.Log($"UpscaleVideoAction: failed. {ex}");
            return FailedResult(request, OnnxProviderHelper.GetCleanUserMessage(ex, Descriptor.DisplayName));
        }
        finally
        {
            monitorStopSource.Cancel();
            try { await monitorTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            TryDeleteDirectory(tempRoot, request.Logger);
        }
    }

    internal static IReadOnlyList<string> BuildExtractArguments(string inputPath, string outputPattern) =>
    [
        "-hide_banner", "-loglevel", "error", "-stats_period", "0.25",
        "-progress", "pipe:1", "-nostats", "-y",
        "-i", inputPath,
        "-map", "0:v:0", "-an", "-sn", "-dn", "-vsync", "0",
        "-c:v", "bmp", outputPattern
    ];

    internal static IReadOnlyList<string> BuildEncodeArguments(
        string framesDirectory,
        string sourceVideoPath,
        string outputPath,
        double frameRate,
        VideoEncodePlan plan,
        bool hasAudio,
        bool transcodeAudio)
    {
        var arguments = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-stats_period", "0.25",
            "-progress", "pipe:1", "-nostats", "-y",
            "-framerate", frameRate.ToString("0.###", CultureInfo.InvariantCulture),
            "-start_number", "1", "-i", Path.Combine(framesDirectory, TempFramePattern),
            "-i", sourceVideoPath,
            "-map", "0:v:0"
        };

        if (hasAudio)
        {
            arguments.Add("-map");
            arguments.Add("1:a?");
        }

        arguments.Add("-c:v");
        arguments.Add(plan.VideoCodec);
        arguments.AddRange(plan.VideoArgs);
        if (hasAudio)
        {
            if (transcodeAudio)
                arguments.AddRange(RifeInterpolateVideoAction.GetAudioCodecArgs(Path.GetExtension(outputPath)));
            else
            {
                arguments.Add("-c:a");
                arguments.Add("copy");
            }
        }

        arguments.Add("-shortest");
        arguments.Add(outputPath);
        return arguments;
    }

    private static List<EncodeAttempt> BuildEncodeAttempts(
        VideoEncodePlan preferred,
        VideoEncodePlan cpu,
        bool hasAudio)
    {
        var attempts = new List<EncodeAttempt> { new(preferred, false) };
        if (!string.Equals(preferred.VideoCodec, cpu.VideoCodec, StringComparison.OrdinalIgnoreCase))
            attempts.Add(new EncodeAttempt(cpu, false));
        if (hasAudio)
            attempts.Add(new EncodeAttempt(cpu, true));
        return attempts;
    }

    internal static string ResolvePipelineMode(string requestedPipeline)
    {
        if (string.Equals(requestedPipeline, "bmp", StringComparison.OrdinalIgnoreCase))
            return "bmp";

        return "rawvideo";
    }

    private static VideoEncodePlan BuildEncodePlan(string extension, bool nvencAvailable) => extension switch
    {
        ".avi" => new VideoEncodePlan("CPU", "mpeg4", ["-q:v", "2", "-pix_fmt", "yuv420p"]),
        ".webm" => new VideoEncodePlan("CPU", "libvpx-vp9", ["-crf", "31", "-b:v", "0", "-deadline", "good", "-cpu-used", "2", "-row-mt", "1", "-pix_fmt", "yuv420p"]),
        _ when nvencAvailable => new VideoEncodePlan("GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p"]),
        _ => new VideoEncodePlan("CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p"])
    };

    private static ActionExecutionResult CanceledResult(
        ActionRequest request,
        CancellationToken globalToken,
        CancellationTokenSource itemSource,
        CancellationScope runnerScope)
    {
        var scope = globalToken.IsCancellationRequested
            ? CancellationScope.All
            : itemSource.IsCancellationRequested
                ? CancellationScope.CurrentItem
                : runnerScope;
        request.ProgressReporter?.ReportState("canceled", "Canceled.");
        return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled("Upscale Video"), null, true, scope);
    }

    private static ActionExecutionResult FailedResult(ActionRequest request, string message)
    {
        request.ProgressReporter?.ReportState("failed", message);
        return new ActionExecutionResult(false, message);
    }

    private static async Task MonitorCurrentItemCancellationAsync(
        IProgressReporter? reporter,
        string inputPath,
        Core.Logging.AppLogger logger,
        CancellationTokenSource itemSource,
        CancellationToken stopToken)
    {
        if (reporter is null) return;
        while (!stopToken.IsCancellationRequested && !itemSource.IsCancellationRequested)
        {
            if (reporter.IsQueueItemCancellationRequested(inputPath))
            {
                logger.Log($"UpscaleVideoAction: current-item cancellation requested for '{inputPath}'.");
                itemSource.Cancel();
                return;
            }

            try { await Task.Delay(100, stopToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private static void TryDeleteDirectory(string path, Core.Logging.AppLogger logger)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); }
        catch (Exception ex) { logger.Log($"UpscaleVideoAction: could not remove temp directory '{path}': {ex.Message}"); }
    }

    private static string NormalizeExtension(string extension) =>
        string.IsNullOrWhiteSpace(extension) ? string.Empty : extension.Trim().ToLowerInvariant();

    private static bool IsSupportedExtension(string extension) =>
        extension is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".m4v";

    private const string SupportedFormatsText = ".mp4, .mkv, .avi, .mov, .webm, .m4v";

    internal sealed record VideoEncodePlan(string ModeLabel, string VideoCodec, IReadOnlyList<string> VideoArgs);
    private sealed record EncodeAttempt(VideoEncodePlan Plan, bool TranscodeAudio);

    private sealed class MappedProgressReporter : IProgressReporter
    {
        private readonly IProgressReporter? _inner;
        private readonly int _start;
        private readonly int _end;
        private readonly string _status;

        public MappedProgressReporter(IProgressReporter? inner, int start, int end, string status)
        {
            _inner = inner;
            _start = start;
            _end = end;
            _status = status;
        }

        public bool IsCancellationRequested => _inner?.IsCancellationRequested == true;
        public bool IsQueueItemRemovalRequested(string inputPath) => _inner?.IsQueueItemRemovalRequested(inputPath) == true;
        public bool IsQueueItemCancellationRequested(string inputPath) => _inner?.IsQueueItemCancellationRequested(inputPath) == true;
        public void ReportQueue(IReadOnlyList<string> items) => _inner?.ReportQueue(items);
        public void ReportState(string state, string? message = null) => _inner?.ReportState(state, message);
        public void ReportQueueItem(string currentFile, string state, string? message = null) =>
            _inner?.ReportQueueItem(currentFile, state, message);

        public void ReportProgress(
            int progressValue,
            string? currentFile,
            string? currentAction,
            string? message,
            string? etaText = null)
        {
            int mapped = _start + (int)Math.Round(
                Math.Clamp(progressValue, 0, 1000) / 1000d * (_end - _start),
                MidpointRounding.AwayFromZero);
            _inner?.ReportProgress(mapped, currentFile, currentAction, message ?? _status, etaText);
        }
    }
}
