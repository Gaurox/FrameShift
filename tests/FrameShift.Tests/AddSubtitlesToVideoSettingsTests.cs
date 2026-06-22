using System.Collections.Generic;
using FrameShift.Core.AI.CreateSubtitles;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class AddSubtitlesToVideoSettingsTests
{
    [Fact]
    public void ToOptions_RoundTrips()
    {
        var original = new AddSubtitlesToVideoSettings(@"C:\subs\track.srt", AddSubtitlesToVideoMode.SelectableTrack);
        var options = original.ToOptions();

        var success = AddSubtitlesToVideoSettings.TryFromOptions(options, out var restored, out _);

        Assert.True(success);
        Assert.NotNull(restored);
        Assert.Equal(original.SubtitleFilePath, restored!.SubtitleFilePath);
        Assert.Equal(AddSubtitlesToVideoMode.SelectableTrack, restored.Mode);
    }

    [Fact]
    public void TryFromOptions_MissingPath_ReturnsError()
    {
        var success = AddSubtitlesToVideoSettings.TryFromOptions(new Dictionary<string, string>(), out _, out var error);

        Assert.False(success);
        Assert.Equal(MediaActionMessages.AddSubtitlesToVideoSettingsMissing(), error);
    }

    [Fact]
    public void TryFromOptions_NonSrtPath_ReturnsError()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.SubtitleFilePath] = @"C:\subs\track.ass"
        };

        var success = AddSubtitlesToVideoSettings.TryFromOptions(options, out _, out var error);

        Assert.False(success);
        Assert.Equal(MediaActionMessages.AddSubtitlesToVideoSelectableSubtitleFormatInvalid(), error);
    }

    [Fact]
    public void TryFromOptions_BurnMode_WithAssPath_ReturnsSettings()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.SubtitleMode] = "burn",
            [ActionOptionKeys.SubtitleFilePath] = @"C:\subs\track.ass"
        };

        var success = AddSubtitlesToVideoSettings.TryFromOptions(options, out var settings, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(settings);
        Assert.Equal(AddSubtitlesToVideoMode.BurnIntoVideo, settings!.Mode);
        Assert.NotNull(settings.BurnSettings);
    }

    [Fact]
    public void TryFromOptions_BurnMode_WithProjectPath_ReturnsSettings()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.SubtitleMode] = "burn",
            [ActionOptionKeys.SubtitleFilePath] = @"C:\subs\track.frameshift-subtitles.json"
        };

        var success = AddSubtitlesToVideoSettings.TryFromOptions(options, out var settings, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(settings);
        Assert.Equal(AddSubtitlesToVideoMode.BurnIntoVideo, settings!.Mode);
    }

    [Fact]
    public void TryFromOptions_InvalidMode_ReturnsError()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.SubtitleMode] = "mystery",
            [ActionOptionKeys.SubtitleFilePath] = @"C:\subs\track.srt"
        };

        var success = AddSubtitlesToVideoSettings.TryFromOptions(options, out _, out var error);

        Assert.False(success);
        Assert.Equal(MediaActionMessages.AddSubtitlesToVideoModeInvalid(), error);
    }

    [Fact]
    public void TryFromOptions_BurnMode_WithVisualOverrides_ParsesBurnSettings()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.SubtitleMode] = "burn",
            [ActionOptionKeys.SubtitleFilePath] = @"C:\subs\track.srt",
            [ActionOptionKeys.SubtitlesAssPreset] = "word-highlight",
            [ActionOptionKeys.SubtitleFontName] = "Tahoma",
            [ActionOptionKeys.SubtitleFontSize] = "42",
            [ActionOptionKeys.SubtitlePrimaryColor] = "#FFEECC",
            [ActionOptionKeys.SubtitleHighlightColor] = "#00FF00",
            [ActionOptionKeys.SubtitleOutlineColor] = "#112233",
            [ActionOptionKeys.SubtitleShadowColor] = "#334455",
            [ActionOptionKeys.SubtitleOutlineThickness] = "3.5",
            [ActionOptionKeys.SubtitleShadowDepth] = "1.2",
            [ActionOptionKeys.SubtitleVerticalAlignment] = "top",
            [ActionOptionKeys.SubtitleMarginVertical] = "96"
        };

        var success = AddSubtitlesToVideoSettings.TryFromOptions(options, out var settings, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(settings);
        Assert.NotNull(settings!.BurnSettings);
        Assert.Equal(CreateSubtitlesAssPreset.WordHighlight, settings.BurnSettings!.AssPreset);
        Assert.Equal("Tahoma", settings.BurnSettings.FontName);
        Assert.Equal(42, settings.BurnSettings.FontSize);
        Assert.Equal("#FFEECC", settings.BurnSettings.PrimaryColor);
        Assert.Equal("#00FF00", settings.BurnSettings.HighlightColor);
        Assert.Equal(AddSubtitlesToVideoVerticalAlignment.Top, settings.BurnSettings.VerticalAlignment);
    }

    [Fact]
    public void ContainsAnyOption_WhenKeyPresent_ReturnsTrue()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.SubtitleFilePath] = @"C:\subs\track.srt"
        };

        Assert.True(AddSubtitlesToVideoSettings.ContainsAnyOption(options));
    }

    [Fact]
    public void ContainsAnyOption_WhenModeKeyPresent_ReturnsTrue()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.SubtitleMode] = "burn"
        };

        Assert.True(AddSubtitlesToVideoSettings.ContainsAnyOption(options));
    }
}
