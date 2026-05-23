using System;
using System.Collections.Generic;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class ChangePitchSettingsTests
{
    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(12, 2.0)]
    [InlineData(-12, 0.5)]
    [InlineData(7, 1.4983)]
    public void PitchFactor_MatchesSemitoneFormula(double semitones, double expectedFactor)
    {
        var settings = new ChangePitchSettings(semitones, true);
        Assert.Equal(expectedFactor, settings.PitchFactor, 4);
    }

    [Theory]
    [InlineData(0, "_pitch_0st")]
    [InlineData(3, "_pitch_plus3st")]
    [InlineData(-5, "_pitch_minus5st")]
    [InlineData(12, "_pitch_plus12st")]
    [InlineData(-12, "_pitch_minus12st")]
    public void OutputSuffix_FormatsCorrectly(double semitones, string expectedSuffix)
    {
        var settings = new ChangePitchSettings(semitones, true);
        Assert.Equal(expectedSuffix, settings.OutputSuffix);
    }

    [Fact]
    public void EstimatedOutputDuration_KeepDurationTrue_ReturnsSameDuration()
    {
        var settings = new ChangePitchSettings(7, KeepDuration: true);
        var result = settings.EstimatedOutputDuration(100.0);
        Assert.Equal(100.0, result, 6);
    }

    [Fact]
    public void EstimatedOutputDuration_KeepDurationFalse_AdjustsByFactor()
    {
        var settings = new ChangePitchSettings(12, KeepDuration: false);
        var result = settings.EstimatedOutputDuration(100.0);
        Assert.Equal(50.0, result, 4);
    }

    [Fact]
    public void ToOptions_RoundTrips()
    {
        var original = new ChangePitchSettings(5.5, KeepDuration: false);
        var options = original.ToOptions();

        var success = ChangePitchSettings.TryFromOptions(options, out var restored, out _);

        Assert.True(success);
        Assert.NotNull(restored);
        Assert.Equal(original.Semitones, restored!.Semitones, 5);
        Assert.Equal(original.KeepDuration, restored.KeepDuration);
    }

    [Fact]
    public void TryFromOptions_MissingKey_ReturnsError()
    {
        var options = new Dictionary<string, string>();
        var success = ChangePitchSettings.TryFromOptions(options, out _, out var error);
        Assert.False(success);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("25")]
    [InlineData("-25")]
    public void TryFromOptions_OutOfRange_ReturnsError(string semiText)
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.PitchSemitones] = semiText
        };
        var success = ChangePitchSettings.TryFromOptions(options, out _, out var error);
        Assert.False(success);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryFromOptions_KeepDurationDefaultsToTrue()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.PitchSemitones] = "3"
        };
        var success = ChangePitchSettings.TryFromOptions(options, out var settings, out _);
        Assert.True(success);
        Assert.True(settings!.KeepDuration);
    }

    [Fact]
    public void TryFromOptions_KeepDurationFalseExplicit()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.PitchSemitones] = "-3",
            [ActionOptionKeys.PitchKeepDuration] = "false"
        };
        var success = ChangePitchSettings.TryFromOptions(options, out var settings, out _);
        Assert.True(success);
        Assert.False(settings!.KeepDuration);
    }

    [Fact]
    public void TryFromOptions_AcceptsCommaDecimalSeparator()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.PitchSemitones] = "2,5"
        };
        var success = ChangePitchSettings.TryFromOptions(options, out var settings, out _);
        Assert.True(success);
        Assert.Equal(2.5, settings!.Semitones, 5);
    }

    [Fact]
    public void ContainsAnyOption_WhenKeyPresent_ReturnsTrue()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.PitchSemitones] = "0"
        };
        Assert.True(ChangePitchSettings.ContainsAnyOption(options));
    }

    [Fact]
    public void ContainsAnyOption_WhenKeyAbsent_ReturnsFalse()
    {
        var options = new Dictionary<string, string>();
        Assert.False(ChangePitchSettings.ContainsAnyOption(options));
    }

    [Fact]
    public void ContainsAnyOption_WhenNull_ReturnsFalse()
    {
        Assert.False(ChangePitchSettings.ContainsAnyOption(null));
    }
}
