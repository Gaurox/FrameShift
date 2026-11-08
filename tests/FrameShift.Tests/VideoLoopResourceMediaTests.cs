using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using FrameShift.Core.Actions;
using Xunit;
using static FrameShift.Core.Actions.VideoLoopFrameReader;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class VideoLoopResourceMediaTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public async Task NaturalRepetitions_CopyTheSameEncodedPackets_AndKeepVfrHolds(int repeats)
    {
        using var fixture = new VideoLoopExportFixture();
        await fixture.GenerateAsync(videoFilter: "select='if(lt(n,10),1,not(mod(n,2)))'");
        var output = Path.Combine(fixture.Root, "copied packets.mp4");
        await fixture.RenderAsync(output, new(VideoLoopMode.Natural, Repetitions: repeats, KeepAudio: false), new(0, 2.2, 0));
        var packets = await Packets(fixture, output);
        Assert.Equal(16 * repeats, packets.Count);
        for (var j = 0; j < repeats; j++)
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(packets[i].Hash, packets[j * 16 + i].Hash);
            Assert.InRange(Math.Abs(packets[j * 16 + i].Time - (j * 2.2 + packets[i].Time)), 0, 0.000003);
        }
        Assert.InRange(packets[^1].Duration, 0.19999, 0.20001);
        for (var j = 1; j < repeats; j++) Assert.InRange(packets[j * 16].Time - packets[j * 16 - 1].Time, 0.19999, 0.20001);
        var probe = await fixture.ReadProbeAsync(output);
        Assert.False(probe.HasAudio); Assert.InRange(probe.Duration!.Value.TotalSeconds, 2.2 * repeats - 0.003, 2.2 * repeats + 0.003);
    }

    [Theory]
    [InlineData("cfr")]
    [InlineData("vfr")]
    [InlineData("odd")]
    [InlineData("sar")]
    public async Task DecodedVideo_MatchesTheLosslessIntermediateReference(string scenario)
    {
        using var fixture = new VideoLoopExportFixture();
        await fixture.GenerateAsync(scenario == "odd" ? "testsrc=s=161x97:r=10" : "testsrc2=s=160x96:r=10",
            videoFilter: scenario == "vfr" ? "select='if(lt(n,10),1,not(mod(n,2)))'" : scenario == "sar" ? "setsar=2/1" : null);
        var bounds = new VideoLoopCandidate(0, scenario == "vfr" ? 2.2 : 2, 0);
        var output = Path.Combine(fixture.Root, "compact.mp4"); var reference = Path.Combine(fixture.Root, "reference.mp4");
        await fixture.RenderAsync(output, new(VideoLoopMode.Natural, Repetitions: 3, KeepAudio: false), bounds);
        await VideoLoopNaturalReference.RenderAsync(fixture.Runner, fixture.Tools.ResolveFfmpegPath(), fixture.Source, reference,
            fixture.Probe, bounds, 3, false, CancellationToken.None);
        var actual = await Frames(fixture, output); var expected = await Frames(fixture, reference);
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < actual.Count; i++)
        {
            Assert.InRange(Math.Abs(actual[i].Time - expected[i].Time), 0, 0.002);
            Assert.InRange(Difference(actual[i], expected[i]), 0, 4);
        }
        var a = await fixture.ReadProbeAsync(output); var b = await fixture.ReadProbeAsync(reference);
        Assert.Equal(b.VideoWidth, a.VideoWidth); Assert.Equal(b.VideoHeight, a.VideoHeight);
        Assert.Equal(b.VideoSampleAspectRatio, a.VideoSampleAspectRatio, 5);
    }

    [Theory]
    [InlineData(1920, 1080, 30, 36)]
    [InlineData(3840, 2160, 2, 90)]
    public async Task LongHdAnd4kNaturalSelections_AreNoLongerRejectedByTheOldRawEstimate(int width, int height, int fps, int seconds)
    {
        using var fixture = new VideoLoopExportFixture();
        await fixture.GenerateAsync($"color=c=blue:s={width}x{height}:r={fps}", seconds);
        Assert.True((double)seconds * fps * width * height * 6 > VideoLoopResourceBudget.MaximumTemporaryBytes);
        long largestTemp = 0;
        var exporter = new VideoLoopExporter(fixture.Runner, fixture.Prober) { AvailableSpace = path =>
        {
            if (Directory.Exists(path) && Path.GetFileName(path).StartsWith("frameshift_loop_render_", StringComparison.Ordinal))
            {
                var bytes = Directory.GetFiles(path).Sum(f => new FileInfo(f).Length);
                Interlocked.Exchange(ref largestTemp, Math.Max(Interlocked.Read(ref largestTemp), bytes));
            }
            return VideoLoopResourceBudget.GetAvailableSpace(path);
        } };
        var output = Path.Combine(fixture.Root, "long loop.mp4");
        await fixture.RenderAsync(output, new(VideoLoopMode.Natural, Repetitions: 3, KeepAudio: false), new(0, seconds, 0), exporter: exporter);
        var info = await fixture.ReadProbeAsync(output);
        Assert.Equal(width, info.VideoWidth); Assert.Equal(height, info.VideoHeight);
        Assert.InRange(info.Duration!.Value.TotalSeconds, seconds * 3 - 0.005, seconds * 3 + 0.005);
        var count = await fixture.Prober.RunProbeAsync(fixture.Tools.ResolveFfprobePath(),
            ["-v", "error", "-select_streams", "v:0", "-count_packets", "-show_entries", "stream=nb_read_packets", "-of", "csv=p=0", output], CancellationToken.None);
        Assert.True(count.Success, count.StandardError); Assert.Equal(seconds * fps * 3, int.Parse(count.Output.Trim(), CultureInfo.InvariantCulture));
        Assert.InRange(largestTemp, 1, VideoLoopResourceBudget.MaximumTemporaryBytes);
        var evidence = EvidenceDirectory();
        await File.WriteAllTextAsync(Path.Combine(evidence, $"natural-{width}x{height}-{seconds}s.json"), JsonSerializer.Serialize(new
        { Width = width, Height = height, Fps = fps, Seconds = seconds, Repetitions = 3, LargestTemporaryBytes = largestTemp, OutputBytes = new FileInfo(output).Length, OutputSeconds = info.Duration.Value.TotalSeconds }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public async Task SparseVfr_AndSingleFrameCycles_KeepTheirFinalHold(int repeats)
    {
        using var fixture = new VideoLoopExportFixture();
        await fixture.GenerateAsync("testsrc2=s=160x96:r=10:d=0.3", 3, videoFilter: "setpts='if(eq(N,0),0,if(eq(N,1),1,29))'");
        var sourceFrames = await Frames(fixture, fixture.Source);
        Assert.Equal(new[] { 0d, 0.1, 2.9 }, sourceFrames.Select(f => f.Time));
        foreach (var (name, bounds, count) in new[] { ("sparse", new VideoLoopCandidate(0, 2.9, 0), 2), ("single", new VideoLoopCandidate(0.1, 2.9, 0), 1) })
        {
            var output = Path.Combine(fixture.Root, name + ".mp4");
            await fixture.RenderAsync(output, new(VideoLoopMode.Natural, Repetitions: repeats, KeepAudio: false), bounds);
            var packets = await Packets(fixture, output);
            Assert.Equal(count * repeats, packets.Count);
            Assert.InRange(packets[^1].Duration, 2.79999, 2.80001);
            var info = await fixture.ReadProbeAsync(output);
            Assert.InRange(info.Duration!.Value.TotalSeconds, bounds.DurationSeconds * repeats - 0.003, bounds.DurationSeconds * repeats + 0.003);
        }
    }

    [Fact]
    public async Task NonNativeStart_UsesTheFirstIncludedImageAndItsActualDuration()
    {
        using var fixture = new VideoLoopExportFixture(); await fixture.GenerateAsync();
        var output = Path.Combine(fixture.Root, "resolved.mp4");
        await fixture.RenderAsync(output, new(VideoLoopMode.Natural, Repetitions: 3, KeepAudio: false), new(0.15, 0.8, 0));
        var reference = Path.Combine(fixture.Root, "native reference.mp4");
        await VideoLoopNaturalReference.RenderAsync(fixture.Runner, fixture.Tools.ResolveFfmpegPath(), fixture.Source, reference,
            fixture.Probe, new(0.2, 0.8, 0), 3, false, CancellationToken.None);
        var frames = await Frames(fixture, output); var expected = await Frames(fixture, reference);
        Assert.Equal(18, frames.Count); Assert.InRange(Difference(frames[0], expected[0]), 0, 4);
        Assert.InRange((await fixture.ReadProbeAsync(output)).Duration!.Value.TotalSeconds, 1.797, 1.803);
    }

    internal static async Task<List<(double Time, double Duration, string Hash)>> Packets(VideoLoopExportFixture fixture, string path)
    {
        var result = await fixture.Prober.RunProbeAsync(fixture.Tools.ResolveFfprobePath(),
            ["-v", "error", "-select_streams", "v:0", "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=pts_time,duration_time,data_hash", "-of", "json", path], CancellationToken.None);
        Assert.True(result.Success, result.StandardError);
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.GetProperty("packets").EnumerateArray().Select(p =>
            (double.Parse(p.GetProperty("pts_time").GetString()!, CultureInfo.InvariantCulture),
                double.Parse(p.GetProperty("duration_time").GetString()!, CultureInfo.InvariantCulture), p.GetProperty("data_hash").GetString()!)).ToList();
    }
    private static Task<List<Frame>> Frames(VideoLoopExportFixture fixture, string source) =>
        new VideoLoopFrameReader(fixture.Runner, fixture.Tools.ResolveFfmpegPath(), source).ReadAsync(0, 120, null, 3000, CancellationToken.None);
    private static double Difference(Frame a, Frame b) => a.Pixels.Zip(b.Pixels, (x, y) => (double)Math.Abs(x - y)).Average();
    private static string EvidenceDirectory()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../scratch/loop-validation/improvements/lot7-2026-10-29"));
        Directory.CreateDirectory(path); return path;
    }
}
