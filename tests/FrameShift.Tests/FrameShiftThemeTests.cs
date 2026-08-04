using System;
using System.Drawing;
using System.IO;
using FrameShift.Windows.Helpers;
using Xunit;

namespace FrameShift.Tests;

public sealed class FrameShiftThemeTests
{
    [Theory]
    [InlineData(null, FrameShiftThemePreference.System)]
    [InlineData("", FrameShiftThemePreference.System)]
    [InlineData("unknown", FrameShiftThemePreference.System)]
    [InlineData("system", FrameShiftThemePreference.System)]
    [InlineData("Light", FrameShiftThemePreference.Light)]
    [InlineData("dark", FrameShiftThemePreference.Dark)]
    public void ParseThemePreference_UsesSystemForMissingOrUnknownValues(string? value, FrameShiftThemePreference expected)
    {
        Assert.Equal(expected, FrameShiftUiSettings.ParseThemePreference(value));
    }

    [Fact]
    public void Load_ReturnsSystemWhenFileIsMissingInvalidOrUnknown()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "ui-settings.json");

        try
        {
            Assert.Equal(FrameShiftThemePreference.System, FrameShiftUiSettings.Load(path).GetThemePreference());

            File.WriteAllText(path, "not json");
            Assert.Equal(FrameShiftThemePreference.System, FrameShiftUiSettings.Load(path).GetThemePreference());

            File.WriteAllText(path, "{ \"Theme\": \"Sepia\" }");
            Assert.Equal(FrameShiftThemePreference.System, FrameShiftUiSettings.Load(path).GetThemePreference());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_PersistsTheNormalizedThemeInItsOwnFile()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "ui-settings.json");

        try
        {
            new FrameShiftUiSettings { Theme = "dark" }.Save(path);

            Assert.Equal("{\r\n  \"Theme\": \"Dark\"\r\n}", File.ReadAllText(path));
            Assert.Equal(FrameShiftThemePreference.Dark, FrameShiftUiSettings.Load(path).GetThemePreference());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(FrameShiftThemePreference.Light, false, FrameShiftThemeMode.Light)]
    [InlineData(FrameShiftThemePreference.Dark, true, FrameShiftThemeMode.Dark)]
    [InlineData(FrameShiftThemePreference.System, true, FrameShiftThemeMode.Light)]
    [InlineData(FrameShiftThemePreference.System, false, FrameShiftThemeMode.Dark)]
    public void ResolveEffectiveTheme_HonorsPreferenceAndWindowsFallback(
        FrameShiftThemePreference preference,
        bool windowsUsesLightTheme,
        FrameShiftThemeMode expected)
    {
        Assert.Equal(expected, FrameShiftTheme.ResolveEffectiveTheme(preference, windowsUsesLightTheme));
    }

    [Theory]
    [InlineData(FrameShiftThemeMode.Light, 0x4D, 0x79, 0xB4)]
    [InlineData(FrameShiftThemeMode.Dark, 0x8E, 0xBA, 0xF3)]
    public void AccentText_UsesTheAccessibleAccentForEachPalette(
        FrameShiftThemeMode theme,
        int red,
        int green,
        int blue)
    {
        Assert.Equal(Color.FromArgb(red, green, blue), FrameShiftTheme.GetAccentTextColor(theme));
    }

    [Theory]
    [InlineData(0xF5, 0xF7, 0xFB, 0x16, 0x1A, 0x22)]
    [InlineData(0xEC, 0xF3, 0xFF, 0x2A, 0x3B, 0x55)]
    public void ResolveCurrentBackgroundColor_MapsEitherPaletteToTheActivePalette(
        int sourceRed,
        int sourceGreen,
        int sourceBlue,
        int expectedRed,
        int expectedGreen,
        int expectedBlue)
    {
        FrameShiftTheme.ApplyPreference(FrameShiftThemePreference.Dark);

        Assert.Equal(
            Color.FromArgb(expectedRed, expectedGreen, expectedBlue),
            FrameShiftTheme.ResolveCurrentBackgroundColor(Color.FromArgb(sourceRed, sourceGreen, sourceBlue)));

        FrameShiftTheme.ApplyPreference(FrameShiftThemePreference.Light);
    }

    [Fact]
    public void ResolveCurrentTextColor_UsesTheAccessibleAccentAfterAThemeChange()
    {
        FrameShiftTheme.ApplyPreference(FrameShiftThemePreference.Dark);

        Assert.Equal(
            FrameShiftTheme.PrimaryBlue,
            FrameShiftTheme.ResolveCurrentTextColor(FrameShiftTheme.SecondaryBlue));

        FrameShiftTheme.ApplyPreference(FrameShiftThemePreference.Light);
    }

    [Fact]
    public void MenuColorTable_UsesTheCentralThemeColors()
    {
        var colors = new FrameShiftMenuColorTable();

        Assert.Equal(FrameShiftTheme.Surface, colors.ToolStripDropDownBackground);
        Assert.Equal(FrameShiftTheme.SurfaceBorder, colors.ToolStripBorder);
        Assert.Equal(FrameShiftTheme.AccentSoft, colors.MenuItemSelected);
        Assert.Equal(FrameShiftTheme.AccentSoftHover, colors.MenuItemPressedGradientBegin);
        Assert.Equal(FrameShiftTheme.AccentSoft, colors.CheckBackground);
        Assert.Equal(FrameShiftTheme.AccentSoftHover, colors.CheckSelectedBackground);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"frameshift-theme-tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
