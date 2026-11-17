using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using Xunit;

namespace FrameShift.Tests;

public sealed class CutVideoActionTimingTests
{
    [Fact]
    public async Task TimeSelection_UsesTheRealFrameOrdinalsOnVariableFrameRateVideo()
    {
        using var fixture = new VideoLoopExportFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var source = Path.Combine(fixture.Root, "horodatages variables.mp4");
        await fixture.RunAsync([
            "-f", "lavfi", "-i", "color=c=black:s=64x64:r=60:d=5",
            "-vf", "drawbox=c=white:t=fill:enable='between(n,60,89)',setpts='if(lt(N,60),2*N,N+60)'",
            "-fps_mode", "vfr", "-c:v", "libx264", source
        ]);

        var output = await CutAsync(fixture, source, new Dictionary<string, string>
        {
            [ActionOptionKeys.StartTime] = "2", [ActionOptionKeys.EndTime] = "2.5"
        }, deadline.Token);
        Assert.EndsWith("__frame_61_to_90.mp4", output, StringComparison.Ordinal);
        var frames = await new VideoLoopFrameReader(fixture.Runner, fixture.Tools.ResolveFfmpegPath(), output)
            .ReadAsync(0, 1, null, 40, deadline.Token);
        Assert.Equal(30, frames.Count);
        Assert.All(frames, frame => Assert.True(frame.Pixels[0] > 210));
    }

