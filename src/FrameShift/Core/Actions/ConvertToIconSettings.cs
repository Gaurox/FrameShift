using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FrameShift.Core.Actions;

public sealed record ConvertToIconSettings(
    IReadOnlyList<int> Sizes,
    string FitMode,
    string Background)
{
    public const string DefaultFitMode = "fit";
    public const string DefaultBackground = "transparent";

    private static readonly int[] SupportedSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    public IReadOnlyDictionary<string, string> ToOptions()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionOptionKeys.IconSizes] = string.Join(",", Sizes.OrderBy(static size => size).Select(static size => size.ToString(CultureInfo.InvariantCulture))),
            [ActionOptionKeys.IconFitMode] = FitMode,
            [ActionOptionKeys.IconBackground] = Background
        };
    }

    public static IReadOnlyList<int> GetSupportedSizes()
    {
        return SupportedSizes;
    }

    public static bool ContainsAnyOption(IReadOnlyDictionary<string, string>? options)
    {
        if (options is null)
        {
            return false;
        }

        return options.ContainsKey(ActionOptionKeys.IconSizes) ||
               options.ContainsKey(ActionOptionKeys.IconFitMode) ||
               options.ContainsKey(ActionOptionKeys.IconBackground);
    }

    public static bool IsSupportedFitMode(string? fitMode)
    {
        return fitMode is "fit" or "fill";
    }

    public static bool IsSupportedBackground(string? background)
    {
        return background is "transparent" or "white" or "black";
    }

    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string>? options,
        out ConvertToIconSettings? settings,
        out string? errorMessage)
    {
        settings = null;
        errorMessage = null;

        if (options is null)
        {
            errorMessage = MediaActionMessages.ConvertToIconSettingsMissing();
            return false;
        }

        var fitMode = (GetOptionValue(options, ActionOptionKeys.IconFitMode) ?? DefaultFitMode).ToLowerInvariant();
        if (!IsSupportedFitMode(fitMode))
        {
            errorMessage = MediaActionMessages.ConvertToIconFitModeInvalid();
            return false;
        }

        var background = (GetOptionValue(options, ActionOptionKeys.IconBackground) ?? DefaultBackground).ToLowerInvariant();
        if (!IsSupportedBackground(background))
        {
            errorMessage = MediaActionMessages.ConvertToIconBackgroundInvalid();
            return false;
        }

        var sizesText = GetOptionValue(options, ActionOptionKeys.IconSizes);
        var sizes = new List<int>();
        if (string.IsNullOrWhiteSpace(sizesText))
        {
            sizes.AddRange(SupportedSizes);
        }
        else
        {
            var tokens = sizesText
                .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var token in tokens)
            {
                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ||
                    !SupportedSizes.Contains(size))
                {
                    errorMessage = MediaActionMessages.ConvertToIconSettingsInvalid();
                    return false;
                }

                if (!sizes.Contains(size))
                {
                    sizes.Add(size);
                }
            }
        }

        if (sizes.Count == 0)
        {
            errorMessage = MediaActionMessages.ConvertToIconNoSizesSelected();
            return false;
        }

        sizes.Sort();
        settings = new ConvertToIconSettings(sizes, fitMode, background);
        return true;
    }

    private static string? GetOptionValue(IReadOnlyDictionary<string, string> options, string key)
    {
        foreach (var option in options)
        {
            if (!string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return string.IsNullOrWhiteSpace(option.Value)
                ? null
                : option.Value.Trim();
        }

        return null;
    }
}
