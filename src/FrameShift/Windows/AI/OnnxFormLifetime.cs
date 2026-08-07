using System;
using System.Threading;
using System.Threading.Tasks;

namespace FrameShift.Windows.AI;

internal sealed class OnnxFormLifetime : IDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _cancellationSource = new();
    private Task? _activeTask;
    private Task? _closingTask;
    private bool _closing;
    private int _disposed;

    public CancellationToken Token => _cancellationSource.Token;

    public bool IsClosing
    {
        get
        {
            lock (_sync)
            {
                return _closing;
            }
        }
    }

    public Task RunAsync(Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_sync)
        {
            if (_closing || (_activeTask is not null && !_activeTask.IsCompleted))
            {
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeTask = completion.Task;

            Task operationTask;
            try
            {
                operationTask = operation(_cancellationSource.Token);
            }
            catch (Exception ex)
            {
                operationTask = Task.FromException(ex);
            }

            _ = CompleteActiveTaskAsync(operationTask, completion);
            return completion.Task;
        }
    }

    public Task BeginClosingAsync(Func<Task> cleanupAsync, Action<Exception> logFailure)
    {
        ArgumentNullException.ThrowIfNull(cleanupAsync);
        ArgumentNullException.ThrowIfNull(logFailure);

        lock (_sync)
        {
            if (_closingTask is not null)
            {
                return _closingTask;
            }

            _closing = true;
            _cancellationSource.Cancel();
            _closingTask = CompleteClosingAsync(_activeTask, cleanupAsync, logFailure);
            return _closingTask;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cancellationSource.Dispose();
    }

    private async Task CompleteActiveTaskAsync(Task operationTask, TaskCompletionSource completion)
    {
        try
        {
            await operationTask.ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (OperationCanceledException ex)
        {
            completion.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeTask, completion.Task))
                {
                    _activeTask = null;
                }
            }
        }
    }

    private static async Task CompleteClosingAsync(Task? activeTask, Func<Task> cleanupAsync, Action<Exception> logFailure)
    {
        if (activeTask is not null)
        {
            try
            {
                await activeTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logFailure(ex);
            }
        }

        try
        {
            await cleanupAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logFailure(ex);
        }
    }
}
