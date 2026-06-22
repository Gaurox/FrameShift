using System;
using FrameShift.Core.Actions;
using FrameShift.Core.FFprobe;
using Xunit;

namespace FrameShift.Tests;

public sealed class AddSubtitlesToVideoPlannerTests
{
    [Fact]
    public void BuildPlan_MkvSource_UsesSubripInMkv()
    {
        var probe = CreateProbe(
            new MediaStreamInfo(0, "video", "h264"),
            new MediaStreamInfo(1, "audio", "aac"));

        var plan = AddSubtitlesToVideoPlanner.BuildPlan(".mkv", probe);

        Assert.Equal(".mkv", plan.TargetExtension);
        Assert.Equal("mkv", plan.TargetContainerId);
        Assert.Equal("subrip", plan.SubtitleCodec);
        Assert.False(plan.UsesFallbackContainer);
    }

    [Fact]
    public void BuildPlan_Mp4WithTextSubtitle_KeepsMp4AndUsesMovText()
    {
        var probe = CreateProbe(
            new MediaStreamInfo(0, "video", "h264"),
            new MediaStreamInfo(1, "audio", "aac"),
            new MediaStreamInfo(2, "subtitle", "subrip"));

        var plan = AddSubtitlesToVideoPlanner.BuildPlan(".mp4", probe);

        Assert.Equal(".mp4", plan.TargetExtension);
        Assert.Equal("mp4", plan.TargetContainerId);
        Assert.Equal("mov_text", plan.SubtitleCodec);
        Assert.False(plan.UsesFallbackContainer);
    }

    [Fact]
    public void BuildPlan_MovWithNonTextSubtitle_FallsBackToMkv()
    {
        var probe = CreateProbe(
            new MediaStreamInfo(0, "video", "prores"),
            new MediaStreamInfo(1, "audio", "pcm_s16le"),
            new MediaStreamInfo(2, "subtitle", "hdmv_pgs_subtitle"));

        var plan = AddSubtitlesToVideoPlanner.BuildPlan(".mov", probe);

        Assert.Equal(".mkv", plan.TargetExtension);
        Assert.Equal("mkv", plan.TargetContainerId);
        Assert.Equal("subrip", plan.SubtitleCodec);
        Assert.True(plan.UsesFallbackContainer);
    }

    [Fact]
    public void BuildPlan_Mp4WithAttachment_FallsBackToMkv()
    {
        var probe = CreateProbe(
            new MediaStreamInfo(0, "video", "h264"),
            new MediaStreamInfo(1, "audio", "aac"),
            new MediaStreamInfo(2, "attachment", "ttf"));

        var plan = AddSubtitlesToVideoPlanner.BuildPlan(".mp4", probe);

        Assert.Equal(".mkv", plan.TargetExtension);
        Assert.True(plan.UsesFallbackContainer);
    }

    [Fact]
    public void BuildPlan_AviSource_FallsBackToMkv()
    {
        var probe = CreateProbe(
            new MediaStreamInfo(0, "video", "mpeg4"),
            new MediaStreamInfo(1, "audio", "mp3"));

        var plan = AddSubtitlesToVideoPlanner.BuildPlan(".avi", probe);

        Assert.Equal(".mkv", plan.TargetExtension);
        Assert.True(plan.UsesFallbackContainer);
    }

    [Fact]
    public void BuildArguments_MkvPlan_TargetsOnlyNewSubtitleTrackForSubrip()
    {
        var probe = CreateProbe(
            new MediaStreamInfo(0, "video", "h264"),
            new MediaStreamInfo(1, "audio", "aac"),
            new MediaStreamInfo(2, "subtitle", "ass"));
        var plan = AddSubtitlesToVideoPlanner.BuildPlan(".mkv", probe);

        var arguments = AddSubtitlesToVideoAction.BuildArguments(
            @"C:\video.mkv",
            @"C:\track.srt",
            @"C:\video_subtitled.mkv",
            plan,
            probe);

        Assert.Contains("-map", arguments);
        Assert.Contains("0", arguments);
        Assert.Contains("1:0", arguments);
        Assert.Contains("-c", arguments);
        Assert.Contains("copy", arguments);
        Assert.Contains("-c:s:1", arguments);
        Assert.Contains("subrip", arguments);
    }

    [Fact]
    public void BuildArguments_Mp4Plan_UsesMovTextForSubtitleStreams()
    {
        var probe = CreateProbe(
            new MediaStreamInfo(0, "video", "h264"),
            new MediaStreamInfo(1, "audio", "aac"));
        var plan = AddSubtitlesToVideoPlanner.BuildPlan(".mp4", probe);

        var arguments = AddSubtitlesToVideoAction.BuildArguments(
            @"C:\video.mp4",
            @"C:\track.srt",
            @"C:\video_subtitled.mp4",
            plan,
            probe);

        Assert.Contains("-c:s", arguments);
        Assert.Contains("mov_text", arguments);
    }

