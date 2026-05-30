using FrameShift.Core.FFmpeg;
using Xunit;

namespace FrameShift.Tests;

public sealed class FfmpegRunnerTests
{
    // TryParseFfmpegTime — valid inputs

    [Theory]
    [InlineData("00:00:00.000", 0.0)]
    [InlineData("00:00:01.000", 1.0)]
    [InlineData("00:01:30.500", 90.5)]
    [InlineData("01:02:03.250", 3723.25)]
    [InlineData("00:59:59.999", 3599.999)]
    public void TryParseFfmpegTime_ValidHhMmSs_ReturnsTrueAndCorrectSeconds(string input, double expected)
    {
        var result = FfmpegRunner.TryParseFfmpegTime(input, out var seconds);

        Assert.True(result);
        Assert.Equal(expected, seconds, precision: 2);
    }

    [Theory]
    [InlineData("01:30.500", 90.5)]
    [InlineData("00:00.000", 0.0)]
    [InlineData("59:59.999", 3599.999)]
    public void TryParseFfmpegTime_ValidMmSs_ReturnsTrueAndCorrectSeconds(string input, double expected)
    {
        var result = FfmpegRunner.TryParseFfmpegTime(input, out var seconds);

        Assert.True(result);
        Assert.Equal(expected, seconds, precision: 2);
    }

    // TryParseFfmpegTime — invalid inputs

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("1:2:3:4")]
    [InlineData("abc:de.fg")]
    [InlineData("30")]
    public void TryParseFfmpegTime_InvalidInput_ReturnsFalse(string input)
    {
        var result = FfmpegRunner.TryParseFfmpegTime(input, out _);

        Assert.False(result);
    }
}
