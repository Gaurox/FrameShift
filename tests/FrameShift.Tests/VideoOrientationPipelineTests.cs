using System;
using System.Collections.Generic;
using FrameShift.Core.Actions;
using FrameShift.Core.AI.Upscale;
using FrameShift.Core.AI.VideoInterpolation;
using FrameShift.Core.AI.VideoUpscale;
using FrameShift.Core.FFprobe;
using Xunit;

namespace FrameShift.Tests;

public sealed class VideoOrientationPipelineTests
{
    [Theory]
    [InlineData(0, 96, 64)]
    [InlineData(90, 64, 96)]
    [InlineData(180, 96, 64)]
    [InlineData(270, 64, 96)]
    [InlineData(-90, 64, 96)]
    public void DisplayGeometry_UsesThePresentationDimensionsForAllQuarterTurns(
        int rotationDegrees,
        int expectedWidth,
        int expectedHeight)
    {
        var probe = CreateProbe(rotationDegrees);

        Assert.Equal(expectedWidth, probe.DisplayVideoWidth);
        Assert.Equal(expectedHeight, probe.DisplayVideoHeight);

        var target = UpscaleFrameProcessor.ResolveFinalSize(
            probe.DisplayVideoWidth,
            probe.DisplayVideoHeight,
            new UpscaleRequest(Factor: 2),
            nativeScaleFactor: 4);

        Assert.Equal(expectedWidth * 2, target.Width);
        Assert.Equal(expectedHeight * 2, target.Height);
    }

    [Fact]
    public void RawvideoDecoders_KeepFfmpegAutorotationEnabled()
    {
        var rifeArguments = RifeRawVideoPipeline.BuildDecodeArguments(@"E:\input.mp4");
        var upscaleArguments = UpscaleRawVideoPipeline.BuildDecodeArguments(@"E:\input.mp4");

        AssertRawvideoDecodeArguments(rifeArguments);
        AssertRawvideoDecodeArguments(upscaleArguments);
    }

    [Fact]
    public void BmpExtractors_KeepFfmpegAutorotationEnabled()
    {
        var rifeArguments = RifeInterpolateVideoAction.BuildExtractArguments(
            @"E:\input.mp4",
            @"E:\frames\%08d.bmp");
        var upscaleArguments = UpscaleVideoAction.BuildExtractArguments(
            @"E:\input.mp4",
            @"E:\frames\%08d.bmp");

        AssertBmpExtractArguments(rifeArguments);
        AssertBmpExtractArguments(upscaleArguments);
    }

    [Theory]
    [InlineData("auto", 2, "rawvideo")]
    [InlineData("rawvideo", 2, "rawvideo")]
    [InlineData("bmp", 2, "bmp")]
    [InlineData("auto", 4, "bmp")]
    [InlineData("rawvideo", 4, "bmp")]
    [InlineData("bmp", 4, "bmp")]
    public void RifePipelineMode_PreservesRawX2AndBmpX4Fallbacks(
        string requestedPipeline,
        int multiplier,
        string expectedPipeline)
    {
        Assert.Equal(
            expectedPipeline,
            RifeInterpolateVideoAction.ResolvePipelineMode(requestedPipeline, multiplier));
    }

    [Theory]
    [InlineData("auto", "rawvideo")]
    [InlineData("rawvideo", "rawvideo")]
    [InlineData("bmp", "bmp")]
    public void UpscalePipelineMode_PreservesRawAndBmpModes(string requestedPipeline, string expectedPipeline)
    {
        Assert.Equal(expectedPipeline, UpscaleVideoAction.ResolvePipelineMode(requestedPipeline));
    }

    [Theory]
    [InlineData(0, "96x64")]
    [InlineData(90, "64x96")]
    [InlineData(180, "96x64")]
    [InlineData(270, "64x96")]
    [InlineData(-90, "64x96")]
    public void RawvideoEncoders_UseDisplayGeometryAndNormalizeRotation(int rotationDegrees, string expectedSize)
    {
        var probe = CreateProbe(rotationDegrees);
        var rifeSettings = new RifeInterpolateVideoSettings("rife-v4.6", 2, 1);

        var rifeArguments = RifeRawVideoPipeline.BuildEncodeArguments(
            @"E:\source.mp4",
            @"E:\rife.mp4",
            "60",
            probe.DisplayVideoWidth,
            probe.DisplayVideoHeight,
            hasAudio: true,
            rifeSettings,
            sampleRate: 48000,
            videoCodec: "libx264",
            videoArgs: ["-crf", "18"]);
        var upscaleArguments = UpscaleRawVideoPipeline.BuildEncodeArguments(
            @"E:\source.mp4",
            @"E:\upscaled.mp4",
            frameRate: 30,
            width: probe.DisplayVideoWidth * 2,
            height: probe.DisplayVideoHeight * 2,
            videoCodec: "libx264",
            videoArgs: ["-crf", "18"],
            hasAudio: true,
            transcodeAudio: false);

        AssertRawvideoEncodeArguments(rifeArguments, expectedSize, expectedAudioCodec: "copy");
        AssertRawvideoEncodeArguments(upscaleArguments, ScaleSize(expectedSize, 2), expectedAudioCodec: "copy");
    }

