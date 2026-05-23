using System.Collections.Generic;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class ConvertToIconSettingsTests
{
    [Fact]
    public void TryFromOptions_AcceptsSupportedValues()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.IconSizes] = "16,32,256",
            [ActionOptionKeys.IconFitMode] = "fill",
            [ActionOptionKeys.IconBackground] = "black"
        };

        var success = ConvertToIconSettings.TryFromOptions(options, out var settings, out var errorMessage);

        Assert.True(success);
        Assert.Null(errorMessage);
        Assert.NotNull(settings);
        Assert.Equal(new[] { 16, 32, 256 }, settings!.Sizes);
        Assert.Equal("fill", settings.FitMode);
        Assert.Equal("black", settings.Background);
    }

    [Fact]
    public void TryFromOptions_DefaultsToAllSizesWhenSizesAreMissing()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.IconFitMode] = "fit"
        };

        var success = ConvertToIconSettings.TryFromOptions(options, out var settings, out var errorMessage);

        Assert.True(success);
        Assert.Null(errorMessage);
        Assert.NotNull(settings);
        Assert.Equal(ConvertToIconSettings.GetSupportedSizes(), settings!.Sizes);
        Assert.Equal("fit", settings.FitMode);
        Assert.Equal("transparent", settings.Background);
    }

    [Fact]
    public void TryFromOptions_RejectsUnsupportedSize()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.IconSizes] = "12,32"
        };

        var success = ConvertToIconSettings.TryFromOptions(options, out var settings, out var errorMessage);

        Assert.False(success);
        Assert.Null(settings);
        Assert.Equal(MediaActionMessages.ConvertToIconSettingsInvalid(), errorMessage);
    }
}
