using System.Collections.Generic;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class RifeInterpolateVideoSettingsTests
{
    [Fact]
    public void TryFromOptions_AcceptsValidModelAndMultiplier()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.InterpolateModelId] = "rife-v426-x2",
            [ActionOptionKeys.InterpolateMultiplier] = "2"
        };

        var success = RifeInterpolateVideoSettings.TryFromOptions(options, out var settings, out var errorMessage);

        Assert.True(success);
        Assert.NotNull(settings);
        Assert.Null(errorMessage);
        Assert.Equal("rife-v426-x2", settings!.ModelId);
        Assert.Equal(2, settings.TargetMultiplier);
        Assert.Equal(1, settings.PlaybackDivisor);
    }

    [Fact]
    public void TryFromOptions_RejectsMissingMultiplier()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.InterpolateModelId] = "rife-v426-x2"
        };

        var success = RifeInterpolateVideoSettings.TryFromOptions(options, out var settings, out var errorMessage);

        Assert.False(success);
        Assert.Null(settings);
        Assert.Equal(MediaActionMessages.RifeInterpolateVideoSettingsMissing(), errorMessage);
    }

    [Fact]
    public void TryFromOptions_RejectsMultiplierBelowTwo()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.InterpolateModelId] = "rife-v426-x2",
            [ActionOptionKeys.InterpolateMultiplier] = "1"
        };

        var success = RifeInterpolateVideoSettings.TryFromOptions(options, out var settings, out var errorMessage);

        Assert.False(success);
        Assert.Null(settings);
        Assert.Equal(MediaActionMessages.RifeInterpolateVideoSettingsInvalid(), errorMessage);
    }

    [Fact]
    public void TryFromOptions_AcceptsPlaybackDivisor()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.InterpolateModelId] = "rife-v426-x2",
            [ActionOptionKeys.InterpolateMultiplier] = "4",
            [ActionOptionKeys.InterpolatePlaybackDivisor] = "2"
        };

        var success = RifeInterpolateVideoSettings.TryFromOptions(options, out var settings, out var errorMessage);

        Assert.True(success);
        Assert.NotNull(settings);
        Assert.Null(errorMessage);
        Assert.Equal(4, settings!.TargetMultiplier);
        Assert.Equal(2, settings.PlaybackDivisor);
        Assert.Equal(32d, settings.GetOutputFrameRate(16d), 3);
        Assert.True(settings.PreservesAudio);
        Assert.True(settings.KeepPitch);
    }
}
