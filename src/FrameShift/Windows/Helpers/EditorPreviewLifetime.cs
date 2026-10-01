namespace FrameShift.Windows.Helpers;

/// <summary>Reaps the previous preview before starting another; closure awaits the last request.</summary>
internal sealed class EditorPreviewLifetime : IDisposable
{
    private CancellationTokenSource? _cancellation;
    private Task _active = Task.CompletedTask;
    internal bool IsClosing { get; private set; }

    internal Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (IsClosing) return Task.CompletedTask;
        _cancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        return _active = ExecuteAsync(_active, operation, cancellation);
    }

    private async Task ExecuteAsync(Task previous, Func<CancellationToken, Task> operation, CancellationTokenSource cancellation)
    {
        try
        {
            await previous;
            cancellation.Token.ThrowIfCancellationRequested();
            await operation(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            cancellation.Dispose();
        }
    }

    internal void Cancel() => _cancellation?.Cancel();

    internal Task CloseAsync()
    {
        IsClosing = true;
        Cancel();
        return _active;
    }

    public void Dispose() => _ = CloseAsync();
}
