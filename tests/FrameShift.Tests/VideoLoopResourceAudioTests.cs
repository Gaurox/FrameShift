using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class VideoLoopResourceAudioTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public async Task DelayedLongerAudio_PreservesEveryCyclesOffset_WithOneFinalAacEncode(int repeats)
    {
        using var fixture = new VideoLoopExportFixture(); await fixture.GenerateAsync(seconds: 2);
        var delayed = Path.Combine(fixture.Root, "audio décalé.mkv");
        await fixture.RunAsync(["-i", fixture.Source, "-itsoffset", "0.5", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=4",
            "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy", "-c:a", "pcm_s16le", delayed]);
        var info = await fixture.ReadProbeAsync(delayed);
        Assert.True(info.Duration!.Value.TotalSeconds > 4);
        var output = Path.Combine(fixture.Root, "continuous audio.mp4");
        await new VideoLoopExporter(fixture.Runner, fixture.Prober).RenderAsync(fixture.Tools.ResolveFfmpegPath(), fixture.Tools.ResolveFfprobePath(),
            delayed, output, info, new(1, 20, 10), new(VideoLoopMode.Natural, Repetitions: repeats), new(0, 2, 0), null, CancellationToken.None);
        var pcm = await DecodeAudio(fixture, output);
        Assert.InRange(pcm.Length / 2, repeats * 20000 - 300, repeats * 20000 + 300);
        for (var cycle = 0; cycle < repeats; cycle++)
        {
            var start = cycle * 2;
            Assert.InRange(Energy(pcm, start + 0.05, start + 0.3), 0, 15);
            Assert.True(Energy(pcm, start + 0.6, start + 0.9) > 250);
            Assert.True(Energy(pcm, start + 1.7, start + 1.9) > 250);
            var onset = Enumerable.Range((int)((start + 0.4) * 10000), 2000).First(i => Math.Abs((int)BitConverter.ToInt16(pcm, i * 2)) > 1000) / 10000d;
            Assert.InRange(onset - start, 0.47, 0.53);
        }
        var probe = await fixture.ReadProbeAsync(output);
        Assert.True(probe.HasAudio); Assert.InRange(probe.Duration!.Value.TotalSeconds, repeats * 2 - 0.03, repeats * 2 + 0.03);
    }

    [Fact]
    public async Task EarlierAudio_UsesTheOffsetVideoTimeline()
    {
        using var fixture = new VideoLoopExportFixture();
        await fixture.GenerateAsync("testsrc2=s=160x96:r=10:d=2", 5, videoFilter: "setpts=PTS+2/TB", audio: true);
        Assert.InRange(fixture.Probe.VideoStartSeconds - fixture.Probe.FormatStartSeconds, 1.99, 2.01);
        var output = Path.Combine(fixture.Root, "shifted video.mp4");
        await fixture.RenderAsync(output, new(VideoLoopMode.Natural, Repetitions: 3), new(0, 2, 0));
        var pcm = await DecodeAudio(fixture, output);
        for (var cycle = 0; cycle < 3; cycle++) Assert.True(Energy(pcm, cycle * 2 + 0.05, cycle * 2 + 0.3) > 250);
        Assert.Equal(60, (await VideoLoopResourceMediaTests.Packets(fixture, output)).Count);
    }

    [Fact]
    public async Task MutedNaturalExport_ExcludesTheSourceAudio()
    {
        using var fixture = new VideoLoopExportFixture(); await fixture.GenerateAsync(audio: true);
        var output = Path.Combine(fixture.Root, "muted.mp4");
        await fixture.RenderAsync(output, new(VideoLoopMode.Natural, Repetitions: 3, KeepAudio: false), new(0, 2, 0));
        Assert.False((await fixture.ReadProbeAsync(output)).HasAudio);
    }

    private static async Task<byte[]> DecodeAudio(VideoLoopExportFixture fixture, string source)
    {
        var output = Path.Combine(fixture.Root, Guid.NewGuid().ToString("N") + ".pcm");
        await fixture.RunAsync(["-i", source, "-map", "0:a:0", "-ar", "10000", "-ac", "1", "-f", "s16le", output]);
        return await File.ReadAllBytesAsync(output);
    }
    private static double Energy(byte[] pcm, double start, double end) =>
        Enumerable.Range((int)(start * 10000), (int)((end - start) * 10000)).Average(i => (double)Math.Abs((int)BitConverter.ToInt16(pcm, i * 2)));
}
