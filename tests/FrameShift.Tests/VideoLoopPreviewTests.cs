using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Core.Progress;
using Xunit;
using static FrameShift.Core.Actions.VideoLoopFrameReader;

namespace FrameShift.Tests;

public sealed class VideoLoopPreviewTests
{
    private static MediaProbeResult Info(int width = 1920, int height = 1080) =>
        new(TimeSpan.FromHours(1), true, true, width, height, "h264", 30, 108000, ["aac"], [], []);

    [Theory]
    [InlineData(36, 3)]
    [InlineData(3600, 3)]
    [InlineData(0.2, 0.2)]
    public void JunctionPlan_IsOnlyTailThenHead_WithOneRealSeam(double duration, double previewDuration)
    {
        var plan = Assert.Single(VideoLoopPreviewBuilder.Plan(Info(3840, 2160), new(0, duration, 99),
            new(VideoLoopMode.Natural), VideoLoopPreviewKind.Junction));
        Assert.Equal(960, plan.Width); Assert.Equal(540, plan.Height);
        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(duration, plan.Segments[0].EndSeconds);
        Assert.Equal(0, plan.Segments[1].StartSeconds);
        Assert.Equal(previewDuration, plan.Segments.Sum(s => s.DurationSeconds), 8);
        Assert.Equal(1, plan.Repetitions);
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(0.75d)]
    [InlineData(0.5d)]
    public void ForwardBackward_LongPlan_HasTwoDistinctTurningClips(double speed)
    {
        var plans = VideoLoopPreviewBuilder.Plan(Info(), new(10, 46, 90),
            new(VideoLoopMode.PingPong, speed, 1, false), VideoLoopPreviewKind.Junction);
        Assert.Equal(2, plans.Count);
        Assert.Equal(VideoLoopPreviewContent.EndTurn, plans[0].Content);
        Assert.Equal(VideoLoopPreviewContent.StartTurn, plans[1].Content);
        Assert.Equal(46, plans[0].Bounds.EndSeconds);
        Assert.Equal(10, plans[1].Bounds.StartSeconds);
        Assert.Contains("Separate clip 1/2", plans[0].Description);
        Assert.Contains("Separate clip 2/2", plans[1].Description);
    }

    [Fact]
    public void FullPlan_UsesThreeCycles_WithoutChangingExportChoices()
    {
        var settings = new VideoLoopSettings(VideoLoopMode.Natural, 1, 7, true);
        var plan = Assert.Single(VideoLoopPreviewBuilder.Plan(Info(), new(0, 36, 90), settings, VideoLoopPreviewKind.FullLoop));
        Assert.Equal(3, plan.Repetitions);
        Assert.Single(plan.Segments);
        Assert.Equal(7, settings.Repetitions); Assert.True(settings.KeepAudio);
        var shortReverse = Assert.Single(VideoLoopPreviewBuilder.Plan(Info(), new(0, 1, 90),
            new(VideoLoopMode.PingPong, 1, 1, false), VideoLoopPreviewKind.Junction));
        Assert.Equal(2, shortReverse.Repetitions);
    }

    [Fact]
    public void ProxyDimensions_RespectRotationAndNonSquarePixels()
    {
        var probe = Info(1920, 1080) with { RotationDegrees = 90, VideoSampleAspectRatio = 2 };
        var plan = Assert.Single(VideoLoopPreviewBuilder.Plan(probe, new(0, 36, 99), new(VideoLoopMode.Natural), VideoLoopPreviewKind.Junction));
        Assert.Equal(270, plan.Width); Assert.Equal(960, plan.Height);
    }

