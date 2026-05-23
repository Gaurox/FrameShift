using System;
using System.Collections.Generic;
using System.Globalization;

namespace FrameShift.Core.Actions;

public sealed record VideoCropSettings(int X, int Y, int Width, int Height)
{
    public IReadOnlyDictionary<string, string> ToOptions()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionOptionKeys.CropX] = X.ToString(CultureInfo.InvariantCulture),
            [ActionOptionKeys.CropY] = Y.ToString(CultureInfo.InvariantCulture),
            [ActionOptionKeys.CropWidth] = Width.ToString(CultureInfo.InvariantCulture),
            [ActionOptionKeys.CropHeight] = Height.ToString(CultureInfo.InvariantCulture)
        };
    }

    public static bool TryFromOptions(IReadOnlyDictionary<string, string>? options, out VideoCropSettings settings, out string? error)
    {
        settings = default!;
        error = null;

        if (options is null)
        {
            error = MediaActionMessages.CropSettingsMissing();
            return false;
        }

        if (!TryReadInt(options, ActionOptionKeys.CropX, out var x) ||
            !TryReadInt(options, ActionOptionKeys.CropY, out var y) ||
            !TryReadInt(options, ActionOptionKeys.CropWidth, out var width) ||
            !TryReadInt(options, ActionOptionKeys.CropHeight, out var height))
        {
            error = MediaActionMessages.CropSettingsInvalid();
            return false;
        }

        settings = new VideoCropSettings(x, y, width, height);
        return true;
    }

    private static bool TryReadInt(IReadOnlyDictionary<string, string> options, string key, out int value)
    {
        value = 0;
        if (!options.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
