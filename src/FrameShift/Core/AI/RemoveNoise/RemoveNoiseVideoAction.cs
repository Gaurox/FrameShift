using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Progress;

namespace FrameShift.Core.AI.RemoveNoise;

internal sealed class RemoveNoiseVideoAction : IFrameShiftAction, IDisposable
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;
    private RemoveNoiseEngine? _engine;
    private bool _disposed;

    public RemoveNoiseVideoAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine?.Dispose();
    }

    public ActionDescriptor Descriptor { get; } = new(
        "remove-noise-video",
        "Remove Noise (Video)",
        "Removes background noise from a video's audio track via DeepFilterNet3. Video stream is copied without re-encoding.");

    public Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath))
            return Task.FromResult(new ActionExecutionResult(false, MediaActionMessages.InputPathRequired()));

        if (!File.Exists(request.InputPath))
            return Task.FromResult(new ActionExecutionResult(false, MediaActionMessages.InputFileNotFound(request.InputPath)));

        var ext = Path.GetExtension(request.InputPath).ToLowerInvariant();
        if (!RemoveNoiseVideoSettings.IsSupportedVideoExtension(ext))
            return Task.FromResult(new ActionExecutionResult(
                false,
                MediaActionMessages.UnsupportedSourceFormat(ext, RemoveNoiseVideoSettings.GetSupportedVideoExtensionsText())));

        if (!DeepFilterNetModelLocator.AllModelsExist())
            return Task.FromResult(new ActionExecutionResult(
                false,
                "AI model not found. Run FrameShift.exe --action remove-noise-video from Explorer to download it first."));

        return ExecuteCoreAsync(request, cancellationToken);
    }

    private async Task<ActionExecutionResult> ExecuteCoreAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        var minGain = RemoveNoiseVideoSettings.ToMinGain(
            ConversionActionHelper.GetOption(request, ActionOptionKeys.NoiseStrength, RemoveNoiseVideoSettings.StrengthMaximum));

        var requestedStereo = string.Equals(
            ConversionActionHelper.GetOption(request, ActionOptionKeys.ProcessStereoAudio, "false"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        var ffmpegPath  = _toolLocator.ResolveFfmpegPath();
        var ffprobePath = _toolLocator.ResolveFfprobePath();

        var probeAttempt = await _ffprobeRunner
            .TryProbeMediaAsync(ffprobePath, request.InputPath, cancellationToken)
            .ConfigureAwait(false);

        if (probeAttempt.Probe is null)
        {
            var probeError = probeAttempt.ErrorMessage ?? MediaActionMessages.ProbeFailed();
            request.ProgressReporter?.ReportState("failed", probeError);
            return new ActionExecutionResult(false, probeError);
        }

        var probe = probeAttempt.Probe;
        if (!probe.HasAudio)
        {
            request.ProgressReporter?.ReportState("failed", MediaActionMessages.MissingAudioTrack());
            return new ActionExecutionResult(false, MediaActionMessages.MissingAudioTrack());
        }

        var processStereo   = requestedStereo && probe.PrimaryAudioChannels >= 2;
        var containerExt    = Path.GetExtension(request.InputPath).ToLowerInvariant();
        var remuxAudioCodec = RemoveNoiseVideoSettings.GetRemuxAudioCodec(containerExt);

        var tempId            = Guid.NewGuid().ToString("N")[..12];
        var tempExtractedWav  = Path.Combine(Path.GetTempPath(), $"fs_rnv_{tempId}.wav");
        var tempExtractedWavL = Path.Combine(Path.GetTempPath(), $"fs_rnv_{tempId}_L.wav");
        var tempExtractedWavR = Path.Combine(Path.GetTempPath(), $"fs_rnv_{tempId}_R.wav");
        string? tempCleanWav  = null;
        string? tempCleanWavL = null;
        string? tempCleanWavR = null;
        string? outputPath    = null;

        using var itemCancellationSource = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, itemCancellationSource.Token);
        using var monitorStop = new CancellationTokenSource();
        var monitorTask = MonitorCurrentItemCancellationAsync(
            request.ProgressReporter, request.InputPath, request.Logger, itemCancellationSource, monitorStop.Token);

        try
        {
            string cleanAudioForRemux;

            if (!processStereo)
            {
                // ── Mono path ────────────────────────────────────────────────
                // Phase 1 — extract audio to temp WAV (0→50 permille)
                request.ProgressReporter?.ReportProgress(10, request.InputPath, Descriptor.DisplayName, "Extracting audio...");
                request.ProgressReporter?.ReportState("processing", "Extracting audio...");

                var extractArgs = new[]
                {
                    "-hide_banner", "-loglevel", "error",
                    "-i", request.InputPath,
                    "-vn", "-c:a", "pcm_s16le",
                    tempExtractedWav
                };

                var extractResult = await _ffmpegRunner
                    .RunAsync(ffmpegPath, extractArgs, probe.Duration, null, request.InputPath, Descriptor.DisplayName, "CPU", linked.Token)
                    .ConfigureAwait(false);

                if (extractResult.Canceled)
                {
                    request.ProgressReporter?.ReportState("canceled", "Canceled.");
                    return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, CancellationScope.All);
                }

                if (extractResult.ExitCode != 0)
                {
                    var err = ConversionActionHelper.GetFriendlyFfmpegError(extractResult.StandardError, "Audio extraction failed.");
                    request.ProgressReporter?.ReportState("failed", err);
                    return new ActionExecutionResult(false, err);
                }

                // Phase 2 — AI denoising (50→900 permille)
                request.ProgressReporter?.ReportProgress(50, request.InputPath, Descriptor.DisplayName, "Loading AI model...");
                request.ProgressReporter?.ReportState("processing", "Loading AI model...");

                _engine ??= new RemoveNoiseEngine();
                var aiProgress = new Progress<(int percent, string status)>(p =>
                {
                    var permille = Math.Clamp(50 + p.percent * 850 / 100, 50, 900);
                    request.ProgressReporter?.ReportProgress(permille, request.InputPath, Descriptor.DisplayName, p.status);
                });

                tempCleanWav = await _engine
                    .RemoveNoiseAsync(tempExtractedWav, aiProgress, linked.Token, minGain)
                    .ConfigureAwait(false);

                cleanAudioForRemux = tempCleanWav;
            }
            else
            {
                // ── Stereo path ───────────────────────────────────────────────
                // Phase 1 — extract L and R channels in one pass (0→50 permille)
                request.ProgressReporter?.ReportProgress(10, request.InputPath, Descriptor.DisplayName, "Extracting stereo channels...");
                request.ProgressReporter?.ReportState("processing", "Extracting stereo channels...");

                var extractStereoArgs = new[]
                {
                    "-hide_banner", "-loglevel", "error",
                    "-i", request.InputPath,
                    "-filter_complex", "[0:a]pan=mono|c0=c0[L];[0:a]pan=mono|c0=c1[R]",
                    "-map", "[L]", "-c:a", "pcm_s16le", tempExtractedWavL,
                    "-map", "[R]", "-c:a", "pcm_s16le", tempExtractedWavR
                };

                var extractStereoResult = await _ffmpegRunner
                    .RunAsync(ffmpegPath, extractStereoArgs, probe.Duration, null, request.InputPath, Descriptor.DisplayName, "CPU", linked.Token)
                    .ConfigureAwait(false);

                if (extractStereoResult.Canceled)
                {
                    request.ProgressReporter?.ReportState("canceled", "Canceled.");
                    return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, CancellationScope.All);
                }

                if (extractStereoResult.ExitCode != 0)
                {
                    var err = ConversionActionHelper.GetFriendlyFfmpegError(extractStereoResult.StandardError, "Stereo channel extraction failed.");
                    request.ProgressReporter?.ReportState("failed", err);
                    return new ActionExecutionResult(false, err);
                }

                // Phase 2a — AI denoising L (50→520 permille)
                request.ProgressReporter?.ReportProgress(50, request.InputPath, Descriptor.DisplayName, "Denoising L channel...");
                request.ProgressReporter?.ReportState("processing", "Denoising L channel...");

                _engine ??= new RemoveNoiseEngine();
                var aiProgressL = new Progress<(int percent, string status)>(p =>
                {
                    var permille = Math.Clamp(50 + p.percent * 470 / 100, 50, 520);
                    request.ProgressReporter?.ReportProgress(permille, request.InputPath, Descriptor.DisplayName, $"L: {p.status}");
                });

                tempCleanWavL = await _engine
                    .RemoveNoiseAsync(tempExtractedWavL, aiProgressL, linked.Token, minGain)
                    .ConfigureAwait(false);

                // Phase 2b — AI denoising R (520→900 permille)
                request.ProgressReporter?.ReportProgress(520, request.InputPath, Descriptor.DisplayName, "Denoising R channel...");
                request.ProgressReporter?.ReportState("processing", "Denoising R channel...");

                var aiProgressR = new Progress<(int percent, string status)>(p =>
                {
                    var permille = Math.Clamp(520 + p.percent * 380 / 100, 520, 900);
                    request.ProgressReporter?.ReportProgress(permille, request.InputPath, Descriptor.DisplayName, $"R: {p.status}");
                });

                tempCleanWavR = await _engine
                    .RemoveNoiseAsync(tempExtractedWavR, aiProgressR, linked.Token, minGain)
                    .ConfigureAwait(false);

                cleanAudioForRemux = tempCleanWavL;
            }

            // Phase 3 — remux video + clean audio (900→1000 permille)
            request.ProgressReporter?.ReportProgress(900, request.InputPath, Descriptor.DisplayName, "Remuxing...");
            request.ProgressReporter?.ReportState("processing", "Remuxing...");

            outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, "_clean");

            var remuxArgs = processStereo
                ? BuildRemuxArgsStereo(request.InputPath, tempCleanWavL!, tempCleanWavR!, outputPath, remuxAudioCodec)
                : BuildRemuxArgs(request.InputPath, cleanAudioForRemux, outputPath, remuxAudioCodec);

            var remuxResult = await _ffmpegRunner
                .RunAsync(ffmpegPath, remuxArgs, probe.Duration, null, request.InputPath, Descriptor.DisplayName, "CPU", linked.Token)
                .ConfigureAwait(false);

            if (remuxResult.Canceled)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, CancellationScope.All);
            }

            if (remuxResult.ExitCode != 0)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                var err = ConversionActionHelper.GetFriendlyFfmpegError(remuxResult.StandardError, "Remux failed.");
                request.ProgressReporter?.ReportState("failed", err);
                return new ActionExecutionResult(false, err);
            }

            request.ProgressReporter?.ReportProgress(1000, request.InputPath, Descriptor.DisplayName, "Completed.");
            request.ProgressReporter?.ReportState("done", "Completed.");
            return new ActionExecutionResult(true, MediaActionMessages.Completed(Descriptor.DisplayName), outputPath);
        }
        catch (OperationCanceledException)
        {
            TryDeleteTemp(outputPath);
            var scope = GetCancellationScope(cancellationToken, itemCancellationSource.IsCancellationRequested);
            request.ProgressReporter?.ReportState("canceled", "Canceled.");
            return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, scope);
        }
        catch (Exception ex)
        {
            TryDeleteTemp(outputPath);
            request.Logger.Log($"RemoveNoiseVideoAction: failed. {ex}");
            var msg = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed(Descriptor.DisplayName));
            request.ProgressReporter?.ReportState("failed", msg);
            return new ActionExecutionResult(false, msg);
        }
        finally
        {
            monitorStop.Cancel();
            try { await monitorTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            TryDeleteTemp(tempExtractedWav);
            TryDeleteTemp(tempExtractedWavL);
            TryDeleteTemp(tempExtractedWavR);
            TryDeleteTemp(tempCleanWav);
            TryDeleteTemp(tempCleanWavL);
            TryDeleteTemp(tempCleanWavR);
        }
    }

    private static string[] BuildRemuxArgs(
        string videoPath,
        string cleanAudioPath,
        string outputPath,
        string audioCodec)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-i", videoPath,
            "-i", cleanAudioPath,
            "-map", "0:v",
            "-map", "0:s?",
            "-map", "1:a",
            "-c:v", "copy",
            "-c:a", audioCodec,
            "-ar", "48000",
            "-ac", "1"
        };

        if (RemoveNoiseVideoSettings.RemuxAudioNeedsBitrateArg(audioCodec))
        {
            args.Add("-b:a");
            args.Add("192k");
        }

        args.Add(outputPath);
        return args.ToArray();
    }

    private static string[] BuildRemuxArgsStereo(
        string videoPath,
        string cleanAudioPathL,
        string cleanAudioPathR,
        string outputPath,
        string audioCodec)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-i", videoPath,
            "-i", cleanAudioPathL,
            "-i", cleanAudioPathR,
            "-filter_complex", "[1:a][2:a]join=inputs=2:channel_layout=stereo[aout]",
            "-map", "0:v",
            "-map", "0:s?",
            "-map", "[aout]",
            "-c:v", "copy",
            "-c:a", audioCodec,
            "-ar", "48000",
            "-ac", "2"
        };

        if (RemoveNoiseVideoSettings.RemuxAudioNeedsBitrateArg(audioCodec))
        {
            args.Add("-b:a");
            args.Add("256k");
        }

        args.Add(outputPath);
        return args.ToArray();
    }

    private static CancellationScope GetCancellationScope(
        CancellationToken globalToken,
        bool itemCancellationRequested)
    {
        if (globalToken.IsCancellationRequested)
            return CancellationScope.All;
        if (itemCancellationRequested)
            return CancellationScope.CurrentItem;
        return CancellationScope.All;
    }

    private static async Task MonitorCurrentItemCancellationAsync(
        IProgressReporter? progressReporter,
        string inputPath,
        Logging.AppLogger logger,
        CancellationTokenSource itemCancellationSource,
        CancellationToken cancellationToken)
    {
        if (progressReporter is null || string.IsNullOrWhiteSpace(inputPath))
            return;

        while (!cancellationToken.IsCancellationRequested && !itemCancellationSource.IsCancellationRequested)
        {
            if (progressReporter.IsQueueItemCancellationRequested(inputPath))
            {
                logger.Log($"RemoveNoiseVideoAction: current-item cancellation for '{inputPath}'.");
                itemCancellationSource.Cancel();
                return;
            }

            try { await Task.Delay(100, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private static void TryDeleteTemp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
