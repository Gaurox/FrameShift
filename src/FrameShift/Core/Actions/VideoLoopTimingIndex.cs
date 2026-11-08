using System.Globalization;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;

namespace FrameShift.Core.Actions;

internal readonly record struct VideoLoopTimestamp(long Pts, long Duration, int Numerator, int Denominator)
{
    internal double Seconds => Pts * (double)Numerator / Denominator;
    internal double DurationSeconds => Duration * (double)Numerator / Denominator;
}

internal sealed record VideoLoopSourceStamp(string Path, long Length, long Written, long Created)
{
    internal static VideoLoopSourceStamp Read(string source)
    {
        var file = new FileInfo(source);
        if (!file.Exists) throw new FileNotFoundException("The video source is no longer available.", source);
        return new(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks, file.CreationTimeUtc.Ticks);
    }
}

internal sealed class VideoLoopSourceChangedException() : IOException("The source changed. Reopen Cut Video to refresh its metadata and selection.");
internal sealed record VideoLoopFramePosition(VideoLoopTimestamp Timestamp, double Time, double End, int? Number);
internal sealed record VideoLoopResolvedSelection(VideoLoopFramePosition Start, VideoLoopFramePosition Last)
{
    internal VideoLoopCandidate Bounds => new(Start.Time, Last.End, 0);
}

/// <summary>Lazy ordinal index on disk, plus eight small native-time windows. No whole-video frame list in RAM.</summary>
internal sealed class VideoLoopTimingIndex : IDisposable
{
    private const int RecordBytes = 24;
    internal const long MaximumIndexBytes = 256L * 1024 * 1024;
    private const int WindowFrames = 4096;
    private const double Epsilon = 0.0000001;
    private readonly FfmpegRunner _runner;
    private readonly string _executable;
    private readonly string _source;
    private readonly MediaProbeResult _probe;
    private readonly VideoLoopSourceStamp _stamp;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Window> _windows = [];
    private readonly string _index = Path.Combine(Path.GetTempPath(), $"frameshift_loop_timing_{Guid.NewGuid():N}.idx");
    private bool _disposed;
    private bool _changed;
    internal int IndexedFrameCount { get; private set; }
    internal bool IsComplete { get; private set; }
    internal int DecodePasses { get; private set; }
    internal long DecodedFrameCount { get; private set; }
    internal long IndexBytes => (long)IndexedFrameCount * RecordBytes;
    internal int CachedWindowFrames => _windows.Sum(w => w.Frames.Length);
    internal string IndexPath => _index;
    private sealed record Window(double Start, double End, VideoLoopTimestamp[] Frames, bool Truncated);

    internal VideoLoopTimingIndex(FfmpegRunner runner, string executable, string source, MediaProbeResult probe)
    {
        _runner = runner; _executable = executable; _source = source; _probe = probe;
        _stamp = VideoLoopSourceStamp.Read(source);
    }

