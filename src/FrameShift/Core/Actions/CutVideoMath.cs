using System;
using System.Globalization;

namespace FrameShift.Core.Actions;

public static class CutVideoMath
{
    public static int EstimateFrameCount(TimeSpan? duration, double fps)
    {
        if (duration is null || duration.Value.TotalSeconds <= 0 || fps <= 0)
        {
            return 0;
        }

        return (int)Math.Round(duration.Value.TotalSeconds * fps);
    }

    public static int ConvertStartTimeToFrame(double seconds, double fps)
    {
        if (seconds < 0)
        {
            seconds = 0;
        }

        return 1 + (int)Math.Floor(seconds * fps);
    }

    public static int ConvertEndTimeToFrame(double seconds, double fps)
    {
        if (seconds < 0)
        {
            seconds = 0;
        }

        return (int)Math.Ceiling(seconds * fps);
    }

    public static string FormatPreciseTime(double seconds)
    {
        if (seconds < 0)
        {
            seconds = 0;
        }

        var hours = (int)Math.Floor(seconds / 3600d);
        var remaining = seconds - (hours * 3600d);
        var minutes = (int)Math.Floor(remaining / 60d);
        var secs = remaining - (minutes * 60d);
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:00.000}", hours, minutes, secs);
    }
}