    [Fact]
    public void BuildBurnPlan_Mp4WithCompatibleAudio_CopiesAudio()
    {
        var probe = CreateProbe(
            new MediaStreamInfo(0, "video", "h264"),
            new MediaStreamInfo(1, "audio", "aac"));

        var plan = AddSubtitlesToVideoBurnPlanner.BuildPlan(".mp4", probe);

        Assert.Equal(".mp4", plan.TargetExtension);
        Assert.Equal("mp4", plan.TargetContainerId);
        Assert.True(plan.CopyAudio);
        Assert.Equal("libx264", plan.VideoCodec);
    }

    [Fact]
    public void BuildBurnPlan_WebmWithIncompatibleAudio_ReencodesAudio()
    {
        var probe = CreateProbe(
            new MediaStreamInfo(0, "video", "vp9"),
            new MediaStreamInfo(1, "audio", "aac"));

        var plan = AddSubtitlesToVideoBurnPlanner.BuildPlan(".webm", probe);

        Assert.False(plan.CopyAudio);
        Assert.Equal("libopus", plan.AudioCodec);
    }

    [Fact]
    public void BuildBurnArguments_WhenAudioCanCopy_UsesAssFilterAndCopyAudio()
    {
        var plan = AddSubtitlesToVideoBurnPlanner.BuildPlan(
            ".mp4",
            CreateProbe(
                new MediaStreamInfo(0, "video", "h264"),
                new MediaStreamInfo(1, "audio", "aac")));

        var arguments = AddSubtitlesToVideoAction.BuildBurnArguments(
            @"C:\video.mp4",
            @"C:\path with spaces\track.ass",
            @"C:\video_subtitled_burned.mp4",
            plan,
            hasAudio: true);

        Assert.Contains("-vf", arguments);
        Assert.Contains("ass=filename='C\\:/path with spaces/track.ass'", arguments);
        Assert.Contains("-c:a", arguments);
        Assert.Contains("copy", arguments);
    }

    [Fact]
    public void BuildBurnArguments_WithClipWindow_AddsSeekAndDuration()
    {
        var plan = AddSubtitlesToVideoBurnPlanner.BuildPlan(
            ".mp4",
            CreateProbe(
                new MediaStreamInfo(0, "video", "h264"),
                new MediaStreamInfo(1, "audio", "aac")));

        var arguments = AddSubtitlesToVideoAction.BuildBurnArguments(
            @"C:\video.mp4",
            @"C:\track.ass",
            @"C:\preview.mp4",
            plan,
            hasAudio: true,
            new AddSubtitlesToVideoBurnClipWindow(2.5d, 2.4d));

        Assert.Contains("-ss", arguments);
        Assert.Contains("2.5", arguments);
        Assert.Contains("-t", arguments);
        Assert.Contains("2.4", arguments);
    }

    [Fact]
    public void BuildBurnPlan_HdrSource_AddsWarning()
    {
        var probe = CreateProbe(
            new MediaStreamInfo(0, "video", "hevc"),
            new MediaStreamInfo(1, "audio", "aac")) with
        {
            VideoColorTransfer = "smpte2084",
            VideoColorPrimaries = "bt2020",
            VideoBitDepth = 10
        };

        var plan = AddSubtitlesToVideoBurnPlanner.BuildPlan(".mp4", probe);

        Assert.Contains(plan.Warnings, warning => warning.Contains("HDR source detected", StringComparison.Ordinal));
    }

    [Fact]
    public void EscapeFilterPath_EscapesWindowsCharacters()
    {
        var escaped = AddSubtitlesToVideoAction.EscapeFilterPath(@"C:\clips\l'été\[sample],final.ass");

        Assert.Equal("C\\:/clips/l\\'été/\\[sample\\]\\,final.ass", escaped);
    }

    private static MediaProbeResult CreateProbe(params MediaStreamInfo[] streams)
    {
        return new MediaProbeResult(
            Duration: TimeSpan.FromMinutes(2),
            HasAudio: true,
            HasVideo: streams.Any(stream => string.Equals(stream.CodecType, "video", StringComparison.OrdinalIgnoreCase)),
            VideoWidth: 1920,
            VideoHeight: 1080,
            VideoCodec: "h264",
            VideoFrameRate: 30d,
            EstimatedVideoFrameCount: 3600,
            AudioCodecs: ["aac"],
            SubtitleCodecs: streams
                .Where(stream => string.Equals(stream.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase))
                .Select(stream => stream.CodecName ?? string.Empty)
                .ToArray(),
            Streams: streams);
    }
}
