using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Actions;
using FrameShift.Core.AI.VideoInterpolation;
using FrameShift.Core.AI.VideoUpscale;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.Logging;
using Xunit;

namespace FrameShift.Tests;

public sealed class RawVideoProcessLifecycleTests
{
    private const int LargeOutputBytes = 256 * 1024;

    [Fact]
    public async Task RawVideoProcess_ExceptionBetweenStarts_StopsDecoderTree()
    {
        using var fixture = new RawVideoFixture();
        using var cancellationSource = new CancellationTokenSource();
        var runner = new FfmpegRunner(new AppLogger());
        var parentReadyPath = fixture.GetPath("decoder-parent.pid");
        var childReadyPath = fixture.GetPath("decoder-child.pid");

        var decoder = runner.StartRawVideoProcess(
            GetPowerShellPath(),
            BuildArguments("tree", parentReadyPath, childReadyPath),
            redirectStandardInput: false,
            drainStandardOutput: false,
            role: "test-decoder",
            cancellationToken: cancellationSource.Token);
        var parentProcessId = await ReadProcessIdAsync(parentReadyPath);
        var childProcessId = await ReadProcessIdAsync(childReadyPath);

        try
        {
            throw new InvalidOperationException("Simulated pipeline failure between decoder and encoder starts.");
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            await decoder.DisposeAsync();
        }

        await AssertProcessExitedAsync(parentProcessId);
        await AssertProcessExitedAsync(childProcessId);
    }

    [Fact]
    public async Task RawVideoProcess_EncoderStartFailure_StopsAlreadyStartedDecoderTree()
    {
        using var fixture = new RawVideoFixture();
        using var cancellationSource = new CancellationTokenSource();
        var runner = new FfmpegRunner(new AppLogger());
        var parentReadyPath = fixture.GetPath("decoder-parent.pid");
        var childReadyPath = fixture.GetPath("decoder-child.pid");

        var decoder = runner.StartRawVideoProcess(
            GetPowerShellPath(),
            BuildArguments("tree", parentReadyPath, childReadyPath),
            redirectStandardInput: false,
            drainStandardOutput: false,
            role: "test-decoder",
            cancellationToken: cancellationSource.Token);
        var parentProcessId = await ReadProcessIdAsync(parentReadyPath);
        var childProcessId = await ReadProcessIdAsync(childReadyPath);

        try
        {
            Assert.ThrowsAny<Exception>(() => runner.StartRawVideoProcess(
                fixture.GetPath("missing-ffmpeg.exe"),
                Array.Empty<string>(),
                redirectStandardInput: true,
                drainStandardOutput: true,
                role: "test-encoder",
                cancellationToken: cancellationSource.Token));
        }
        finally
        {
            await decoder.DisposeAsync();
        }

        await AssertProcessExitedAsync(parentProcessId);
        await AssertProcessExitedAsync(childProcessId);
    }

    [Fact]
    public async Task RawVideoProcess_PrematureDecoderExit_StopsEncoder()
    {
        using var fixture = new RawVideoFixture();
        using var cancellationSource = new CancellationTokenSource();
        var runner = new FfmpegRunner(new AppLogger());
        var decoderReadyPath = fixture.GetPath("decoder.pid");
        var encoderReadyPath = fixture.GetPath("encoder.pid");

        var decoder = runner.StartRawVideoProcess(
            GetPowerShellPath(),
            BuildArguments("fail", decoderReadyPath),
            redirectStandardInput: false,
            drainStandardOutput: false,
            role: "test-decoder",
            cancellationToken: cancellationSource.Token);
        var encoder = runner.StartRawVideoProcess(
            GetPowerShellPath(),
            BuildArguments("long", encoderReadyPath),
            redirectStandardInput: true,
            drainStandardOutput: true,
            role: "test-encoder",
            cancellationToken: cancellationSource.Token);
        var encoderProcessId = await ReadProcessIdAsync(encoderReadyPath);

        try
        {
            await decoder.WaitForExitAsync(CancellationToken.None);
            Assert.NotEqual(0, decoder.ExitCode);
        }
        finally
        {
            encoder.RequestStop("premature-decoder");
            await encoder.DisposeAsync();
            await decoder.DisposeAsync();
        }

        await AssertProcessExitedAsync(encoderProcessId);
    }

