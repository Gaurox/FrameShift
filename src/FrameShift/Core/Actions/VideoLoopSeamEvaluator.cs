using static FrameShift.Core.Actions.VideoLoopFrameReader;

namespace FrameShift.Core.Actions;

/// <summary>Bounded, time-aware comparison of the actual last-to-first transition.</summary>
internal sealed class VideoLoopSeamEvaluator
{
    internal const double MaximumContextSeconds = 0.35;
    internal const int MaximumVerifiedPairs = 24;
    private readonly IReadOnlyList<Frame> _left;
    private readonly IReadOnlyList<Frame> _right;
    private readonly byte[][] _dl;
    private readonly byte[][] _dr;
    private readonly Dictionary<(int, int), (double Pixels, double Lighting)> _spatial = [];

    internal VideoLoopSeamEvaluator(IReadOnlyList<Frame> left, IReadOnlyList<Frame> right)
    {
        _left = left; _right = right;
        _dl = left.Select(Descriptor).ToArray();
        _dr = right.Select(Descriptor).ToArray();
    }

    internal sealed record Pair(int Start, int End, double Error);

    internal IReadOnlyList<Pair> Shortlist(double minimum, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var pairs = new List<Pair>();
        for (var a = 1; a < _left.Count - 1; a++)
        {
            token.ThrowIfCancellationRequested();
            for (var b = 1; b < _right.Count - 1; b++)
            {
                if (_right[b].Time - _left[a].Time + 1e-7 < minimum) continue;
                // Include signed velocity and the real seam before pruning. Matching
                // poses with incoherent movement must not crowd out coherent pairs.
                var motion = VelocityDifference(_dl[a - 1], _dl[a + 1], _left[a + 1].Time - _left[a - 1].Time,
                    _dr[b - 1], _dr[b + 1], _right[b + 1].Time - _right[b - 1].Time);
                var seam = VelocityDifference(_dr[b - 1], _dl[a], _right[b].Time - _right[b - 1].Time,
                    _dl[a], _dl[a + 1], _left[a + 1].Time - _left[a].Time);
                pairs.Add(new(a, b, Difference(_dl[a], _dr[b]) + motion * 0.75 + seam * 0.5));
            }
        }
        return pairs.OrderBy(p => p.Error).ThenBy(p => _right[p.End].Time - _left[p.Start].Time)
            .ThenBy(p => p.Start).ThenBy(p => p.End).Take(MaximumVerifiedPairs).ToArray();
    }

    internal sealed record BoundaryPair(int Start, int Last, double End, double Error);

    internal IReadOnlyList<BoundaryPair> BoundaryShortlist(double minimum, double videoEnd, bool includesLast, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var pairs = new List<BoundaryPair>();
        for (var a = 0; a < _left.Count - 1; a++)
        {
            token.ThrowIfCancellationRequested();
            for (var b = 1; b < _right.Count; b++)
            {
                if (b + 1 == _right.Count && !includesLast && _right[b].Duration <= 0) continue;
                var end = b + 1 < _right.Count ? _right[b + 1].Time : includesLast ? videoEnd : Math.Min(videoEnd, _right[b].Time + _right[b].Duration);
                if (end - _left[a].Time + 1e-7 < minimum) continue;
                var seam = VelocityDifference(_dr[b], _dl[a], end - _right[b].Time,
                    _dl[a], _dl[a + 1], _left[a + 1].Time - _left[a].Time);
                var appearance = b + 1 < _right.Count ? Difference(_dl[a], _dr[b + 1]) : 0;
                pairs.Add(new(a, b, end, appearance + seam));
            }
        }
        return pairs.OrderBy(p => p.Error).ThenBy(p => p.End - _left[p.Start].Time).ThenBy(p => p.Start)
            .Take(MaximumVerifiedPairs).ToArray();
    }

