using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Actions;
using FrameShift.Core.Progress;

namespace FrameShift.Core.AI.RemoveNoise;

internal sealed class RemoveNoiseAction : IFrameShiftAction
{
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

        return ExecuteCoreAsync(request, cancellationToken);
    }

    internal static bool IsAudioExtensionSupported(string ext) =>
        ext is ".wav" or ".flac" or ".mp3" or ".ogg" or ".m4a";

    internal static string GetSupportedExtensionsText() =>
        ".wav, .flac, .mp3, .ogg, .m4a";

    private static async Task<ActionExecutionResult> ExecuteCoreAsync(
        ActionRequest request,
        CancellationToken cancellationToken)
    {
        if (!DeepFilterNetModelLocator.AllModelsExist())
        {
            return new ActionExecutionResult(
                false,
                "AI model not found. Run FrameShift.exe --action remove-noise from Explorer to download it first.");
        }

        using var itemCancellationSource = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, itemCancellationSource.Token);
        using var monitorStop = new CancellationTokenSource();
        var monitorTask = MonitorCurrentItemCancellationAsync(
            request.ProgressReporter, request.InputPath, request.Logger, itemCancellationSource, monitorStop.Token);

        try
        {
            using var engine = new RemoveNoiseEngine();
            request.ProgressReporter?.ReportState("processing", "Loading AI model...");

            var progress = new Progress<(int percent, string status)>(p =>
            {
                request.ProgressReporter?.ReportProgress(
                    Math.Clamp(p.percent * 10, 0, 1000),
                    request.InputPath,
                    "Remove Noise",
                    p.status);
            });

            string outputPath = await engine.RemoveNoiseAsync(request.InputPath, progress, linked.Token)
                .ConfigureAwait(false);

            request.ProgressReporter?.ReportProgress(1000, request.InputPath, "Remove Noise", "Completed.");
            request.ProgressReporter?.ReportState("done", "Completed.");
            return new ActionExecutionResult(true, "Noise removal complete.", outputPath);
        }
        catch (OperationCanceledException)
        {
            var scope = GetCancellationScope(
                request.ProgressReporter, request.InputPath, cancellationToken, itemCancellationSource.IsCancellationRequested);
            return new ActionExecutionResult(false, "Canceled.", null, true, scope);
        }
        catch (Exception ex)
        {
            request.Logger.Log($"RemoveNoiseAction: failed. {ex}");
            return new ActionExecutionResult(false, $"Remove Noise failed: {ex.Message}");
        }
        finally
        {
            monitorStop.Cancel();
            try { await monitorTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

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
                logger.Log($"RemoveNoiseAction: current-item cancellation for '{inputPath}'.");
                itemCancellationSource.Cancel();
                return;
            }

            try { await Task.Delay(100, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }
}
