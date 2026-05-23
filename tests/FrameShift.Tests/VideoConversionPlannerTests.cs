using FrameShift.Core.Actions;
using FrameShift.Core.FFprobe;
using Xunit;

namespace FrameShift.Tests;

public sealed class VideoConversionPlannerTests
{
    [Fact]
    public void BuildPlan_MkvUniversal_UsesAacAudioForMultichannelSources()
    {
        var probe = new MediaProbeResult(
            Duration: TimeSpan.FromMinutes(63),
            HasAudio: true,
            HasVideo: true,
            VideoWidth: 1920,
            VideoHeight: 1080,
            VideoCodec: "h264",
            VideoFrameRate: 24000d / 1001d,
            EstimatedVideoFrameCount: 90_000,
            AudioCodecs: ["ac3", "ac3"],
            SubtitleCodecs: ["subrip"],
            Streams:
            [
                new MediaStreamInfo(0, "video", "h264"),
                new MediaStreamInfo(1, "audio", "ac3"),
                new MediaStreamInfo(2, "audio", "ac3"),
                new MediaStreamInfo(3, "subtitle", "subrip")
            ]);

        var target = VideoConversionCatalog.GetTargetById("mkv");
        var profile = VideoConversionCatalog.GetProfileById("universal");

        var plan = VideoConversionPlanner.BuildPlan(target, profile, probe, nvencAvailable: false);

        Assert.Equal("aac", plan.AudioCodec);
    }

    [Fact]
    public void BuildPlan_MkvStreaming_UsesAacAudioForMultichannelSources()
    {
        var probe = new MediaProbeResult(
            Duration: TimeSpan.FromMinutes(63),
            HasAudio: true,
            HasVideo: true,
            VideoWidth: 1920,
            VideoHeight: 1080,
            VideoCodec: "h264",
            VideoFrameRate: 24000d / 1001d,
            EstimatedVideoFrameCount: 90_000,
            AudioCodecs: ["ac3", "ac3"],
            SubtitleCodecs: ["subrip"],
            Streams:
            [
                new MediaStreamInfo(0, "video", "h264"),
                new MediaStreamInfo(1, "audio", "ac3"),
                new MediaStreamInfo(2, "audio", "ac3"),
                new MediaStreamInfo(3, "subtitle", "subrip")
            ]);

        var target = VideoConversionCatalog.GetTargetById("mkv");
        var profile = VideoConversionCatalog.GetProfileById("streaming");

        var plan = VideoConversionPlanner.BuildPlan(target, profile, probe, nvencAvailable: false);

        Assert.Equal("aac", plan.AudioCodec);
    }

    [Fact]
    public void GetFriendlyFfmpegError_InvalidChannelLayout_ReturnsAudioCompatibilityMessage()
    {
        const string stderr = "[libopus @ 000001] Invalid channel layout 5.1(side) for specified mapping family -1.";

        var message = ConversionActionHelper.GetFriendlyFfmpegError(stderr, MediaActionMessages.FfmpegFailure("video"));

        Assert.Equal("The selected output audio settings are not compatible with this source file.", message);
    }
}
