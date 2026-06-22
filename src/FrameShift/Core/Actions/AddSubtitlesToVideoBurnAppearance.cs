using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using FrameShift.Core.AI.CreateSubtitles;
using FrameShift.Core.FFprobe;

namespace FrameShift.Core.Actions;

internal enum AddSubtitlesToVideoVerticalAlignment
{
    Bottom,
    Middle,
    Top
}

internal static class AddSubtitlesToVideoVerticalAlignments
{
    public static AddSubtitlesToVideoVerticalAlignment Default => AddSubtitlesToVideoVerticalAlignment.Bottom;

    public static bool TryParse(string? value, out AddSubtitlesToVideoVerticalAlignment alignment)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "bottom":
                alignment = AddSubtitlesToVideoVerticalAlignment.Bottom;
                return true;

            case "middle":
            case "center":
                alignment = AddSubtitlesToVideoVerticalAlignment.Middle;
                return true;

            case "top":
                alignment = AddSubtitlesToVideoVerticalAlignment.Top;
                return true;

            default:
                alignment = Default;
                return false;
        }
    }

    public static string ToOptionValue(this AddSubtitlesToVideoVerticalAlignment alignment) => alignment switch
    {
        AddSubtitlesToVideoVerticalAlignment.Middle => "middle",
        AddSubtitlesToVideoVerticalAlignment.Top => "top",
        _ => "bottom"
    };

    public static string GetDisplayName(this AddSubtitlesToVideoVerticalAlignment alignment) => alignment switch
    {
        AddSubtitlesToVideoVerticalAlignment.Middle => "Middle",
        AddSubtitlesToVideoVerticalAlignment.Top => "Top",
        _ => "Bottom"
    };

    public static int ToAssAlignmentCode(this AddSubtitlesToVideoVerticalAlignment alignment) => alignment switch
    {
        AddSubtitlesToVideoVerticalAlignment.Middle => 5,
        AddSubtitlesToVideoVerticalAlignment.Top => 8,
        _ => 2
    };
}

