using System.Collections.Generic;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class ProgramCliTests
{
    // TryParseArguments — success cases

    [Fact]
    public void TryParseArguments_BasicAction_ParsesActionIdAndInputPath()
    {
        var args = new[] { "--action", "convert-video", @"C:\test\video.mp4" };

        var result = Program.TryParseArguments(args, out var actionId, out var inputPaths, out var options, out var error);

        Assert.True(result);
        Assert.Equal("convert-video", actionId);
        Assert.Single(inputPaths, @"C:\test\video.mp4");
        Assert.Empty(options);
        Assert.Null(error);
    }

    [Fact]
    public void TryParseArguments_WithTargetOption_ParsesOption()
    {
        var args = new[] { "--action", "convert-video", "--target", "mkv", @"C:\test\video.mp4" };

        var result = Program.TryParseArguments(args, out _, out var inputPaths, out var options, out _);

        Assert.True(result);
        Assert.Equal("mkv", options[ActionOptionKeys.Target]);
        Assert.Single(inputPaths, @"C:\test\video.mp4");
    }

    [Fact]
    public void TryParseArguments_WithProfileOption_ParsesOption()
    {
        var args = new[] { "--action", "convert-video", "--profile", "universal", "--target", "mp4", @"C:\test\video.mp4" };

        var result = Program.TryParseArguments(args, out _, out _, out var options, out _);

        Assert.True(result);
        Assert.Equal("universal", options[ActionOptionKeys.Profile]);
    }

    [Fact]
    public void TryParseArguments_WithUpscalePipeline_ParsesOption()
    {
        var args = new[] { "--action", "upscale-video", "--upscale-pipeline", "bmp", @"C:\test\video.mp4" };

        var result = Program.TryParseArguments(args, out _, out _, out var options, out _);

        Assert.True(result);
        Assert.Equal("bmp", options[ActionOptionKeys.UpscalePipeline]);
    }

    [Fact]
    public void TryParseArguments_WithStartAndEnd_ParsesTimingOptions()
    {
        var args = new[] { "--action", "cut-audio", "--start", "00:00:05", "--end", "00:00:30", @"C:\test\audio.mp3" };

        var result = Program.TryParseArguments(args, out _, out _, out var options, out _);

        Assert.True(result);
        Assert.Equal("00:00:05", options[ActionOptionKeys.StartTime]);
        Assert.Equal("00:00:30", options[ActionOptionKeys.EndTime]);
    }

    [Fact]
    public void TryParseArguments_MultipleInputPaths_ParsesAllPaths()
    {
        var args = new[] { "--action", "convert-video", @"C:\a.mp4", @"C:\b.mp4" };

        var result = Program.TryParseArguments(args, out _, out var inputPaths, out _, out _);

        Assert.True(result);
        Assert.Equal(2, inputPaths.Count);
    }

    // TryParseArguments — failure cases

    [Fact]
    public void TryParseArguments_MissingActionFlag_ReturnsFalse()
    {
        var args = new[] { "convert-video", @"C:\test\video.mp4" };

        var result = Program.TryParseArguments(args, out _, out _, out _, out var error);

        Assert.False(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParseArguments_ActionFlagAtEnd_ReturnsFalse()
    {
        var args = new[] { "--action" };

        var result = Program.TryParseArguments(args, out _, out _, out _, out var error);

        Assert.False(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParseArguments_UnknownFlags_AreIgnoredSilently()
    {
        var args = new[] { "--action", "convert-video", "--unknown-flag", "value", @"C:\test\video.mp4" };

        var result = Program.TryParseArguments(args, out var actionId, out var inputPaths, out var options, out _);

        Assert.True(result);
        Assert.Equal("convert-video", actionId);
        Assert.Single(inputPaths, @"C:\test\video.mp4");
        Assert.Empty(options);
    }
}
