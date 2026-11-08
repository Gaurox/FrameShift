using FrameShift.Core.Actions;
using FrameShift.Core.FFprobe;
using Xunit;
using static FrameShift.Core.Actions.VideoLoopFrameReader;

namespace FrameShift.Tests;

public sealed class VideoLoopQualityTests
{
    private static Frame Solid(double time, int level, int width = 64, int height = 48)
        => new(time, Enumerable.Repeat((byte)Math.Clamp(level, 0, 255), width * height * 3).ToArray())
        { PixelWidth = width, PixelHeight = height };

    private static Frame[] Ramp(double offset = 0, int step = 12)
        => Enumerable.Range(0, 11).Select(i => Solid(offset + i / 10d, 40 + i * step)).ToArray();

    [Theory]
    [InlineData(1920, 1080, 0, 1, 192, 108)]
    [InlineData(1080, 1920, 0, 1, 108, 192)]
    [InlineData(1920, 1080, 90, 1, 108, 192)]
    [InlineData(720, 576, 0, 1.6, 192, 96)]
    [InlineData(720, 576, 90, 1.6, 96, 192)]
    [InlineData(3840, 960, 0, 1, 192, 48)]
    public void Refinement_PreservesDisplayedAspectRatio(int width, int height, int rotation, double sar, int expectedWidth, int expectedHeight)
    {
        var probe = new MediaProbeResult(TimeSpan.FromSeconds(4), false, true, width, height, "ffv1", 30, 120, [], [], [])
        { RotationDegrees = rotation, VideoSampleAspectRatio = sar };
        Assert.Equal((expectedWidth, expectedHeight), VideoLoopDetector.RefinementSize(probe));
    }

    [Theory]
    [InlineData(108, 192)]
    [InlineData(192, 108)]
    [InlineData(192, 48)]
    public void SmallLocalDetail_IsNotHiddenByAStaticBackground(int width, int height)
    {
        var left = Ramp().Select(f => Solid(f.Time, 40, width, height)).ToArray();
        var right = left.Select(f => Solid(f.Time + 2, 40, width, height)).ToArray();
        foreach (var frame in right)
        for (var y = 0; y < 2; y++)
        for (var x = 0; x < 2; x++)
        for (var c = 0; c < 3; c++) frame.Pixels[(y * width + x) * 3 + c] = 255;
        var coarseDelta = VideoLoopSeamEvaluator.Descriptor(left[0]).Zip(VideoLoopSeamEvaluator.Descriptor(right[0]), (a, b) => Math.Abs(a - b)).Max();
        Assert.InRange(coarseDelta, 0, 4);
        Assert.Null(new VideoLoopSeamEvaluator(left, right).Evaluate(5, 4, 2.5, LoopSensitivity.Strict, CancellationToken.None));
    }

    [Fact]
    public void Dimensions_AreValidatedBeforeComparingPixels()
    {
        Assert.Throws<ArgumentException>(() => VideoLoopSeamEvaluator.Descriptor(new(0, new byte[10])));
        var left = Ramp(); var right = Ramp(2).Select(f => Solid(f.Time, 40, 108, 192)).ToArray();
        Assert.Throws<ArgumentException>(() => new VideoLoopSeamEvaluator(left, right).Evaluate(5, 4, 2.5, LoopSensitivity.Normal, CancellationToken.None));
    }

    [Fact]
    public void Descriptors_KeepTheSameSpatialLayoutAtDifferentDimensions()
    {
        Frame Pattern(int width, int height)
        {
            var frame = Solid(0, 0, width, height);
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            for (var c = 0; c < 3; c++) frame.Pixels[(y * width + x) * 3 + c] = (byte)(x * 8 / width * 20 + y * 6 / height * 10 + c);
            return frame;
        }
        var descriptor = VideoLoopSeamEvaluator.Descriptor(Pattern(64, 48));
        Assert.Equal(descriptor, VideoLoopSeamEvaluator.Descriptor(Pattern(192, 144)));
        Assert.Equal(descriptor, VideoLoopSeamEvaluator.Descriptor(Pattern(64, 144)));
        Assert.Equal(descriptor, VideoLoopSeamEvaluator.Descriptor(Pattern(192, 48)));
    }

