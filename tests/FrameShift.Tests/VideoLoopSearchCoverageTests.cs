using FrameShift.Core.Actions;
using Xunit;
using static FrameShift.Core.Actions.VideoLoopDetector;

namespace FrameShift.Tests;

public sealed class VideoLoopSearchCoverageTests
{
    [Fact]
    public void HourSampling_KeepsEightFps_AndBoundedDecodeBlocks()
    {
        var plan = PlanSampling(3600, 30);
        Assert.Equal(8, plan.Rate); Assert.True(plan.Complete);
        Assert.Equal(113, plan.Blocks.Count);
        Assert.Equal(3600, plan.Blocks.Sum(b => b.Duration), 6);
        Assert.All(plan.Blocks, b => Assert.True(b.Duration * plan.Rate <= SamplingBlockFrames + 0.000001));
        Assert.Equal(0, plan.Blocks[0].Start); Assert.Equal(3600, plan.Blocks[^1].Start + plan.Blocks[^1].Duration, 6);
    }

    [Fact]
    public void BeyondSamplingBudget_UsesDistributedWindows_AndReportsGaps()
    {
        var plan = PlanSampling(14400, 60);
        Assert.Equal(8, plan.Rate); Assert.False(plan.Complete);
        Assert.Equal(MaximumSamples, plan.Blocks.Sum(b => b.Duration * plan.Rate), 6);
        Assert.Equal(0, plan.Blocks[0].Start); Assert.Equal(14400, plan.Blocks[^1].Start + plan.Blocks[^1].Duration, 6);
        Assert.True(plan.Blocks[1].Start > plan.Blocks[0].Duration);
    }

    [Fact]
    public void DistributedSeedBudget_CoversMoreThanSixteenDistinctRegions()
    {
        var samples = new List<SearchSample>();
        for (var region = 0; region < 20; region++)
        {
            var random = new Random(100 + region);
            var phases = Enumerable.Range(0, 8).Select(_ => { var pixels = new byte[144]; random.NextBytes(pixels); return pixels; }).ToArray();
            for (var n = 0; n < 24; n++) samples.Add(new(region * 3 + n / 8d, phases[n % 8]));
        }
        var first = SelectSeeds(samples, 60, 1, 8, LoopSensitivity.Normal, CancellationToken.None);
        Assert.True(first.Candidates.Count > 16);
        Assert.All(Enumerable.Range(0, 20), r => Assert.Contains(first.Candidates, c => (int)(c.StartSeconds / 3) == r));
        Assert.Equal(first.Candidates, SelectSeeds(samples, 60, 1, 8, LoopSensitivity.Normal, CancellationToken.None).Candidates);
        Assert.True(first.ComparedPairs < (long)samples.Count * 100);
    }

    [Fact]
    public void IdenticalSignatures_WithLargeMinimum_AvoidTheOldLocalNeighborBlindSpot()
    {
        var descriptor = Enumerable.Repeat((byte)80, 144).ToArray();
        var samples = Enumerable.Range(0, 28800).Select(i => new SearchSample(i / 8d, descriptor)).ToArray();
        var result = SelectSeeds(samples, 3600, 120, 8, LoopSensitivity.Strict, CancellationToken.None);
        Assert.NotEmpty(result.Candidates);
        Assert.All(result.Candidates, c => Assert.True(c.DurationSeconds >= 120));
        Assert.True(result.Candidates.Count <= MaximumSeedTracks);
        Assert.True(result.ComparedPairs < (long)samples.Length * 100);
        Assert.True(result.Limited);
        Assert.Equal(24, result.Regions);
        Assert.Contains(result.Candidates, c => c.StartSeconds >= 3000);
    }

    [Fact]
    public void CompactIndex_FindsMatchingSignaturesSeveralMinutesApart()
    {
        var rng = new Random(17);
        var phases = Enumerable.Range(0, 24).Select(_ => { var d = new byte[144]; rng.NextBytes(d); return d; }).ToArray();
        var samples = Enumerable.Range(0, 24).Select(i => new SearchSample(i / 8d, phases[i]))
            .Concat(Enumerable.Range(0, 24).Select(i => new SearchSample(600 + i / 8d, phases[i]))).ToArray();
        var result = SelectSeeds(samples, 603, 60, 8, LoopSensitivity.Normal, CancellationToken.None);
        Assert.Contains(result.Candidates, c => Math.Abs(c.DurationSeconds - 600) < 0.001);
    }

