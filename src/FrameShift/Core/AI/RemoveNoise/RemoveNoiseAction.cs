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

internal sealed class RemoveNoiseAction : IFrameShiftAction
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public RemoveNoiseAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner  = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator   = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "remove-noise",
        "Remove Noise",
        "Supprime le bruit de fond d'un fichier audio via un modèle IA local (DeepFilterNet3).");

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath))
            return Task.FromResult(new ActionExecutionResult(false, MediaActionMessages.InputPathRequired()));

        if (!File.Exists(request.InputPath))
            return Task.FromResult(new ActionExecutionResult(false, MediaActionMessages.InputFileNotFound(request.InputPath)));

        var ext = Path.GetExtension(request.InputPath).ToLowerInvariant();
        if (!IsAudioExtensionSupported(ext))
            return Task.FromResult(new ActionExecutionResult(
                false,
                MediaActionMessages.UnsupportedSourceFormat(ext, GetSupportedExtensionsText())));

        if (!DeepFilterNetModelLocator.AllModelsExist())
            return Task.FromResult(new ActionExecutionResult(
                false,
                "AI model not found. Run FrameShift.exe --action remove-noise from Explorer to download it first."));

        return ExecuteCoreAsync(request, cancellationToken);
    }

    internal static bool IsAudioExtensionSupported(string ext) =>
        ext is ".wav" or ".flac" or ".mp3" or ".ogg" or ".m4a";

    internal static string GetSupportedExtensionsText() =>
        ".wav, .flac, .mp3, .ogg, .m4a";

    private async Task<ActionExecutionResult> ExecuteCoreAsync(
        ActionRequest request,
        CancellationToken cancellationToken)
    {
        var minGain = RemoveNoiseAudioSettings.ToMinGain(
            ConversionActionHelper.GetOption(request, ActionOptionKeys.NoiseStrength, RemoveNoiseAudioSettings.StrengthMaximum));

        var requestedStereo = string.Equals(
            ConversionActionHelper.GetOption(request, ActionOptionKeys.ProcessStereoAudio, "false"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        var ext = Path.GetExtension(request.InputPath!).ToLowerInvariant();

        var processStereo = false;
        if (requestedStereo)
        {
            try
            {
                var ffprobePath  = _toolLocator.ResolveFfprobePath();
                var probeAttempt = await _ffprobeRunner
                    .TryProbeMediaAsync(ffprobePath, request.InputPath!, cancellationToken)
                    .ConfigureAwait(false);
                if (probeAttempt.Probe is not null)
                    processStereo = probeAttempt.Probe.PrimaryAudioChannels >= 2;
            }
            catch { /* non-fatal: fall back to mono */ }
        }

        var tempId            = Guid.NewGuid().ToString("N")[..12];
        var tempExtractedWavL = Path.Combine(Path.GetTempPath(), $"fs_rna_{tempId}_L.wav");
        var tempExtractedWavR = Path.Combine(Path.GetTempPath(), $"fs_rna_{tempId}_R.wav");
        string? tempCleanWavL = null;
        string? tempCleanWavR = null;
        string? outputPath    = null;

        using var itemCancellationSource = new CancellationTokenSource();
        using var linked     = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, itemCancellationSource.Token);
        using var monitorStop = new CancellationTokenSource();
        var monitorTask = MonitorCurrentItemCancellationAsync(
            request.ProgressReporter, request.InputPath!, request.Logger, itemCancellationSource, monitorStop.Token);

        try
        {
            if (!processStereo)
            {
                // ── Mono path ────────────────────────────────────────────────
                request.ProgressReporter?.ReportState("processing", "Loading AI model...");

                using var engine = new RemoveNoiseEngine();
                var progress = new Progress<(int percent, string status)>(p =>
                {
                    request.ProgressReporter?.ReportProgress(
                        Math.Clamp(p.percent * 10, 0, 1000),
                        request.InputPath!,
                        Descriptor.DisplayName,
                        p.status);
                });

                outputPath = await engine
                    .RemoveNoiseAsync(request.InputPath!, progress, linked.Token, minGain)
                    .ConfigureAwait(false);
            }
            else
            {
                // ── Stereo path ───────────────────────────────────────────────
                var ffmpegPath = _toolLocator.ResolveFfmpegPath();

                // Phase 1 — extract L and R channels in one pass (0→50 permille)
                request.ProgressReporter?.ReportProgress(10, request.InputPath!, Descriptor.DisplayName, "Extracting stereo channels...");
                request.ProgressReporter?.ReportState("processing", "Extracting stereo channels...");

                var extractArgs = new[]
                {
                    "-hide_banner", "-loglevel", "error",
                    "-i", request.InputPath!,
                    "-filter_complex", "[0:a]pan=mono|c0=c0[L];[0:a]pan=mono|c0=c1[R]",
                    "-map", "[L]", "-c:a", "pcm_s16le", tempExtractedWavL,
                    "-map", "[R]", "-c:a", "pcm_s16le", tempExtractedWavR
                };

                var extractResult = await _ffmpegRunner
                    .RunAsync(ffmpegPath, extractArgs, null, null, request.InputPath!, Descriptor.DisplayName, "CPU", linked.Token)
                    .ConfigureAwait(false);

                if (extractResult.Canceled)
                {
                    request.ProgressReporter?.ReportState("canceled", "Canceled.");
                    return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, CancellationScope.All);
                }

                if (extractResult.ExitCode != 0)
                {
                    var err = ConversionActionHelper.GetFriendlyFfmpegError(extractResult.StandardError, "Stereo channel extraction failed.");
                    request.ProgressReporter?.ReportState("failed", err);
                    return new ActionExecutionResult(false, err);
                }

                // Phase 2a — AI denoising L (50→520 permille)
                request.ProgressReporter?.ReportProgress(50, request.InputPath!, Descriptor.DisplayName, "Denoising L channel...");
                request.ProgressReporter?.ReportState("processing", "Denoising L channel...");

                using var engineStereo = new RemoveNoiseEngine();
                var aiProgressL = new Progress<(int percent, string status)>(p =>
                {
                    var permille = Math.Clamp(50 + p.percent * 470 / 100, 50, 520);
                    request.ProgressReporter?.ReportProgress(permille, request.InputPath!, Descriptor.DisplayName, $"L: {p.status}");
                });

                tempCleanWavL = await engineStereo
                    .RemoveNoiseAsync(tempExtractedWavL, aiProgressL, linked.Token, minGain)
                    .ConfigureAwait(false);

                // Phase 2b — AI denoising R (520→900 permille)
                request.ProgressReporter?.ReportProgress(520, request.InputPath!, Descriptor.DisplayName, "Denoising R channel...");
                request.ProgressReporter?.ReportState("processing", "Denoising R channel...");

                var aiProgressR = new Progress<(int percent, string status)>(p =>
                {
                    var permille = Math.Clamp(520 + p.percent * 380 / 100, 520, 900);
                    request.ProgressReporter?.ReportProgress(permille, request.InputPath!, Descriptor.DisplayName, $"R: {p.status}");
                });

                tempCleanWavR = await engineStereo
                    .RemoveNoiseAsync(tempExtractedWavR, aiProgressR, linked.Token, minGain)
                    .ConfigureAwait(false);

                // Phase 3 — merge L+R → stereo audio output (900→1000 permille)
                request.ProgressReporter?.ReportProgress(900, request.InputPath!, Descriptor.DisplayName, "Merging stereo channels...");
                request.ProgressReporter?.ReportState("processing", "Merging stereo channels...");

                outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath!, "_clean");
                var codec = RemoveNoiseAudioSettings.GetOutputAudioCodec(ext);

                var mergeArgs = new List<string>
                {
                    "-hide_banner", "-loglevel", "error",
                    "-stats_period", "0.25",
                    "-progress", "pipe:1",
                    "-nostats",
                    "-i", tempCleanWavL!,
                    "-i", tempCleanWavR!,
                    "-filter_complex", "[0:a][1:a]join=inputs=2:channel_layout=stereo[aout]",
                    "-map", "[aout]",
                    "-c:a", codec,
                    "-ar", "48000",
                    "-ac", "2"
                };

                if (RemoveNoiseAudioSettings.OutputAudioNeedsBitrateArg(codec))
                {
                    mergeArgs.Add("-b:a");
                    mergeArgs.Add("192k");
                }

                mergeArgs.Add(outputPath);

                var mergeResult = await _ffmpegRunner
                    .RunAsync(ffmpegPath, mergeArgs.ToArray(), null, null, request.InputPath!, Descriptor.DisplayName, "CPU", linked.Token)
                    .ConfigureAwait(false);

                if (mergeResult.Canceled)
                {
                    TryDeleteTemp(outputPath);
                    request.ProgressReporter?.ReportState("canceled", "Canceled.");
                    return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, CancellationScope.All);
                }

                if (mergeResult.ExitCode != 0)
                {
                    TryDeleteTemp(outputPath);
                    var err = ConversionActionHelper.GetFriendlyFfmpegError(mergeResult.StandardError, "Stereo merge failed.");
                    request.ProgressReporter?.ReportState("failed", err);
                    return new ActionExecutionResult(false, err);
                }
            }

            request.ProgressReporter?.ReportProgress(1000, request.InputPath!, Descriptor.DisplayName, "Completed.");
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
            request.Logger.Log($"RemoveNoiseAction: failed. {ex}");
            var msg = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed(Descriptor.DisplayName));
            request.ProgressReporter?.ReportState("failed", msg);
            return new ActionExecutionResult(false, msg);
        }
        finally
        {
            monitorStop.Cancel();
            try { await monitorTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            TryDeleteTemp(tempExtractedWavL);
            TryDeleteTemp(tempExtractedWavR);
            TryDeleteTemp(tempCleanWavL);
            TryDeleteTemp(tempCleanWavR);
        }
    }

    private static CancellationScope GetCancellationScope(CancellationToken globalToken, bool itemCancellationRequested)
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
        FrameShift.Core.Logging.AppLogger logger,
        CancellationTokenSource itemCancellationSource,
        CancellationToken cancellationToken)
    {
        if (progressReporter is null || string.IsNullOrWhiteSpace(inputPath))
            return;

        while (!cancellationToken.IsCancellationRequested && !itemCancellationSource.IsCancellationRequested)
        {
            if (progressReporter.IsQueueItemCancellationRequested(inputPath))
            {
                logger.Log($"RemoveNoiseAction: current-item cancellation for '{inputPath}'.");
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
