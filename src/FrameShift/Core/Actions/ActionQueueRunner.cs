using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            var currentAction = action.Descriptor.DisplayName;

            if (cancellationToken.IsCancellationRequested || reporter?.IsCancellationRequested == true)
            {
                reporter?.ReportQueueItem(request.InputPath, "canceled", MediaActionMessages.CanceledBeforeProcessing());
                results.Add(new ActionExecutionResult(false, MediaActionMessages.CanceledBeforeProcessing(), null, true, CancellationScope.All));
                continue;
            }

            var queueItemId = CreateQueueItemId(index);
            if (reporter?.IsQueueItemRemovalRequested(request.InputPath, queueItemId) == true)
            {
                results.Add(new ActionExecutionResult(false, L.Get("ActionQueueRunner_Removed_from_queue_ddc111da"), null, true, CancellationScope.CurrentItem));
                continue;
            }

            reporter?.ReportQueueItem(request.InputPath, "processing", L.Format("ActionQueueRunner_Processing_0_1_84b673f6",index + 1,requests.Count));
            reporter?.ReportProgress(0, request.InputPath, currentAction, L.Get("ActionQueueRunner_Starting_1a300c49"));

            var result = await action.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            results.Add(result);

            if (result.Canceled)
            {
                reporter?.ReportQueueItem(request.InputPath, "canceled", result.Message);
                continue;
            }

            if (result.Success)
            {
                reporter?.ReportQueueItem(request.InputPath, "done", result.Message);
                continue;
            }

            reporter?.ReportQueueItem(request.InputPath, "failed", result.Message);
        }

        return results;
    }
}
