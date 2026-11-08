using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class VideoLoopContractTests
{
    [Fact]
    public void ExportSelections_KeepNativeBoundsAndDoNotShareOptions()
    {
        var settings = new VideoLoopSettings(VideoLoopMode.Natural, Repetitions: 3, KeepAudio: false);
        var first = new VideoLoopExportSelection("first", new(1.001 / 24, 61.061 / 24, 99), settings);
        var second = new VideoLoopExportSelection("second", new(10.125, 46.125, 98), settings);
        var options = first.ToOptions();
        Assert.Equal(first.Bounds.StartSeconds, double.Parse(options["loop-start"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(first.Bounds.EndSeconds, double.Parse(options["loop-end"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(settings, VideoLoopSettings.FromOptions(options));
        options["loop-start"] = "999";
        options["loop-repeat"] = "10";
        Assert.NotEqual(options["loop-start"], first.ToOptions()["loop-start"]);
        Assert.Equal("10.125", second.ToOptions()["loop-start"]);
        Assert.Equal("3", second.ToOptions()["loop-repeat"]);
        Assert.DoesNotContain("loop-start", VideoLoopSettings.OptionNames); // Bounds remain internal, not public CLI options.
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(double.NaN, 2)]
    [InlineData(0, double.PositiveInfinity)]
    public void ExportSelection_RejectsInvalidBounds(double start, double end)
        => Assert.Throws<ArgumentException>(() => new VideoLoopExportSelection("candidate", new(start, end, 99),
            new(VideoLoopMode.Natural)).ToOptions());

    [Fact]
    public void ExportSelection_RejectsInvalidIdentityAndSettings()
    {
        Assert.Throws<ArgumentException>(() => new VideoLoopExportSelection(" ", new(0, 1, 99), new(VideoLoopMode.Natural)).ToOptions());
        Assert.Throws<ArgumentException>(() => new VideoLoopExportSelection("candidate", new(0, 1, 99),
            new(VideoLoopMode.PingPong, KeepAudio: true)).ToOptions());
    }

    [Fact]
    public void CandidateIdentity_IsStableAcrossQualityUpdatesAndDistinctForOtherBounds()
    {
        var candidate = new VideoLoopCandidate(0.125, 36.125, 99);
        var settings = new VideoLoopSettings(VideoLoopMode.Natural);
        var first = VideoLoopExportSelection.FromCandidate(candidate, settings);
        Assert.Equal(first.CandidateId, VideoLoopExportSelection.FromCandidate(candidate with { Quality = 98 }, settings).CandidateId);
        Assert.NotEqual(first.CandidateId, VideoLoopExportSelection.FromCandidate(candidate with { StartSeconds = 1 }, settings).CandidateId);
    }
}
