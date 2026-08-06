namespace FrameShift.Core.Progress;

public interface IProgressReporter
{
    bool IsCancellationRequested { get; }

    bool IsQueueItemRemovalRequested(string inputPath);

    /// <summary>
    /// Checks removal for one concrete queue occurrence. The default keeps existing
    /// reporters source-compatible; queue-aware reporters should use the item ID.
    /// </summary>
    bool IsQueueItemRemovalRequested(string inputPath, string queueItemId)
        => IsQueueItemRemovalRequested(inputPath);

    bool IsQueueItemCancellationRequested(string inputPath);

    void ReportQueue(IReadOnlyList<string> items);

    void ReportProgress(int progressValue, string? currentFile, string? currentAction, string? message, string? etaText = null);

    void ReportState(string state, string? message = null);

    void ReportQueueItem(string currentFile, string state, string? message = null);
}
