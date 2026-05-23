using System;
using System.Collections.Generic;
using System.Globalization;

namespace FrameShift.Core.Actions;

public sealed record InterpolateVideoSettings(double TargetFps)
{
    public string OutputSuffix
    {
        get
        {
            var fpsText = TargetFps.ToString("0.###", CultureInfo.InvariantCulture);
            return $"_interp_{fpsText}fps";
        }
    }

    public IReadOnlyDictionary<string, string> ToOptions()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionOptionKeys.InterpolateFps] = TargetFps.ToString("0.###", CultureInfo.InvariantCulture)
        };
    }

    public static bool ContainsAnyOption(IReadOnlyDictionary<string, string>? options)
    {
        return options is not null && options.ContainsKey(ActionOptionKeys.InterpolateFps);
    }

    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string>? options,
        out InterpolateVideoSettings? settings,
        out string? errorMessage)
    {
        settings = null;
        errorMessage = null;

        if (options is null || !options.TryGetValue(ActionOptionKeys.InterpolateFps, out var fpsText) ||
            string.IsNullOrWhiteSpace(fpsText))
        {
            errorMessage = MediaActionMessages.InterpolateVideoSettingsMissing();
            return false;
        }

        var normalized = fpsText.Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) || fps <= 0)
        {
            errorMessage = MediaActionMessages.InterpolateVideoSettingsInvalid();
            return false;
        }

        settings = new InterpolateVideoSettings(fps);
        return true;
    }
}
