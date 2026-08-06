using System;
using System.Collections.Generic;
using System.Linq;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class ExtractFramesSettingsTests
{
    [Fact]
    public void TryFromOptions_WithoutFrameMode_PreservesHistoricalAllMode()
    {
        var parsed = ExtractFramesSettings.TryFromOptions(null, out var settings, out var error);

        Assert.True(parsed);
        Assert.NotNull(settings);
        Assert.Equal(ExtractFramesMode.All, settings!.Mode);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("all", "All")]
    [InlineData("FIRST", "First")]
    [InlineData("last", "Last")]
    [InlineData("KeyFrames", "Keyframes")]
    public void TryFromOptions_AcceptsSupportedModes(string value, string expectedMode)
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.FrameMode] = value
        };

        var parsed = ExtractFramesSettings.TryFromOptions(options, out var settings, out var error);

        Assert.True(parsed);
        Assert.Equal(Enum.Parse<ExtractFramesMode>(expectedMode), settings!.Mode);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("timestamp")]
    [InlineData("all-frames")]
    public void TryFromOptions_RejectsUnsupportedModes(string value)
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.FrameMode] = value
        };

        var parsed = ExtractFramesSettings.TryFromOptions(options, out var settings, out var error);

        Assert.False(parsed);
        Assert.Null(settings);
        Assert.Equal(MediaActionMessages.FrameExtractionModeInvalid(), error);
    }

    [Fact]
    public void BuildAllArguments_PreservesTheHistoricalArgumentContract()
    {
        var arguments = ExtractFramesAction.BuildAllArguments("input.mp4", "frames\\input_%06d.png");

        Assert.Equal(
        [
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-i", "input.mp4",
            "-map", "0:v:0?",
            "-an",
            "-sn",
            "-dn",
            "-vsync", "0",
            "-c:v", "png",
            "-compression_level", "6",
            "frames\\input_%06d.png"
        ],
        arguments);
    }

    [Fact]
    public void BuildFirstFrameArguments_MapsTheFirstVideoStreamAndWritesOnePng()
    {
        var arguments = ExtractFramesAction.BuildFirstFrameArguments("input.mp4", "input_first_frame.png");

        Assert.Contains("-map", arguments);
        Assert.Contains("0:v:0", arguments);
        Assert.Contains("-frames:v", arguments);
        Assert.Contains("1", arguments);
        Assert.Equal("input_first_frame.png", arguments[^1]);
    }

    [Fact]
    public void BuildKeyframesArguments_CombinesDecoderSkippingWithTheKeyframeFilter()
    {
        var arguments = ExtractFramesAction.BuildKeyframesArguments("input.webm", "keyframe_%06d.png");

        Assert.Equal("-skip_frame", arguments[9]);
        Assert.Equal("nokey", arguments[10]);
        Assert.Equal("-i", arguments[11]);
        Assert.Contains("select=eq(key\\,1)", arguments);
        Assert.Contains("-fps_mode", arguments);
        Assert.Equal("keyframe_%06d.png", arguments[^1]);
    }

    [Fact]
    public void BuildLastFrameArguments_UsesSseofOnlyForSearchAttempts()
    {
        var searchArguments = ExtractFramesAction.BuildLastFrameArguments("input.mp4", "temporary.png", TimeSpan.FromSeconds(60));
        var fallbackArguments = ExtractFramesAction.BuildLastFrameArguments("input.mp4", "temporary.png", null);

        Assert.Contains("-sseof", searchArguments);
        Assert.Contains("-60", searchArguments);
        Assert.DoesNotContain("-sseof", fallbackArguments);
        Assert.Contains("-update", fallbackArguments);
        Assert.Equal("1", fallbackArguments[fallbackArguments.ToList().IndexOf("-update") + 1]);
        Assert.Equal("temporary.png", fallbackArguments[^1]);
    }
}