    internal VideoLoopCandidate? Evaluate(int a, int last, double end, LoopSensitivity sensitivity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (a < 0 || a + 1 >= _left.Count || last < 1 || last >= _right.Count || end <= _left[a].Time) return null;
        var next = last + 1 < _right.Count && Math.Abs(_right[last + 1].Time - end) < 1e-7 ? last + 1 : -1;
        var outgoingDt = _left[a + 1].Time - _left[a].Time;
        var incomingDt = _right[last].Time - _right[last - 1].Time;
        var seamDt = end - _right[last].Time;
        if (outgoingDt <= 0 || incomingDt <= 0 || seamDt <= 0) return null;

        var outgoing = Spatial(_left[a], _left[a + 1]).Pixels;
        var incoming = Spatial(_right[last - 1], _right[last]).Pixels;
        var activity = Math.Max(outgoing / outgoingDt, incoming / incomingDt);
        var context = Math.Clamp(0.025 / Math.Max(activity, 0.001), 0.12, MaximumContextSeconds);
        var appearance = 0d; var lighting = 0d; var compared = 0;
        var visited = new HashSet<(int, int)>();
        // Offsets are seconds, not frame counts. At most five context comparisons
        // are made, regardless of FPS; sparse VFR intervals are never interpolated.
        foreach (var offset in new[] { -context, -context / 2, 0, context / 2, context })
        {
            token.ThrowIfCancellationRequested();
            var l = Nearest(_left, _left[a].Time + offset);
            var r = Nearest(_right, end + offset);
            if (l < 0 || r < 0) continue;
            var alignment = Math.Abs((_left[l].Time - _left[a].Time) - (_right[r].Time - end));
            if (alignment > Math.Max(1e-6, Math.Min(outgoingDt, incomingDt) * 0.55) || !visited.Add((l, r))) continue;
            if (!_spatial.TryGetValue((l, r), out var spatial))
                _spatial[(l, r)] = spatial = Spatial(_left[l], _right[r]);
            appearance = Math.Max(appearance, spatial.Pixels);
            lighting = Math.Max(lighting, spatial.Lighting);
            compared++;
        }

        var seamPixels = Spatial(_right[last], _left[a]);
        var seamMotion = PixelVelocity(_right[last], _left[a], seamDt, _left[a], _left[a + 1], outgoingDt);
        seamMotion = Math.Max(seamMotion,
            PixelVelocity(_right[last - 1], _right[last], incomingDt, _right[last], _left[a], seamDt));
        var normalAcceleration = 0d;
        if (a + 2 < _left.Count)
            normalAcceleration = PixelVelocity(_left[a], _left[a + 1], outgoingDt,
                _left[a + 1], _left[a + 2], _left[a + 2].Time - _left[a + 1].Time);
        if (last >= 2)
            normalAcceleration = Math.Max(normalAcceleration, PixelVelocity(_right[last - 2], _right[last - 1],
                _right[last - 1].Time - _right[last - 2].Time, _right[last - 1], _right[last], incomingDt));
        var continuity = Math.Max(0, seamMotion - normalAcceleration * 1.3);
        var scene = Math.Max(0, seamPixels.Pixels - Math.Max(outgoing, incoming) * 1.7);
        // At EOF there is no hypothetical next image. Judge the observed transition
        // against native incoming/outgoing velocities instead of inventing a frame.
        if (next < 0)
        {
            appearance = Math.Max(appearance, Math.Max(0, seamPixels.Pixels - (outgoing + incoming) * 0.65));
            lighting = Math.Max(lighting, Math.Max(0, seamPixels.Lighting - (outgoing + incoming) * 0.65));
        }
        var motion = next >= 0 && next + 1 < _right.Count
            ? PixelVelocity(_left[a], _left[a + 1], outgoingDt, _right[next], _right[next + 1], _right[next + 1].Time - _right[next].Time)
            : 0;
        // Without exterior context, chaotic motion is uncertainty, not evidence of
        // a perfect seam. Its own acceleration must not cancel every seam defect.
        if (next < 0) motion = Math.Max(motion, normalAcceleration * 0.5);
        var isStatic = activity < 0.001 && appearance < 0.002 && seamPixels.Pixels < 0.002;
        var metrics = new VideoLoopSeamMetrics(appearance, lighting, motion, continuity, scene, isStatic, context, compared);
        var threshold = sensitivity switch { LoopSensitivity.Strict => 0.025, LoopSensitivity.Normal => 0.055, _ => 0.095 };
        if (!double.IsFinite(metrics.Error) || metrics.Error > threshold) return null;
        return new(_left[a].Time, end, 100 / (1 + 20 * metrics.Error)) { Seam = metrics };
    }

    internal static byte[] Descriptor(Frame frame)
    {
        Validate(frame);
        var descriptor = new byte[8 * 6 * 3];
        for (var y = 0; y < 6; y++)
        for (var x = 0; x < 8; x++)
        {
            var x0 = Math.Min(frame.PixelWidth - 1, x * frame.PixelWidth / 8);
            var x1 = Math.Min(frame.PixelWidth, Math.Max(x0 + 1, (x + 1) * frame.PixelWidth / 8));
            var y0 = Math.Min(frame.PixelHeight - 1, y * frame.PixelHeight / 6);
            var y1 = Math.Min(frame.PixelHeight, Math.Max(y0 + 1, (y + 1) * frame.PixelHeight / 6));
            for (var c = 0; c < 3; c++)
            {
                var sum = 0;
                for (var py = y0; py < y1; py++)
                for (var px = x0; px < x1; px++) sum += frame.Pixels[(py * frame.PixelWidth + px) * 3 + c];
                descriptor[(y * 8 + x) * 3 + c] = (byte)(sum / ((x1 - x0) * (y1 - y0)));
            }
        }
        return descriptor;
    }