    internal VideoLoopFramePosition? TryGetIndexedPosition(double time, bool before)
    {
        if (_disposed || _changed || IndexedFrameCount == 0 || !IsComplete && time > Relative(Read(IndexedFrameCount))) return null;
        var low = 1; var high = IndexedFrameCount + 1;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (middle <= IndexedFrameCount && Relative(Read(middle)) < time - Epsilon) low = middle + 1;
            else high = middle;
        }
        var number = before ? low - 1 : low;
        return number >= 1 && (number < IndexedFrameCount || number == IndexedFrameCount && IsComplete) ? Position(number) : null;
    }

    internal Task<VideoLoopResolvedSelection> ResolveFramesAsync(int start, int end, CancellationToken token, Action<string>? status = null) =>
        ExecuteAsync(async t =>
        {
            if (start < 1 || end < start) throw new ArgumentException("Video frame bounds must be ordered positive integers.");
            await EnsureOrdinalAsync(end, t, status).ConfigureAwait(false);
            if (IndexedFrameCount == 0) throw new InvalidOperationException("No decoded video frames are available.");
            return new VideoLoopResolvedSelection(Position(Math.Min(start, IndexedFrameCount)), Position(Math.Min(end, IndexedFrameCount)));
        }, token);

    internal Task<VideoLoopFramePosition> ResolveFrameAsync(int frame, CancellationToken token, Action<string>? status = null) =>
        ExecuteAsync(async t =>
        {
            if (frame < 1) throw new ArgumentException("A positive frame number is required.");
            await EnsureOrdinalAsync(frame, t, status).ConfigureAwait(false);
            if (IndexedFrameCount == 0) throw new InvalidOperationException("No decoded video frames are available.");
            return Position(Math.Min(frame, IndexedFrameCount));
        }, token);

    internal Task<VideoLoopFramePosition> ResolveTimeAsync(double seconds, bool beforeExclusiveEnd, CancellationToken token) =>
        ExecuteAsync(async t =>
        {
            var frame = await FindAsync(seconds, beforeExclusiveEnd, t).ConfigureAwait(false)
                ?? throw new InvalidOperationException("No video frame is available at this boundary.");
            return await PositionAsync(frame, t).ConfigureAwait(false);
        }, token);

    internal Task<VideoLoopFramePosition?> NeighborAsync(VideoLoopFramePosition current, int delta, CancellationToken token) =>
        ExecuteAsync(async t =>
        {
            if (delta is not (-1 or 1)) throw new ArgumentException("Native navigation moves one frame at a time.");
            var stamp = await FindAsync(current.Time + (delta > 0 ? 2 * Epsilon : 0), delta < 0, t).ConfigureAwait(false);
            return stamp is null ? null : await PositionAsync(stamp.Value, t).ConfigureAwait(false);
        }, token);

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            CheckSource();
            var result = await work(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            CheckSource();
            return result;
        }
        finally { if (_disposed) _windows.Clear(); _gate.Release(); }
    }

    private void CheckSource()
    {
        try { if (!_changed && VideoLoopSourceStamp.Read(_source) == _stamp) return; }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        _changed = true; _windows.Clear(); IndexedFrameCount = 0; IsComplete = false;
        ConversionActionHelper.DeleteIfExists(_index);
        throw new VideoLoopSourceChangedException();
    }

    private async Task EnsureOrdinalAsync(int frame, CancellationToken token, Action<string>? status)
    {
        if (frame < IndexedFrameCount || IsComplete) return;
        var wanted = Math.Min((long)frame + 129, MaximumIndexBytes / RecordBytes);
        if (wanted <= frame) throw new InvalidOperationException("The native frame index exceeds its 256 MiB budget. Use time bounds for this selection.");
        status?.Invoke("Indexing native frames for the first access; cancellation remains available...");
        var last = IndexedFrameCount > 0 ? Read(IndexedFrameCount) : (VideoLoopTimestamp?)null;
        var count = (int)(wanted - IndexedFrameCount);
        var chunk = _index + $".{Guid.NewGuid():N}.chunk";
        var received = 0;
        try
        {
            using (var stream = new FileStream(chunk, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new BinaryWriter(stream))
            {
                await DecodeAsync(last is null ? 0 : Math.Max(0, Relative(last.Value) - 0.1), null, count, last,
                    stamp => { Write(writer, stamp); received++; }, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested(); CheckSource();
            await using (var target = new FileStream(_index, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read | FileShare.Delete))
            await using (var input = File.OpenRead(chunk))
            {
                target.Position = target.Length;
                await input.CopyToAsync(target, token).ConfigureAwait(false);
            }
            IndexedFrameCount += received;
            IsComplete = received < count;
        }
        finally
        {
            // Roll back a canceled append, retaining only the previous committed prefix.
            if (_disposed || _changed) ConversionActionHelper.DeleteIfExists(_index);
            else if (File.Exists(_index))
            {
                using var file = new FileStream(_index, FileMode.Open, FileAccess.Write, FileShare.Read | FileShare.Delete);
                file.SetLength(IndexBytes);
            }
            ConversionActionHelper.DeleteIfExists(chunk);
        }
    }

    private async Task DecodeAsync(double seek, double? end, int count, VideoLoopTimestamp? after,
        Action<VideoLoopTimestamp> observe, CancellationToken token)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "info", "-progress", "pipe:1", "-nostats" };
        if (seek > 0) args.AddRange(["-ss", VideoLoopFrameReader.Number(Math.Max(0, _probe.VideoStartSeconds - _probe.FormatStartSeconds + seek - 0.1))]);
        args.AddRange(["-copyts", "-i", _source, "-map", "0:v:0", "-an", "-sn", "-dn"]);
        var filter = after is { } previous ? $"select='gt(pts,{previous.Pts})'," :
            end is not null ? $"trim=start={VideoLoopFrameReader.Number(_probe.VideoStartSeconds + seek)}:end={VideoLoopFrameReader.Number(_probe.VideoStartSeconds + end.Value)}," : "";
        if (after is not null && end is not null) filter += $"trim=end={VideoLoopFrameReader.Number(_probe.VideoStartSeconds + end.Value)},";
        filter += "scale=2:2,showinfo,setpts=PTS-STARTPTS";
        args.AddRange(["-vf", filter, "-frames:v", count.ToString(CultureInfo.InvariantCulture), "-fps_mode", "passthrough", "-f", "null", "-"]);
        VideoLoopTimestamp? last = after;
        var timing = new VideoLoopTimingCollector(count, stamp =>
        {
            if (last is { } p && stamp.Seconds <= p.Seconds) throw new InvalidOperationException("Native video timestamps are not strictly increasing.");
            last = stamp; observe(stamp); DecodedFrameCount++;
        }, retainTimings: false);
        DecodePasses++;
        var result = await _runner.RunAsync(_executable, args, null, null, _source, "Video native timing", "CPU", token, timing.Observe).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (result.Canceled) throw new OperationCanceledException(token);
        if (result.ExitCode != 0 || !result.ProcessTerminationConfirmed)
            throw new InvalidOperationException(ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, "Cannot read native frame timing."));
    }

    private async Task<VideoLoopTimestamp?> FindAsync(double time, bool before, CancellationToken token)
    {
        if (!double.IsFinite(time) || time < 0) return null;
        if (IndexedFrameCount > 0 && (IsComplete || time <= Relative(Read(IndexedFrameCount))))
        {
            var low = 1; var high = IndexedFrameCount + 1;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (middle <= IndexedFrameCount && Relative(Read(middle)) < time - Epsilon) low = middle + 1;
                else high = middle;
            }
            var found = before ? low - 1 : low;
            return found is >= 1 && found <= IndexedFrameCount ? Read(found) : null;
        }
        foreach (var cached in _windows.AsEnumerable().Reverse())
        {
            if (cached.Start > time || cached.End < time || cached.Truncated && Relative(cached.Frames[^1]) < time) continue;
            var hit = before ? cached.Frames.LastOrDefault(f => Relative(f) < time - Epsilon)
                : cached.Frames.FirstOrDefault(f => Relative(f) >= time - Epsilon);
            if (hit.Denominator > 0) return hit;
        }
        var radius = Math.Max(0.5, 3 / (_probe.VideoFrameRate ?? 1));
        var maximum = Math.Max(time + radius, _probe.Duration?.TotalSeconds ?? time + radius);
        for (;;)
        {
            token.ThrowIfCancellationRequested();
            var start = Math.Max(0, time - radius);
            var end = Math.Min(maximum, time + radius);
            var decoded = new List<VideoLoopTimestamp>();
            await DecodeAsync(start, end, WindowFrames, null, decoded.Add, token).ConfigureAwait(false);
            var window = new Window(start, end, decoded.ToArray(), decoded.Count == WindowFrames);
            _windows.Add(window);
            if (_windows.Count > 8) _windows.RemoveAt(0);
            while (window.Truncated && Relative(window.Frames[^1]) < time)
            {
                // Continue in bounded blocks if an unusually large gap required a wide seek.
                start = Relative(window.Frames[^1]);
                var previous = window.Frames[^1];
                var frames = new List<VideoLoopTimestamp> { previous };
                await DecodeAsync(start, end, WindowFrames - 1, previous, frames.Add, token).ConfigureAwait(false);
                _windows.Remove(window);
                window = new(start, end, frames.ToArray(), frames.Count == WindowFrames);
                _windows.Add(window);
            }
            var match = before ? window.Frames.LastOrDefault(f => Relative(f) < time - Epsilon)
                : window.Frames.FirstOrDefault(f => Relative(f) >= time - Epsilon);
            if (match.Denominator > 0) return match;
            if (before ? start <= 0 : end >= maximum) return null;
            radius = Math.Min(maximum + time + 1, radius * 2);
        }
    }

    private async Task<VideoLoopFramePosition> PositionAsync(VideoLoopTimestamp frame, CancellationToken token)
    {
        var time = Relative(frame);
        var next = await FindAsync(time + 2 * Epsilon, false, token).ConfigureAwait(false);
        return new(frame, time, next is { } n ? Relative(n) : time + LastDuration(frame), KnownOrdinal(frame));
    }

    private VideoLoopFramePosition Position(int number)
    {
        var frame = Read(number); var time = Relative(frame);
        return new(frame, time, number < IndexedFrameCount ? Relative(Read(number + 1)) : time + LastDuration(frame), number);
    }

    private int? KnownOrdinal(VideoLoopTimestamp stamp)
    {
        var low = 1; var high = IndexedFrameCount;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var frame = Read(middle);
            if (frame.Seconds == stamp.Seconds) return middle;
            if (frame.Seconds < stamp.Seconds) low = middle + 1; else high = middle - 1;
        }
        return null;
    }

    private double Relative(VideoLoopTimestamp stamp) => stamp.Seconds - _probe.VideoStartSeconds;
    private double LastDuration(VideoLoopTimestamp stamp)
    {
        var duration = stamp.DurationSeconds > 0 ? stamp.DurationSeconds : 1 / (_probe.VideoFrameRate ?? 1);
        var tick = (double)stamp.Numerator / stamp.Denominator;
        var time = Relative(stamp);
        var step = 1 / (_probe.VideoFrameRate ?? 1);
        if (!_probe.HasAudio && !_probe.HasSubtitles && Math.Abs(duration - step) <= tick + 0.000000001 &&
            Math.Abs(time - Math.Round(time / step) * step) <= tick + 0.000000001)
        {
            var alignedEnd = Math.Round((time + duration) / step) * step;
            if (alignedEnd > time && Math.Abs(alignedEnd - time - duration) <= tick + 0.000000001)
                duration = alignedEnd - time;
        }
        // Matroska's millisecond frame duration can truncate a fractional interval.
        // With no audio to extend the container, recover at most one native clock tick.
        var containerEnd = (_probe.Duration?.TotalSeconds ?? 0) - Math.Max(0, _probe.VideoStartSeconds - _probe.FormatStartSeconds);
        var missing = containerEnd - (Relative(stamp) + duration);
        if (!_probe.HasAudio && !_probe.HasSubtitles && missing > 0 && missing <= tick + 0.000000001)
            duration += missing;
        return duration;
    }
    private VideoLoopTimestamp Read(int number)
    {
        using var stream = new FileStream(_index, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Position = (long)(number - 1) * RecordBytes;
        using var reader = new BinaryReader(stream);
        return new(reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt32(), reader.ReadInt32());
    }
    private static void Write(BinaryWriter writer, VideoLoopTimestamp stamp)
    { writer.Write(stamp.Pts); writer.Write(stamp.Duration); writer.Write(stamp.Numerator); writer.Write(stamp.Denominator); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose();
        ConversionActionHelper.DeleteIfExists(_index);
        if (_gate.CurrentCount > 0) _windows.Clear();
    }
}