    [Fact]
    public void UnrelatedNoisyEndpoints_DoNotReceiveAPromisingAssessment()
    {
        var random = new Random(23);
        Frame Noise(double time) { var f = Solid(time, 0); random.NextBytes(f.Pixels); return f; }
        var left = Enumerable.Range(0, 10).Select(i => Noise(i / 10d)).ToArray();
        var right = Enumerable.Range(0, 10).Select(i => Noise(2 + i / 10d)).ToArray();
        var candidate = new VideoLoopSeamEvaluator(left, right).Evaluate(0, 9, 3, LoopSensitivity.Normal, CancellationToken.None);
        Assert.True(candidate is null || candidate.JunctionAssessment != "Promising", candidate?.Seam?.ToString());
    }

    [Theory]
    [InlineData(10)]
    [InlineData(24)]
    [InlineData(60)]
    [InlineData(240)]
    public void ExactMotion_UsesBoundedTimeContextAcrossFrameRates(int fps)
    {
        var left = Enumerable.Range(0, fps + 1).Select(i => Solid(i / (double)fps, 40 + (int)Math.Round(i * 120d / fps))).ToArray();
        var right = left.Select(f => f with { Time = f.Time + 2 }).ToArray();
        var a = fps / 2;
        var candidate = new VideoLoopSeamEvaluator(left, right).Evaluate(a, a - 1, right[a].Time, LoopSensitivity.Strict, CancellationToken.None);
        Assert.NotNull(candidate);
        Assert.Equal("Promising", candidate.JunctionAssessment);
        Assert.InRange(candidate.Seam!.ComparedFrames, 1, 5);
        Assert.InRange(candidate.Seam.ContextSeconds, 0.12, 0.35);
    }

    [Fact]
    public void IrregularVfr_UsesElapsedTimeForVelocity()
    {
        double[] lt = [0, 0.06, 0.1, 0.2, 0.3, 0.4, 0.5];
        double[] rt = [0, 0.05, 0.1, 0.2, 0.3, 0.45, 0.5];
        var left = lt.Select(t => Solid(t, 40 + (int)Math.Round(t * 100))).ToArray();
        var right = rt.Select(t => Solid(2 + t, 40 + (int)Math.Round(t * 100))).ToArray();
        var candidate = new VideoLoopSeamEvaluator(left, right).Evaluate(3, 2, 2.2, LoopSensitivity.Strict, CancellationToken.None);
        Assert.NotNull(candidate);
        Assert.InRange(candidate.Seam!.Error, 0, 0.001);
    }

    [Fact]
    public void OppositeMotion_AndLocalLighting_ArePenalizedDespiteMatchingPoses()
    {
        var left = Ramp();
        var opposite = Enumerable.Range(0, 11).Select(i => Solid(2 + i / 10d, 160 - i * 12)).ToArray();
        Assert.Null(new VideoLoopSeamEvaluator(left, opposite).Evaluate(5, 4, 2.5, LoopSensitivity.Normal, CancellationToken.None));
        var lit = Ramp(2).Select(f => f with { Pixels = f.Pixels.Select(p => (byte)(p + 20)).ToArray() }).ToArray();
        Assert.Null(new VideoLoopSeamEvaluator(left, lit).Evaluate(5, 4, 2.5, LoopSensitivity.Normal, CancellationToken.None));
    }

