using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using FrameShift.Windows.Batch;
using FrameShift.Windows.ProgressUI;
using Xunit;

namespace FrameShift.Tests;

public sealed class ActionQueueRemovalTests
{
    [Fact]
    public void ProgressForm_RemovedQueuedItem_RemainsMarkedByItsOccurrenceId()
    {
        StaTest.Run(() =>
        {
            using var progressForm = new ProgressForm();
            var paths = new[] { @"C:\first.mp3", @"C:\second.mp3" };
            var secondItemId = ActionQueueRunner.CreateQueueItemId(1);

            progressForm.ReportQueue(paths);
            progressForm.RemoveQueueItem(secondItemId);

            Assert.True(progressForm.IsQueueItemRemovalRequested(paths[1], secondItemId));
            Assert.False(progressForm.IsQueueItemRemovalRequested(paths[1], secondItemId));
        });
    }

    [Fact]
    public void RunAsync_RemovedPendingItem_DoesNotExecuteIt()
    {
        StaTest.Run(() =>
        {
            using var progressForm = new ProgressForm();
            var paths = new[] { @"C:\first.mp3", @"C:\second.mp3" };
            var action = new RecordingAction(request =>
            {
                if (string.Equals(request.InputPath, paths[0], StringComparison.Ordinal))
                {
                    progressForm.RemoveQueueItem(ActionQueueRunner.CreateQueueItemId(1));
                }
            });

            var results = Run(action, paths, progressForm);

            Assert.Equal(new[] { paths[0] }, action.ExecutedPaths);
            Assert.True(results[0].Success);
            Assert.True(results[1].Canceled);
            Assert.Equal(CancellationScope.CurrentItem, results[1].CancellationScope);
        });
    }

    [Fact]
    public void RunAsync_RemovingSecondDuplicate_DoesNotSkipTheFirstOccurrence()
    {
        StaTest.Run(() =>
        {
            using var progressForm = new ProgressForm();
            const string path = @"C:\same.mp3";
            var action = new RecordingAction(_ => progressForm.RemoveQueueItem(ActionQueueRunner.CreateQueueItemId(1)));

            var results = Run(action, new[] { path, path }, progressForm);

            Assert.Equal(new[] { path }, action.ExecutedPaths);
            Assert.True(results[0].Success);
            Assert.True(results[1].Canceled);
        });
    }

    [Fact]
    public void RunAsync_RemovalImmediatelyBeforeNextCheck_IsObserved()
    {
        StaTest.Run(() =>
        {
            using var progressForm = new ProgressForm();
            var paths = new[] { @"C:\first.mp3", @"C:\second.mp3", @"C:\third.mp3" };
            var action = new RecordingAction(request =>
            {
                if (string.Equals(request.InputPath, paths[1], StringComparison.Ordinal))
                {
                    progressForm.RemoveQueueItem(ActionQueueRunner.CreateQueueItemId(2));
                }
            });

            var results = Run(action, paths, progressForm);

            Assert.Equal(new[] { paths[0], paths[1] }, action.ExecutedPaths);
            Assert.True(results[2].Canceled);
        });
    }

    [Fact]
    public void ConversionBatchSession_RemovalById_RemainsIndependentOfGenericQueueIds()
    {
        StaTest.Run(() =>
        {
            using var progressForm = new ProgressForm();
            var session = new ConversionBatchSession(
                ConversionBatchSession.CreateRemoveBackgroundDefinition(),
                new RecordingAction(_ => { }),
                new AppLogger(),
                progressForm);
            const string path = @"C:\image.png";
            const string queueItemId = "remove-background:1";

            EnqueueWithoutStartingListener(session, path);
            progressForm.RemoveQueueItem(queueItemId);

            Assert.True(ConsumeSessionRemoval(session, queueItemId));
            Assert.False(ConsumeSessionRemoval(session, queueItemId));
        });
    }

    private static IReadOnlyList<ActionExecutionResult> Run(
        RecordingAction action,
        IReadOnlyList<string> paths,
        ProgressForm progressForm)
    {
        var requests = paths
            .Select(path => new ActionRequest(path, new AppLogger(), progressForm))
            .ToArray();
        return new ActionQueueRunner()
            .RunAsync(action, requests, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static void EnqueueWithoutStartingListener(ConversionBatchSession session, string path)
    {
        var method = typeof(ConversionBatchSession).GetMethod("EnqueueItem", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(session, new object?[] { path, null });
    }

    private static bool ConsumeSessionRemoval(ConversionBatchSession session, string queueItemId)
    {
        var method = typeof(ConversionBatchSession).GetMethod("IsRemoved", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method!.Invoke(session, new object[] { queueItemId }));
    }

    private sealed class RecordingAction : IFrameShiftAction
    {
        private readonly Action<ActionRequest> _onExecute;

        public RecordingAction(Action<ActionRequest> onExecute)
        {
            _onExecute = onExecute;
        }

        public List<string> ExecutedPaths { get; } = new();

        public ActionDescriptor Descriptor { get; } = new("test", "Test action", "Test action");

        public Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
        {
            ExecutedPaths.Add(request.InputPath);
            _onExecute(request);
            return Task.FromResult(new ActionExecutionResult(true, "Done."));
        }
    }
}