    [Fact]
    public void CacheIdentity_CoversSourceBoundsModeSpeedColorAndPreviewKind()
    {
        var source = Path.GetTempFileName();
        try
        {
            var probe = Info(); var cut = new CutVideoSettings(1, 60, 30); var bounds = new VideoLoopCandidate(0, 2, 90);
            var settings = new VideoLoopSettings(VideoLoopMode.Natural);
            VideoLoopPreviewKey Key(MediaProbeResult? p = null, CutVideoSettings? c = null, VideoLoopSettings? s = null,
                VideoLoopCandidate? b = null, VideoLoopPreviewKind kind = VideoLoopPreviewKind.Junction) =>
                VideoLoopPreviewBuilder.Key(source, p ?? probe, c ?? cut, s ?? settings, b ?? bounds, kind);
            var key = Key();
            Assert.Equal(key, Key(b: bounds with { Quality = 10 }));
            Assert.Equal(key, Key(b: bounds with { Seam = new(0.02, 0, 0, 0, 0, false, 0.2, 5) }));
            Assert.Equal(key, Key(s: settings with { KeepAudio = false, Repetitions = 9 }));
            Assert.NotEqual(key, Key(c: cut with { StartFrame = 2 }));
            Assert.NotEqual(key, Key(b: bounds with { EndSeconds = 1.9 }));
            Assert.NotEqual(key, Key(s: new(VideoLoopMode.PingPong, 0.5, 1, false)));
            Assert.NotEqual(key, Key(p: probe with { VideoColorTransfer = "bt709" }));
            Assert.NotEqual(key, Key(kind: VideoLoopPreviewKind.FullLoop));
            File.WriteAllText(source, "changed source");
            Assert.NotEqual(key, Key());
            File.Delete(source);
            Assert.Throws<FileNotFoundException>(() => Key());
        }
        finally { File.Delete(source); }
    }

    [Fact]
    public void StreamingTimes_UseIntegerTicksAtLongHighFpsPositions()
    {
        var timing = new VideoLoopTimingCollector(1);
        timing.Observe("[Parsed_showinfo_0] config in time_base: 1/24000, frame_rate: 24000/1001");
        timing.Observe("[Parsed_showinfo_0] n: 0 pts: 960001001 pts_time:40000 duration:1001 duration_time:0.0417083");
        Assert.Equal(960001001d / 24000, Assert.Single(timing.Times), 8);
    }
}

public sealed class VideoLoopPreviewMediaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"FrameShift aperçu d'été {Guid.NewGuid():N}");
    private readonly ToolLocator _tools = new();
    private readonly FfmpegRunner _runner = new(new AppLogger());
    private readonly FfprobeRunner _prober = new(new AppLogger());
    private string Source => Path.Combine(_root, "source avec accents.mkv");
    public VideoLoopPreviewMediaTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private async Task<MediaProbeResult> Probe(string file) =>
        (await _prober.TryProbeMediaAsync(_tools.ResolveFfprobePath(), file, CancellationToken.None)).Probe!;

    private async Task Generate(string filter, double duration, bool audio = false, string? outputFilter = null)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", filter };
        if (audio) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000"]);
        if (outputFilter is not null) args.AddRange(["-vf", outputFilter]);
        args.AddRange(["-t", Number(duration), "-fps_mode", "passthrough", "-c:v", "ffv1", "-pix_fmt", "yuv420p"]);
        if (audio) args.AddRange(["-c:a", "pcm_s16le"]);
        args.Add(Source);
        var result = await _runner.RunAsync(_tools.ResolveFfmpegPath(), args, null, null, Source, "Preview fixture", "CPU", CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.StandardError);
    }

    private async Task<VideoLoopPreview> Build(MediaProbeResult info, VideoLoopCandidate bounds, VideoLoopPreviewKind kind = VideoLoopPreviewKind.Junction,
        VideoLoopSettings? settings = null, IProgressReporter? progress = null, CancellationToken token = default) =>
        await new VideoLoopPreviewBuilder(_runner, _prober).BuildAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), Source,
            info, new(1, (int)Math.Round(bounds.EndSeconds * info.VideoFrameRate!.Value), info.VideoFrameRate.Value),
            settings ?? new(VideoLoopMode.Natural, 1, 7, true), bounds, kind, progress, token);

    private Task<List<Frame>> Frames(string file) =>
        new VideoLoopFrameReader(_runner, _tools.ResolveFfmpegPath(), file).ReadAsync(0, 120, null, 3000, CancellationToken.None);
    private static double Difference(Frame a, Frame b) => a.Pixels.Zip(b.Pixels, (x, y) => (double)Math.Abs(x - y)).Average();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContinuousNaturalProxy_HasOnlyTheExpectedSeams_AndNoAudio(bool full)
    {
        await Generate("nullsrc=s=160x96:r=10,geq=lum='20+3*N':cb=128:cr=128", 6, audio: true);
        var info = await Probe(Source);
        using var preview = await Build(info, new(1, 5, 99), full ? VideoLoopPreviewKind.FullLoop : VideoLoopPreviewKind.Junction);
        var clip = Assert.Single(preview.Clips);
        var proxyInfo = await Probe(clip.Path);
        Assert.False(proxyInfo.HasAudio);
        var decoded = await Frames(clip.Path);
        var sourceFrames = await Frames(Source);
        var expected = full ? Enumerable.Range(10, 40).Concat(Enumerable.Range(10, 40)).Concat(Enumerable.Range(10, 40)).ToArray()
            : Enumerable.Range(35, 15).Concat(Enumerable.Range(10, 15)).ToArray();
        Assert.Equal(expected.Length, decoded.Count);
        for (var i = 0; i < expected.Length; i++) Assert.InRange(Difference(sourceFrames[expected[i]], decoded[i]), 0, 4);
        var jumps = Enumerable.Range(1, decoded.Count - 1).Where(i => Difference(decoded[i - 1], decoded[i]) > 30).ToArray();
        Assert.Equal(full ? [40, 80] : new[] { 15 }, jumps);
        Assert.InRange(proxyInfo.Duration!.Value.TotalSeconds, (full ? 12 : 3) - 0.003, (full ? 12 : 3) + 0.003);
        // Also compare the real last/first junction against the published full-resolution render.
        var reference = Path.Combine(_root, "export.mp4");
        await new VideoLoopExporter(_runner, _prober).RenderAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), Source,
            reference, info, new(11, 50, 10), new(VideoLoopMode.Natural, 1, 1, true), new(1, 5, 99), null, CancellationToken.None);
        var referenceFrames = await Frames(reference);
        var seam = full ? 40 : 15;
        Assert.InRange(Difference(referenceFrames[^1], decoded[seam - 1]), 0, 4);
        Assert.InRange(Difference(referenceFrames[0], decoded[seam]), 0, 4);
        Assert.True((await Probe(reference)).HasAudio);
    }

    [Theory]
    [InlineData(0.4, 4)]
    [InlineData(0.2, 2)]
    public async Task VeryShortNaturalSelection_IsNotRepeatedArtificially(double duration, int count)
    {
        await Generate("testsrc2=s=160x96:r=10", 1);
        using var preview = await Build(await Probe(Source), new(0, duration, 90));
        var clip = Assert.Single(preview.Clips);
        Assert.Equal(count, (await Frames(clip.Path)).Count);
        Assert.InRange((await Probe(clip.Path)).Duration!.Value.TotalSeconds, duration - 0.003, duration + 0.003);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VfrPreview_PreservesIntervalsAndFinalHold(bool full)
    {
        await Generate("testsrc2=s=160x96:r=10", 3, outputFilter: "select='if(lt(n,10),1,not(mod(n,2)))'");
        var info = await Probe(Source);
        using var preview = await Build(info, new(0, 2.2, 99), full ? VideoLoopPreviewKind.FullLoop : VideoLoopPreviewKind.Junction);
        var clip = Assert.Single(preview.Clips);
        var frames = await Frames(clip.Path);
        if (full)
        {
            Assert.Equal(48, frames.Count);
            Assert.InRange(frames[16].Time - frames[15].Time, 0.199, 0.201);
            Assert.InRange((await Probe(clip.Path)).Duration!.Value.TotalSeconds, 6.597, 6.603);
        }
        else
        {
            // Tail starts at native 1.2, not the requested in-between-frame 1.1.
            Assert.Equal(16, frames.Count);
            Assert.InRange(frames[5].Time - frames[4].Time, 0.199, 0.201);
            Assert.InRange(frames[6].Time - frames[5].Time, 0.099, 0.101);
            Assert.InRange((await Probe(clip.Path)).Duration!.Value.TotalSeconds, 2.097, 2.103);
        }
    }

    [Fact]
    public async Task SparseVfrTail_CropsAnExistingHeldImage_WithoutInventingFrames()
    {
        await Generate("testsrc2=s=160x96:r=10:d=3", 3, outputFilter: "select='eq(n,0)+eq(n,1)+eq(n,29)'");
        using var preview = await Build(await Probe(Source), new(0, 2.9, 99));
        var clip = Assert.Single(preview.Clips);
        var frames = await Frames(clip.Path);
        Assert.Equal(3, frames.Count);
        Assert.InRange(frames[1].Time, 1.449, 1.451);
        Assert.InRange(frames[2].Time, 1.549, 1.551);
        Assert.InRange((await Probe(clip.Path)).Duration!.Value.TotalSeconds, 2.897, 2.903);
        var sourceFrames = await Frames(Source);
        Assert.InRange(Difference(sourceFrames[1], frames[0]), 0, 4);
        Assert.InRange(Difference(sourceFrames[0], frames[1]), 0, 4);
        Assert.InRange(Difference(sourceFrames[1], frames[2]), 0, 4);
    }

    [Fact]
    public async Task SingleFrameSelection_StillShowsItsRealLastToFirstJoin()
    {
        await Generate("color=c=blue:s=160x96:r=10", 1);
        using var preview = await Build(await Probe(Source), new(0, 0.1, 99));
        var clip = Assert.Single(preview.Clips);
        var frames = await Frames(clip.Path);
        Assert.Equal(2, frames.Count);
        Assert.InRange(frames[1].Time, 0.049, 0.051);
        Assert.InRange((await Probe(clip.Path)).Duration!.Value.TotalSeconds, 0.097, 0.103);
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(0.75d)]
    [InlineData(0.5d)]
    public async Task ForwardBackward_LongPreview_ContainsBothRealTurns_InDistinctClips(double speed)
    {
        await Generate("nullsrc=s=160x96:r=10,geq=lum='20+3*N':cb=128:cr=128", 6);
        using var preview = await Build(await Probe(Source), new(0, 6, 99), settings: new(VideoLoopMode.PingPong, speed, 1, false));
        Assert.Equal(2, preview.Clips.Count);
        var source = await Frames(Source);
        for (var clipIndex = 0; clipIndex < 2; clipIndex++)
        {
            var clip = preview.Clips[clipIndex];
            var frames = await Frames(clip.Path);
            Assert.False((await Probe(clip.Path)).HasAudio);
            var n = (frames.Count + 1) / 2;
            Assert.Equal(2 * n - 1, frames.Count);
            var expected = clipIndex == 0
                ? Enumerable.Range(source.Count - n, n).Concat(Enumerable.Range(source.Count - n, n - 1).Reverse()).ToArray()
                : Enumerable.Range(0, n).Reverse().Concat(Enumerable.Range(1, n - 1)).ToArray();
            for (var i = 0; i < expected.Length; i++) Assert.InRange(Difference(source[expected[i]], frames[i]), 0, 5);
            Assert.InRange((await Probe(clip.Path)).Duration!.Value.TotalSeconds, frames.Count / (10 * speed) - 0.01, frames.Count / (10 * speed) + 0.01);
        }
    }

    [Theory]
    [InlineData(1920, 1080, 36d, 8)]
    [InlineData(3840, 2160, 90d, 2)]
    public async Task LongHdAnd4kSelections_OnlyRenderABoundedJunction(int width, int height, double duration, int fps)
    {
        await Generate($"color=c=blue:s={width}x{height}:r={fps}", duration, audio: true);
        var info = await Probe(Source);
        using var preview = await Build(info, new(0, duration, 90));
        var clip = Assert.Single(preview.Clips);
        var output = await Probe(clip.Path);
        Assert.Equal(960, output.VideoWidth); Assert.Equal(540, output.VideoHeight);
        Assert.False(output.HasAudio);
        Assert.InRange(output.Duration!.Value.TotalSeconds, 2.99, 3.01);
        Assert.Equal(3 * fps, (await Frames(clip.Path)).Count);
        Assert.True(Directory.EnumerateFiles(preview.Directory).Sum(file => new FileInfo(file).Length) < 2 * 1024 * 1024);
    }

    [Fact]
    public async Task CanceledAndFailedPreview_CleanTheirOwnedDirectories_AndPermitRetry()
    {
        await Generate("testsrc2=s=160x96:r=10", 3);
        var info = await Probe(Source);
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Build(info, new(0, 3, 90), progress: new CancelProgress(cts), token: cts.Token));
        // A second render proves that cancellation released the decoder/source handles.
        using var retry = await Build(info, new(0, 3, 90));
        var path = retry.Directory;
        retry.Dispose(); Assert.False(Directory.Exists(path));
        var bad = Source + ".missing";
        await Assert.ThrowsAnyAsync<Exception>(() => new VideoLoopPreviewBuilder(_runner, _prober).BuildAsync(
            _tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), bad, info, new(1, 30, 10),
            new(VideoLoopMode.Natural), new(0, 3, 90), VideoLoopPreviewKind.Junction, null, CancellationToken.None));
        File.Delete(Source); Assert.False(File.Exists(Source));
    }

    [Fact]
    public async Task FullHdPreview_UsesProxyResolution_ForThreeCompleteCycles()
    {
        await Generate("color=c=blue:s=1920x1080:r=8", 36, audio: true);
        using var preview = await Build(await Probe(Source), new(0, 36, 90), VideoLoopPreviewKind.FullLoop);
        var clip = Assert.Single(preview.Clips);
        var output = await Probe(clip.Path);
        Assert.Equal(960, output.VideoWidth); Assert.Equal(540, output.VideoHeight);
        Assert.False(output.HasAudio);
        Assert.InRange(output.Duration!.Value.TotalSeconds, 107.997, 108.003);
        Assert.Equal(864, (await Frames(clip.Path)).Count);
    }

    [Fact]
    public async Task FullPreviewBudget_ExplainsTheShortJunctionAlternative_BeforeDecoding()
    {
        var probe = new MediaProbeResult(TimeSpan.FromHours(1), false, true, 3840, 2160, "h264", 30, 108000, [], [], []);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Build(probe, new(0, 3600, 90), VideoLoopPreviewKind.FullLoop));
        Assert.Contains("Use Preview junction", error.Message);
    }

    [Fact]
    public async Task VideoOffset_IsSharedByPreviewAndExport()
    {
        await Generate("nullsrc=s=160x96:r=10:d=3,geq=lum='20+3*N':cb=128:cr=128", 5, audio: true,
            outputFilter: "setpts=PTS+2/TB");
        var info = await Probe(Source);
        Assert.InRange(info.VideoStartSeconds - info.FormatStartSeconds, 1.99, 2.01);
        using var preview = await Build(info, new(0, 2.2, 90));
        var decoded = await Frames(Assert.Single(preview.Clips).Path);
        var originals = await Frames(Source);
        var expected = Enumerable.Range(11, 11).Concat(Enumerable.Range(0, 11)).ToArray();
        Assert.Equal(expected.Length, decoded.Count);
        for (var i = 0; i < expected.Length; i++) Assert.InRange(Difference(originals[expected[i]], decoded[i]), 0, 4);
        Assert.InRange((await Probe(preview.Clips[0].Path)).Duration!.Value.TotalSeconds, 2.197, 2.203);
    }

    private sealed class CancelProgress(CancellationTokenSource cts) : IProgressReporter
    {
        public bool IsCancellationRequested => cts.IsCancellationRequested;
        public bool IsQueueItemRemovalRequested(string path) => false;
        public bool IsQueueItemCancellationRequested(string path) => false;
        public void ReportQueue(IReadOnlyList<string> items) { }
        public void ReportQueueItem(string path, string state, string? message = null) { }
        public void ReportState(string state, string? message = null) => cts.Cancel();
        public void ReportProgress(int value, string? file, string? action, string? message, string? etaText = null) => cts.Cancel();
    }
}
