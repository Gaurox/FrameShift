using System;
using System.IO;
using FrameShift.Core.Actions;
using FrameShift.Core.AI.CreateSubtitles;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Logging;
using FrameShift.Windows.Forms;
using Xunit;

namespace FrameShift.Tests;

public sealed class AddSubtitlesToVideoBurnEditorFormTests
{
    [Fact]
    public void BurnEditor_WithAssSource_DisablesStyleControls()
    {
        using var form = CreateForm(@"C:\subs\track.ass");

        Assert.False(form.IsStyleEditingEnabled);
        Assert.Equal(@"C:\subs\track.ass", form.CurrentSubtitlePath);
    }

    [Fact]
    public void BurnEditor_WithSrtSource_EnablesStyleControls()
    {
        using var form = CreateForm(@"C:\subs\track.srt");

        Assert.True(form.IsStyleEditingEnabled);
        Assert.Equal(@"C:\subs\track.srt", form.CurrentSubtitlePath);
    }

    [Fact]
    public void BurnEditor_WithHdrProbe_ShowsHdrWarning()
    {
        using var form = CreateForm(
            @"C:\subs\track.srt",
            probe: CreateProbe() with
            {
                VideoColorTransfer = "smpte2084",
                VideoColorPrimaries = "bt2020",
                VideoBitDepth = 10
            });

        Assert.Contains("HDR source detected", form.CompatibilityWarningText, StringComparison.Ordinal);
    }

    [Fact]
    public void BurnEditor_WithMissingFont_ShowsFallbackWarning()
    {
        var settings = new AddSubtitlesToVideoSettings(
            @"C:\subs\track.srt",
            AddSubtitlesToVideoMode.BurnIntoVideo,
            new AddSubtitlesToVideoBurnSettings(
                CreateSubtitlesAssPresets.Default,
                "Definitely Missing Font 12345",
                42,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null));

        using var form = CreateForm(@"C:\subs\track.srt", settings: settings);

        Assert.Contains("fallback font", form.CompatibilityWarningText, StringComparison.OrdinalIgnoreCase);
    }

    private static AddSubtitlesToVideoBurnEditorForm CreateForm(
        string subtitlePath,
        MediaProbeResult? probe = null,
        AddSubtitlesToVideoSettings? settings = null)
    {
        return new AddSubtitlesToVideoBurnEditorForm(
            Path.Combine(Path.GetTempPath(), "video.mp4"),
            "ffmpeg.exe",
            probe ?? CreateProbe(),
            new FfmpegRunner(new AppLogger()),
            settings ?? new AddSubtitlesToVideoSettings(subtitlePath, AddSubtitlesToVideoMode.BurnIntoVideo));
    }

    private static MediaProbeResult CreateProbe()
    {
        return new MediaProbeResult(
            TimeSpan.FromSeconds(12),
            HasAudio: true,
            HasVideo: true,
            VideoWidth: 1280,
            VideoHeight: 720,
            VideoCodec: "h264",
            VideoFrameRate: 30d,
            EstimatedVideoFrameCount: 360,
            AudioCodecs: ["aac"],
            SubtitleCodecs: Array.Empty<string>(),
            Streams: Array.Empty<MediaStreamInfo>());
    }
}
