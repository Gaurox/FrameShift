using System.Collections.Generic;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class CutAudioSettingsTests
{
    [Fact]
    public void TryFromOptions_AcceptsStartAndEndInClockFormat()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.StartTime] = "01:02.5",
            [ActionOptionKeys.EndTime] = "00:02:10"
        };

        var success = CutAudioSettings.TryFromOptions(options, out var settings, out var errorMessage);

        Assert.True(success);
        Assert.Null(errorMessage);
        Assert.NotNull(settings);
        Assert.Equal(62.5, settings!.StartSeconds, 3);
        Assert.Equal(130, settings.EndSeconds, 3);
        Assert.Equal(67.5, settings.DurationSeconds, 3);
    }

    [Fact]
    public void TryFromOptions_AcceptsDurationWhenEndIsMissing()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.StartTime] = "12.5",
            [ActionOptionKeys.Duration] = "00:30"
        };

        var success = CutAudioSettings.TryFromOptions(options, out var settings, out var errorMessage);

        Assert.True(success);
        Assert.Null(errorMessage);
        Assert.NotNull(settings);
        Assert.Equal(12.5, settings!.StartSeconds, 3);
        Assert.Equal(42.5, settings.EndSeconds, 3);
    }

    [Fact]
    public void TryFromOptions_RejectsEndBeforeStart()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.StartTime] = "90",
            [ActionOptionKeys.EndTime] = "45"
        };

        var success = CutAudioSettings.TryFromOptions(options, out var settings, out var errorMessage);

        Assert.False(success);
        Assert.Null(settings);
        Assert.Equal(MediaActionMessages.CutAudioEndInvalid(), errorMessage);
    }
}