    [Fact]
    public async Task RawVideoProcess_PrematureEncoderExit_StopsDecoderTree()
    {
        using var fixture = new RawVideoFixture();
        using var cancellationSource = new CancellationTokenSource();
        var runner = new FfmpegRunner(new AppLogger());
        var decoderParentReadyPath = fixture.GetPath("decoder-parent.pid");
        var decoderChildReadyPath = fixture.GetPath("decoder-child.pid");
        var encoderReadyPath = fixture.GetPath("encoder.pid");

        var decoder = runner.StartRawVideoProcess(
            GetPowerShellPath(),
            BuildArguments("tree", decoderParentReadyPath, decoderChildReadyPath),
            redirectStandardInput: false,
            drainStandardOutput: false,
            role: "test-decoder",
            cancellationToken: cancellationSource.Token);
        var parentProcessId = await ReadProcessIdAsync(decoderParentReadyPath);
        var childProcessId = await ReadProcessIdAsync(decoderChildReadyPath);
        var encoder = runner.StartRawVideoProcess(
            GetPowerShellPath(),
            BuildArguments("fail", encoderReadyPath),
            redirectStandardInput: true,
            drainStandardOutput: true,
            role: "test-encoder",
            cancellationToken: cancellationSource.Token);

        try
        {
            await encoder.WaitForExitAsync(CancellationToken.None);
            Assert.NotEqual(0, encoder.ExitCode);
        }
        finally
        {
            decoder.RequestStop("premature-encoder");
            await encoder.DisposeAsync();
            await decoder.DisposeAsync();
        }

        await AssertProcessExitedAsync(parentProcessId);
        await AssertProcessExitedAsync(childProcessId);
    }

    [Fact]
    public async Task RawVideoProcess_CancellationDuringRead_StopsProcessTree()
    {
        using var fixture = new RawVideoFixture();
        using var cancellationSource = new CancellationTokenSource();
        var runner = new FfmpegRunner(new AppLogger());
        var parentReadyPath = fixture.GetPath("reader-parent.pid");
        var childReadyPath = fixture.GetPath("reader-child.pid");
        var decoder = runner.StartRawVideoProcess(
            GetPowerShellPath(),
            BuildArguments("tree", parentReadyPath, childReadyPath),
            redirectStandardInput: false,
            drainStandardOutput: false,
            role: "test-decoder",
            cancellationToken: cancellationSource.Token);
        var parentProcessId = await ReadProcessIdAsync(parentReadyPath);
        var childProcessId = await ReadProcessIdAsync(childReadyPath);

        try
        {
            var readTask = decoder.StandardOutput.ReadAsync(new byte[1].AsMemory(), cancellationSource.Token).AsTask();
            cancellationSource.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            await decoder.DisposeAsync();
        }

        await AssertProcessExitedAsync(parentProcessId);
        await AssertProcessExitedAsync(childProcessId);
    }

    [Fact]
    public async Task RawVideoProcess_CancellationDuringInference_StopsDecoderAndEncoder()
    {
        using var fixture = new RawVideoFixture();
        using var cancellationSource = new CancellationTokenSource();
        var runner = new FfmpegRunner(new AppLogger());
        var decoderReadyPath = fixture.GetPath("decoder.pid");
        var encoderReadyPath = fixture.GetPath("encoder.pid");
        var decoder = runner.StartRawVideoProcess(
            GetPowerShellPath(),
            BuildArguments("long", decoderReadyPath),
            redirectStandardInput: false,
            drainStandardOutput: false,
            role: "test-decoder",
            cancellationToken: cancellationSource.Token);
        var encoder = runner.StartRawVideoProcess(
            GetPowerShellPath(),
            BuildArguments("long", encoderReadyPath),
            redirectStandardInput: true,
            drainStandardOutput: true,
            role: "test-encoder",
            cancellationToken: cancellationSource.Token);
        var decoderProcessId = await ReadProcessIdAsync(decoderReadyPath);
        var encoderProcessId = await ReadProcessIdAsync(encoderReadyPath);

        try
        {
            var inferenceTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationSource.Token);
            cancellationSource.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inferenceTask.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            await encoder.DisposeAsync();
            await decoder.DisposeAsync();
        }

