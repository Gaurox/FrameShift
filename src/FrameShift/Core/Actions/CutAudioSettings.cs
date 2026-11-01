using System;
using System.Collections.Generic;
using System.Globalization;

namespace FrameShift.Core.Actions;

public sealed record CutAudioSettings(double StartSeconds, double EndSeconds)
{
    public double DurationSeconds => EndSeconds - StartSeconds;

    public IReadOnlyDictionary<string, string> ToOptions()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionOptionKeys.StartTime] = FormatSeconds(StartSeconds),
            [ActionOptionKeys.EndTime] = FormatSeconds(EndSeconds),
            [ActionOptionKeys.Duration] = FormatSeconds(DurationSeconds)
        };
    }

    public static bool ContainsAnyOption(IReadOnlyDictionary<string, string>? options)
    {
        if (options is null)
        {
            return false;
        }

        return options.ContainsKey(ActionOptionKeys.StartTime) ||
               options.ContainsKey(ActionOptionKeys.EndTime) ||
               options.ContainsKey(ActionOptionKeys.Duration);
    }

    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string>? options,
        out CutAudioSettings? settings,
        out string? errorMessage)
    {
        settings = null;
        errorMessage = null;

        if (options is null || options.Count == 0)
        {
            errorMessage = MediaActionMessages.CutAudioSettingsMissing();
            return false;
        }

        var hasStart = TryGetOption(options, ActionOptionKeys.StartTime, out var startText);
        var hasEnd = TryGetOption(options, ActionOptionKeys.EndTime, out var endText);
        var hasDuration = TryGetOption(options, ActionOptionKeys.Duration, out var durationText);

        if (!hasStart && !hasEnd && !hasDuration)
        {
            errorMessage = MediaActionMessages.CutAudioSettingsMissing();
            return false;
        }

        var startSeconds = 0d;
        if (hasStart && !TryParseTimeText(startText!, out startSeconds))
        {
            errorMessage = L.Get("CutAudioSettings_Start_time_must_use_seconds_MM_SS_or_709d843a");
            return false;
        }

        if (startSeconds < 0)
        {
            errorMessage = MediaActionMessages.CutAudioStartInvalid();
            return false;
        }

        double endSeconds;
        if (hasEnd)
        {
            if (!TryParseTimeText(endText!, out endSeconds))
            {
                errorMessage = L.Get("CutAudioSettings_End_time_must_use_seconds_MM_SS_or_H_cd2f2978");
                return false;
            }
        }
        else if (hasDuration)
        {
            if (!TryParseTimeText(durationText!, out var durationSeconds))
            {
                errorMessage = L.Get("CutAudioSettings_Duration_must_use_seconds_MM_SS_or_H_5bc455e6");
                return false;
            }

            endSeconds = startSeconds + durationSeconds;
        }
        else
        {
            errorMessage = L.Get("CutAudioSettings_Provide_an_end_time_or_a_duration_fo_198d76d3");
            return false;
        }

        if (endSeconds <= startSeconds)
        {
            errorMessage = MediaActionMessages.CutAudioEndInvalid();
            return false;
        }

        settings = new CutAudioSettings(startSeconds, endSeconds);
        return true;
    }

    public static bool TryParseTimeText(string? text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
        {
            return seconds >= 0;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out seconds))
        {
            return seconds >= 0;
        }

        var parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        if (!double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var secondPart) &&
            !double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.CurrentCulture, out secondPart))
        {
            return false;
        }

        if (!int.TryParse(parts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutePart))
        {
            return false;
        }

        var hourPart = 0;
        if (parts.Length == 3 &&
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hourPart))
        {
            return false;
        }

        seconds = (hourPart * 3600d) + (minutePart * 60d) + secondPart;
        return seconds >= 0;
    }

    public static string FormatDisplayTime(double seconds)
    {
        if (seconds < 0)
        {
            seconds = 0;
        }

        var hours = (int)Math.Floor(seconds / 3600d);
        var remaining = seconds - (hours * 3600d);
        var minutes = (int)Math.Floor(remaining / 60d);
        var secondPart = remaining - (minutes * 60d);
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:00.###}", hours, minutes, secondPart);
    }

    private static string FormatSeconds(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static bool TryGetOption(
        IReadOnlyDictionary<string, string> options,
        string key,
        out string? value)
    {
        value = null;
        foreach (var option in options)
        {
            if (!string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(option.Value))
            {
                return false;
            }

            value = option.Value;
            return true;
        }

        return false;
    }
}
