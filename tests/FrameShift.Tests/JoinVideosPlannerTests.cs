using System;
using System.Collections.Generic;
using System.Linq;
using FrameShift.Core.Actions;
using FrameShift.Core.FFprobe;
using Xunit;

namespace FrameShift.Tests;

public sealed class JoinVideosPlannerTests
{
    [Fact]
    public void BuildPlan_CompatibleSdrClips_UsesDirectCopy()
    {
        var plan = JoinVideosPlanner.BuildPlan([CreateProbe(), CreateProbe()], JoinVideosMode.Auto);

        Assert.Equal(JoinVideosPipeline.DirectCopy, plan.Pipeline);
        Assert.True(plan.IncludeAudio);
    }

    [Fact]
    public void BuildPlan_DifferentResolution_NormalizesToFirstDisplayGeometry()
    {
        var first = CreateProbe(width: 1920, height: 1080);
        var second = CreateProbe(width: 1280, height: 720);

        var plan = JoinVideosPlanner.BuildPlan([first, second], JoinVideosMode.Auto);

        Assert.Equal(JoinVideosPipeline.Normalize, plan.Pipeline);
        Assert.Equal(1920, plan.TargetWidth);
        Assert.Equal(1080, plan.TargetHeight);
    }

    [Fact]
    public void BuildPlan_MissingAudio_NormalizesAndKeepsAudioTrack()
    {
        var plan = JoinVideosPlanner.BuildPlan([CreateProbe(), CreateProbe(hasAudio: false)], JoinVideosMode.Auto);

        Assert.Equal(JoinVideosPipeline.Normalize, plan.Pipeline);
        Assert.True(plan.IncludeAudio);
    }

    [Fact]
    public void BuildPlan_MixedHdrAndSdr_RejectsInV1()
    {
        var hdr = CreateProbe(pixelFormat: "yuv420p10le", colorTransfer: "smpte2084");
        var sdr = CreateProbe();

        var plan = JoinVideosPlanner.BuildPlan([hdr, sdr], JoinVideosMode.Auto);

        Assert.Equal(JoinVideosPipeline.Unsupported, plan.Pipeline);
        Assert.Contains("HDR and SDR", plan.DecisionReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlan_ForcedCopyRejectsMismatchedStreams()
    {
        var plan = JoinVideosPlanner.BuildPlan([CreateProbe(), CreateProbe(width: 1280)], JoinVideosMode.Copy);

        Assert.Equal(JoinVideosPipeline.Unsupported, plan.Pipeline);
    }

    [Fact]
    public void BuildNormalizationFilterScript_InsertsSilenceInputForAudioLessClip()
    {
        var probes = new[] { CreateProbe(), CreateProbe(hasAudio: false) };
        var plan = JoinVideosPlanner.BuildPlan(probes, JoinVideosMode.Auto);

        var script = JoinVideosAction.BuildNormalizationFilterScript(probes, plan);

        Assert.Contains("[2:a:0]atrim=duration=12", script, StringComparison.Ordinal);
        Assert.Contains("concat=n=2:v=1:a=1", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildNormalizationArguments_PassesFilterGraphInline()
    {
        var probes = new[] { CreateProbe(), CreateProbe(width: 1280, height: 720) };
        var plan = JoinVideosPlanner.BuildPlan(probes, JoinVideosMode.Auto);
        var filterComplex = JoinVideosAction.BuildNormalizationFilterScript(probes, plan);

        var arguments = JoinVideosAction.BuildNormalizationArguments(
            [@"C:\clips\a.mp4", @"C:\clips\b.mp4"],
            probes,
            plan,
            filterComplex,
            @"C:\clips\out.mp4",
            useNvenc: false);

        // The bundled FFmpeg 9 essentials build has no -filter_complex_script option;
        // the graph must travel as the value of -filter_complex instead.
        Assert.DoesNotContain("-filter_complex_script", arguments);
        var filterIndex = arguments.ToList().IndexOf("-filter_complex");
        Assert.True(filterIndex >= 0);
        Assert.Equal(filterComplex, arguments[filterIndex + 1]);
    }

    [Fact]
    public void BuildConcatManifest_EscapesApostrophesAndUsesForwardSlashes()
    {
        var manifest = JoinVideosAction.BuildConcatManifest([@"C:\clips\l'été.mp4"]);

        Assert.Contains("file 'C:/clips/l'\\''été.mp4'", manifest, StringComparison.Ordinal);
    }

    private static JoinVideoProbeResult CreateProbe(
        int width = 1920,
        int height = 1080,
        bool hasAudio = true,
        string pixelFormat = "yuv420p",
        string? colorTransfer = "bt709")
    {
        var video = new JoinVideoStreamInfo(
            "h264",
            "avc1",
            "High",
            "40",
            width,
            height,
            pixelFormat,
            "bt709",
            colorTransfer,
            "bt709",
            "tv",
            "progressive",
            "1/90000",
            "30000/1001",
            "1:1",
            "0.000000",
            "ABC",
            0);
        var audio = hasAudio
            ? new JoinAudioStreamInfo("aac", "mp4a", "LC", "fltp", "48000", 2, "stereo", "1/48000", "0.000000", "DEF")
            : null;
        return new JoinVideoProbeResult(
            TimeSpan.FromSeconds(12),
            "mov,mp4,m4a,3gp,3g2,mj2",
            1,
            hasAudio ? 1 : 0,
            0,
            video,
            audio);
    }
}