    [Fact]
    public void BmpEncoders_PreserveAudioAndDoNotCopyVideoRotation()
    {
        var rifeSettings = new RifeInterpolateVideoSettings("rife-v4.6", 4, 1);
        var rifeArguments = RifeInterpolateVideoAction.BuildEncodeArguments(
            @"E:\rife-frames",
            @"E:\source.mp4",
            @"E:\rife.mp4",
            targetFps: 120,
            new RifeInterpolateVideoAction.RifeEncodePlan("CPU", "libx264", ["-crf", "18"]),
            hasAudio: true,
            rifeSettings,
            sampleRate: 48000);
        var upscaleArguments = UpscaleVideoAction.BuildEncodeArguments(
            @"E:\upscale-frames",
            @"E:\source.mp4",
            @"E:\upscaled.mp4",
            frameRate: 30,
            new UpscaleVideoAction.VideoEncodePlan("CPU", "libx264", ["-crf", "18"]),
            hasAudio: true,
            transcodeAudio: true);

        AssertBmpEncodeArguments(rifeArguments, expectedAudioCodec: "copy");
        AssertBmpEncodeArguments(upscaleArguments, expectedAudioCodec: "aac");
    }

    [Fact]
    public void CustomUpscaleTarget_UsesDisplayOrientationWithoutStretching()
    {
        var portraitProbe = CreateProbe(90);
        var target = UpscaleFrameProcessor.ResolveFinalSize(
            portraitProbe.DisplayVideoWidth,
            portraitProbe.DisplayVideoHeight,
            new UpscaleRequest(TargetWidth: 128, TargetHeight: 192),
            nativeScaleFactor: 4);

        Assert.Equal(128, target.Width);
        Assert.Equal(192, target.Height);

        var selected = UpscaleModelCatalog.GetById("realesr-animevideov3");
        Assert.NotNull(selected);

        var executionModel = UpscaleModelCatalog.ResolveVideoExecutionModel(
            selected!,
            new UpscaleRequest(TargetWidth: target.Width, TargetHeight: target.Height),
            portraitProbe.DisplayVideoWidth,
            portraitProbe.DisplayVideoHeight);

        Assert.Equal("realesr-animevideov3-x2", executionModel.Id);
    }

    private static MediaProbeResult CreateProbe(int rotationDegrees)
    {
        return new MediaProbeResult(
            Duration: TimeSpan.FromSeconds(1),
            HasAudio: true,
            HasVideo: true,
            VideoWidth: 96,
            VideoHeight: 64,
            VideoCodec: "h264",
            VideoFrameRate: 30,
            EstimatedVideoFrameCount: 30,
            AudioCodecs: ["aac"],
            SubtitleCodecs: Array.Empty<string>(),
            Streams: Array.Empty<MediaStreamInfo>())
        {
            RotationDegrees = rotationDegrees
        };
    }

    private static void AssertRawvideoDecodeArguments(IReadOnlyList<string> arguments)
    {
        Assert.Contains("-f", arguments);
        Assert.Contains("rawvideo", arguments);
        Assert.Contains("-pix_fmt", arguments);
        Assert.Contains("rgb24", arguments);
        AssertNoRotationOverride(arguments);
    }

    private static void AssertBmpExtractArguments(IReadOnlyList<string> arguments)
    {
        Assert.Contains("-c:v", arguments);
        Assert.Contains("bmp", arguments);
        AssertNoRotationOverride(arguments);
    }

    private static void AssertRawvideoEncodeArguments(
        IReadOnlyList<string> arguments,
        string expectedSize,
        string expectedAudioCodec)
    {
        Assert.Equal(expectedSize, GetOptionValue(arguments, "-video_size"));
        Assert.Equal("0:v:0", GetOptionValue(arguments, "-map", occurrence: 0));
        Assert.Equal("1:a?", GetOptionValue(arguments, "-map", occurrence: 1));
        Assert.Equal(expectedAudioCodec, GetOptionValue(arguments, "-c:a"));
        AssertNoRotationOverride(arguments);
    }

    private static void AssertBmpEncodeArguments(IReadOnlyList<string> arguments, string expectedAudioCodec)
    {
        Assert.Equal("0:v:0", GetOptionValue(arguments, "-map", occurrence: 0));
        Assert.Equal("1:a?", GetOptionValue(arguments, "-map", occurrence: 1));
        Assert.Equal(expectedAudioCodec, GetOptionValue(arguments, "-c:a"));
        AssertNoRotationOverride(arguments);
    }

    private static void AssertNoRotationOverride(IReadOnlyList<string> arguments)
    {
        Assert.DoesNotContain(arguments, argument =>
            argument.Equals("-noautorotate", StringComparison.OrdinalIgnoreCase) ||
            argument.Contains("rotate", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("-map_metadata", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetOptionValue(IReadOnlyList<string> arguments, string option, int occurrence = 0)
    {
        var matches = 0;
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (!string.Equals(arguments[index], option, StringComparison.Ordinal))
            {
                continue;
            }

            if (matches++ == occurrence)
            {
                return arguments[index + 1];
            }
        }

        throw new Xunit.Sdk.XunitException($"Option '{option}' occurrence {occurrence} was not found.");
    }

    private static string ScaleSize(string size, int factor)
    {
        var parts = size.Split('x');
        return $"{int.Parse(parts[0]) * factor}x{int.Parse(parts[1]) * factor}";
    }
}
