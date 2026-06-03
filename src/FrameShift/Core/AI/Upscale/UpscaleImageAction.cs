using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.AI;
using FrameShift.Core.Actions;
using FrameShift.Core.Progress;

namespace FrameShift.Core.AI.Upscale;

internal sealed class UpscaleImageAction : IFrameShiftAction, IDisposable
{
    private UpscaleEngine? _engine;
    private string? _engineModelId;
    private bool _disposed;

    public ActionDescriptor Descriptor { get; } = new(
        "upscale-image",
        "Upscale Image 4x",
        "Agrandit une image x4 via un modèle IA local (Real-ESRGAN).");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine?.Dispose();
    }

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

        var model = ResolveModel(request.Options);
        if (!ModelLocator.ModelExists(model))
        {
            return new ActionExecutionResult(
                false,
                "AI model not found. Run FrameShift.exe --action upscale-image from Explorer to download it first.");
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
            if (_engine is null || !string.Equals(_engineModelId, model.Id, StringComparison.OrdinalIgnoreCase))
            {
                _engine?.Dispose();
                _engine = new UpscaleEngine(model);
                _engineModelId = model.Id;
            }

            var progress = new Progress<UpscaleProgress>(p =>
            {
                var currentAction = _engine.Provider is "DirectML" or "CPU"
                    ? $"Upscale Image 4x ({_engine.Provider})"
                    : "Upscale Image 4x";
                request.ProgressReporter?.ReportProgress(
                    p.Percent * 10,
                    request.InputPath,
                    currentAction,
                    p.Status);
            });

            var outputPath = await _engine.UpscaleAsync(
                request.InputPath,
                progress,
                linkedCancellationSource.Token).ConfigureAwait(false);

            return new ActionExecutionResult(true, "Image upscaled.", outputPath);
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
            request.Logger.Log($"UpscaleImageAction: failed. {ex}");
            return new ActionExecutionResult(false,
                OnnxProviderHelper.GetCleanUserMessage(ex, "Upscale Image 4x"));
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

    private static UpscaleModelDefinition ResolveModel(IReadOnlyDictionary<string, string>? options)
    {
        if (options is not null &&
            options.TryGetValue(ActionOptionKeys.UpscaleModel, out var modelId) &&
            !string.IsNullOrWhiteSpace(modelId))
        {
            return UpscaleModelCatalog.GetById(modelId) ?? UpscaleModelCatalog.GetDefault();
        }

        return UpscaleModelCatalog.GetDefault();
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
                logger.Log($"UpscaleImageAction: current-item cancellation requested for '{inputPath}'.");
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
