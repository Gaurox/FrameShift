using System.Diagnostics;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Windows.Helpers;
using Xunit;
using Xunit.Abstractions;
using static FrameShift.Core.Actions.VideoLoopFrameReader;

namespace FrameShift.Tests;

public sealed class VideoLoopTimingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"FrameShift temps d'été {Guid.NewGuid():N}");
    private readonly FfmpegRunner _runner = new(new AppLogger());
    private readonly FfprobeRunner _prober = new(new AppLogger());
    private readonly ToolLocator _tools = new();
    private readonly ITestOutputHelper _output;
    private string Source => Path.Combine(_root, "source.mkv");
    public VideoLoopTimingTests(ITestOutputHelper output) { _output = output; Directory.CreateDirectory(_root); }
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private async Task<MediaProbeResult> Generate(string rate = "10", double duration = 6, string? filter = null)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i",
            $"nullsrc=s=160x96:r={rate}:d={Number(duration)},geq=lum='100+80*sin(2*PI*N/43)':cb=128:cr=128" };
        if (filter is not null) args.AddRange(["-vf", filter]);
        args.AddRange(["-fps_mode", "passthrough", "-c:v", "ffv1", Source]);
        var result = await _runner.RunAsync(_tools.ResolveFfmpegPath(), args, null, null, Source, "Timing fixture", "CPU", CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return (await _prober.TryProbeMediaAsync(_tools.ResolveFfprobePath(), Source, CancellationToken.None)).Probe!;
    }

    [Fact]
    public async Task OrdinalIndex_IsLazyResumableAndReused_ForLateEdits()
    {
        var probe = await Generate("30", 60);
        using var index = new VideoLoopTimingIndex(_runner, _tools.ResolveFfmpegPath(), Source, probe);
        Assert.Equal(0, index.DecodePasses); Assert.False(File.Exists(index.IndexPath));
        var firstWatch = Stopwatch.StartNew();
        var first = await index.ResolveFramesAsync(1500, 1600, CancellationToken.None);
        firstWatch.Stop();
        Assert.Equal(1500, first.Start.Number); Assert.Equal(1600, first.Last.Number);
        Assert.Equal(1499d / 30, first.Start.Time, 3);
        Assert.False(index.IsComplete); Assert.Equal(1, index.DecodePasses);
        var decoded = index.DecodedFrameCount;
        var repeatWatch = Stopwatch.StartNew();
        var repeated = await index.ResolveFramesAsync(1501, 1601, CancellationToken.None);
        repeatWatch.Stop();
        Assert.Equal(1501, repeated.Start.Number);
        Assert.Equal(decoded, index.DecodedFrameCount); Assert.Equal(1, index.DecodePasses);
        var final = await index.ResolveFrameAsync(1800, CancellationToken.None);
        Assert.Equal(1800, final.Number); Assert.True(index.IsComplete);
        Assert.Equal(2, index.DecodePasses); Assert.Equal(1800, index.IndexedFrameCount);
        Assert.Equal(1800 * 24, index.IndexBytes);
        _output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            Source = "160x96 FFV1, 30 FPS, 60 seconds", SourceSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(Source))),
            Environment.ProcessorCount, OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            FirstAccessMilliseconds = firstWatch.Elapsed.TotalMilliseconds, RepeatAccessMilliseconds = repeatWatch.Elapsed.TotalMilliseconds,
            FramesDecodedAtFirstAccess = decoded, RepeatDecodePasses = 0, index.IndexBytes
        }));
        var file = index.IndexPath; index.Dispose(); Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task IrregularVfr_NeighborsAndExclusiveEnd_UseNativeFrames()
    {
        var probe = await Generate(filter: "select='eq(n,0)+eq(n,1)+eq(n,8)+eq(n,13)+eq(n,40)+eq(n,59)'");
        using var index = new VideoLoopTimingIndex(_runner, _tools.ResolveFfmpegPath(), Source, probe);
        var start = await index.ResolveTimeAsync(0.1, false, CancellationToken.None);
        Assert.Equal(0.1, start.Time, 6); Assert.Equal(0.8, start.End, 6);
        var end = await index.ResolveTimeAsync(4, true, CancellationToken.None);
        Assert.Equal(1.3, end.Time, 6); Assert.Equal(4, end.End, 6);
        var passes = index.DecodePasses;
        var next = await index.NeighborAsync(start, 1, CancellationToken.None);
        Assert.NotNull(next); Assert.Equal(0.8, next.Time, 6);
        var previous = await index.NeighborAsync(next, -1, CancellationToken.None);
        Assert.Equal(start.Timestamp, previous!.Timestamp);
        Assert.True(index.DecodePasses <= passes + 1);
        Assert.Equal(0, index.IndexedFrameCount); Assert.False(File.Exists(index.IndexPath));
        var frames = await index.ResolveFramesAsync(2, 4, CancellationToken.None);
        Assert.Equal(0.1, frames.Bounds.StartSeconds, 6); Assert.Equal(4, frames.Bounds.EndSeconds, 6);
        var last = await index.ResolveFrameAsync(999, CancellationToken.None);
        Assert.Equal(6, last.Number); Assert.Equal(5.9, last.Time, 6); Assert.Equal(6, last.End, 6);
    }

    [Theory]
    [InlineData("24000/1001")]
    [InlineData("60000/1001")]
    [InlineData("240")]
    public async Task NativeCaptures_MatchTheSelectedPixels_AtFractionalAndHighRates(string rate)
    {
        var probe = await Generate(rate, 3);
        using var index = new VideoLoopTimingIndex(_runner, _tools.ResolveFfmpegPath(), Source, probe);
        var selection = await index.ResolveFramesAsync(20, 25, CancellationToken.None);
        using var first = await PreviewFrameHelper.CaptureNativeFrameAsync(_tools.ResolveFfmpegPath(), _runner, Source, probe, selection.Start.Timestamp, CancellationToken.None);
        using var last = await PreviewFrameHelper.CaptureNativeFrameAsync(_tools.ResolveFfmpegPath(), _runner, Source, probe, selection.Last.Timestamp, CancellationToken.None);
        var output = Path.Combine(_root, "export.mp4");
        await new VideoLoopExporter(_runner, _prober).RenderAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), Source, output,
            probe, new(20, 25, probe.VideoFrameRate!.Value), new(VideoLoopMode.Natural, 1, 1, false), selection.Bounds, null, CancellationToken.None);
        var decoded = await new VideoLoopFrameReader(_runner, _tools.ResolveFfmpegPath(), output).ReadAsync(0, 2, null, 20, CancellationToken.None);
        Assert.Equal(6, decoded.Count);
        Assert.InRange(Math.Abs(first.GetPixel(20, 20).R - decoded[0].Pixels[0]), 0, 5);
        Assert.InRange(Math.Abs(last.GetPixel(20, 20).R - decoded[^1].Pixels[0]), 0, 5);
    }

    [Fact]
    public async Task SourceChange_InvalidatesTheIndex_WithoutReturningOldFrames()
    {
        var probe = await Generate();
        using var index = new VideoLoopTimingIndex(_runner, _tools.ResolveFfmpegPath(), Source, probe);
        await index.ResolveFrameAsync(10, CancellationToken.None);
        Assert.True(File.Exists(index.IndexPath));
        File.SetLastWriteTimeUtc(Source, File.GetLastWriteTimeUtc(Source).AddSeconds(2));
        await Assert.ThrowsAsync<VideoLoopSourceChangedException>(() => index.ResolveFrameAsync(10, CancellationToken.None));
        Assert.False(File.Exists(index.IndexPath)); Assert.Equal(0, index.IndexedFrameCount);
    }

    [Fact]
    public async Task CancellationDuringExtension_RetainsCommittedPrefix_AndAllowsRetry()
    {
        var probe = await Generate("240", 30);
        using var index = new VideoLoopTimingIndex(_runner, _tools.ResolveFfmpegPath(), Source, probe);
        await index.ResolveFrameAsync(10, CancellationToken.None);
        var committed = index.IndexedFrameCount;
        var decoded = index.DecodedFrameCount;
        using var cts = new CancellationTokenSource();
        var operation = index.ResolveFrameAsync(7000, cts.Token);
        var deadline = Stopwatch.StartNew();
        while (!operation.IsCompleted && index.DecodedFrameCount < decoded + 32 && deadline.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(1);
        Assert.False(operation.IsCompleted, "The fixture must still be indexing when canceled.");
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(committed, index.IndexedFrameCount);
        Assert.Equal(index.IndexBytes, new FileInfo(index.IndexPath).Length);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(index.IndexPath)!, Path.GetFileName(index.IndexPath) + ".*.chunk"));
        Assert.Equal(committed + 1, (await index.ResolveFrameAsync(committed + 1, CancellationToken.None)).Number);
        File.Delete(Source); Assert.False(File.Exists(Source));
    }

    [Theory]
    [InlineData("24000/1001")]
    [InlineData("60000/1001")]
    [InlineData("240")]
    public async Task LargeNativePts_CaptureAndExport_DoNotRoundToPtsTimeText(string rate)
    {
        var probe = await Generate(rate, 3, "setpts=PTS+40000/TB");
        Assert.InRange(probe.VideoStartSeconds, 40000 - 1 / probe.VideoFrameRate!.Value, 40000 + 1 / probe.VideoFrameRate.Value);
        using var index = new VideoLoopTimingIndex(_runner, _tools.ResolveFfmpegPath(), Source, probe);
        var selection = await index.ResolveFramesAsync(20, 25, CancellationToken.None);
        Assert.True(selection.Start.Timestamp.Pts > 1000000);
        using var first = await PreviewFrameHelper.CaptureNativeFrameAsync(_tools.ResolveFfmpegPath(), _runner, Source, probe, selection.Start.Timestamp, CancellationToken.None);
        using var last = await PreviewFrameHelper.CaptureNativeFrameAsync(_tools.ResolveFfmpegPath(), _runner, Source, probe, selection.Last.Timestamp, CancellationToken.None);
        var output = Path.Combine(_root, "large-pts.mp4");
        await new VideoLoopExporter(_runner, _prober).RenderAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), Source, output,
            probe, new(20, 25, probe.VideoFrameRate!.Value), new(VideoLoopMode.Natural, 1, 1, false), selection.Bounds, null, CancellationToken.None);
        var decoded = await new VideoLoopFrameReader(_runner, _tools.ResolveFfmpegPath(), output).ReadAsync(0, 2, null, 20, CancellationToken.None);
        Assert.Equal(6, decoded.Count);
        Assert.InRange(Math.Abs(first.GetPixel(20, 20).R - decoded[0].Pixels[0]), 0, 5);
        Assert.InRange(Math.Abs(last.GetPixel(20, 20).R - decoded[^1].Pixels[0]), 0, 5);
    }

    [Fact]
    public async Task BudgetAndPrecanceledRequests_DoNotStartAFullScan()
    {
        var probe = await Generate();
        using var index = new VideoLoopTimingIndex(_runner, _tools.ResolveFfmpegPath(), Source, probe);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => index.ResolveFrameAsync(int.MaxValue, CancellationToken.None));
        Assert.Contains("256 MiB", error.Message);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => index.ResolveFrameAsync(10, cts.Token));
        Assert.Equal(0, index.DecodePasses); Assert.False(File.Exists(index.IndexPath));
    }

    [Fact]
    public async Task FirstLastAndSingleFrameSelection_KeepNativeExclusiveEnd()
    {
        var probe = await Generate(filter: "select='eq(n,0)+eq(n,1)+eq(n,8)+eq(n,13)+eq(n,40)+eq(n,59)'");
        using var index = new VideoLoopTimingIndex(_runner, _tools.ResolveFfmpegPath(), Source, probe);
        var first = await index.ResolveFramesAsync(1, 1, CancellationToken.None);
        Assert.Equal(0, first.Bounds.StartSeconds); Assert.Equal(0.1, first.Bounds.EndSeconds, 6);
        var last = await index.ResolveFramesAsync(6, 6, CancellationToken.None);
        Assert.Equal(5.9, last.Bounds.StartSeconds, 6); Assert.Equal(6, last.Bounds.EndSeconds, 6);
        var output = Path.Combine(_root, "one-frame.mp4");
        await new VideoLoopExporter(_runner, _prober).RenderAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), Source, output,
            probe, new(6, 6, probe.VideoFrameRate!.Value), new(VideoLoopMode.Natural, 1, 1, false), last.Bounds, null, CancellationToken.None);
        var frames = await new VideoLoopFrameReader(_runner, _tools.ResolveFfmpegPath(), output).ReadAsync(0, 1, null, 10, CancellationToken.None);
        Assert.Single(frames);
        Assert.Null(await index.NeighborAsync(first.Start, -1, CancellationToken.None));
        Assert.Null(await index.NeighborAsync(last.Last, 1, CancellationToken.None));
    }

    [Fact]
    public async Task QuantizedVideoOnlyCfrEnd_ReconcilesAtMostOneNativeClockTick()
    {
        var probe = await Generate("15", 5);
        using var index = new VideoLoopTimingIndex(_runner, _tools.ResolveFfmpegPath(), Source, probe);
        var last = await index.ResolveFrameAsync(75, CancellationToken.None);
        Assert.Equal(5, last.End, 8);
        var rawEnd = last.Time + last.Timestamp.DurationSeconds;
        Assert.InRange(Math.Abs(last.End - rawEnd), 0, (double)last.Timestamp.Numerator / last.Timestamp.Denominator + 0.000000001);
    }

    [Fact]
    public async Task ShiftedVideoAndContainer_UseTheSameTimelineInCaptureAndExport()
    {
        await Generate(duration: 3);
        var shifted = Path.Combine(_root, "offset vidéo.mkv");
        var result = await _runner.RunAsync(_tools.ResolveFfmpegPath(),
            ["-hide_banner", "-loglevel", "error", "-y", "-i", Source, "-f", "lavfi", "-i", "sine=frequency=440:duration=5",
                "-filter_complex", "[0:v]setpts=PTS+2/TB[v]", "-map", "[v]", "-map", "1:a", "-c:v", "ffv1", "-c:a", "pcm_s16le", shifted],
            null, null, shifted, "Offset timing fixture", "CPU", CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        var probe = (await _prober.TryProbeMediaAsync(_tools.ResolveFfprobePath(), shifted, CancellationToken.None)).Probe!;
        Assert.Equal(2, probe.VideoStartSeconds - probe.FormatStartSeconds, 6);
        using var index = new VideoLoopTimingIndex(_runner, _tools.ResolveFfmpegPath(), shifted, probe);
        var selection = await index.ResolveFramesAsync(5, 6, CancellationToken.None);
        Assert.Equal(0.4, selection.Bounds.StartSeconds, 6); Assert.Equal(0.6, selection.Bounds.EndSeconds, 6);
        using var first = await PreviewFrameHelper.CaptureNativeFrameAsync(_tools.ResolveFfmpegPath(), _runner, shifted, probe, selection.Start.Timestamp, CancellationToken.None);
        var output = Path.Combine(_root, "offset-export.mp4");
        await new VideoLoopExporter(_runner, _prober).RenderAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), shifted, output,
            probe, new(5, 6, 10), new(VideoLoopMode.Natural, 1, 1, false), selection.Bounds, null, CancellationToken.None);
        var frames = await new VideoLoopFrameReader(_runner, _tools.ResolveFfmpegPath(), output).ReadAsync(0, 1, null, 10, CancellationToken.None);
        Assert.Equal(2, frames.Count);
        Assert.InRange(Math.Abs(first.GetPixel(20, 20).R - frames[0].Pixels[0]), 0, 5);
    }

    [Theory]
    [InlineData("24000/1001")]
    [InlineData("60000/1001")]
    [InlineData("240")]
    public async Task NativeAnalysisWindows_KeepOriginalTicks_AfterAnUnalignedSeek(string rate)
    {
        await Generate(rate, 3);
        var mp4 = Path.Combine(_root, "native-clock.mp4");
        var encoded = await _runner.RunAsync(_tools.ResolveFfmpegPath(),
            ["-hide_banner", "-loglevel", "error", "-y", "-i", Source, "-c:v", "libx264", "-bf", "0", "-fps_mode", "passthrough", mp4],
            null, null, mp4, "Native clock fixture", "CPU", CancellationToken.None);
        Assert.Equal(0, encoded.ExitCode);
        var probe = (await _prober.TryProbeMediaAsync(_tools.ResolveFfprobePath(), mp4, CancellationToken.None)).Probe!;
        using var index = new VideoLoopTimingIndex(_runner, _tools.ResolveFfmpegPath(), mp4, probe);
        var window = await new VideoLoopFrameReader(_runner, _tools.ResolveFfmpegPath(), mp4,
            probe.VideoStartSeconds - probe.FormatStartSeconds, probe.VideoStartSeconds)
            .ReadAsync(0.123456789, 0.4, null, 150, CancellationToken.None);
        Assert.NotEmpty(window);
        foreach (var frame in window)
        {
            var original = await index.ResolveTimeAsync(frame.Time, false, CancellationToken.None);
            Assert.Equal(original.Time, frame.Time, 9);
        }
    }
}
