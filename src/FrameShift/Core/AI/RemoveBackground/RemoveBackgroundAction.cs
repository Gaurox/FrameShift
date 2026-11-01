using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.AI;
using FrameShift.Core.Actions;
using FrameShift.Core.Progress;

namespace FrameShift.Core.AI.RemoveBackground;

internal sealed class RemoveBackgroundAction : IFrameShiftAction, IDisposable
{
    private BackgroundRemovalEngine? _engine;
    private string? _engineModelId;
    private bool _disposed;

    public ActionDescriptor Descriptor { get; } = new(
        "remove-background",
        L.Get("RemoveBackgroundAction_Remove_Background_15a097fe"),
        L.Get("RemoveBackgroundAction_Supprime_le_fond_d_une_image_via_un__339ed1c6"));

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
            var notFoundMessage = model.ManualOnly
                ? L.Format("RemoveBackgroundAction_BRIA_model_not_installed_Place_0_in__5df494e1",model.FileName,ModelLocator.GetModelDirectory(model),model.InfoPageUrl)
                : L.Get("RemoveBackgroundAction_AI_model_not_found_Run_FrameShift_ex_799b5c88");
            return new ActionExecutionResult(false, notFoundMessage);
        }

        request.ProgressReporter?.ReportState("processing", L.Get("RemoveBackgroundAction_Loading_ONNX_model_559af544"));

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
                _engine = new BackgroundRemovalEngine(model);
                _engineModelId = model.Id;
            }

            var progress = new Progress<InferenceProgress>(p =>
            {
                var modeLabel = GetModeLabel(model);
                var currentAction = _engine.Provider is "DirectML" or "CPU"
                    ? L.Format("RemoveBackgroundAction_Remove_Background_0_1_45bf95b5",modeLabel,_engine.Provider)
                    : L.Format("RemoveBackgroundAction_Remove_Background_0_fb12371a",modeLabel);
                request.ProgressReporter?.ReportProgress(
                    p.Percent * 10,
                    request.InputPath,
                    currentAction,
                    p.Status);
            });

            var outputPath = await _engine.RemoveBackgroundAsync(
                request.InputPath,
                progress,
                linkedCancellationSource.Token).ConfigureAwait(false);

            return new ActionExecutionResult(true, L.Get("RemoveBackgroundAction_Background_removed_a90a3f73"), outputPath);
        }
        catch (OperationCanceledException)
        {
            var cancellationScope = GetCancellationScope(
                request.ProgressReporter,
                request.InputPath,
                cancellationToken,
                itemCancellationSource.IsCancellationRequested);
            return new ActionExecutionResult(false, L.Get("RemoveBackgroundAction_Canceled_437433cc"), null, true, cancellationScope);
        }
        catch (Exception ex)
        {
            request.Logger.Log($"RemoveBackgroundAction: failed. {ex}");
            return new ActionExecutionResult(false,
                OnnxProviderHelper.GetCleanUserMessage(ex, L.Get("RemoveBackgroundAction_Remove_Background_15a097fe")));
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

    private static string GetModeLabel(BackgroundRemovalModelDefinition model)
    {
        if (!string.IsNullOrWhiteSpace(model.ModeLabel))
        {
            return model.ModeLabel!;
        }

        if (string.Equals(model.Id, "fast", StringComparison.OrdinalIgnoreCase))
        {
            return L.Get("RemoveBackgroundAction_Fast_59b32028");
        }

        if (string.Equals(model.Id, "high-resolution-general", StringComparison.OrdinalIgnoreCase))
        {
            return L.Get("RemoveBackgroundAction_High_Resolution_General_a261f555");
        }

        return L.Get("RemoveBackgroundAction_High_Resolution_Matting_8b6eb99f");
    }

    private static BackgroundRemovalModelDefinition ResolveModel(
        IReadOnlyDictionary<string, string>? options)
    {
        if (options is not null &&
            options.TryGetValue(ActionOptionKeys.BackgroundRemovalModel, out var modelId) &&
            !string.IsNullOrWhiteSpace(modelId))
        {
            return BackgroundRemovalModelCatalog.GetById(modelId) ??
                BackgroundRemovalModelCatalog.GetDefault();
        }

        return BackgroundRemovalModelCatalog.GetDefault();
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
