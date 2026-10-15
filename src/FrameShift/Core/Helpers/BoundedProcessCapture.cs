using System.Diagnostics;
using System.Text;

namespace FrameShift.Core.Helpers;

internal static class BoundedProcessCapture
{
    internal sealed record Result(string Output, string Error, bool ExceededLimit, bool TimedOut);

    internal static async Task<Result> ReadAsync(Process process, Action kill, Func<Task<bool>> confirmStop,
        TimeSpan timeout, int maximumOutput, CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(timeout);
        using var overflow = new CancellationTokenSource();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token, overflow.Token);
        using var reads = new CancellationTokenSource();
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = stop.Token.Register(() => { kill(); signal.TrySetResult(); });
        var output = new StringBuilder();
        var error = new BoundedProcessText(BoundedProcessText.DiagnosticLimit);
        var outputTask = ReadOutput();
        var errorTask = ReadError();
        var exit = process.WaitForExitAsync();
        var terminated = true;
        if (await Task.WhenAny(exit, signal.Task).ConfigureAwait(false) != exit)
            terminated = await confirmStop().ConfigureAwait(false);
        else await exit.ConfigureAwait(false);
        var drain = Task.WhenAll(outputTask, errorTask);
        var drainExpired = await Task.WhenAny(drain, Task.Delay(1500)).ConfigureAwait(false) != drain;
        if (drainExpired) reads.Cancel();
        try { await drain.ConfigureAwait(false); }
        catch (OperationCanceledException) when (reads.IsCancellationRequested) { }
        if (!terminated) throw new TimeoutException("The media process did not exit after its bounded stop attempts.");
        token.ThrowIfCancellationRequested();
        var message = overflow.IsCancellationRequested ? "Captured output exceeded the permitted size." : deadline.IsCancellationRequested || drainExpired ? "The media probe/capture timed out." : null;
        if (message is not null) error.Append((Environment.NewLine + message).AsSpan());
        return new(overflow.IsCancellationRequested || drainExpired ? "" : output.ToString(), error.ToString(), overflow.IsCancellationRequested, deadline.IsCancellationRequested || drainExpired);

        async Task ReadOutput()
        {
            var buffer = new char[4096];
            while (true)
            {
                var count = await process.StandardOutput.ReadAsync(buffer.AsMemory(), reads.Token).ConfigureAwait(false);
                if (count == 0) return;
                var available = maximumOutput - output.Length;
                output.Append(buffer, 0, Math.Min(count, available));
                if (count > available) overflow.Cancel();
            }
        }
        async Task ReadError()
        {
            var buffer = new char[4096];
            while (true)
            {
                var count = await process.StandardError.ReadAsync(buffer.AsMemory(), reads.Token).ConfigureAwait(false);
                if (count == 0) return;
                error.Append(buffer.AsSpan(0, count));
            }
        }
    }
}
