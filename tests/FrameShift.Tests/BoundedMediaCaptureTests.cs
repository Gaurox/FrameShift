using System.Diagnostics;
using System.Text;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using Xunit;

namespace FrameShift.Tests;

public sealed class BoundedMediaCaptureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FrameShift captures été " + Guid.NewGuid().ToString("N"));
    private static string Shell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
    public BoundedMediaCaptureTests() => Directory.CreateDirectory(_root);
    private string[] Arguments(string mode, int bytes = 0) => ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
        Path.Combine(AppContext.BaseDirectory, "Assets", "FakeMediaProcess.ps1"), "-Mode", mode, "-ReadyPath", Path.Combine(_root, "pid"), "-Bytes", bytes.ToString()];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureOverflowFailsWithoutReturningPartialStructuredOutput(bool probe)
    {
        var args = Arguments("diagnostic-flood", 2 * 1024 * 1024);
        if (probe)
        {
            var result = await new FfprobeRunner(new AppLogger(), TimeSpan.FromSeconds(10), 4096).RunProbeAsync(Shell, args, default).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(result.Success); Assert.Empty(result.Output);
            Assert.Contains("permitted size", result.StandardError);
        }
        else
        {
            var runner = new FfmpegRunner(new AppLogger(), p => p.Kill(true), captureLimit: 4096);
            var result = await runner.RunCaptureAsync(Shell, args, default).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(result.Success); Assert.Empty(result.StandardOutput);
            Assert.Contains("permitted size", result.StandardError);
        }
        AssertStopped();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImmobileProcessHitsInternalDeadlineAndStops(bool probe)
    {
        var args = Arguments("long");
        var timeout = TimeSpan.FromSeconds(2);
        if (probe)
        {
            var result = await new FfprobeRunner(new AppLogger(), timeout).RunProbeAsync(Shell, args, default).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(result.Success); Assert.Contains("timed out", result.StandardError);
        }
        else
        {
            var result = await new FfmpegRunner(new AppLogger(), p => p.Kill(true), captureTimeout: timeout).RunCaptureAsync(Shell, args, default).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(result.Success); Assert.Contains("timed out", result.StandardError);
        }
        AssertStopped();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LargeDiagnosticsKeepTheFinalErrorWithoutChangingACompleteCapture(bool probe)
    {
        const int bytes = 2 * 1024 * 1024;
        string output, error;
        if (probe)
        {
            var result = await new FfprobeRunner(new AppLogger()).RunProbeAsync(Shell, Arguments("diagnostic-flood", bytes), default).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(result.Success); output = result.Output; error = result.StandardError;
        }
        else
        {
            var result = await new FfmpegRunner(new AppLogger()).RunCaptureAsync(Shell, Arguments("diagnostic-flood", bytes), default).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(result.Success); output = result.StandardOutput; error = result.StandardError;
        }
        Assert.Equal(bytes, output.Length);
        Assert.Equal(BoundedProcessText.DiagnosticLimit, error.Length);
        Assert.Contains("truncated", error);
        Assert.EndsWith("final diagnostic", error);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("")]
    public async Task LongLinesKeepTheirPrefixAndFinalErrorWithoutUnboundedAllocation(string ending)
    {
        var text = "showinfo prefix " + new string('x', 2 * 1024 * 1024) + " final error" + ending;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        await foreach (var line in BoundedProcessText.ReadLinesAsync(reader, default)) lines.Add(line);
        var captured = Assert.Single(lines);
        Assert.StartsWith("showinfo prefix ", captured);
        Assert.EndsWith(" final error", captured);
        Assert.Contains("truncated", captured);
        Assert.InRange(captured.Length, 1, BoundedProcessText.LineLimit);
    }

    [Fact]
    public void TailWrapsAcrossSmallChunksAndRetainsTheLastDiagnostic()
    {
        var capture = new BoundedProcessText(128);
        for (var i = 0; i < 100; i++) capture.Append($"message {i}\n".AsSpan());
        Assert.StartsWith("[earlier output truncated]", capture.ToString());
        Assert.EndsWith("message 99\n", capture.ToString());
        Assert.Equal(128, capture.ToString().Length);
    }

    [Fact]
    public async Task UnconfirmedCaptureTerminationRemainsAnErrorEvenWhenUserCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new FfmpegRunner(new AppLogger(), _ => throw new System.ComponentModel.Win32Exception(5), captureTimeout: TimeSpan.FromSeconds(30));
        var run = runner.RunCaptureAsync(Shell, Arguments("long"), cancellation.Token);
        var ready = Path.Combine(_root, "pid");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!File.Exists(ready)) await Task.Delay(20, timeout.Token);
            cancellation.Cancel();
            await Assert.ThrowsAsync<TimeoutException>(() => run.WaitAsync(TimeSpan.FromSeconds(20)));
        }
        finally
        {
            cancellation.Cancel();
            if (File.Exists(ready))
            {
                try { using var process = Process.GetProcessById(int.Parse(File.ReadAllText(ready))); process.Kill(true); await process.WaitForExitAsync(); }
                catch (ArgumentException) { }
            }
            try { await run; } catch (Exception) { }
        }
    }

    [Fact]
    public async Task FailedTimingObserverStopsTheProcessInsteadOfAbandoningItsPipe()
    {
        var run = new FfmpegRunner(new AppLogger()).RunAsync(Shell, Arguments("log-then-wait"), null, null,
            "source été.mp4", "Observer test", "CPU", default, _ => throw new InvalidDataException("Bad timing."));
        await Assert.ThrowsAsync<InvalidDataException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
        AssertStopped();
    }

    private void AssertStopped()
    {
        var pid = int.Parse(File.ReadAllText(Path.Combine(_root, "pid")));
        try { using var process = Process.GetProcessById(pid); Assert.True(process.HasExited); }
        catch (ArgumentException) { }
    }
    public void Dispose() => Directory.Delete(_root, true);
}