        await AssertProcessExitedAsync(decoderProcessId);
        await AssertProcessExitedAsync(encoderProcessId);
    }

    [Fact]
    public async Task RawVideoProcess_CancellationDuringWrite_StopsEncoder()
    {
        using var fixture = new RawVideoFixture();
        using var cancellationSource = new CancellationTokenSource();
        var runner = new FfmpegRunner(new AppLogger());
        var encoderReadyPath = fixture.GetPath("encoder.pid");
        var encoder = runner.StartRawVideoProcess(
            GetPowerShellPath(),
            BuildArguments("stdin-block", encoderReadyPath),
            redirectStandardInput: true,
            drainStandardOutput: true,
            role: "test-encoder",
            cancellationToken: cancellationSource.Token);
        var encoderProcessId = await ReadProcessIdAsync(encoderReadyPath);

        try
        {
            var writeTask = encoder.StandardInput.WriteAsync(new byte[16 * 1024 * 1024].AsMemory(), cancellationSource.Token).AsTask();
            await Task.Delay(100);
            cancellationSource.Cancel();
            await Assert.ThrowsAnyAsync<Exception>(() => writeTask.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            await encoder.DisposeAsync();
        }

        await AssertProcessExitedAsync(encoderProcessId);
    }

    [Fact]
    public async Task RawVideoProcess_LargeStandardError_IsDrainedWithoutDeadlock()
    {
        using var fixture = new RawVideoFixture();
        var runner = new FfmpegRunner(new AppLogger());
        var readyPath = fixture.GetPath("large.pid");
        var process = runner.StartRawVideoProcess(
            GetPowerShellPath(),
            BuildArguments("large", readyPath, bytes: LargeOutputBytes),
            redirectStandardInput: false,
            drainStandardOutput: true,
            role: "test-encoder",
            cancellationToken: CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(LargeOutputBytes, process.StandardError.Length);
        }
        finally
        {
            await process.DisposeAsync();
        }
    }

    [Fact]
    public async Task RawVideoProcess_StopsBeforePartialOutputCleanup_ReleasesOutputFile()
    {
        using var fixture = new RawVideoFixture();
        using var cancellationSource = new CancellationTokenSource();
        var runner = new FfmpegRunner(new AppLogger());
        var readyPath = fixture.GetPath("encoder.pid");
        var partialOutputPath = fixture.GetPath("partial-output.mp4");
        var encoder = runner.StartRawVideoProcess(
            GetPowerShellPath(),
            BuildArguments("file-lock", readyPath, outputPath: partialOutputPath),
            redirectStandardInput: true,
            drainStandardOutput: true,
            role: "test-encoder",
            cancellationToken: cancellationSource.Token);
        var encoderProcessId = await ReadProcessIdAsync(readyPath);
        await WaitForFileAsync(partialOutputPath);

        await encoder.DisposeAsync();
        await AssertProcessExitedAsync(encoderProcessId);

        File.Delete(partialOutputPath);
        Assert.False(File.Exists(partialOutputPath));
    }

    [Fact]
    public void RawVideoPipelineSelection_PreservesBmpFallback()
    {
        Assert.Equal("bmp", RifeInterpolateVideoAction.ResolvePipelineMode("bmp", targetMultiplier: 2));
        Assert.Equal("bmp", RifeInterpolateVideoAction.ResolvePipelineMode("rawvideo", targetMultiplier: 4));
        Assert.Equal("rawvideo", RifeInterpolateVideoAction.ResolvePipelineMode("auto", targetMultiplier: 2));
        Assert.Equal("bmp", UpscaleVideoAction.ResolvePipelineMode("bmp"));
        Assert.Equal("rawvideo", UpscaleVideoAction.ResolvePipelineMode("auto"));
    }

    private static string[] BuildArguments(
        string mode,
        string readyPath,
        string? childReadyPath = null,
        string? outputPath = null,
        int bytes = 0)
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

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            arguments.Add("-OutputPath");
            arguments.Add(outputPath);
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
        await WaitForFileAsync(path);
        Assert.True(int.TryParse(File.ReadAllText(path), out var processId) && processId > 0, $"Invalid PID marker: {path}");
        return int.Parse(File.ReadAllText(path), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task WaitForFileAsync(string path)
    {
        var timeoutAt = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 10d);
        while (Stopwatch.GetTimestamp() < timeoutAt)
        {
            if (File.Exists(path))
            {
                return;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        throw new TimeoutException($"The controlled process did not create its marker: {path}");
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

        Assert.Fail($"Controlled process {processId} is still running after cleanup.");
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

    private sealed class RawVideoFixture : IDisposable
    {
        private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "FrameShift.Tests", "RawVideoLifecycle", Guid.NewGuid().ToString("N"));

        public RawVideoFixture()
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
