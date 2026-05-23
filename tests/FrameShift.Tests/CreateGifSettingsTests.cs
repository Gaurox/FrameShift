using System.Collections.Generic;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class CreateGifSettingsTests
{
    [Fact]
    public void TryFromOptions_AcceptsDefaultsWhenOnlyGifPresetsAreProvided()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.GifResolution] = "960",
            [ActionOptionKeys.GifFps] = "20",
            [ActionOptionKeys.GifQuality] = "high"
        };

        var success = CreateGifSettings.TryFromOptions(options, 12.5, out var settings, out var errorMessage);

        Assert.True(success);
        Assert.Null(errorMessage);
        Assert.NotNull(settings);
        Assert.Equal(0d, settings!.StartSeconds, 3);
        Assert.Equal(12.5, settings.DurationSeconds, 3);
        Assert.Equal("960", settings.ResolutionKey);
        Assert.Equal(20, settings.Fps);
        Assert.Equal("high", settings.QualityKey);
    }

    [Fact]
    public void TryFromOptions_AcceptsStartAndDuration()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.StartTime] = "00:00:01.250",
            [ActionOptionKeys.Duration] = "2.5",
            [ActionOptionKeys.GifResolution] = "720",
            [ActionOptionKeys.GifFps] = "12",
            [ActionOptionKeys.GifQuality] = "balanced"
        };

        var success = CreateGifSettings.TryFromOptions(options, 9d, out var settings, out var errorMessage);

        Assert.True(success);
        Assert.Null(errorMessage);
        Assert.NotNull(settings);
        Assert.Equal(1.25, settings!.StartSeconds, 3);
        Assert.Equal(2.5, settings.DurationSeconds, 3);
        Assert.True(settings.IsCustomRange(9d));
    }

    [Fact]
    public void TryFromOptions_RejectsUnsupportedFps()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.GifFps] = "9"
        };

        var success = CreateGifSettings.TryFromOptions(options, 5d, out var settings, out var errorMessage);

        Assert.False(success);
        Assert.Null(settings);
        Assert.Equal(MediaActionMessages.CreateGifFpsInvalid(), errorMessage);
    }
}
