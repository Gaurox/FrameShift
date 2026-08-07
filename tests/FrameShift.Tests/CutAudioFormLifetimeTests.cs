using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Logging;
using FrameShift.Windows.Forms;
using Xunit;

namespace FrameShift.Tests;

public sealed class CutAudioFormLifetimeTests
{
    [Theory]
    [InlineData("initialization")]
    [InlineData("preview")]
    [InlineData("remove")]
    [InlineData("silence")]
    [InlineData("waveform")]
    [InlineData("probe")]
    public async Task ClosingDuringActiveWork_CancelsAndAwaitsTheNamedOperationBeforeCleanup(string operationName)
    {
        using var lifetime = new CutAudioFormLifetime();
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowOperationToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupCount = 0;

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
                Interlocked.Increment(ref cleanupCount);
                return Task.CompletedTask;
            },
            exception => throw new Xunit.Sdk.XunitException($"Unexpected close failure during {operationName}: {exception}"));

        Assert.True(lifetime.Token.IsCancellationRequested);
        Assert.False(closeTask.IsCompleted);
        Assert.Equal(0, Volatile.Read(ref cleanupCount));

        allowOperationToFinish.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => activeTask);
        await closeTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, Volatile.Read(ref cleanupCount));
    }

    [Fact]
    public async Task Closing_IsIdempotentAndKeepsTemporariesUntilTheActiveTaskHasEnded()
    {
        using var lifetime = new CutAudioFormLifetime();
        var allowOperationToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupCount = 0;

        var activeTask = lifetime.RunAsync(async cancellationToken =>
        {
            await allowOperationToFinish.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        });

        var firstCloseTask = lifetime.BeginClosingAsync(
            () =>
            {
                Interlocked.Increment(ref cleanupCount);
                return Task.CompletedTask;
            },
            _ => { });
        var secondCloseTask = lifetime.BeginClosingAsync(
            () =>
            {
                Interlocked.Increment(ref cleanupCount);
                return Task.CompletedTask;
            },
            _ => { });

        Assert.Same(firstCloseTask, secondCloseTask);
        Assert.False(firstCloseTask.IsCompleted);
        Assert.Equal(0, Volatile.Read(ref cleanupCount));

        allowOperationToFinish.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => activeTask);
        await firstCloseTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, Volatile.Read(ref cleanupCount));
    }

    [Fact]
    public async Task Closing_SuppressesPostOperationContinuationAndDoesNotReportVoluntaryCancellation()
    {
        using var lifetime = new CutAudioFormLifetime();
        var allowOperationToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continuationUpdated = false;
        var loggedFailures = new List<Exception>();

        var activeTask = lifetime.RunAsync(async cancellationToken =>
        {
            await allowOperationToFinish.Task.ConfigureAwait(false);
            if (!lifetime.IsClosing)
            {
                continuationUpdated = true;
            }

            cancellationToken.ThrowIfCancellationRequested();
        });

        var closeTask = lifetime.BeginClosingAsync(
            () => Task.CompletedTask,
            loggedFailures.Add);

        allowOperationToFinish.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => activeTask);
        await closeTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(continuationUpdated);
        Assert.Empty(loggedFailures);
    }

    [Fact]
    public void Constructor_DefersPreparationUntilTheFormIsShown()
    {
        string? temporaryRootPath = null;

        try
        {
            StaTest.Run(() =>
            {
                using var form = new CutAudioForm(
                    "missing-input.wav",
                    "missing-ffmpeg.exe",
                    "missing-ffprobe.exe",
                    1d,
                    new FfmpegRunner(new AppLogger()),
                    new FfprobeRunner(new AppLogger()));

                temporaryRootPath = form.TemporaryRootPathForTesting;
                Assert.False(form.IsInitializationStartedForTesting);
                Assert.False(form.IsWorkspaceReadyForTesting);
                Assert.False(form.Visible);
                Assert.True(form.Enabled);
            });
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryRootPath) && Directory.Exists(temporaryRootPath))
            {
                Directory.Delete(temporaryRootPath, recursive: true);
            }
        }
    }

    [Fact]
    public void ShownWindow_IsVisibleAndCanBeginClosingBeforePreparationCompletes()
    {
        var ffmpegPath = Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg", "ffmpeg.exe");
        var ffprobePath = Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg", "ffprobe.exe");
        Assert.True(File.Exists(ffmpegPath), "The bundled FFmpeg executable was not copied to the test output.");
        Assert.True(File.Exists(ffprobePath), "The bundled FFprobe executable was not copied to the test output.");

        string? temporaryRootPath = null;
        var wasVisibleBeforePreparationCompleted = false;

        try
        {
            StaTest.Run(() =>
            {
                using var form = new CutAudioForm(
                    "missing-input.wav",
                    ffmpegPath,
                    ffprobePath,
                    1d,
                    new FfmpegRunner(new AppLogger()),
                    new FfprobeRunner(new AppLogger()));

                temporaryRootPath = form.TemporaryRootPathForTesting;
                form.Shown += (_, _) =>
                {
                    wasVisibleBeforePreparationCompleted = form.Visible && !form.IsWorkspaceReadyForTesting;
                    form.Close();
                };

                Application.Run(form);
            });

            Assert.True(wasVisibleBeforePreparationCompleted);
            Assert.NotNull(temporaryRootPath);
            Assert.False(Directory.Exists(temporaryRootPath));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryRootPath) && Directory.Exists(temporaryRootPath))
            {
                Directory.Delete(temporaryRootPath, recursive: true);
            }
        }
    }
}
