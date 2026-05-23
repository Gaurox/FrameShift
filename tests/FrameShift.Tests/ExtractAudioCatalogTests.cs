using System;
using FrameShift.Core.Actions;
using FrameShift.Core.FFprobe;
using Xunit;

namespace FrameShift.Tests;

public sealed class ExtractAudioCatalogTests
{
    [Fact]
    public void CreatePlan_UsesStreamCopyForMp3TargetWhenSourceIsMp3()
    {
        var probe = CreateProbe("mp3");

        var plan = ExtractAudioCatalog.CreatePlan("mp3", probe);

        Assert.True(plan.CopyAudio);
        Assert.Equal("copy", plan.Codec);
        Assert.Equal(".mp3", plan.OutputExtension);
    }

    [Fact]
    public void CreatePlan_FallsBackToAacEncodingForM4aWhenSourceCodecIsNotCompatible()
    {
        var probe = CreateProbe("opus");

        var plan = ExtractAudioCatalog.CreatePlan("m4a", probe);

        Assert.False(plan.CopyAudio);
        Assert.Equal("aac", plan.Codec);
        Assert.Equal(".m4a", plan.OutputExtension);
    }

    [Fact]
    public void CreatePlan_UsesStreamCopyForWaveTargetWhenSourceIsPcm()
    {
        var probe = CreateProbe("pcm_s24le");

        var plan = ExtractAudioCatalog.CreatePlan("wav", probe);

        Assert.True(plan.CopyAudio);
        Assert.Equal("copy", plan.Codec);
    }

    [Fact]
    public void CreatePlan_UsesVorbisEncodingForOggWhenSourceCodecIsUnsupported()
    {
        var probe = CreateProbe("aac");

        var plan = ExtractAudioCatalog.CreatePlan("ogg", probe);

        Assert.False(plan.CopyAudio);
        Assert.Equal("libvorbis", plan.Codec);
        Assert.Contains("-q:a", plan.Args);
    }

    private static MediaProbeResult CreateProbe(string audioCodec)
    {
        return new MediaProbeResult(
            TimeSpan.FromSeconds(12),
            HasAudio: true,
            HasVideo: true,
            VideoWidth: 1920,
            VideoHeight: 1080,
            VideoCodec: "h264",
            VideoFrameRate: 30,
            EstimatedVideoFrameCount: 360,
            AudioCodecs: [audioCodec],
            SubtitleCodecs: [],
            Streams:
            [
                new MediaStreamInfo(0, "video", "h264"),
                new MediaStreamInfo(1, "audio", audioCodec)
            ]);
    }
}
