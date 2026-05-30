using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class ConversionActionHelperTests
{
    // GetFriendlyFfmpegError

    [Fact]
    public void GetFriendlyFfmpegError_NullOrEmpty_ReturnsFallback()
    {
        Assert.Equal("fallback", ConversionActionHelper.GetFriendlyFfmpegError("", "fallback"));
        Assert.Equal("fallback", ConversionActionHelper.GetFriendlyFfmpegError("   ", "fallback"));
    }

    [Theory]
    [InlineData("no space left on device")]
    [InlineData("DISK FULL error")]
    public void GetFriendlyFfmpegError_DiskFull_ReturnsDiskFullMessage(string stderr)
    {
        var result = ConversionActionHelper.GetFriendlyFfmpegError(stderr, "fallback");
        Assert.Equal(MediaActionMessages.DiskFull(), result);
    }

    [Theory]
    [InlineData("permission denied writing output")]
    [InlineData("access is denied")]
    [InlineData("cannot open output file for writing")]
    public void GetFriendlyFfmpegError_PermissionDenied_ReturnsOutputWriteFailed(string stderr)
    {
        var result = ConversionActionHelper.GetFriendlyFfmpegError(stderr, "fallback");
        Assert.Equal(MediaActionMessages.OutputWriteFailed(), result);
    }

    [Theory]
    [InlineData("invalid data found when processing input")]
    [InlineData("corrupt frame detected")]
    [InlineData("no jpeg data found in image")]
    [InlineData("nothing was written into output file")]
    public void GetFriendlyFfmpegError_CorruptData_ReturnsFileCorrupted(string stderr)
    {
        var result = ConversionActionHelper.GetFriendlyFfmpegError(stderr, "fallback");
        Assert.Equal(MediaActionMessages.FileCorrupted(), result);
    }

    [Theory]
    [InlineData("stream map '0:v' matches no streams")]
    [InlineData("no streams to mux were found")]
    [InlineData("input does not contain any stream")]
    public void GetFriendlyFfmpegError_StreamIssue_ReturnsStreamMessage(string stderr)
    {
        var result = ConversionActionHelper.GetFriendlyFfmpegError(stderr, "fallback");
        Assert.Contains("stream", result, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("unknown encoder 'libfoo'")]
    [InlineData("decoder not found")]
    public void GetFriendlyFfmpegError_UnknownCodec_ReturnsCodecUnsupported(string stderr)
    {
        var result = ConversionActionHelper.GetFriendlyFfmpegError(stderr, "fallback");
        Assert.Equal(MediaActionMessages.CodecUnsupported(), result);
    }

    [Fact]
    public void GetFriendlyFfmpegError_UnknownError_ReturnsLastThreeLines()
    {
        var nl = System.Environment.NewLine;
        var stderr = $"line1{nl}line2{nl}line3{nl}line4";
        var result = ConversionActionHelper.GetFriendlyFfmpegError(stderr, "fallback");
        Assert.Contains("line2", result);
        Assert.Contains("line3", result);
        Assert.Contains("line4", result);
        Assert.DoesNotContain("line1", result);
    }

    // GetOption

    [Fact]
    public void GetOption_KeyPresent_ReturnsValue()
    {
        var request = new ActionRequest(
            "input.mp4",
            new Core.Logging.AppLogger(),
            null,
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                [ActionOptionKeys.Target] = "mkv"
            });

        Assert.Equal("mkv", ConversionActionHelper.GetOption(request, ActionOptionKeys.Target, "mp4"));
    }

    [Fact]
    public void GetOption_KeyAbsent_ReturnsDefault()
    {
        var request = new ActionRequest("input.mp4", new Core.Logging.AppLogger(), null,
            new System.Collections.Generic.Dictionary<string, string>());

        Assert.Equal("mp4", ConversionActionHelper.GetOption(request, ActionOptionKeys.Target, "mp4"));
    }
}
