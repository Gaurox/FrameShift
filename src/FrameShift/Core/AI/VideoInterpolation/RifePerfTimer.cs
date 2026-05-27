using System;
using System.Diagnostics;

namespace FrameShift.Core.AI.VideoInterpolation;

internal readonly ref struct RifePerfTimer
{
    private readonly Stopwatch _stopwatch;
    private readonly Action<TimeSpan> _commit;

    public RifePerfTimer(Action<TimeSpan> commit)
    {
        _stopwatch = Stopwatch.StartNew();
        _commit = commit;
    }

    public void Dispose()
    {
        _stopwatch.Stop();
        _commit(_stopwatch.Elapsed);
    }
}