    [Fact]
    public void CanceledCompactSearch_DoesNotEnterThePairLoops()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => SelectSeeds([], 3600, 1, 8, LoopSensitivity.Normal, cts.Token));
    }

    [Fact]
    public void EofCandidate_UsesTheKnownExclusiveEnd_WhenPacketDurationIsMissing()
    {
        var pixels = Enumerable.Repeat((byte)80, 64 * 48 * 3).ToArray();
        var left = Enumerable.Range(0, 5).Select(i => new VideoLoopFrameReader.Frame(i / 10d, pixels)).ToArray();
        var right = Enumerable.Range(13, 7).Select(i => new VideoLoopFrameReader.Frame(i / 10d, pixels)).ToArray();
        var candidates = BoundaryVariants(left, right, 2, 2, true, LoopSensitivity.Strict, CancellationToken.None);
        Assert.Contains(candidates, c => c.StartSeconds == 0 && c.EndSeconds == 2);
        Assert.Empty(BoundaryVariants(left, right, 2, 2, false, LoopSensitivity.Strict, CancellationToken.None));
    }

    [Fact]
    public async Task WholeSourceCycle_IsDetectedWithItsActualExclusiveEof()
    {
        using var corpus = new VideoLoopTestCorpus();
        await corpus.GenerateAsync("whole-source", 5, CancellationToken.None);
        var result = await new VideoLoopDetector(corpus.Runner).AnalyzeAsync(corpus.Tools.ResolveFfmpegPath(), corpus.Source,
            corpus.Probe, 1, LoopSensitivity.Normal, null, CancellationToken.None);
        Assert.Contains(result.Candidates, c => Math.Abs(c.StartSeconds) < 0.001 && Math.Abs(c.EndSeconds - 5) < 0.001);
    }

    [Fact]
    public async Task SourceExactlyAtTheMinimum_IsStillAWholeCycleCandidate()
    {
        using var corpus = new VideoLoopTestCorpus();
        await corpus.GenerateAsync("periodic", 1, CancellationToken.None);
        var result = await new VideoLoopDetector(corpus.Runner).AnalyzeAsync(corpus.Tools.ResolveFfmpegPath(), corpus.Source,
            corpus.Probe, 1, LoopSensitivity.Normal, null, CancellationToken.None);
        Assert.Contains(result.Candidates, c => c.StartSeconds == 0 && Math.Abs(c.EndSeconds - 1) < 0.000001);
    }

    [Fact]
    public async Task SourceChangedDuringSearch_DoesNotPublishAMixedResult()
    {
        using var corpus = new VideoLoopTestCorpus();
        await corpus.GenerateAsync("periodic", 5, CancellationToken.None);
        var touched = false;
        await Assert.ThrowsAsync<VideoLoopSourceChangedException>(() => new VideoLoopDetector(corpus.Runner).AnalyzeAsync(
            corpus.Tools.ResolveFfmpegPath(), corpus.Source, corpus.Probe, 1, LoopSensitivity.Normal,
            new DirectProgress(p =>
            {
                if (!touched && p.Percent >= 30) { touched = true; File.SetLastWriteTimeUtc(corpus.Source, File.GetLastWriteTimeUtc(corpus.Source).AddSeconds(2)); }
            }), CancellationToken.None));
        Assert.True(touched);
    }

    [Fact]
    public async Task RealFourRegionSearch_ReturnsMoreThanThreeValidatedCandidates()
    {
        using var corpus = new VideoLoopTestCorpus();
        await corpus.GenerateAsync("four-regions", 12, CancellationToken.None);
        var result = await new VideoLoopDetector(corpus.Runner).AnalyzeAsync(corpus.Tools.ResolveFfmpegPath(), corpus.Source,
            corpus.Probe, 1, LoopSensitivity.Normal, null, CancellationToken.None);
        Assert.True(result.Candidates.Count > 3);
        Assert.All(Enumerable.Range(0, 4), region => Assert.Contains(result.Candidates,
            c => c.StartSeconds >= region * 3 && c.EndSeconds <= (region + 1) * 3 + 0.001));
        Assert.True(result.Diagnostics.MaximumResidentSampleFrames <= SamplingBlockFrames);
        Assert.True(result.Diagnostics.RetainedSignatureBytes < result.Diagnostics.SampledFrames * 200L);
        Assert.True(result.Diagnostics.FullTemporalCoverage);
    }

    [Fact]
    public async Task MoreThanSixteenCyclicRegions_AreRetainedAndPublishedInBatches()
    {
        using var corpus = new VideoLoopTestCorpus();
        await corpus.GenerateAsync("many-regions", 60, CancellationToken.None);
        var batches = new List<VideoLoopAnalysis>();
        var result = await new VideoLoopDetector(corpus.Runner).AnalyzeAsync(corpus.Tools.ResolveFfmpegPath(), corpus.Source,
            corpus.Probe, 1, LoopSensitivity.Normal, new DirectProgress(p => { if (p.Results is { } a) batches.Add(a); }), CancellationToken.None);
        Assert.True(result.Candidates.Count > 16, $"Only {result.Candidates.Count} candidates were retained.");
        Assert.All(Enumerable.Range(0, 20), region => Assert.Contains(result.Candidates,
            c => c.StartSeconds >= region * 3 && c.EndSeconds <= (region + 1) * 3 + 0.001));
        Assert.True(result.Diagnostics.ExaminedSeeds > 16);
        Assert.Contains(batches, b => !b.SearchCompleted);
        Assert.True(batches[^1].SearchCompleted);
        Assert.All(batches, b => Assert.Throws<NotSupportedException>(() => ((IList<VideoLoopCandidate>)b.Candidates).Clear()));
        Assert.True(result.Diagnostics.PeakRawTemporaryBytes <= SamplingBlockFrames * 64L * 48 * 3);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(30)]
    [InlineData(35)]
    public async Task CancellationAtEachSearchPhase_ReleasesTheSource(int phase)
    {
        using var corpus = new VideoLoopTestCorpus();
        await corpus.GenerateAsync("periodic", 5, CancellationToken.None);
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new VideoLoopDetector(corpus.Runner).AnalyzeAsync(
            corpus.Tools.ResolveFfmpegPath(), corpus.Source, corpus.Probe, 1, LoopSensitivity.Normal,
            new DirectProgress(p => { if (p.Percent >= phase) cts.Cancel(); }), cts.Token));
        File.Delete(corpus.Source); Assert.False(File.Exists(corpus.Source));
    }

    private sealed class DirectProgress(Action<VideoLoopProgress> report) : IProgress<VideoLoopProgress>
    { public void Report(VideoLoopProgress value) => report(value); }
}