    private static (double Pixels, double Lighting) Spatial(Frame a, Frame b)
    {
        RequireSameSize(a, b);
        var sum = 0d; var worst = 0d; var lighting = 0d; var count = 0;
        for (var y = 0; y < a.PixelHeight; y += 8)
        for (var x = 0; x < a.PixelWidth; x += 8)
        {
            var tile = 0d; var light = 0d; var pixels = 0;
            for (var py = y; py < Math.Min(y + 8, a.PixelHeight); py++)
            for (var px = x; px < Math.Min(x + 8, a.PixelWidth); px++)
            for (var c = 0; c < 3; c++)
            {
                var i = (py * a.PixelWidth + px) * 3 + c;
                var delta = (a.Pixels[i] - b.Pixels[i]) / 255d;
                tile += Math.Abs(delta); light += delta; pixels++;
            }
            tile /= pixels; light = Math.Abs(light / pixels);
            sum += tile; worst = Math.Max(worst, tile); lighting = Math.Max(lighting, light); count++;
        }
        return (sum / count * 0.3 + worst * 0.7, lighting);
    }

    private static double PixelVelocity(Frame a0, Frame a1, double at, Frame b0, Frame b1, double bt)
    {
        RequireSameSize(a0, a1); RequireSameSize(a0, b0); RequireSameSize(a0, b1);
        if (at <= 0 || bt <= 0) return double.PositiveInfinity;
        var sum = 0d; var worst = 0d; var count = 0;
        for (var y = 0; y < a0.PixelHeight; y += 8)
        for (var x = 0; x < a0.PixelWidth; x += 8)
        {
            var tile = 0d; var pixels = 0;
            for (var py = y; py < Math.Min(y + 8, a0.PixelHeight); py++)
            for (var px = x; px < Math.Min(x + 8, a0.PixelWidth); px++)
            for (var c = 0; c < 3; c++)
            {
                var i = (py * a0.PixelWidth + px) * 3 + c;
                tile += Math.Abs((a1.Pixels[i] - a0.Pixels[i]) * 0.1 / at - (b1.Pixels[i] - b0.Pixels[i]) * 0.1 / bt) / 510;
                pixels++;
            }
            tile /= pixels; sum += tile; worst = Math.Max(worst, tile); count++;
        }
        return sum / count * 0.3 + worst * 0.7;
    }

    private static double Difference(byte[] a, byte[] b)
    {
        var sum = 0d; var worst = 0d;
        for (var i = 0; i < a.Length; i++) { var d = Math.Abs(a[i] - b[i]) / 255d; sum += d; worst = Math.Max(worst, d); }
        return sum / a.Length * 0.4 + worst * 0.6;
    }

    private static double VelocityDifference(byte[] a0, byte[] a1, double at, byte[] b0, byte[] b1, double bt)
    {
        if (at <= 0 || bt <= 0) return double.PositiveInfinity;
        var sum = 0d; var worst = 0d;
        for (var i = 0; i < a0.Length; i++)
        {
            var d = Math.Abs((a1[i] - a0[i]) * 0.1 / at - (b1[i] - b0[i]) * 0.1 / bt) / 510;
            sum += d; worst = Math.Max(worst, d);
        }
        return sum / a0.Length * 0.4 + worst * 0.6;
    }

    private static int Nearest(IReadOnlyList<Frame> frames, double time)
    {
        if (frames.Count == 0 || time < frames[0].Time - 1e-7 || time > frames[^1].Time + 1e-7) return -1;
        var low = 0; var high = frames.Count - 1;
        while (low < high) { var mid = (low + high) / 2; if (frames[mid].Time < time) low = mid + 1; else high = mid; }
        return low > 0 && time - frames[low - 1].Time <= frames[low].Time - time ? low - 1 : low;
    }

    private static void Validate(Frame frame)
    {
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || frame.Pixels.Length != (long)frame.PixelWidth * frame.PixelHeight * 3)
            throw new ArgumentException("Loop frame dimensions do not match its RGB pixels.");
    }

    private static void RequireSameSize(Frame a, Frame b)
    {
        Validate(a); Validate(b);
        if (a.PixelWidth != b.PixelWidth || a.PixelHeight != b.PixelHeight)
            throw new ArgumentException("Loop frames must use the same comparison dimensions.");
    }
}
