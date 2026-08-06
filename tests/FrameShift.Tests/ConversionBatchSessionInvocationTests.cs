using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using FrameShift.Windows.Batch;
using Xunit;

namespace FrameShift.Tests;

public sealed class ConversionBatchSessionInvocationTests
{
    [Fact]
    public void SendPathsToPrimaryInstance_PreservesTwoIdenticalOccurrences()
    {
        using var session = CreateSession(out var definition, new RecordingAction());
        session.Initialize([], CancellationToken.None);

        Assert.True(ConversionBatchSession.SendPathsToPrimaryInstance(
            definition,
            ["same.item", "same.item"]));

        WaitUntil(() => session.GetPendingQueueItems().Count == 2);
        var items = session.GetPendingQueueItems();
        Assert.Equal(new[] { "same.item", "same.item" }, items.Select(item => item.InputPath));
        Assert.NotEqual(items[0].QueueItemId, items[1].QueueItemId);
    }

    [Fact]
    public void InvocationsDuringDebounceAndAfterPickerResolution_AreAccepted()
    {
        using var session = CreateSession(out var definition, new RecordingAction());
        session.Initialize(["initial.item"], CancellationToken.None);

        Assert.True(ConversionBatchSession.SendPathsToPrimaryInstance(definition, ["during-debounce.item"]));
        session.WaitForPickerDebounce(CancellationToken.None);
        Assert.Equal(2, session.GetPendingQueueItems().Count);

        // The specialized picker is owned by ProgramCompressBatch; setting shared options here
        // represents the point immediately after that picker has completed.
        session.SetSharedOptions(new Dictionary<string, string> { [ActionOptionKeys.Profile] = "shared" });
        Assert.True(ConversionBatchSession.SendPathsToPrimaryInstance(definition, ["after-picker.item"]));

        WaitUntil(() => session.GetPendingQueueItems().Count == 3);
        Assert.Contains(session.GetPendingQueueItems(), item => item.InputPath == "after-picker.item");
    }

    [Fact]
    public async Task InvocationDuringActiveProcessing_IsAcceptedAndExecuted()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellationSource = new CancellationTokenSource();
        var action = new RecordingAction(async (request, cancellationToken) =>
        {
            if (request.InputPath == "first.item")
            {
                started.Set();
                release.Wait(cancellationToken);
            }

            await Task.CompletedTask;
            return new ActionExecutionResult(true, "Done.");
        });

        using var session = CreateSession(out var definition, action);
        session.Initialize(["first.item"], cancellationSource.Token);
        session.SetSharedOptions(new Dictionary<string, string>());
        var processingTask = session.StartProcessing(cancellationSource.Token);

        Assert.True(started.Wait(TimeSpan.FromSeconds(3)));
        Assert.True(ConversionBatchSession.SendPathsToPrimaryInstance(definition, ["late.item"]));
        release.Set();

        WaitUntil(() => action.ExecutedPaths.Count == 2);
        cancellationSource.Cancel();
        await processingTask;