    [Fact]
    public async Task MatroskaSubtitles_AreClippedAndRebasedWithoutExtendingTheCut()
    {
        using var fixture = new VideoLoopExportFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var subtitles = Path.Combine(fixture.Root, "sous-titres # été.srt");
        await File.WriteAllTextAsync(subtitles, """
            1
            00:00:00,100 --> 00:00:00,700
            Outside before

            2
            00:00:00,800 --> 00:00:01,200
            Overlap start

            3
            00:00:01,350 --> 00:00:01,450
            Inside

            4
            00:00:01,800 --> 00:00:02,400
            Overlap end

            5
            00:00:07,000 --> 00:00:09,000
            Outside after
            """ + "\n", deadline.Token);
        await fixture.RunAsync([
            "-f", "lavfi", "-i", "color=c=white:s=64x64:r=25:d=10", "-i", subtitles,
            "-c:v", "libx264", "-c:s", "srt", fixture.Source
        ]);

        var output = await CutAsync(fixture, fixture.Source, 26, 50, deadline.Token);
        var probe = await fixture.ReadProbeAsync(output);
        Assert.True(probe.HasSubtitles);
        Assert.InRange(probe.Duration!.Value.TotalSeconds, 0.999, 1.001);
        var packets = await fixture.Prober.RunProbeAsync(fixture.Tools.ResolveFfprobePath(),
            ["-v", "error", "-select_streams", "s:0", "-show_packets",
                "-show_entries", "packet=pts_time,duration_time", "-of", "csv=p=0", output], deadline.Token);
        Assert.True(packets.Success, packets.StandardError);
        Assert.Equal(new[] { "0.000000,0.200000", "0.350000,0.100000", "0.800000,0.200000" },
            packets.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    [Fact]
    public async Task VariableFrameRate_KeepsFrameIntervalsAndTheMatchingAudio()
    {
        using var fixture = new VideoLoopExportFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var source = Path.Combine(fixture.Root, "cadence variable.mp4");
        await fixture.RunAsync([
            "-f", "lavfi", "-i", "color=c=black:s=64x64:r=60:d=5",
            "-f", "lavfi", "-i", @"aevalsrc=0.5*sin(2*PI*1000*t)*between(t\,2.5\,2.6):s=48000:d=6",
            "-vf", "drawbox=c=white:t=fill:enable='eq(n,90)+eq(n,119)',setpts='if(lt(N,60),2*N,N+60)'",
            "-fps_mode", "vfr", "-c:v", "libx264", "-c:a", "aac", source
        ]);

        var output = await CutAsync(fixture, source, 31, 120, deadline.Token);
        var frames = await new VideoLoopFrameReader(fixture.Runner, fixture.Tools.ResolveFfmpegPath(), output)
            .ReadAsync(0, 2.2, null, 100, deadline.Token);
        Assert.Equal(90, frames.Count);
        Assert.Equal(1d / 30, frames[1].Time - frames[0].Time, 4);
        Assert.Equal(1d / 60, frames[41].Time - frames[40].Time, 4);
        Assert.True(frames[60].Pixels[0] > 210);
        Assert.True(frames[^1].Pixels[0] > 210);
        Assert.Equal(1.5, frames[60].Time, 4);

        var onset = await ReadToneOnsetAsync(fixture, output, deadline.Token);
        Assert.InRange(onset - frames[60].Time, -0.015, 0.015);
    }

    [Fact]
    public async Task DelayedAudio_KeepsItsOffsetFromTheFirstVideoFrame()
    {
        using var fixture = new VideoLoopExportFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var source = Path.Combine(fixture.Root, "piste décalée.mp4");
        await fixture.RunAsync([
            "-f", "lavfi", "-i", "color=c=black:s=64x64:r=25:d=4",
            "-itsoffset", "1", "-f", "lavfi", "-i", "sine=frequency=1000:sample_rate=48000:d=3",
            "-vf", "drawbox=c=white:t=fill:enable='eq(n,25)'", "-c:v", "libx264", "-c:a", "aac", source
        ]);

        var output = await CutAsync(fixture, source, 1, 50, deadline.Token);
        var frames = await new VideoLoopFrameReader(fixture.Runner, fixture.Tools.ResolveFfmpegPath(), output)
            .ReadAsync(0, 2.1, null, 60, deadline.Token);
        Assert.Equal(50, frames.Count);
        Assert.Equal(1, frames[25].Time, 4);
        Assert.True(frames[25].Pixels[0] > 210);

        var onset = await ReadToneOnsetAsync(fixture, output, deadline.Token);
        Assert.InRange(onset - frames[25].Time, -0.015, 0.015);
    }

    private static async Task<string> CutAsync(VideoLoopExportFixture fixture, string source,
        int first, int last, CancellationToken token)
    {
        var probe = await fixture.ReadProbeAsync(source);
        return await CutAsync(fixture, source,
            new CutVideoSettings(first, last, probe.VideoFrameRate!.Value).ToOptions(), token);
    }

    private static async Task<string> CutAsync(VideoLoopExportFixture fixture, string source,
        IReadOnlyDictionary<string, string> options, CancellationToken token)
    {
        var action = new CutVideoAction(fixture.Runner, fixture.Prober, fixture.Tools);
        var result = await action.ExecuteAsync(new(source, new AppLogger(), null, options), token);
        Assert.True(result.Success, result.Message);
        return Assert.IsType<string>(result.OutputPath);
    }

    internal static async Task<double> ReadToneOnsetAsync(VideoLoopExportFixture fixture, string video, CancellationToken token, int audioStream = 0)
    {
        const int sampleRate = 48000;
        const int windowSamples = 96;
        var pcm = Path.Combine(fixture.Root, $"audio-{audioStream}.pcm");
        await fixture.RunAsync(["-i", video, "-map", $"0:a:{audioStream}", "-af", "aresample=async=1:first_pts=0", "-ar", "48000", "-ac", "1", "-f", "s16le", pcm]);
        var bytes = await File.ReadAllBytesAsync(pcm, token);
        var levels = Enumerable.Range(0, bytes.Length / (2 * windowSamples)).Select(window =>
            Enumerable.Range(0, windowSamples).Average(i =>
                Math.Abs((int)BitConverter.ToInt16(bytes, 2 * (window * windowSamples + i))))).ToArray();
        var peak = levels.Max();
        Assert.True(peak > 1000, "The expected tone is missing.");
        // Use short windows so isolated AAC pre-echo samples do not define the onset.
        var onset = Array.FindIndex(levels, level => level > 0.4 * peak);
        return onset * windowSamples / (double)sampleRate;
    }
}
