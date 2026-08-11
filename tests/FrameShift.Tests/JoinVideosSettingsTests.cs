using System.Collections.Generic;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class JoinVideosSettingsTests
{
    [Fact]
    public void Payload_RoundTripsOrderedDuplicatePaths()
    {
        var source = new JoinVideosSettings
        {
            InputPaths = [@"C:\clips\one.mp4", @"C:\clips\two.mp4", @"C:\clips\one.mp4"],
            Mode = JoinVideosMode.Auto
        };

        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.JoinVideosSettings] = source.ToOptionPayload()
        };

        var parsed = JoinVideosSettings.TryFromOptions(options, out var settings, out var error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.NotNull(settings);
        Assert.Equal(source.InputPaths, settings.InputPaths);
        Assert.Equal(JoinVideosMode.Auto, settings.Mode);
    }

    [Theory]
    [InlineData("auto", JoinVideosMode.Auto)]
    [InlineData("COPY", JoinVideosMode.Copy)]
    [InlineData("normalize", JoinVideosMode.Normalize)]
    public void TryParseMode_AcceptsSupportedModes(string text, JoinVideosMode expected)
    {
        Assert.True(JoinVideosSettings.TryParseMode(text, out var mode));
        Assert.Equal(expected, mode);
    }

    [Fact]
    public void TryFromOptions_RejectsOneClip()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.JoinVideosSettings] = new JoinVideosSettings { InputPaths = [@"C:\clips\one.mp4"] }.ToOptionPayload()
        };

        Assert.False(JoinVideosSettings.TryFromOptions(options, out _, out _));
    }
}
