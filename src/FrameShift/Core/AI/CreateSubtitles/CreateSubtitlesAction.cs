using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Progress;

namespace FrameShift.Core.AI.CreateSubtitles;

internal enum CreateSubtitlesSourceKind
{
    Audio,
    Video
}

internal sealed class CreateSubtitlesAction : IFrameShiftAction
{
    private const int WorkerWindowMilliseconds = 29_000;
    private const int WorkerOverlapMilliseconds = 1_500;

    private readonly CreateSubtitlesSourceKind _sourceKind;
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public CreateSubtitlesAction(
        CreateSubtitlesSourceKind sourceKind,
        FfmpegRunner ffmpegRunner,
        FfprobeRunner ffprobeRunner,
        ToolLocator toolLocator)
    {
        _sourceKind = sourceKind;
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
        Descriptor = sourceKind switch
        {
            CreateSubtitlesSourceKind.Audio => new ActionDescriptor(
                "create-subtitles-audio",
                "Create Subtitle File",
                "Creates local SRT subtitles from an audio file with Whisper ONNX."),
            _ => new ActionDescriptor(
                "create-subtitles-video",
                "Create Subtitle File",
                "Extracts audio from a video, then creates local SRT subtitles with Whisper ONNX.")
        };
    }

    public ActionDescriptor Descriptor { get; }

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

        var extension = Path.GetExtension(request.InputPath).ToLowerInvariant();
        if (!IsSupportedExtension(extension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(extension, GetSupportedExtensionsText()));
        }

        var ffprobePath = _toolLocator.ResolveFfprobePath();
        var probeAttempt = await _ffprobeRunner.TryProbeMediaAsync(ffprobePath, request.InputPath, cancellationToken).ConfigureAwait(false);
        if (probeAttempt.Probe is null)
        {
            return new ActionExecutionResult(false, probeAttempt.ErrorMessage ?? MediaActionMessages.ProbeFailed());
        }

        var probe = probeAttempt.Probe;
        if (!probe.HasAudio)
        {
            return new ActionExecutionResult(false, MediaActionMessages.MissingAudioTrack());
        }

