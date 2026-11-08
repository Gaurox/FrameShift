using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class VideoLoopCorpusTests
{
    [Fact]
    public async Task EntireSourceCycle_ExportsTheFullCycleIncludingTheEofSeam()
    {
        using var corpus = new VideoLoopTestCorpus();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await corpus.GenerateAsync("whole-source", 5, timeout.Token);
        var output = Path.Combine(corpus.Root, "two cycles.mp4");
        await new VideoLoopExporter(corpus.Runner, corpus.Prober).RenderAsync(corpus.Tools.ResolveFfmpegPath(),
            corpus.Tools.ResolveFfprobePath(), corpus.Source, output, corpus.Probe, new(1, 75, 15),
            new(VideoLoopMode.Natural, Repetitions: 2, KeepAudio: false), new(0, 5, 100), null, timeout.Token);
        var frames = await new VideoLoopFrameReader(corpus.Runner, corpus.Tools.ResolveFfmpegPath(), output)
            .ReadAsync(0, 11, null, 151, timeout.Token);
        Assert.Equal(150, frames.Count);
        Assert.InRange(frames[^1].Time, 9.93, 9.94);
        for (var i = 0; i < 75; i++)
            Assert.InRange(MeanDifference(frames[i].Pixels, frames[i + 75].Pixels), 0, 2);
        var seam = MeanDifference(frames[74].Pixels, frames[75].Pixels);
        var nearby = (MeanDifference(frames[73].Pixels, frames[74].Pixels) + MeanDifference(frames[75].Pixels, frames[76].Pixels)) / 2;
        Assert.InRange(seam, nearby * 0.7, nearby * 1.3);
    }

    [Fact]
    public async Task FourRegionCorpus_HasFourDistinctBackgroundsAndExactLocalCycles()
    {
        using var corpus = new VideoLoopTestCorpus();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await corpus.GenerateAsync("four-regions", 12, timeout.Token);
        var frames = await new VideoLoopFrameReader(corpus.Runner, corpus.Tools.ResolveFfmpegPath(), corpus.Source)
            .ReadAsync(0, 12, null, 181, timeout.Token);
        Assert.Equal(180, frames.Count);
        for (var region = 0; region < 4; region++)
        {
            var start = region * 45;
            for (var i = 0; i < 30; i++) Assert.Equal(frames[start + i].Pixels, frames[start + i + 15].Pixels);
            if (region > 0) Assert.True(MeanDifference(frames[0].Pixels, frames[start].Pixels) > 10);
        }
    }

    [Fact]
    public async Task LongerBetterCorpus_HasAnExactFourSecondCycleAndAnImperfectOneSecondCycle()
    {
        using var corpus = new VideoLoopTestCorpus();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await corpus.GenerateAsync("longer-better", 12, timeout.Token);
        var frames = await new VideoLoopFrameReader(corpus.Runner, corpus.Tools.ResolveFfmpegPath(), corpus.Source)
            .ReadAsync(0, 12, null, 181, timeout.Token);
        Assert.Equal(180, frames.Count);
        for (var i = 0; i < 120; i++) Assert.Equal(frames[i].Pixels, frames[i + 60].Pixels);
        Assert.True(Enumerable.Range(0, 60).Average(i => MeanDifference(frames[i].Pixels, frames[i + 15].Pixels)) > 1);
    }

    [Fact]
    public async Task SearchDiagnostics_AccountForTheActualDecodedFramesAndTemporaryFile()
    {
        using var corpus = new VideoLoopTestCorpus();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await corpus.GenerateAsync("periodic", 5, timeout.Token);
        var result = await new VideoLoopDetector(corpus.Runner).AnalyzeAsync(corpus.Tools.ResolveFfmpegPath(),
            corpus.Source, corpus.Probe, 1, LoopSensitivity.Normal, null, timeout.Token);
        Assert.NotEmpty(result.Candidates);
        Assert.Equal(40, result.Diagnostics.SampledFrames);
        Assert.True(result.Diagnostics.PeakRawTemporaryBytes >= 40L * VideoLoopFrameReader.Width * VideoLoopFrameReader.Height * 3);
        Assert.Equal(160, result.Diagnostics.RefinementWidth);
        Assert.Equal(96, result.Diagnostics.RefinementHeight);
        Assert.True(result.Diagnostics.PeakRawTemporaryBytes <= VideoLoopDetector.MaximumRefinementFrames * 160L * 96 * 3);
        Assert.True(result.Diagnostics.RefinedFrames > 0);
        Assert.True(result.Diagnostics.ExaminedSeeds > 0);
        Assert.True(result.Diagnostics.AcceptedCandidates >= result.Candidates.Count);
        Assert.True(result.Diagnostics.SamplingElapsed > TimeSpan.Zero);
        Assert.True(result.Diagnostics.SeedSelectionElapsed > TimeSpan.Zero);
        Assert.True(result.Diagnostics.RefinementElapsed > TimeSpan.Zero);
        Assert.True(result.Limits.HasFlag(VideoLoopSearchLimits.Sampling));
        Assert.Throws<NotSupportedException>(() => ((IList<VideoLoopCandidate>)result.Candidates).Clear());
    }

    private static double MeanDifference(byte[] a, byte[] b)
        => a.Zip(b, (x, y) => (double)Math.Abs(x - y)).Average();
}
