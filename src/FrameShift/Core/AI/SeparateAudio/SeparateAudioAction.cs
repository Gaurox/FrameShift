using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Actions;
using FrameShift.Core.Progress;

namespace FrameShift.Core.AI.SeparateAudio;

internal sealed class SeparateAudioAction : IFrameShiftAction
{
    public ActionDescriptor Descriptor { get; } = new(
        "separate-audio",
        "Audio Separation",
        "Sépare les pistes audio (voix, batterie, basse, autres) via un modèle IA local.");

    public async Task<ActionExecutionResult> ExecuteAsync(
        ActionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath))
            return new ActionExecutionResult(false, MediaActionMessages.InputPathRequired());

        if (!File.Exists(request.InputPath))
            return new ActionExecutionResult(false, MediaActionMessages.InputFileNotFound(request.InputPath));

        var sourceExtension = Path.GetExtension(request.InputPath).ToLowerInvariant();
        if (!IsAudioExtensionSupported(sourceExtension))
            return new ActionExecutionResult(
                false,
                MediaActionMessages.UnsupportedSourceFormat(sourceExtension, GetSupportedExtensionsText()));

        var stems = StemSelection.FromOptions(request.Options ?? new Dictionary<string, string>());
        if (!stems.HasAnyStem)
            return new ActionExecutionResult(false, "No stems selected. Select at least one stem to extract.");

        var options = request.Options ?? new Dictionary<string, string>();
        var preferGpu = !string.Equals(
            options.TryGetValue(ActionOptionKeys.SeparateEngine, out var eng) ? eng : "auto",
            "cpu",
            StringComparison.OrdinalIgnoreCase);

        var modelPath = preferGpu ? ModelLocator.GpuModelPath : ModelLocator.CpuModelPath;
        if (!File.Exists(modelPath))
            return new ActionExecutionResult(
                false,
                "AI model not found. Run FrameShift.exe --action separate-audio from Explorer to download it first.");

        request.ProgressReporter?.ReportState("processing", "Loading model...");

        using var itemCancellationSource = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, itemCancellationSource.Token);
        using var monitorStop = new CancellationTokenSource();
        var monitorTask = MonitorCurrentItemCancellationAsync(
            request.ProgressReporter, request.InputPath, request.Logger, itemCancellationSource, monitorStop.Token);

        try
        {
            using var engine = new AudioSeparationEngine();

            var progress = new Progress<AudioSeparationProgress>(p =>
            {
                var label = $"Audio Separation ({engine.Provider})";
                request.ProgressReporter?.ReportProgress(p.Percent * 10, request.InputPath, label, p.Status);
            });

            await engine.SeparateAsync(request.InputPath, stems, preferGpu, progress, linked.Token)
                .ConfigureAwait(false);

            request.ProgressReporter?.ReportProgress(1000, request.InputPath, $"Audio Separation ({engine.Provider})", "Completed.");
            request.ProgressReporter?.ReportState("done", "Completed.");

            return new ActionExecutionResult(true, "Audio separation complete.");
        }
        catch (OperationCanceledException)
        {
            var scope = GetCancellationScope(
                request.ProgressReporter, request.InputPath, cancellationToken, itemCancellationSource.IsCancellationRequested);
            return new ActionExecutionResult(false, "Canceled.", null, true, scope);
        }
        catch (Exception ex)
        {
            request.Logger.Log($"SeparateAudioAction: failed. {ex}");
            return new ActionExecutionResult(false, $"Audio Separation failed: {ex.Message}");
        }
        finally
        {
            monitorStop.Cancel();
            try { await monitorTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    private static bool IsAudioExtensionSupported(string ext) =>
        ext is ".wav" or ".mp3" or ".flac" or ".m4a" or ".ogg" or ".aac" or ".wma";

    internal static string GetSupportedExtensionsText() =>
        ".wav, .mp3, .flac, .m4a, .ogg, .aac, .wma";

    private static CancellationScope GetCancellationScope(
        IProgressReporter? progressReporter,
        string inputPath,
        CancellationToken cancellationToken,
        bool currentItemCancellationRequested)
    {
        if (cancellationToken.IsCancellationRequested)
            return CancellationScope.All;

        if (currentItemCancellationRequested ||
            progressReporter?.IsQueueItemCancellationRequested(inputPath) == true)
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
                logger.Log($"SeparateAudioAction: current-item cancellation for '{inputPath}'.");
                itemCancellationSource.Cancel();
                return;
            }

            try { await Task.Delay(100, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }
}