        Assert.Equal(new[] { "first.item", "late.item" }, action.ExecutedPaths);
    }

    [Fact]
    public void SecondarySuccessIsReturnedOnlyAfterTheInvocationIsInThePrimaryQueue()
    {
        using var session = CreateSession(out var definition, new RecordingAction());
        session.Initialize([], CancellationToken.None);

        var accepted = ConversionBatchSession.SendPathsToPrimaryInstance(definition, ["confirmed.item"]);

        Assert.True(accepted);
        var item = Assert.Single(session.GetPendingQueueItems());
        Assert.Equal("confirmed.item", item.InputPath);
    }

    [Fact]
    public void ClosingBoundary_DoesNotAcknowledgeAnAlreadyConnectedLateInvocation()
    {
        using var session = CreateSession(out var definition, new RecordingAction());
        session.Initialize([], CancellationToken.None);

        using var client = new NamedPipeClientStream(".", definition.PipeName, PipeDirection.InOut);
        client.Connect(3000);
        session.Close();

        try
        {
            var payload = Encoding.UTF8.GetBytes("{\"invocationId\":\"late\",\"items\":[{\"path\":\"late.item\"}]}\n");
            client.Write(payload, 0, payload.Length);
            client.Flush();
            Assert.Equal(-1, client.ReadByte());
        }
        catch (IOException)
        {
            // Closing may win before the client can write or read, which is also a rejected invocation.
        }

        Assert.Empty(session.GetPendingQueueItems());
    }

    [Fact]
    public async Task PerItemOptionsOverrideSameForAllOptions()
    {
        using var cancellationSource = new CancellationTokenSource();
        var action = new RecordingAction();
        using var session = CreateSession(out _, action);
        session.Initialize(["first.item", "second.item"], cancellationSource.Token);
        var items = session.GetPendingQueueItems();
        session.SetSharedOptions(new Dictionary<string, string> { [ActionOptionKeys.Profile] = "shared" });
        session.SetItemOptions(items[1].QueueItemId, new Dictionary<string, string> { [ActionOptionKeys.Profile] = "per-file" });

        var processingTask = session.StartProcessing(cancellationSource.Token);
        WaitUntil(() => action.ExecutedRequests.Count == 2);
        cancellationSource.Cancel();
        await processingTask;

        Assert.Equal("shared", action.ExecutedRequests[0].Options![ActionOptionKeys.Profile]);
        Assert.Equal("per-file", action.ExecutedRequests[1].Options![ActionOptionKeys.Profile]);
    }

    [Fact]
    public async Task RemovingOneDuplicateOccurrence_DoesNotRemoveTheOther()
    {
        using var cancellationSource = new CancellationTokenSource();
        var action = new RecordingAction();
        using var session = CreateSession(out _, action);
        session.Initialize(["same.item", "same.item"], cancellationSource.Token);
        var secondItemId = session.GetPendingQueueItems()[1].QueueItemId;
        RemoveQueuedItem(session, secondItemId);
        session.SetSharedOptions(new Dictionary<string, string>());

        var processingTask = session.StartProcessing(cancellationSource.Token);
        WaitUntil(() => action.ExecutedPaths.Count == 1);
        cancellationSource.Cancel();
        await processingTask;

        Assert.Equal(new[] { "same.item" }, action.ExecutedPaths);
    }

    [Fact]
    public void CancelAllRejectsALateInvocation()
    {
        using var session = CreateSession(out var definition, new RecordingAction());
        session.Initialize(["first.item"], CancellationToken.None);
        RequestCancelAll(session);

        Assert.False(ConversionBatchSession.SendPathsToPrimaryInstance(definition, ["late.item"]));
        Assert.Empty(session.GetPendingQueueItems());
    }

    private static ConversionBatchSession CreateSession(
        out ConversionBatchSession.BatchDefinition definition,
        RecordingAction action)
    {
        var uniqueId = Guid.NewGuid().ToString("N");
        definition = new ConversionBatchSession.BatchDefinition(
            ActionId: $"test-{uniqueId}",
            DisplayName: "Test",
            RequiresSharedOptions: false,
            DefaultOptions: new Dictionary<string, string>(),
            PickerTitle: null,
            PickerDescription: null,
            SupportedSourceFormatsText: ".item",
            MutexName: $@"Local\FrameShiftTests_{uniqueId}",
            PipeName: $"FrameShiftTests_{uniqueId}",
            ShowProfiles: false,
            IsSupportedSourceExtension: extension => extension.Equals(".item", StringComparison.OrdinalIgnoreCase),
            GetTargetsForSelection: _ => [],
            GetProfiles: static () => []);
        return new ConversionBatchSession(definition, action, new AppLogger());
    }

    private static void RemoveQueuedItem(ConversionBatchSession session, string queueItemId)
    {
        var method = typeof(ConversionBatchSession).GetMethod(
            "OnQueueItemRemoveRequested",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(session, [null, queueItemId]);
    }

    private static void RequestCancelAll(ConversionBatchSession session)
    {
        var method = typeof(ConversionBatchSession).GetMethod(
            "OnCancelRequested",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(session, [null, EventArgs.Empty]);
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout)
            {
                throw new TimeoutException("The expected batch session state was not reached.");
            }

            Thread.Sleep(20);
        }
    }

    private sealed class RecordingAction : IFrameShiftAction
    {
        private readonly Func<ActionRequest, CancellationToken, Task<ActionExecutionResult>> _execute;
        private readonly ConcurrentQueue<string> _executedPaths = new();
        private readonly ConcurrentQueue<ActionRequest> _executedRequests = new();

        public RecordingAction(Func<ActionRequest, CancellationToken, Task<ActionExecutionResult>>? execute = null)
        {
            _execute = execute ?? ((_, _) => Task.FromResult(new ActionExecutionResult(true, "Done.")));
        }

        public ActionDescriptor Descriptor { get; } = new("test", "Test", "Test");

        public IReadOnlyList<string> ExecutedPaths => _executedPaths.ToArray();

        public IReadOnlyList<ActionRequest> ExecutedRequests => _executedRequests.ToArray();

        public async Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
        {
            _executedPaths.Enqueue(request.InputPath);
            _executedRequests.Enqueue(request);
            return await _execute(request, cancellationToken);
        }
    }
}