        var model = ResolveModel(request.Options, request.Logger);
        if (!CreateSubtitlesModelDownloader.IsModelReady(model, request.Logger))
        {
            return new ActionExecutionResult(
                false,
                "Whisper model is not ready. Run FrameShift from Explorer to verify and download the subtitles model first.");
        }

        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, string.Empty, ".srt");
        var tempRoot = Path.Combine(Path.GetTempPath(), "FrameShift", "CreateSubtitles", Guid.NewGuid().ToString("N"));
        var extractedAudioPath = Path.Combine(tempRoot, "extracted.wav");
        var normalizedAudioPath = Path.Combine(tempRoot, "normalized.wav");
        var workerModelRoot = Path.Combine(tempRoot, "worker-model");
        var responsePath = Path.Combine(tempRoot, "worker-response.json");
        var cancelSignalPath = Path.Combine(tempRoot, "cancel.signal");
        var tempOutputPath = Path.Combine(tempRoot, "output.srt");

        using var itemCancellationSource = new CancellationTokenSource();
        using var linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, itemCancellationSource.Token);
        using var monitorStopSource = new CancellationTokenSource();
        var monitorTask = MonitorCurrentItemCancellationAsync(
            request.ProgressReporter,
            request.InputPath,
            request.Logger,
            itemCancellationSource,
            monitorStopSource.Token);

        try
        {
            Directory.CreateDirectory(tempRoot);

            var ffmpegPath = _toolLocator.ResolveFfmpegPath();
            var effectiveAudioInput = request.InputPath;
            if (_sourceKind == CreateSubtitlesSourceKind.Video)
            {
                request.ProgressReporter?.ReportState("processing", "Extracting audio track...");
                var extractionResult = await _ffmpegRunner.RunAsync(
                    ffmpegPath,
                    BuildVideoExtractionArguments(request.InputPath, extractedAudioPath),
                    probe.Duration,
                    new MappedProgressReporter(request.ProgressReporter, 20, 180, "Extracting audio track..."),
                    request.InputPath,
                    Descriptor.DisplayName,
                    "CPU",
                    linkedCancellationSource.Token).ConfigureAwait(false);

                if (extractionResult.Canceled)
                {
                    return BuildCanceledResult(cancellationToken, itemCancellationSource, extractionResult.CancellationScope);
                }

                if (extractionResult.ExitCode != 0 || !File.Exists(extractedAudioPath))
                {
                    return new ActionExecutionResult(
                        false,
                        ConversionActionHelper.GetFriendlyFfmpegError(extractionResult.StandardError, "FFmpeg failed while extracting the audio track."));
                }

                effectiveAudioInput = extractedAudioPath;
            }

            request.ProgressReporter?.ReportState("processing", "Preparing Whisper audio...");
            var normalizationResult = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                BuildNormalizeArguments(effectiveAudioInput, normalizedAudioPath),
                probe.Duration,
                new MappedProgressReporter(request.ProgressReporter, _sourceKind == CreateSubtitlesSourceKind.Video ? 180 : 20, 360, "Preparing Whisper audio..."),
                request.InputPath,
                Descriptor.DisplayName,
                "CPU",
                linkedCancellationSource.Token).ConfigureAwait(false);

            if (normalizationResult.Canceled)
            {
                return BuildCanceledResult(cancellationToken, itemCancellationSource, normalizationResult.CancellationScope);
            }

            if (normalizationResult.ExitCode != 0 || !File.Exists(normalizedAudioPath))
            {
                return new ActionExecutionResult(
                    false,
                    ConversionActionHelper.GetFriendlyFfmpegError(normalizationResult.StandardError, "FFmpeg failed while preparing the Whisper audio input."));
            }

            request.ProgressReporter?.ReportState("processing", $"Transcribing with {model.DisplayName}...");
            var workerRunner = new CreateSubtitlesWorkerRunner(request.Logger);
            var workerModelFiles = PrepareWorkerModelFiles(model, workerModelRoot);
            var workerResponse = await workerRunner.RunAsync(
                new CreateSubtitlesWorkerRequest
                {
                    EncoderPath = workerModelFiles.EncoderPath,
                    DecoderPath = workerModelFiles.DecoderPath,
                    TokensPath = workerModelFiles.TokensPath,
                    WavPath = normalizedAudioPath,
                    ResponsePath = responsePath,
                    CancelSignalPath = cancelSignalPath,
                    NumThreads = 1,
                    WindowMilliseconds = WorkerWindowMilliseconds,
                    OverlapMilliseconds = WorkerOverlapMilliseconds,
                    FeatureDim = model.FeatureDim
                },
                (percent, message) =>
                {
                    var mappedPercent = 360 + (int)Math.Round(percent * 5.9d, MidpointRounding.AwayFromZero);
                    request.ProgressReporter?.ReportProgress(
                        Math.Clamp(mappedPercent, 360, 950),
                        request.InputPath,
                        Descriptor.DisplayName,
                        message);
                    request.ProgressReporter?.ReportState("processing", message);
                },
                linkedCancellationSource.Token).ConfigureAwait(false);

            if (workerResponse.Canceled)
            {
                return BuildCanceledResult(cancellationToken, itemCancellationSource, CancellationScope.All);
            }

            if (!workerResponse.Success)
            {
                return new ActionExecutionResult(false, workerResponse.ErrorMessage ?? "Whisper transcription failed.");
            }

            request.Logger.Log($"CreateSubtitlesAction: worker completed. ProviderUsed={workerResponse.ProviderUsed ?? "unknown"}. DetectedLanguage={workerResponse.DetectedLanguage ?? "auto"}.");

            var normalizedWords = CreateSubtitlesWordNormalizer.Normalize(workerResponse.Words);
            if (normalizedWords.Count == 0)
            {
                return new ActionExecutionResult(false, "No speech could be recognized in this media.");
            }

            request.ProgressReporter?.ReportState("processing", "Building SRT subtitles...");
            var cues = CreateSubtitlesSegmenter.BuildCues(
                normalizedWords,
                TimeSpan.FromSeconds(workerResponse.AudioDurationSeconds > 0
                    ? workerResponse.AudioDurationSeconds
                    : probe.Duration?.TotalSeconds ?? normalizedWords[^1].StartSeconds + 1d));
            var srtText = CreateSubtitlesSrtFormatter.Format(cues);

            await File.WriteAllTextAsync(tempOutputPath, srtText, new UTF8Encoding(false), linkedCancellationSource.Token).ConfigureAwait(false);
            File.Move(tempOutputPath, outputPath, overwrite: false);

            request.ProgressReporter?.ReportProgress(1000, request.InputPath, Descriptor.DisplayName, "Completed.");
            request.ProgressReporter?.ReportState("done", $"Completed. Language: {workerResponse.DetectedLanguage ?? "auto"}");
            return new ActionExecutionResult(true, MediaActionMessages.Completed(Descriptor.DisplayName), outputPath);
        }
        catch (OperationCanceledException)
        {
            return BuildCanceledResult(cancellationToken, itemCancellationSource, CancellationScope.All);
        }
        catch (Exception ex)
        {
            request.Logger.Log($"CreateSubtitlesAction: failed. {ex}");
            return new ActionExecutionResult(
                false,
                ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed(Descriptor.DisplayName)));
        }
        finally
        {
            monitorStopSource.Cancel();
            try
            {
                await monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            ConversionActionHelper.DeleteIfExists(tempOutputPath);
            ConversionActionHelper.DeleteIfExists(outputPath + ".tmp");
            TryDeleteFile(extractedAudioPath);
            TryDeleteFile(normalizedAudioPath);
            TryDeleteFile(responsePath);
            TryDeleteFile(responsePath + ".request.json");
            TryDeleteFile(cancelSignalPath);
            TryDeleteDirectory(tempRoot, request.Logger);
        }
    }

    internal static bool IsSupportedAudioExtension(string extension) =>
        extension is ".wav" or ".wave" or ".mp3" or ".flac" or ".m4a" or ".ogg" or ".aac" or ".wma";

    internal static bool IsSupportedVideoExtension(string extension) =>
        extension is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".m4v";

    internal static string GetSupportedAudioExtensionsText() => ".wav, .wave, .mp3, .flac, .m4a, .ogg, .aac, .wma";

    internal static string GetSupportedVideoExtensionsText() => ".mp4, .mkv, .avi, .mov, .webm, .m4v";

    private bool IsSupportedExtension(string extension) => _sourceKind switch
    {
        CreateSubtitlesSourceKind.Audio => IsSupportedAudioExtension(extension),
        _ => IsSupportedVideoExtension(extension)
    };

    private string GetSupportedExtensionsText() => _sourceKind switch
    {
        CreateSubtitlesSourceKind.Audio => GetSupportedAudioExtensionsText(),
        _ => GetSupportedVideoExtensionsText()
    };

    private static CreateSubtitlesModelDefinition ResolveModel(
        IReadOnlyDictionary<string, string>? options,
        Logging.AppLogger logger)
    {
        options ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        options.TryGetValue(ActionOptionKeys.SubtitlesModel, out var requestedModelId);

        var model = CreateSubtitlesModelCatalog.GetById(requestedModelId) ?? CreateSubtitlesModelCatalog.GetDefault();
        if (!string.IsNullOrWhiteSpace(requestedModelId) &&
            !string.Equals(requestedModelId, model.Id, StringComparison.OrdinalIgnoreCase))
        {
            logger.Log($"CreateSubtitlesAction: unknown subtitles model requested '{requestedModelId}', using '{model.Id}'.");
        }

        return model;
    }

    private static IReadOnlyCollection<string> BuildVideoExtractionArguments(string inputPath, string outputPath) =>
    [
        "-hide_banner", "-loglevel", "error", "-stats_period", "0.25",
        "-progress", "pipe:1", "-nostats", "-y",
        "-i", inputPath,
        "-map", "0:a:0", "-vn", "-sn", "-dn",
        "-c:a", "pcm_s16le",
        outputPath
    ];

    private static IReadOnlyCollection<string> BuildNormalizeArguments(string inputPath, string outputPath) =>
    [
        "-hide_banner", "-loglevel", "error", "-stats_period", "0.25",
        "-progress", "pipe:1", "-nostats", "-y",
        "-i", inputPath,
        "-vn", "-sn", "-dn",
        "-ac", "1",
        "-ar", "16000",
        "-c:a", "pcm_s16le",
        outputPath
    ];

    private static async Task MonitorCurrentItemCancellationAsync(
        IProgressReporter? progressReporter,
        string inputPath,
        Logging.AppLogger logger,
        CancellationTokenSource itemCancellationSource,
        CancellationToken cancellationToken)
    {
        if (progressReporter is null || string.IsNullOrWhiteSpace(inputPath))
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested && !itemCancellationSource.IsCancellationRequested)
        {
            if (progressReporter.IsQueueItemCancellationRequested(inputPath))
            {
                logger.Log($"CreateSubtitlesAction: current-item cancellation for '{inputPath}'.");
                itemCancellationSource.Cancel();
                return;
            }

            try
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static ActionExecutionResult BuildCanceledResult(
        CancellationToken globalToken,
        CancellationTokenSource itemCancellationSource,
        CancellationScope requestedScope)
    {
        var scope = globalToken.IsCancellationRequested
            ? CancellationScope.All
            : itemCancellationSource.IsCancellationRequested
                ? CancellationScope.CurrentItem
                : requestedScope;
        return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled("Create Subtitles"), null, true, scope);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path, Logging.AppLogger logger)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            logger.Log($"CreateSubtitlesAction: failed to remove temp directory '{path}'. {ex.Message}");
        }
    }

    private static CreateSubtitlesModelFiles PrepareWorkerModelFiles(
        CreateSubtitlesModelDefinition model,
        string workerModelRoot)
    {
        var sourceFiles = CreateSubtitlesModelLocator.GetModelFiles(model);
        if (IsAsciiOnly(sourceFiles.EncoderPath) &&
            IsAsciiOnly(sourceFiles.DecoderPath) &&
            IsAsciiOnly(sourceFiles.TokensPath))
        {
            return sourceFiles;
        }

        Directory.CreateDirectory(workerModelRoot);
        var stagedFiles = new CreateSubtitlesModelFiles(
            workerModelRoot,
            Path.Combine(workerModelRoot, Path.GetFileName(sourceFiles.EncoderPath)),
            Path.Combine(workerModelRoot, Path.GetFileName(sourceFiles.DecoderPath)),
            Path.Combine(workerModelRoot, Path.GetFileName(sourceFiles.TokensPath)));

        File.Copy(sourceFiles.EncoderPath, stagedFiles.EncoderPath, overwrite: true);
        File.Copy(sourceFiles.DecoderPath, stagedFiles.DecoderPath, overwrite: true);
        File.Copy(sourceFiles.TokensPath, stagedFiles.TokensPath, overwrite: true);

        // Copy extra artifacts beyond [0..2] (e.g. turbo-encoder.weights — ONNX Runtime resolves
        // encoder external data by looking in the same directory as the encoder .onnx file).
        for (var i = 3; i < model.Artifacts.Count; i++)
        {
            var src = CreateSubtitlesModelLocator.GetArtifactPath(model, model.Artifacts[i]);
            File.Copy(src, Path.Combine(workerModelRoot, model.Artifacts[i].FileName), overwrite: true);
        }

        return stagedFiles;
    }

    private static bool IsAsciiOnly(string path)
    {
        foreach (var character in path)
        {
            if (character > 127)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class MappedProgressReporter : IProgressReporter
    {
        private readonly IProgressReporter? _inner;
        private readonly int _start;
        private readonly int _end;
        private readonly string _defaultMessage;

        public MappedProgressReporter(IProgressReporter? inner, int start, int end, string defaultMessage)
        {
            _inner = inner;
            _start = start;
            _end = end;
            _defaultMessage = defaultMessage;
        }

        public bool IsCancellationRequested => _inner?.IsCancellationRequested == true;

        public bool IsQueueItemRemovalRequested(string inputPath) => _inner?.IsQueueItemRemovalRequested(inputPath) == true;

        public bool IsQueueItemCancellationRequested(string inputPath) => _inner?.IsQueueItemCancellationRequested(inputPath) == true;

        public void ReportQueue(IReadOnlyList<string> items) => _inner?.ReportQueue(items);

        public void ReportProgress(int progressValue, string? currentFile, string? currentAction, string? message, string? etaText = null)
        {
            var mapped = _start + (int)Math.Round(
                Math.Clamp(progressValue, 0, 1000) / 1000d * (_end - _start),
                MidpointRounding.AwayFromZero);
            _inner?.ReportProgress(mapped, currentFile, currentAction, message ?? _defaultMessage, etaText);
        }

        public void ReportState(string state, string? message = null) => _inner?.ReportState(state, message);

        public void ReportQueueItem(string currentFile, string state, string? message = null) =>
            _inner?.ReportQueueItem(currentFile, state, message);
    }
}
