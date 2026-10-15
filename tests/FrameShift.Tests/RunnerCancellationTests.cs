using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Logging;
using FrameShift.Core.Actions;
using FrameShift.Core.Helpers;
using FrameShift.Core.Progress;
using Xunit;

namespace FrameShift.Tests;

public sealed class RunnerCancellationTests
{
    private const int LargeOutputBytes = 256 * 1024;

    [Fact]
    public async Task Transcode_PreCanceledToken_DoesNotStartProcess()
    {
        using var fixture = new FakeProcessFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var ready = fixture.GetPath("pre-canceled.pid");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FfmpegRunner(new AppLogger()).RunAsync(
            GetPowerShellPath(), BuildArguments("long", ready, null, 0), null, null,
            "source été.mp4", "Test", "CPU", cancellation.Token));

        Assert.False(File.Exists(ready));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transcode_LateCancellationRetriesFailedKillAndStopsEntireTree(bool currentItem)
    {
        using var fixture = new FakeProcessFixture();
        using var cancellation = new CancellationTokenSource();
        var reporter = new QueueCancelReporter();
        var attempts = 0;
        var runner = new FfmpegRunner(new AppLogger(), process =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new System.ComponentModel.Win32Exception(5, "Simulated first kill failure");
            process.Kill(entireProcessTree: true);
        });
        var parentReady = fixture.GetPath("parent.pid");
        var childReady = fixture.GetPath("child.pid");
        var run = runner.RunAsync(GetPowerShellPath(), BuildArguments("tree", parentReady, childReady, 0),
            null, reporter, "source été.mp4", "Test", "CPU", cancellation.Token);
        var parent = 0;
        var child = 0;
        try
        {
            parent = await ReadProcessIdAsync(parentReady);
            child = await ReadProcessIdAsync(childReady);
            if (currentItem) reporter.CancelItem();
            else cancellation.Cancel();

            var result = await run.WaitAsync(TimeSpan.FromSeconds(15));

            Assert.True(result.Canceled);
            Assert.True(result.ProcessTerminationConfirmed);
            Assert.Equal(currentItem ? CancellationScope.CurrentItem : CancellationScope.All, result.CancellationScope);
            Assert.Equal(2, Volatile.Read(ref attempts));
            await AssertProcessExitedAsync(parent);
            await AssertProcessExitedAsync(child);
        }
        finally
        {
            cancellation.Cancel();
            await KillControlledProcessAsync(parent);
            await KillControlledProcessAsync(child);
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Transcode_GlobalCancellationTakesPrecedenceWhileCurrentItemIsStopping()
    {
        using var fixture = new FakeProcessFixture();
        using var cancellation = new CancellationTokenSource();
        var reporter = new QueueCancelReporter();
        var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FfmpegRunner(new AppLogger(), process =>
        {
            if (firstAttempt.TrySetResult()) return;
            process.Kill(entireProcessTree: true);
        });
        var ready = fixture.GetPath("scope.pid");
        var run = runner.RunAsync(GetPowerShellPath(), BuildArguments("long", ready, null, 0),
            null, reporter, "source été.mp4", "Test", "CPU", cancellation.Token);
        var pid = 0;
        try
        {
            pid = await ReadProcessIdAsync(ready);
            reporter.CancelItem();
            await firstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            var result = await run.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(result.Canceled);
            Assert.True(result.ProcessTerminationConfirmed);
            Assert.Equal(CancellationScope.All, result.CancellationScope);
            await AssertProcessExitedAsync(pid);
        }
        finally
        {
            cancellation.Cancel();
            await KillControlledProcessAsync(pid);
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Transcode_FailedKillsReturnUnconfirmedTerminationAndLeavePartialFileUntouched()
    {
        using var fixture = new FakeProcessFixture();
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var runner = new FfmpegRunner(new AppLogger(), _ =>
        {
            Interlocked.Increment(ref attempts);
            throw new System.ComponentModel.Win32Exception(5, "Simulated persistent kill failure");
        });
        var ready = fixture.GetPath("locked.pid");
        var output = fixture.GetPath("partial été.mp4");
        var run = runner.RunAsync(GetPowerShellPath(), BuildArguments("file-lock", ready, null, 0, output),
            null, null, "source été.mp4", "Test", "CPU", cancellation.Token);
        var pid = 0;
        try
        {
            pid = await ReadProcessIdAsync(ready);
            await WaitForFileAsync(output);
            cancellation.Cancel();

            var result = await run.WaitAsync(TimeSpan.FromSeconds(15));

            Assert.True(result.Canceled);
            Assert.False(result.ProcessTerminationConfirmed);
            Assert.Equal(-1, result.ExitCode);
            Assert.Equal(CancellationScope.All, result.CancellationScope);
            Assert.Equal(2, Volatile.Read(ref attempts));
            Assert.True(IsProcessRunning(pid));
            Assert.True(File.Exists(output));
            Assert.Throws<IOException>(() =>
            {
                using var locked = new FileStream(output, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            });
        }
        finally
        {
            cancellation.Cancel();
            await KillControlledProcessAsync(pid);
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        using var released = new FileStream(output, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData("normal", 0)]
    [InlineData("large", LargeOutputBytes)]
    public async Task Transcode_NormalExitDrainsOutputAndKeepsSuccess(string mode, int bytes)
    {
        using var fixture = new FakeProcessFixture();

        var result = await new FfmpegRunner(new AppLogger()).RunAsync(GetPowerShellPath(),
            BuildArguments(mode, fixture.GetPath("normal.pid"), null, bytes), null, null,
            "source été.mp4", "Test", "CPU", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.Canceled);
        Assert.Equal(CancellationScope.None, result.CancellationScope);
        Assert.True(result.ProcessTerminationConfirmed);
        if (mode == "normal") Assert.Equal("normal-error", result.StandardError.TrimEnd());
        else
        {
            Assert.InRange(result.StandardError.Length, 1, BoundedProcessText.DiagnosticLimit);
            Assert.Contains("truncated", result.StandardError);
            Assert.EndsWith(new string('e', 100), result.StandardError.TrimEnd());
        }
    }

    [Fact]
    public async Task Transcode_RealFfmpegCancellationReleasesOutputFile()
    {
        using var fixture = new FakeProcessFixture();
        using var cancellation = new CancellationTokenSource();
        var output = fixture.GetPath("annulation réelle.mp4");
        var run = new FfmpegRunner(new AppLogger()).RunAsync(new ToolLocator().ResolveFfmpegPath(),
            ["-hide_banner", "-loglevel", "error", "-progress", "pipe:1", "-nostats", "-y",
                "-re", "-f", "lavfi", "-i", "color=size=64x48:rate=10", "-c:v", "libx264", output],
            null, null, "source été.mp4", "Test", "CPU", cancellation.Token);
        try
        {
            await WaitForFileAsync(output);
            cancellation.Cancel();
            var result = await run.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(result.Canceled);
            Assert.True(result.ProcessTerminationConfirmed);
            using var released = new FileStream(output, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            cancellation.Cancel();
            await run.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Runner_PreCanceledToken_DoesNotStartProcess(bool useProbe)
    {
        using var fixture = new FakeProcessFixture();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var readyPath = fixture.GetPath("pre-canceled.pid");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RunAsync(useProbe, "long", readyPath, null, 0, cancellationSource.Token));

        Assert.False(File.Exists(readyPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Runner_CancellationAfterStart_StopsProcessAndPropagatesCancellation(bool useProbe)
    {
        using var fixture = new FakeProcessFixture();
        using var cancellationSource = new CancellationTokenSource();
        var readyPath = fixture.GetPath("started.pid");

        var runTask = RunAsync(useProbe, "long", readyPath, null, 0, cancellationSource.Token);
        var processId = await ReadProcessIdAsync(readyPath);

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask.WaitAsync(TimeSpan.FromSeconds(15)));
        await AssertProcessExitedAsync(processId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Runner_CancellationAfterStart_StopsParentAndChildProcessTree(bool useProbe)
    {
        using var fixture = new FakeProcessFixture();
        using var cancellationSource = new CancellationTokenSource();
        var parentReadyPath = fixture.GetPath("parent.pid");
        var childReadyPath = fixture.GetPath("child.pid");

        var runTask = RunAsync(useProbe, "tree", parentReadyPath, childReadyPath, 0, cancellationSource.Token);
        var parentProcessId = await ReadProcessIdAsync(parentReadyPath);
        var childProcessId = await ReadProcessIdAsync(childReadyPath);

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask.WaitAsync(TimeSpan.FromSeconds(15)));
        await AssertProcessExitedAsync(parentProcessId);
        await AssertProcessExitedAsync(childProcessId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Runner_LargeStandardOutputAndError_DrainsBothStreamsWithoutDeadlock(bool useProbe)
    {
        using var fixture = new FakeProcessFixture();
        var readyPath = fixture.GetPath("large.pid");

        var result = await RunAsync(useProbe, "large", readyPath, null, LargeOutputBytes, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Success);
        Assert.Equal(LargeOutputBytes, result.StandardOutput.Length);
        Assert.Equal(BoundedProcessText.DiagnosticLimit, result.StandardError.Length);
        Assert.Contains("truncated", result.StandardError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Runner_NormalProcess_PreservesCapturedOutputAndSuccess(bool useProbe)
    {
        using var fixture = new FakeProcessFixture();
        var readyPath = fixture.GetPath("normal.pid");

        var result = await RunAsync(useProbe, "normal", readyPath, null, 0, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Success);
        Assert.Equal("normal-output", result.StandardOutput);
        Assert.Equal("normal-error", result.StandardError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Runner_CreateStartInfo_PreservesArgumentListAndRedirectedNoWindowConfiguration(bool useProbe)
    {
        var arguments = new[] { "argument with spaces", "accent-é", "plain" };
        var startInfo = useProbe
            ? new FfprobeRunner(new AppLogger()).CreateStartInfo("fake.exe", arguments)
            : new FfmpegRunner(new AppLogger()).CreateStartInfo("fake.exe", arguments);

        Assert.Equal(arguments, startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
    }

    private static async Task<RunnerResult> RunAsync(
        bool useProbe,
        string mode,
        string readyPath,
        string? childReadyPath,
        int bytes,
        CancellationToken cancellationToken)
    {
        var arguments = BuildArguments(mode, readyPath, childReadyPath, bytes);
        var logger = new AppLogger();

        if (useProbe)
        {
            var result = await new FfprobeRunner(logger)
                .RunProbeAsync(GetPowerShellPath(), arguments, cancellationToken)
                .ConfigureAwait(false);
            return new RunnerResult(result.Success, result.Output, result.StandardError);
        }

        var captureResult = await new FfmpegRunner(logger)
            .RunCaptureAsync(GetPowerShellPath(), arguments, cancellationToken)
            .ConfigureAwait(false);
        return new RunnerResult(captureResult.Success, captureResult.StandardOutput, captureResult.StandardError);
    }

    private static string[] BuildArguments(string mode, string readyPath, string? childReadyPath, int bytes, string? outputPath = null)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Assets", "FakeMediaProcess.ps1");
        Assert.True(File.Exists(scriptPath), $"Fake media process script was not copied: {scriptPath}");

        var arguments = new System.Collections.Generic.List<string>
        {
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath,
            "-Mode",
            mode,
            "-ReadyPath",
            readyPath
        };

        if (!string.IsNullOrWhiteSpace(childReadyPath))
        {
            arguments.Add("-ChildReadyPath");
            arguments.Add(childReadyPath);
        }

        if (bytes > 0)
        {
            arguments.Add("-Bytes");
            arguments.Add(bytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (outputPath is not null)
        {
            arguments.Add("-OutputPath");
            arguments.Add(outputPath);
        }

        return arguments.ToArray();
    }

    private static string GetPowerShellPath()
    {
        var path = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        Assert.True(File.Exists(path), $"Windows PowerShell is required by these Windows-only process lifecycle tests: {path}");
        return path;
    }

    private static async Task<int> ReadProcessIdAsync(string path)
    {
        var timeoutAt = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 10d);
        while (Stopwatch.GetTimestamp() < timeoutAt)
        {
            if (File.Exists(path) && int.TryParse(File.ReadAllText(path), out var processId) && processId > 0)
            {
                return processId;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        throw new TimeoutException($"The controlled process did not create its PID marker: {path}");
    }

    private static async Task AssertProcessExitedAsync(int processId)
    {
        var timeoutAt = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 10d);
        while (Stopwatch.GetTimestamp() < timeoutAt)
        {
            if (!IsProcessRunning(processId))
            {
                return;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        Assert.Fail($"Controlled process {processId} is still running after cancellation.");
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task WaitForFileAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(path)) await Task.Delay(25, timeout.Token);
    }

    private static async Task KillControlledProcessAsync(int pid)
    {
        if (pid <= 0) return;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException) { }
    }

    private sealed class QueueCancelReporter : IProgressReporter
    {
        private int _cancelItem;
        public void CancelItem() => Interlocked.Exchange(ref _cancelItem, 1);
        public bool IsCancellationRequested => false;
        public bool IsQueueItemRemovalRequested(string inputPath) => false;
        public bool IsQueueItemCancellationRequested(string inputPath) => Volatile.Read(ref _cancelItem) != 0;
        public void ReportQueue(System.Collections.Generic.IReadOnlyList<string> items) { }
        public void ReportProgress(int value, string? file, string? action, string? message, string? etaText = null) { }
        public void ReportState(string state, string? message = null) { }
        public void ReportQueueItem(string currentFile, string state, string? message = null) { }
    }

    private sealed record RunnerResult(bool Success, string StandardOutput, string StandardError);

    private sealed class FakeProcessFixture : IDisposable
    {
        private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "FrameShift.Tests", "RunnerCancellation", Guid.NewGuid().ToString("N"));

        public FakeProcessFixture()
        {
            Directory.CreateDirectory(_rootPath);
        }

        public string GetPath(string fileName) => Path.Combine(_rootPath, fileName);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_rootPath))
                {
                    Directory.Delete(_rootPath, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