    [Fact]
    public void DuplicatedPose_AndSkippedPhase_DoNotReceiveTheExactSeamAssessment()
    {
        var left = Ramp(); var exact = Ramp(2);
        var evaluator = new VideoLoopSeamEvaluator(left, exact);
        var good = evaluator.Evaluate(5, 4, 2.5, LoopSensitivity.Permissive, CancellationToken.None)!;
        var pause = exact.ToArray(); pause[4] = Solid(2.4, 100);
        var skipped = exact.ToArray(); skipped[4] = Solid(2.4, 64);
        foreach (var altered in new[] { pause, skipped })
        {
            var bad = new VideoLoopSeamEvaluator(left, altered).Evaluate(5, 4, 2.5, LoopSensitivity.Permissive, CancellationToken.None);
            Assert.True(bad is null || bad.Seam!.Error > good.Seam!.Error + 0.01);
            Assert.True(bad is null || bad.JunctionAssessment != "Promising");
        }
    }

    [Fact]
    public void MovementAwareShortlist_DoesNotLoseCoherentPairsBehindMatchingStillPoses()
    {
        var left = Ramp(step: 16);
        var right = Enumerable.Range(0, 40).Select(i => Solid(2 + i / 10d, i < 29 ? 120 : 40 + (i - 29) * 16)).ToArray();
        var shortlist = new VideoLoopSeamEvaluator(left, right).Shortlist(1, CancellationToken.None);
        Assert.Contains(shortlist, p => p.End >= 30 && p.Start == p.End - 29);
        Assert.True(shortlist.Count <= VideoLoopSeamEvaluator.MaximumVerifiedPairs);
    }

    [Theory]
    [InlineData(0, "Promising")]
    [InlineData(0.012, "Promising")]
    [InlineData(0.02, "Approximate")]
    [InlineData(0.04, "Approximate")]
    [InlineData(0.05, "Needs review")]
    public void Assessments_UseAbsoluteError_AndNeverAPercentage(double error, string expected)
    {
        var candidate = new VideoLoopCandidate(0, 4, 100) { Seam = new(error, 0, 0, 0, 0, false, 0.2, 5) };
        Assert.Equal(expected, candidate.JunctionAssessment);
        Assert.DoesNotContain("%", candidate.ToString());
        Assert.Equal("Needs review", new VideoLoopCandidate(0, 4, 100).JunctionAssessment);
    }

    [Fact]
    public void StaticVariants_AreGrouped_WhileDistinctDurationsAndStartsRemainAvailable()
    {
        var seam = new VideoLoopSeamMetrics(0, 0, 0, 0, 0, true, 0.35, 5);
        var candidates = new[] { new VideoLoopCandidate(0, 1, 100) { Seam = seam }, new(2, 3.5, 100) { Seam = seam },
            new(0, 2, 100) { Seam = seam }, new(0, 4, 100) { Seam = seam }, new(8, 9, 99) };
        var ranked = VideoLoopDetector.Rank(candidates);
        Assert.Equal(4, ranked.Count);
        Assert.Contains(ranked, c => c.DurationSeconds == 4);
        Assert.Contains(ranked, c => c.StartSeconds == 8);
        Assert.Equal(ranked, VideoLoopDetector.Rank(candidates.Reverse()));
    }

    [Fact]
    public void NumericalNoise_DoesNotReorderEquivalentCycles()
    {
        var first = new VideoLoopCandidate(0, 1, 100 - 1e-10);
        var later = new VideoLoopCandidate(30, 31 - 1e-12, 100);
        var ranked = VideoLoopDetector.Rank([later, first]);
        Assert.Equal(first, ranked[0]);
        Assert.Equal(ranked, VideoLoopDetector.Rank([first, later]));
    }

    [Theory]
    [InlineData(30, 30, 1)]
    [InlineData(1, 30, 0.5)]
    [InlineData(0.4, 30, 0.2)]
    [InlineData(0.2, 15, 0.2)]
    public void AutoMinimum_KeepsTheNormalGuard_WithoutExcludingShortSources(double duration, double fps, double expected)
        => Assert.Equal(expected, VideoLoopDetector.AutoMinimum(duration, fps), 6);

    [Fact]
    public void CanceledRefinement_StopsBeforePixelVerification()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => VideoLoopDetector.RefineVariants(Ramp(), Ramp(2), new(0.5, 2.5, 100), 1, LoopSensitivity.Normal, cancel.Token));
    }
}
