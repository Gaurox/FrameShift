using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FrameShift.Core.AI.RemoveNoise;
using FrameShift.Core.AI.RemoveObject;
using FrameShift.Windows.AI;
using Xunit;

namespace FrameShift.Tests;

public sealed class OnnxFormLifetimeTests
{
    [Theory]
    [InlineData("Remove Object session loading")]
    [InlineData("Remove Object native inference")]
    [InlineData("Remove Noise FFmpeg extraction")]
    [InlineData("Remove Noise encoder inference")]
    [InlineData("Remove Noise decoder inference")]
    public async Task ClosingDuringOnnxWork_CancelsAndWaitsBeforeDisposingResources(string phase)
    {
        using var lifetime = new OnnxFormLifetime();
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowOperationToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeCount = 0;

        var activeTask = lifetime.RunAsync(async cancellationToken =>
        {
            operationStarted.TrySetResult();
            await allowOperationToFinish.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        });

        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var closeTask = lifetime.BeginClosingAsync(
            () =>
            {
                Interlocked.Increment(ref disposeCount);
                return Task.CompletedTask;
            },
            exception => throw new Xunit.Sdk.XunitException($"Unexpected close failure during {phase}: {exception}"));

        Assert.True(lifetime.Token.IsCancellationRequested);
        Assert.False(closeTask.IsCompleted);
        Assert.Equal(0, Volatile.Read(ref disposeCount));

        allowOperationToFinish.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => activeTask);
        await closeTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, Volatile.Read(ref disposeCount));
    }

    [Fact]
    public async Task Closing_IsIdempotentAndObservesFaultedBackgroundWork()
    {
        using var lifetime = new OnnxFormLifetime();
        var finishWithFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loggedFailures = new List<Exception>();
        var disposeCount = 0;

        _ = lifetime.RunAsync(_ => finishWithFailure.Task);
        var firstCloseTask = lifetime.BeginClosingAsync(
            () =>
            {
                Interlocked.Increment(ref disposeCount);
                return Task.CompletedTask;
            },
            loggedFailures.Add);
        var secondCloseTask = lifetime.BeginClosingAsync(
            () =>
            {
                Interlocked.Increment(ref disposeCount);
                return Task.CompletedTask;
            },
            loggedFailures.Add);

        Assert.Same(firstCloseTask, secondCloseTask);
        finishWithFailure.TrySetException(new InvalidOperationException("simulated native failure"));

        await firstCloseTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Single(loggedFailures);
        Assert.Equal(1, Volatile.Read(ref disposeCount));
    }

    [Fact]
    public void OnnxEngines_DisposeIsIdempotent()
    {
        var objectEngine = new ObjectRemovalEngine(ObjectRemovalModelCatalog.GetDefault());
        objectEngine.Dispose();
        objectEngine.Dispose();

        var noiseEngine = new RemoveNoiseEngine();
        noiseEngine.Dispose();
        noiseEngine.Dispose();
    }

    [Fact]
    public void Closing_DoesNotBlockTheStaMessagePumpWhileNativeWorkFinishes()
    {
        StaTest.Run(() =>
        {
            using var lifetime = new OnnxFormLifetime();
            using var window = new Form();
            using var timer = new System.Windows.Forms.Timer { Interval = 10 };
            var allowOperationToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var messagePumpTicks = 0;

            window.Shown += (_, _) =>
            {
                _ = lifetime.RunAsync(async cancellationToken =>
                {
                    await allowOperationToFinish.Task.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                });

                _ = lifetime.BeginClosingAsync(
                    () =>
                    {
                        window.BeginInvoke(new Action(window.Close));
                        return Task.CompletedTask;
                    },
                    exception => throw new Xunit.Sdk.XunitException($"Unexpected close failure: {exception}"));
                timer.Start();
            };

            timer.Tick += (_, _) =>
            {
                messagePumpTicks++;
                allowOperationToFinish.TrySetResult();
            };

            Application.Run(window);

            Assert.True(messagePumpTicks > 0);
        });
    }

    [Theory]
    [InlineData(false, "ok")]
    [InlineData(false, "cancel")]
    [InlineData(false, "close")]
    [InlineData(false, "escape")]
    [InlineData(true, "ok")]
    [InlineData(true, "cancel")]
    [InlineData(true, "close")]
    [InlineData(true, "escape")]
    public void RemoveNoisePickers_PreserveRequestedDialogResultAcrossEveryCloseRoute(bool videoPicker, string closeRoute)
    {
        var result = ShowPickerDialog(videoPicker, closeRoute);

        Assert.Equal(string.Equals(closeRoute, "ok", StringComparison.Ordinal) ? DialogResult.OK : DialogResult.Cancel, result);
    }

    private static DialogResult ShowPickerDialog(bool videoPicker, string closeRoute)
    {
        DialogResult result = DialogResult.None;

        StaTest.Run(() =>
        {
            using Form picker = videoPicker
                ? new RemoveNoiseVideoPickerForm("source.wav", sourceIsStereo: false)
                : new RemoveNoiseAudioPickerForm("source.wav", sourceIsStereo: false);

            picker.Shown += (_, _) => picker.BeginInvoke(new Action(() => TriggerClose(picker, closeRoute)));
            Application.Run(picker);
            result = picker.DialogResult;
        });

        return result;
    }

    private static void TriggerClose(Form picker, string closeRoute)
    {
        switch (closeRoute)
        {
            case "ok":
                ((Button)picker.AcceptButton!).PerformClick();
                return;
            case "cancel":
                ((Button)picker.CancelButton!).PerformClick();
                return;
            case "close":
                picker.Close();
                return;
            case "escape":
                // WinForms routes Escape through CancelButton; invoking that route directly
                // avoids relying on the focus state of the test-host desktop.
                ((Button)picker.CancelButton!).PerformClick();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(closeRoute), closeRoute, null);
        }
    }
}
