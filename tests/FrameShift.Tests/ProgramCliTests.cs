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

    [Theory]
    [InlineData("all")]
    [InlineData("first")]
    [InlineData("last")]
    [InlineData("keyframes")]
    public void TryParseArguments_WithFrameMode_ParsesOption(string frameMode)
    {
        var args = new[] { "--action", "extract-frames", "--frame-mode", frameMode, @"C:\test\video.mp4" };

        var result = Program.TryParseArguments(args, out _, out var inputPaths, out var options, out _);

        Assert.True(result);
        Assert.Equal(frameMode, options[ActionOptionKeys.FrameMode]);
        Assert.Single(inputPaths, @"C:\test\video.mp4");
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
    public void TryParseArguments_WithSubtitlesOutputFormat_ParsesOption()
    {
        var args = new[] { "--action", "create-subtitles-audio", "--subtitles-format", "ass", @"C:\test\audio.wav" };

        var result = Program.TryParseArguments(args, out _, out _, out var options, out _);

        Assert.True(result);
        Assert.Equal("ass", options[ActionOptionKeys.SubtitlesOutputFormat]);
    }

    [Fact]
    public void TryParseArguments_WithSubtitlesAssPreset_ParsesOption()
    {
        var args = new[] { "--action", "create-subtitles-audio", "--ass-preset", "word-highlight", @"C:\test\audio.wav" };

        var result = Program.TryParseArguments(args, out _, out _, out var options, out _);

        Assert.True(result);
        Assert.Equal("word-highlight", options[ActionOptionKeys.SubtitlesAssPreset]);
    }

    [Fact]
    public void TryParseArguments_WithSubtitleFile_ParsesOption()
    {
        var args = new[] { "--action", "add-subtitles-video", "--subtitle-file", @"C:\test\track.srt", @"C:\test\video.mp4" };

        var result = Program.TryParseArguments(args, out _, out _, out var options, out _);

        Assert.True(result);
        Assert.Equal(@"C:\test\track.srt", options[ActionOptionKeys.SubtitleFilePath]);
    }

    [Fact]
    public void TryParseArguments_WithSubtitleMode_ParsesOption()
    {
        var args = new[] { "--action", "add-subtitles-video", "--subtitle-mode", "burn", @"C:\test\video.mp4" };

        var result = Program.TryParseArguments(args, out _, out _, out var options, out _);

        Assert.True(result);
        Assert.Equal("burn", options[ActionOptionKeys.SubtitleMode]);
    }

    [Fact]
    public void TryParseArguments_WithSubtitleAliases_ParsesOptions()
    {
        var args = new[]
        {
            "--action", "add-subtitles-video",
            "--subtitle-path", @"C:\test\track.ass",
            "--subtitles-mode", "burn-into-video",
            @"C:\test\video.mp4"
        };

        var result = Program.TryParseArguments(args, out _, out _, out var options, out _);

        Assert.True(result);
        Assert.Equal(@"C:\test\track.ass", options[ActionOptionKeys.SubtitleFilePath]);
        Assert.Equal("burn-into-video", options[ActionOptionKeys.SubtitleMode]);
    }

    [Fact]
    public void TryParseArguments_WithSubtitleVisualOptions_ParsesOptions()
    {
        var args = new[]
        {
            "--action", "add-subtitles-video",
            "--subtitle-font", "Tahoma",
            "--subtitle-size", "42",
            "--subtitle-color", "#FFEECC",
            "--subtitle-position", "top",
            "--subtitle-margin-v", "96",
            @"C:\test\video.mp4"
        };

        var result = Program.TryParseArguments(args, out _, out _, out var options, out _);

        Assert.True(result);
        Assert.Equal("Tahoma", options[ActionOptionKeys.SubtitleFontName]);
        Assert.Equal("42", options[ActionOptionKeys.SubtitleFontSize]);
        Assert.Equal("#FFEECC", options[ActionOptionKeys.SubtitlePrimaryColor]);
        Assert.Equal("top", options[ActionOptionKeys.SubtitleVerticalAlignment]);
        Assert.Equal("96", options[ActionOptionKeys.SubtitleMarginVertical]);
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
    public void TryParseArguments_FrameModeWithoutValue_ReturnsFalse()
    {
        var args = new[] { "--action", "extract-frames", "--frame-mode" };

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
