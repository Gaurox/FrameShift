using System;
using System.Collections.Generic;
using System.Globalization;

namespace FrameShift.Core.Actions;

public sealed record ResizeSettings(int Width, int Height)
{
    public IReadOnlyDictionary<string, string> ToOptions()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionOptionKeys.ResizeWidth] = Width.ToString(CultureInfo.InvariantCulture),
            [ActionOptionKeys.ResizeHeight] = Height.ToString(CultureInfo.InvariantCulture)
        };
    }

    public static bool TryFromOptions(IReadOnlyDictionary<string, string>? options, out ResizeSettings settings, out string? error)
    {
        settings = default!;
        error = null;

        if (options is null)
        {
            error = MediaActionMessages.ResizeSettingsMissing();
            return false;
        }

        if (!TryReadPositiveInt(options, ActionOptionKeys.ResizeWidth, out var width) ||
            !TryReadPositiveInt(options, ActionOptionKeys.ResizeHeight, out var height))
        {
            error = MediaActionMessages.ResizeSettingsInvalid();
            return false;
        }

        settings = new ResizeSettings(width, height);
        return true;
    }

    private static bool TryReadPositiveInt(IReadOnlyDictionary<string, string> options, string key, out int value)
    {
        value = 0;
        if (!options.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
    }
}
