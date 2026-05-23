using System;
using FrameShift.Core.Actions;
using FrameShift.Core.FFprobe;
using Xunit;

namespace FrameShift.Tests;

public sealed class VideoCompressionPlannerTests
{
    [Fact]
    public void BuildPlan_HighQuality_UsesExpectedCodecSettings()
    {
        var profile = VideoCompressionCatalog.GetProfileById("high");
        var probe = new MediaProbeResult(
            Duration: TimeSpan.FromMinutes(12),
            HasAudio: true,
            HasVideo: true,
            VideoWidth: 1920,
            VideoHeight: 1080,
            VideoCodec: "h264",
            VideoFrameRate: 30000d / 1001d,
            EstimatedVideoFrameCount: 21_600,
            AudioCodecs: ["aac"],
            SubtitleCodecs: ["subrip"],
            Streams:
            [
                new MediaStreamInfo(0, "video", "h264"),
                new MediaStreamInfo(1, "audio", "aac"),
                new MediaStreamInfo(2, "subtitle", "subrip")
            ]);

        var plan = VideoCompressionPlanner.BuildPlan(profile, probe, ".mp4", targetBytes: null);

        Assert.Equal("libx264", plan.VideoCodec);
        Assert.Contains("-crf", plan.VideoArgs);
        Assert.Contains("18", plan.VideoArgs);
        Assert.Equal("aac", plan.AudioCodec);
        Assert.Contains("-c:s", plan.SubtitleCodecArgs);
        Assert.Contains("mov_text", plan.SubtitleCodecArgs);
        Assert.Equal(".mp4", plan.OutputExtension);
        Assert.False(plan.UseTargetSize);
    }

    [Fact]
    public void BuildPlan_TargetSize_UsesBitrateBasedEncoding()
    {
        var profile = VideoCompressionCatalog.GetProfileById("balanced");
        var probe = new MediaProbeResult(
            Duration: TimeSpan.FromMinutes(12),
            HasAudio: true,
            HasVideo: true,
            VideoWidth: 1920,
            VideoHeight: 1080,
            VideoCodec: "h264",
            VideoFrameRate: 30000d / 1001d,
            EstimatedVideoFrameCount: 21_600,
            AudioCodecs: ["aac"],
            SubtitleCodecs: ["subrip", "pgs"],
            Streams:
            [
                new MediaStreamInfo(0, "video", "h264"),
                new MediaStreamInfo(1, "audio", "aac"),
                new MediaStreamInfo(2, "subtitle", "subrip"),
                new MediaStreamInfo(3, "subtitle", "pgs")
            ]);

        var plan = VideoCompressionPlanner.BuildPlan(profile, probe, ".mp4", 250_000_000);

        Assert.True(plan.UseTargetSize);
        Assert.Contains("-b:v", plan.VideoArgs);
        Assert.Contains("-maxrate", plan.VideoArgs);
        Assert.Contains("-bufsize", plan.VideoArgs);
        Assert.Equal(250_000_000, plan.TargetBytes);
        Assert.Contains(plan.Warnings, warning => warning.Contains("output format", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("-map", plan.MapArgs);
        Assert.Contains("0:2", plan.MapArgs);
    }

    [Fact]
    public void BuildPlan_MkvKeepsTextSubtitlesAsCopy()
    {
        var profile = VideoCompressionCatalog.GetProfileById("balanced");
        var probe = new MediaProbeResult(
            Duration: TimeSpan.FromMinutes(12),
            HasAudio: true,
            HasVideo: true,
            VideoWidth: 1920,
            VideoHeight: 1080,
            VideoCodec: "h264",
            VideoFrameRate: 30000d / 1001d,
            EstimatedVideoFrameCount: 21_600,
            AudioCodecs: ["aac"],
            SubtitleCodecs: ["subrip"],
            Streams:
            [
                new MediaStreamInfo(0, "video", "h264"),
                new MediaStreamInfo(1, "audio", "aac"),
                new MediaStreamInfo(2, "subtitle", "subrip")
            ]);

        var plan = VideoCompressionPlanner.BuildPlan(profile, probe, ".mkv", targetBytes: null);

        Assert.Contains("copy", plan.SubtitleCodecArgs);
    }
}
