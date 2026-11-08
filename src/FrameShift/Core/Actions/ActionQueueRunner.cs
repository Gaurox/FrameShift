using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Progress;

namespace FrameShift.Core.Actions;

public sealed class ActionQueueRunner
{
    internal static string CreateQueueItemId(int index) => $"generic:{index}";

    public async Task<IReadOnlyList<ActionExecutionResult>> RunAsync(
        IFrameShiftAction action,
        IReadOnlyList<ActionRequest> requests,
        CancellationToken cancellationToken)
    {
        var results = new List<ActionExecutionResult>(requests.Count);
        if (requests.Count == 0)
        {
            return results;
        }

        var reporter = requests[0].ProgressReporter;
        reporter?.ReportQueue(requests.Select(request => request.InputPath).ToArray());
        // Populate descriptions before execution, including pending duplicate paths.
        for (var i = 0; i < requests.Count; i++)
            if (requests[i].QueueItemDescription is { } description)
                reporter?.ReportQueueItem(requests[i].InputPath, CreateQueueItemId(i), "queued", description);

        var globalCanceled = false;
        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            var currentAction = action.Descriptor.DisplayName;
            var queueItemId = CreateQueueItemId(index);
            var scoped = request.ProgressReporter is { } progress
                ? new QueueItemProgressReporter(progress, request.InputPath, queueItemId, request.QueueItemDescription) : null;
            request = request with { ProgressReporter = scoped };

            if (globalCanceled || cancellationToken.IsCancellationRequested || reporter?.IsCancellationRequested == true)
            {
                scoped?.ReportQueueItem(request.InputPath, "canceled", MediaActionMessages.CanceledBeforeProcessing());
                results.Add(new ActionExecutionResult(false, MediaActionMessages.CanceledBeforeProcessing(), null, true, CancellationScope.All));
                continue;
            }

            if (reporter?.IsQueueItemRemovalRequested(request.InputPath, queueItemId) == true)
            {
                results.Add(new ActionExecutionResult(false, L.Get("ActionQueueRunner_Removed_from_queue_ddc111da"), null, true, CancellationScope.CurrentItem));
                continue;
            }

            scoped?.ReportQueueItem(request.InputPath, "processing", L.Format("ActionQueueRunner_Processing_0_1_84b673f6",index + 1,requests.Count));
            scoped?.ReportProgress(0, request.InputPath, currentAction, L.Get("ActionQueueRunner_Starting_1a300c49"));

            ActionExecutionResult result;
            try { result = await action.ExecuteAsync(request, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                result = new(false, L.Get("CutVideoAction_Canceled_437433cc"), null, true,
                    cancellationToken.IsCancellationRequested || reporter?.IsCancellationRequested == true
                        ? CancellationScope.All : scoped?.IsQueueItemCancellationRequested(request.InputPath) == true
                            ? CancellationScope.CurrentItem : CancellationScope.All);
            }
            catch (Exception ex)
            {
                request.Logger.Log($"Queue item {queueItemId} ({request.QueueItemDescription}) failed: {ex}");
                result = new(false, ex.Message);
            }
            results.Add(result);
            globalCanceled |= result.CancellationScope == CancellationScope.All;

            if (result.Canceled)
            {
                scoped?.ReportQueueItem(request.InputPath, "canceled", result.Message);
                continue;
            }

            if (result.Success)
            {
                scoped?.ReportQueueItem(request.InputPath, "done", result.Message);
                continue;
            }

            scoped?.ReportQueueItem(request.InputPath, "failed", result.Message);
        }

        return results;
    }
}
