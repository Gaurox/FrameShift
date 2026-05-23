using System;
using System.Collections.Generic;
using System.Globalization;

namespace FrameShift.Core.Actions;

public sealed record CutVideoSettings(int StartFrame, int EndFrame, double Fps)
{
    public int StartFrameZeroBased => StartFrame - 1;

    public double StartTimeSeconds => StartFrameZeroBased / Fps;

    public double EndTimeSeconds => EndFrame / Fps;

    public double DurationSeconds => EndTimeSeconds - StartTimeSeconds;

    public IReadOnlyDictionary<string, string> ToOptions()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionOptionKeys.StartFrame] = StartFrame.ToString(CultureInfo.InvariantCulture),
            [ActionOptionKeys.EndFrame] = EndFrame.ToString(CultureInfo.InvariantCulture),
            [ActionOptionKeys.StartTime] = FormatSeconds(StartTimeSeconds),
            [ActionOptionKeys.EndTime] = FormatSeconds(EndTimeSeconds),
            [ActionOptionKeys.Duration] = FormatSeconds(DurationSeconds)
        };
    }

    public static bool ContainsAnyOption(IReadOnlyDictionary<string, string>? options)
    {
        if (options is null)
        {
            return false;
        }

        return options.ContainsKey(ActionOptionKeys.StartFrame) ||
               options.ContainsKey(ActionOptionKeys.EndFrame) ||
               options.ContainsKey(ActionOptionKeys.StartTime) ||
               options.ContainsKey(ActionOptionKeys.EndTime) ||
               options.ContainsKey(ActionOptionKeys.Duration);
    }

    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string>? options,
        double fps,
        int totalFrames,
        out CutVideoSettings? settings,
        out string? errorMessage)
    {
        settings = null;
        errorMessage = null;

        if (fps <= 0)
        {
            errorMessage = MediaActionMessages.VideoFrameRateUnavailable();
            return false;
        }

        if (totalFrames <= 0)
        {
            errorMessage = MediaActionMessages.VideoFrameCountUnavailable();
            return false;
        }

        if (options is null || options.Count == 0)
        {
            errorMessage = MediaActionMessages.CutVideoSettingsMissing();
            return false;
        }

        var hasStartFrame = TryGetOption(options, ActionOptionKeys.StartFrame, out var startFrameText);
        var hasEndFrame = TryGetOption(options, ActionOptionKeys.EndFrame, out var endFrameText);

        if (hasStartFrame || hasEndFrame)
        {
            if (!hasStartFrame || !hasEndFrame)
            {
                errorMessage = MediaActionMessages.CutVideoSettingsInvalid();
                return false;
            }

            if (!TryReadPositiveInt(startFrameText!, out var startFrame) ||
                !TryReadPositiveInt(endFrameText!, out var endFrame))
            {
                errorMessage = MediaActionMessages.CutVideoSettingsInvalid();
                return false;
            }

            startFrame = Math.Clamp(startFrame, 1, totalFrames);
            endFrame = Math.Clamp(endFrame, 1, totalFrames);

            if (startFrame > endFrame)
            {
                errorMessage = MediaActionMessages.CutVideoEndInvalid();
                return false;
            }

            settings = new CutVideoSettings(startFrame, endFrame, fps);
            return true;
        }

        var hasStartTime = TryGetOption(options, ActionOptionKeys.StartTime, out var startText);
        var hasEndTime = TryGetOption(options, ActionOptionKeys.EndTime, out var endText);
        var hasDuration = TryGetOption(options, ActionOptionKeys.Duration, out var durationText);

        if (!hasStartTime && !hasEndTime && !hasDuration)
        {
            errorMessage = MediaActionMessages.CutVideoSettingsMissing();
            return false;
        }

        if (!hasStartTime)
        {
            errorMessage = MediaActionMessages.CutVideoSettingsInvalid();
            return false;
        }

        if (!TryParseTimeText(startText!, out var startSeconds))
        {
            errorMessage = MediaActionMessages.CutVideoStartTimeInvalid();
            return false;
        }

        if (startSeconds < 0)
        {
            errorMessage = MediaActionMessages.CutVideoStartInvalid();
            return false;
        }

        double endSeconds;
        if (hasEndTime)
        {
            if (!TryParseTimeText(endText!, out endSeconds))
            {
                errorMessage = MediaActionMessages.CutVideoEndTimeInvalid();
                return false;
            }
        }
        else if (hasDuration)
        {
            if (!TryParseTimeText(durationText!, out var durationSeconds))
            {
                errorMessage = MediaActionMessages.CutVideoDurationInvalid();
                return false;
            }

            endSeconds = startSeconds + durationSeconds;
        }
        else
        {
            errorMessage = MediaActionMessages.CutVideoSettingsInvalid();
            return false;
        }

        if (endSeconds <= startSeconds)
        {
            errorMessage = MediaActionMessages.CutVideoEndInvalid();
            return false;
        }

        var startFrameFromTime = CutVideoMath.ConvertStartTimeToFrame(startSeconds, fps);
        var endFrameFromTime = CutVideoMath.ConvertEndTimeToFrame(endSeconds, fps);

        startFrameFromTime = Math.Clamp(startFrameFromTime, 1, totalFrames);
        endFrameFromTime = Math.Clamp(endFrameFromTime, 1, totalFrames);

        if (startFrameFromTime > endFrameFromTime)
        {
            errorMessage = MediaActionMessages.CutVideoEndInvalid();
            return false;
        }

        settings = new CutVideoSettings(startFrameFromTime, endFrameFromTime, fps);
        return true;
    }

    public static bool TryParseTimeText(string? text, out double seconds)
    {
        seconds = 0d;
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

    private static bool TryReadPositiveInt(string? text, out int value)
    {
        value = 0;
        return !string.IsNullOrWhiteSpace(text) &&
               int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
               value > 0;
    }

    private static string FormatSeconds(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
