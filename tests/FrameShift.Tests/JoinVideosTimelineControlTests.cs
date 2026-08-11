using System;
using System.Linq;
using FrameShift.Core.FFprobe;
using FrameShift.Windows.Controls;
using Xunit;

namespace FrameShift.Tests;

public sealed class JoinVideosTimelineControlTests
{
    [Fact]
    public void SegmentWidths_FillTimelineAndFollowDurations()
    {
        var widths = JoinVideosTimelineControl.CalculateSegmentWidths([10d, 20d, 30d], 600);

        Assert.Equal(600, widths.Sum());
        Assert.Equal(new[] { 100, 200, 300 }, widths);
    }

    [Fact]
    public void SegmentWidths_KeepVeryShortClipUsableWithoutOverflowing()
    {
        var widths = JoinVideosTimelineControl.CalculateSegmentWidths([1d, 99d], 400);

        Assert.Equal(400, widths.Sum());
        Assert.Equal(12, widths[0]);
        Assert.Equal(388, widths[1]);
    }

    [Fact]
    public void SegmentWidths_TreatUnknownDurationsEquallyWhileLoading()
    {
        var widths = JoinVideosTimelineControl.CalculateSegmentWidths([0d, 0d, 0d], 301);

        Assert.Equal(301, widths.Sum());
        Assert.True(widths.Max() - widths.Min() <= 1);
    }

    [Fact]
    public void ShowExternalDropIndicator_ReturnsInsertionIndexBasedOnCursorPosition()
    {
        var control = new JoinVideosTimelineControl { Width = 300, Height = 200 };
        control.SetItems([
            new JoinVideoTimelineItem(@"C:\missing\a.mp4", 0),
            new JoinVideoTimelineItem(@"C:\missing\b.mp4", 1),
            new JoinVideoTimelineItem(@"C:\missing\c.mp4", 2)
        ]);

        Assert.Equal(0, control.ShowExternalDropIndicator(0));
        Assert.Equal(1, control.ShowExternalDropIndicator(120));
        Assert.Equal(3, control.ShowExternalDropIndicator(299));
    }

    [Fact]
    public void BuildCompatibilityHint_DifferentResolution_ReturnsHint()
    {
        var control = new JoinVideosTimelineControl();
        var anchor = CreateItem("clip1.mp4", width: 1920, height: 1080, hasAudio: true);
        var other = CreateItem("clip2.mp4", width: 1280, height: 720, hasAudio: true);
        control.SetItems([anchor, other]);

        Assert.Null(control.BuildCompatibilityHint(anchor, 0));
        Assert.Equal("Different resolution than the first clip.", control.BuildCompatibilityHint(other, 1));
    }

    [Fact]
    public void BuildCompatibilityHint_NoAudioButAnchorHasAudio_ReturnsHint()
    {
        var control = new JoinVideosTimelineControl();
        var anchor = CreateItem("clip1.mp4", width: 1920, height: 1080, hasAudio: true);
        var other = CreateItem("clip2.mp4", width: 1920, height: 1080, hasAudio: false);
        control.SetItems([anchor, other]);

        Assert.Equal("No audio on this clip.", control.BuildCompatibilityHint(other, 1));
    }

    [Fact]
    public void BuildCompatibilityHint_AnchorAlsoLacksAudio_ReturnsNull()
    {
        var control = new JoinVideosTimelineControl();
        var anchor = CreateItem("clip1.mp4", width: 1920, height: 1080, hasAudio: false);
        var other = CreateItem("clip2.mp4", width: 1920, height: 1080, hasAudio: false);
        control.SetItems([anchor, other]);

        Assert.Null(control.BuildCompatibilityHint(other, 1));
    }

    [Fact]
    public void BuildCompatibilityHint_MatchingClip_ReturnsNull()
    {
        var control = new JoinVideosTimelineControl();
        var anchor = CreateItem("clip1.mp4", width: 1920, height: 1080, hasAudio: true);
        var other = CreateItem("clip2.mp4", width: 1920, height: 1080, hasAudio: true);
        control.SetItems([anchor, other]);

        Assert.Null(control.BuildCompatibilityHint(other, 1));
    }

    private static JoinVideoTimelineItem CreateItem(string fileName, int width, int height, bool hasAudio)
    {
        var item = new JoinVideoTimelineItem($@"C:\missing\{fileName}", 0)
        {
            IsLoaded = true
        };
        var video = new JoinVideoStreamInfo(null, null, null, null, width, height, null, null, null, null, null, null, null, null, null, null, null, 0);
        var audio = hasAudio ? new JoinAudioStreamInfo(null, null, null, null, null, 2, null, null, null, null) : null;
        item.Probe = new JoinVideoProbeResult(TimeSpan.FromSeconds(10), "mov,mp4", 1, hasAudio ? 1 : 0, 0, video, audio);
        return item;
    }
}
