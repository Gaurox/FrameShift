using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Actions;
using FrameShift.Core.Progress;

namespace FrameShift.Core.AI.RemoveBackground;

internal sealed class RemoveBackgroundAction : IFrameShiftAction
{
    public ActionDescriptor Descriptor { get; } = new(
        "remove-background",
        "Remove Background",
        "Supprime le fond d'une image via un modèle IA local.");

    public async Task<ActionExecutionResult> ExecuteAsync(
        ActionRequest request,
        CancellationToken cancellationToken)
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
        if (sourceExtension is not ".png" and not ".jpg" and not ".jpeg" and not ".webp" and not ".bmp")
        {
            return new ActionExecutionResult(
                false,
                MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ".png, .jpg, .jpeg, .webp, .bmp"));
        }

        if (!ModelLocator.ModelExists())
        {
            return new ActionExecutionResult(
                false,
                "AI model not found. Run FrameShift.exe --action remove-background from Explorer to download it first.");
        }

        request.ProgressReporter?.ReportState("processing", "Loading ONNX model...");

        using var itemCancellationSource = new CancellationTokenSource();
        using var linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            itemCancellationSource.Token);
        using var monitorStopSource = new CancellationTokenSource();
        var monitorTask = MonitorCurrentItemCancellationAsync(
            request.ProgressReporter,
            request.InputPath,
            request.Logger,
            itemCancellationSource,
            monitorStopSource.Token);

        try
        {
            using var engine = new BackgroundRemovalEngine();

            var progress = new Progress<InferenceProgress>(p =>
            {
                var currentAction = engine.Provider is "DirectML" or "CPU"
                    ? $"Remove Background ({engine.Provider})"
                    : "Remove Background";
                request.ProgressReporter?.ReportProgress(
                    p.Percent * 10,
                    request.InputPath,
                    currentAction,
                    p.Status);
            });

            var outputPath = await engine.RemoveBackgroundAsync(
                request.InputPath,
                progress,
                linkedCancellationSource.Token).ConfigureAwait(false);

            return new ActionExecutionResult(true, "Background removed.", outputPath);
        }
        catch (OperationCanceledException)
        {
            var cancellationScope = GetCancellationScope(
                request.ProgressReporter,
                request.InputPath,
                cancellationToken,
                itemCancellationSource.IsCancellationRequested);
            return new ActionExecutionResult(false, "Canceled.", null, true, cancellationScope);
        }
        catch (Exception ex)
        {
            request.Logger.Log($"RemoveBackgroundAction: failed. {ex}");
            return new ActionExecutionResult(false, $"Remove Background failed: {ex.Message}");
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
        }
    }

    private static CancellationScope GetCancellationScope(
        IProgressReporter? progressReporter,
        string inputPath,
        CancellationToken cancellationToken,
        bool currentItemCancellationRequested)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return CancellationScope.All;
        }

        if (currentItemCancellationRequested ||
            progressReporter?.IsQueueItemCancellationRequested(inputPath) == true)
        {
            return CancellationScope.CurrentItem;
        }

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
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested && !itemCancellationSource.IsCancellationRequested)
        {
            if (progressReporter.IsQueueItemCancellationRequested(inputPath))
            {
                logger.Log($"RemoveBackgroundAction: current-item cancellation requested for '{inputPath}'.");
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
}
