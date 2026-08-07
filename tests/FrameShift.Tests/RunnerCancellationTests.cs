using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Logging;
using Xunit;

namespace FrameShift.Tests;

public sealed class RunnerCancellationTests
{
    private const int LargeOutputBytes = 256 * 1024;

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
        Assert.Equal(LargeOutputBytes, result.StandardError.Length);
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

    private static string[] BuildArguments(string mode, string readyPath, string? childReadyPath, int bytes)
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
