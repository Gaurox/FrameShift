namespace FrameShift.Core.Progress;

/// <summary>Routes an existing action's path-based calls to one queue occurrence.</summary>
internal sealed class QueueItemProgressReporter(IProgressReporter parent, string source, string id, string? description) : IProgressReporter
{
    public bool IsCancellationRequested => parent.IsCancellationRequested;
    public bool IsQueueItemRemovalRequested(string inputPath) => parent.IsQueueItemRemovalRequested(source, id);
    public bool IsQueueItemRemovalRequested(string inputPath, string queueItemId) => parent.IsQueueItemRemovalRequested(source, id);
    public bool IsQueueItemCancellationRequested(string inputPath) => parent.IsQueueItemCancellationRequested(source, id);
    public void ReportQueue(IReadOnlyList<string> items) => parent.ReportQueue(items);
    public void ReportProgress(int value, string? file, string? action, string? message, string? etaText = null)
        => parent.ReportProgress(value, source, action, Describe(message), etaText, id);
    public void ReportState(string state, string? message = null) => parent.ReportState(state, Describe(message));
    public void ReportQueueItem(string file, string state, string? message = null)
        => parent.ReportQueueItem(source, id, state, Describe(message));
    private string? Describe(string? message) => string.IsNullOrWhiteSpace(description) ? message
        : string.IsNullOrWhiteSpace(message) ? description : description + Environment.NewLine + message;
}