internal sealed record AddSubtitlesToVideoBurnSettings(
    CreateSubtitlesAssPreset AssPreset,
    string? FontName,
    int? FontSize,
    string? PrimaryColor,
    string? HighlightColor,
    string? OutlineColor,
    string? ShadowColor,
    double? OutlineThickness,
    double? ShadowDepth,
    AddSubtitlesToVideoVerticalAlignment? VerticalAlignment,
    int? MarginVertical)
{
    public static AddSubtitlesToVideoBurnSettings Default { get; } = new(
        CreateSubtitlesAssPresets.Default,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null);

    public AddSubtitlesToVideoBurnAppearance ResolveAppearanceForVideo(MediaProbeResult probe)
    {
        var baseline = AddSubtitlesToVideoBurnAppearance.CreateDefaultForVideo(probe);
        return baseline with
        {
            AssPreset = AssPreset,
            FontName = NormalizeFontName(FontName, baseline.FontName),
            FontSize = NormalizeInt(FontSize, baseline.FontSize, 12, 240),
            PrimaryColor = NormalizeColorText(PrimaryColor, baseline.PrimaryColor),
            HighlightColor = NormalizeColorText(HighlightColor, baseline.HighlightColor),
            OutlineColor = NormalizeColorText(OutlineColor, baseline.OutlineColor),
            ShadowColor = NormalizeColorText(ShadowColor, baseline.ShadowColor),
            OutlineThickness = NormalizeDouble(OutlineThickness, baseline.OutlineThickness, 0d, 12d),
            ShadowDepth = NormalizeDouble(ShadowDepth, baseline.ShadowDepth, 0d, 12d),
            VerticalAlignment = VerticalAlignment ?? baseline.VerticalAlignment,
            MarginVertical = NormalizeInt(MarginVertical, baseline.MarginVertical, 0, 600)
        };
    }

    public IReadOnlyDictionary<string, string> ToOptions()
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionOptionKeys.SubtitlesAssPreset] = AssPreset.ToOptionValue()
        };

        if (!string.IsNullOrWhiteSpace(FontName))
        {
            options[ActionOptionKeys.SubtitleFontName] = FontName!.Trim();
        }

        if (FontSize.HasValue)
        {
            options[ActionOptionKeys.SubtitleFontSize] = FontSize.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(PrimaryColor))
        {
            options[ActionOptionKeys.SubtitlePrimaryColor] = NormalizeColorText(PrimaryColor, AddSubtitlesToVideoBurnAppearance.DefaultPrimaryColor);
        }

        if (!string.IsNullOrWhiteSpace(HighlightColor))
        {
            options[ActionOptionKeys.SubtitleHighlightColor] = NormalizeColorText(HighlightColor, AddSubtitlesToVideoBurnAppearance.DefaultHighlightColor);
        }

        if (!string.IsNullOrWhiteSpace(OutlineColor))
        {
            options[ActionOptionKeys.SubtitleOutlineColor] = NormalizeColorText(OutlineColor, AddSubtitlesToVideoBurnAppearance.DefaultOutlineColor);
        }

        if (!string.IsNullOrWhiteSpace(ShadowColor))
        {
            options[ActionOptionKeys.SubtitleShadowColor] = NormalizeColorText(ShadowColor, AddSubtitlesToVideoBurnAppearance.DefaultShadowColor);
        }

        if (OutlineThickness.HasValue)
        {
            options[ActionOptionKeys.SubtitleOutlineThickness] = OutlineThickness.Value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        if (ShadowDepth.HasValue)
        {
            options[ActionOptionKeys.SubtitleShadowDepth] = ShadowDepth.Value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        if (VerticalAlignment.HasValue)
        {
            options[ActionOptionKeys.SubtitleVerticalAlignment] = VerticalAlignment.Value.ToOptionValue();
        }

        if (MarginVertical.HasValue)
        {
            options[ActionOptionKeys.SubtitleMarginVertical] = MarginVertical.Value.ToString(CultureInfo.InvariantCulture);
        }

        return options;
    }

    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string>? options,
        out AddSubtitlesToVideoBurnSettings settings,
        out string? error)
    {
        settings = Default;
        error = null;

        var preset = CreateSubtitlesAssPresets.Default;
        if (options is not null &&
            options.TryGetValue(ActionOptionKeys.SubtitlesAssPreset, out var presetValue) &&
            !string.IsNullOrWhiteSpace(presetValue) &&
            !CreateSubtitlesAssPresets.TryParse(presetValue, out preset))
        {
            error = MediaActionMessages.AddSubtitlesToVideoBurnSettingsInvalid();
            return false;
        }

        if (!TryGetOptionalInt(options, ActionOptionKeys.SubtitleFontSize, 12, 240, out var fontSize) ||
            !TryGetOptionalColor(options, ActionOptionKeys.SubtitlePrimaryColor, out var primaryColor) ||
            !TryGetOptionalColor(options, ActionOptionKeys.SubtitleHighlightColor, out var highlightColor) ||
            !TryGetOptionalColor(options, ActionOptionKeys.SubtitleOutlineColor, out var outlineColor) ||
            !TryGetOptionalColor(options, ActionOptionKeys.SubtitleShadowColor, out var shadowColor) ||
            !TryGetOptionalDouble(options, ActionOptionKeys.SubtitleOutlineThickness, 0d, 12d, out var outlineThickness) ||
            !TryGetOptionalDouble(options, ActionOptionKeys.SubtitleShadowDepth, 0d, 12d, out var shadowDepth) ||
            !TryGetOptionalInt(options, ActionOptionKeys.SubtitleMarginVertical, 0, 600, out var marginVertical))
        {
            error = MediaActionMessages.AddSubtitlesToVideoBurnSettingsInvalid();
            return false;
        }

        AddSubtitlesToVideoVerticalAlignment? verticalAlignment = null;
        if (options is not null &&
            options.TryGetValue(ActionOptionKeys.SubtitleVerticalAlignment, out var alignmentValue) &&
            !string.IsNullOrWhiteSpace(alignmentValue))
        {
            if (!AddSubtitlesToVideoVerticalAlignments.TryParse(alignmentValue, out var parsedAlignment))
            {
                error = MediaActionMessages.AddSubtitlesToVideoBurnSettingsInvalid();
                return false;
            }

            verticalAlignment = parsedAlignment;
        }

        string? fontName = null;
        if (options is not null &&
            options.TryGetValue(ActionOptionKeys.SubtitleFontName, out var fontNameValue) &&
            !string.IsNullOrWhiteSpace(fontNameValue))
        {
            fontName = fontNameValue.Trim();
        }

        settings = new AddSubtitlesToVideoBurnSettings(
            preset,
            fontName,
            fontSize,
            primaryColor,
            highlightColor,
            outlineColor,
            shadowColor,
            outlineThickness,
            shadowDepth,
            verticalAlignment,
            marginVertical);
        return true;
    }

    public static AddSubtitlesToVideoBurnSettings FromAppearance(AddSubtitlesToVideoBurnAppearance appearance) => new(
        appearance.AssPreset,
        appearance.FontName,
        appearance.FontSize,
        appearance.PrimaryColor,
        appearance.HighlightColor,
        appearance.OutlineColor,
        appearance.ShadowColor,
        appearance.OutlineThickness,
        appearance.ShadowDepth,
        appearance.VerticalAlignment,
        appearance.MarginVertical);

    private static bool TryGetOptionalInt(
        IReadOnlyDictionary<string, string>? options,
        string key,
        int minimum,
        int maximum,
        out int? value)
    {
        value = null;
        if (options is null || !options.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < minimum ||
            parsed > maximum)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetOptionalDouble(
        IReadOnlyDictionary<string, string>? options,
        string key,
        double minimum,
        double maximum,
        out double? value)
    {
        value = null;
        if (options is null || !options.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < minimum ||
            parsed > maximum)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetOptionalColor(
        IReadOnlyDictionary<string, string>? options,
        string key,
        out string? value)
    {
        value = null;
        if (options is null || !options.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        if (!AddSubtitlesToVideoBurnAppearance.TryNormalizeColorText(rawValue, out var normalized))
        {
            return false;
        }

        value = normalized;
        return true;
    }

    private static string NormalizeFontName(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static int NormalizeInt(int? value, int fallback, int minimum, int maximum)
    {
        if (!value.HasValue)
        {
            return fallback;
        }

        return Math.Clamp(value.Value, minimum, maximum);
    }

    private static double NormalizeDouble(double? value, double fallback, double minimum, double maximum)
    {
        if (!value.HasValue)
        {
            return fallback;
        }

        return Math.Clamp(value.Value, minimum, maximum);
    }

    private static string NormalizeColorText(string? value, string fallback)
    {
        return AddSubtitlesToVideoBurnAppearance.TryNormalizeColorText(value, out var normalized)
            ? normalized
            : fallback;
    }
}

internal sealed record AddSubtitlesToVideoBurnAppearance(
    CreateSubtitlesAssPreset AssPreset,
    string FontName,
    int FontSize,
    string PrimaryColor,
    string HighlightColor,
    string OutlineColor,
    string ShadowColor,
    double OutlineThickness,
    double ShadowDepth,
    AddSubtitlesToVideoVerticalAlignment VerticalAlignment,
    int MarginVertical)
{
    public const string DefaultPrimaryColor = "#FFFFFF";
    public const string DefaultHighlightColor = "#FFFF00";
    public const string DefaultOutlineColor = "#000000";
    public const string DefaultShadowColor = "#000000";

    private static readonly Regex s_colorRegex = new("^#?[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public CreateSubtitlesAssLayout ToAssLayout(int videoWidth, int videoHeight)
    {
        return new CreateSubtitlesAssLayout(
            videoWidth > 0 ? videoWidth : 1920,
            videoHeight > 0 ? videoHeight : 1080,
            FontName,
            FontSize,
            ParseColor(PrimaryColor, Color.White),
            ParseColor(HighlightColor, Color.Yellow),
            ParseColor(OutlineColor, Color.Black),
            ParseColor(ShadowColor, Color.Black),
            90,
            90,
            MarginVertical,
            OutlineThickness,
            ShadowDepth,
            VerticalAlignment.ToAssAlignmentCode());
    }

    public static AddSubtitlesToVideoBurnAppearance CreateDefaultForVideo(MediaProbeResult probe)
    {
        var layout = CreateSubtitlesAssLayout.ForVideo(probe.DisplayVideoWidth, probe.DisplayVideoHeight);
        return new AddSubtitlesToVideoBurnAppearance(
            CreateSubtitlesAssPresets.Default,
            layout.FontName,
            layout.FontSize,
            DefaultPrimaryColor,
            DefaultHighlightColor,
            DefaultOutlineColor,
            DefaultShadowColor,
            layout.Outline,
            layout.Shadow,
            AddSubtitlesToVideoVerticalAlignments.Default,
            layout.MarginV);
    }

    public static bool TryNormalizeColorText(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (!s_colorRegex.IsMatch(candidate))
        {
            return false;
        }

        normalized = candidate.StartsWith('#')
            ? $"#{candidate[1..].ToUpperInvariant()}"
            : $"#{candidate.ToUpperInvariant()}";
        return true;
    }

    public static Color ParseColor(string? value, Color fallback)
    {
        return TryNormalizeColorText(value, out var normalized)
            ? ColorTranslator.FromHtml(normalized)
            : fallback;
    }
}
