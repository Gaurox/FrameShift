using System.Text.RegularExpressions;

namespace FrameShift.Core.Actions;

// showinfo is parsed before the diagnostic tail is truncated. Keep only the number
// of timestamps requested by this decode, independently of other FFmpeg messages.
internal sealed class VideoLoopTimingCollector(int maximumFrames, Action<VideoLoopTimestamp>? observer = null, bool retainTimings = true)
{
    private string _timeBase = "";
    private readonly List<double> _times = [];
    private readonly List<(double Time, double Duration)> _timings = [];
    private int _observed;
    private int _numerator;
    private int _denominator;

    public void Observe(string line)
    {
        if (!line.Contains("showinfo", StringComparison.Ordinal)) return;
        var config = Regex.Match(line, @"config in time_base:\s*\d+/\d+");
        if (config.Success)
        {
            _timeBase = config.Value;
            var fraction = Regex.Match(config.Value, @"(\d+)/(\d+)");
            _numerator = int.Parse(fraction.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            _denominator = int.Parse(fraction.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        var times = VideoLoopFrameReader.ParseTimes(line);
        if (times.Count == 0) return;
        // A filter can inspect a frame beyond -frames:v. The decoded output uses only
        // the requested prefix, as it did before diagnostics were bounded.
        if (_observed >= maximumFrames) return;
        _observed++;
        if (observer is not null)
        {
            var native = Regex.Match(line, @"\bn:\s*\d+\s+pts:\s*(-?\d+)\s+pts_time:\s*\S+\s+duration:\s*(-?\d+)");
            if (!native.Success || _denominator <= 0) throw new InvalidOperationException("Native frame ticks are unavailable.");
            observer(new(long.Parse(native.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                long.Parse(native.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), _numerator, _denominator));
        }
        if (!retainTimings) return;
        var timings = VideoLoopFrameReader.ParseTimings(_timeBase + "\n" + line);
        _times.Add(timings.Count > 0 ? timings[0].Time : times[0]);
        if (timings.Count > 0) _timings.Add(timings[0]);
    }

    public IReadOnlyList<double> Times => _times;
    public IReadOnlyList<(double Time, double Duration)> Timings => _timings;
}
