using System;
using System.Collections.Generic;
using System.Globalization;

namespace FrameShift.Core.Actions;

public sealed record ChangeSpeedSettings(double SpeedFactor, bool KeepPitch)
{
    public const double MinFactor = 0.25;
    public const double MaxFactor = 8.0;

    public double EstimatedOutputDuration(double sourceDurationSeconds)
    {
        return sourceDurationSeconds / SpeedFactor;
    }

    public string OutputSuffix
    {
        get
        {
            var pct = (int)Math.Round(SpeedFactor * 100.0);
            return $"_speed_{pct}pct";
        }
    }

    public IReadOnlyDictionary<string, string> ToOptions()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionOptionKeys.SpeedFactor]    = SpeedFactor.ToString("0.######", CultureInfo.InvariantCulture),
            [ActionOptionKeys.SpeedKeepPitch] = KeepPitch ? "true" : "false"
        };
    }

    public static bool ContainsAnyOption(IReadOnlyDictionary<string, string>? options)
    {
        return options is not null && options.ContainsKey(ActionOptionKeys.SpeedFactor);
    }

    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string>? options,
        out ChangeSpeedSettings? settings,
        out string? errorMessage)
    {
        settings = null;
        errorMessage = null;

        if (options is null || !options.TryGetValue(ActionOptionKeys.SpeedFactor, out var factorText) ||
            string.IsNullOrWhiteSpace(factorText))
        {
            errorMessage = MediaActionMessages.ChangeSpeedSettingsMissing();
            return false;
        }

        if (!double.TryParse(factorText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var factor) || factor <= 0)
        {
            errorMessage = MediaActionMessages.ChangeSpeedSettingsInvalid();
            return false;
        }

        if (factor < MinFactor || factor > MaxFactor)
        {
            errorMessage = MediaActionMessages.ChangeSpeedFactorOutOfRange();
            return false;
        }

        var keepPitch = true;
        if (options.TryGetValue(ActionOptionKeys.SpeedKeepPitch, out var keepText) &&
            !string.IsNullOrWhiteSpace(keepText))
        {
            keepPitch = !string.Equals(keepText.Trim(), "false", StringComparison.OrdinalIgnoreCase);
        }

        settings = new ChangeSpeedSettings(factor, keepPitch);
        return true;
    }
}
