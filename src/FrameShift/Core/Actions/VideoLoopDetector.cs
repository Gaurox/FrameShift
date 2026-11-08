using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using static FrameShift.Core.Actions.VideoLoopFrameReader;

namespace FrameShift.Core.Actions;

public sealed partial class VideoLoopDetector(FfmpegRunner runner)
{
    public Task<VideoLoopAnalysis> AnalyzeAsync(string executable, string source, MediaProbeResult probe,
        double? minimumSeconds, LoopSensitivity sensitivity, IProgress<VideoLoopProgress>? progress, CancellationToken token)
        => AnalyzeSearchAsync(executable, source, probe, minimumSeconds, sensitivity, progress, token);

    internal static VideoLoopCandidate? Refine(IReadOnlyList<Frame> left, IReadOnlyList<Frame> right,
        VideoLoopCandidate seed, double minimum, LoopSensitivity sensitivity, CancellationToken token)
        => RefineVariants(left, right, seed, minimum, sensitivity, token).FirstOrDefault();

    internal static IReadOnlyList<VideoLoopCandidate> RefineVariants(IReadOnlyList<Frame> left, IReadOnlyList<Frame> right,
        VideoLoopCandidate seed, double minimum, LoopSensitivity sensitivity, CancellationToken token)
    {
        var accepted = new List<VideoLoopCandidate>();
        var evaluator = new VideoLoopSeamEvaluator(left, right);
        foreach (var pair in evaluator.Shortlist(minimum, token))
            if (evaluator.Evaluate(pair.Start, pair.End - 1, right[pair.End].Time, sensitivity, token) is { } candidate)
                accepted.Add(candidate);
        return Rank(accepted).Take(3).ToArray();
    }

    private static byte[] Descriptor(byte[] pixels)
        => VideoLoopSeamEvaluator.Descriptor(new(0, pixels));

    private static int SpatialProjection(byte[] descriptor, int projection)
    {
        var sum = 0;
        for (var j = 0; j < descriptor.Length; j++)
        {
            // Repeated poses can have exactly the same global mean as every other
            // pose. Location-sensitive weights retain shape in the candidate index.
            var hash = unchecked((uint)(j + 1) * 2654435761u ^ (uint)(projection + 1) * 2246822519u);
            hash ^= hash >> 13;
            sum += descriptor[j] * ((int)(hash % 101) - 50);
        }
        return sum;
    }

    private static double Difference(byte[] a, byte[] b)
    {
        if (ReferenceEquals(a, b)) return 0;
        var sum = 0d; var worst = 0d;
        for (var i = 0; i < a.Length; i++) { var d = Math.Abs(a[i] - b[i]) / 255d; sum += d; worst = Math.Max(worst, d); }
        return sum / a.Length * 0.4 + worst * 0.6;
    }

    private static double MotionDifference(byte[] a0, byte[] a1, byte[] b0, byte[] b1)
    {
        if (ReferenceEquals(a0, a1) && ReferenceEquals(b0, b1)) return 0;
        var sum = 0d; var worst = 0d;
        for (var i = 0; i < a0.Length; i++)
        { var d = Math.Abs((a1[i] - a0[i]) - (b1[i] - b0[i])) / 510d; sum += d; worst = Math.Max(worst, d); }
        return sum / a0.Length * 0.4 + worst * 0.6;
    }

}
