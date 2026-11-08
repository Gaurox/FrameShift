using System.Diagnostics;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Logging;
using static FrameShift.Core.Actions.VideoLoopFrameReader;

namespace FrameShift.Core.Actions;

public sealed partial class VideoLoopDetector
{
    internal const int SamplingBlockFrames = 256;
    internal const int MaximumSamples = 57600;
    internal const int MaximumSeedTracks = 192;
    internal const int MaximumResults = 512;
    internal const int MaximumRefinementFrames = 384;

    internal static (int Width, int Height) RefinementSize(MediaProbeResult probe)
    {
        var sar = probe.VideoSampleAspectRatio;
        if (!double.IsFinite(sar) || sar <= 0 || probe.VideoWidth <= 0 || probe.VideoHeight <= 0)
            throw new ArgumentException("Valid video dimensions and pixel aspect ratio are required.");
        var width = probe.DisplayVideoWidth * (probe.RequiresDisplayDimensionSwap ? 1 : sar);
        var height = probe.DisplayVideoHeight * (probe.RequiresDisplayDimensionSwap ? sar : 1);
        var scale = Math.Min(1, 192 / Math.Max(width, height));
        return (Math.Max(8, (int)Math.Round(width * scale)), Math.Max(8, (int)Math.Round(height * scale)));
    }

    internal static double AutoMinimum(double duration, double fps)
        => Math.Min(1, Math.Max(0.1, Math.Min(duration, Math.Max(duration * 0.5, 3 / Math.Max(1, fps)))));
    internal sealed record SearchSample(double Time, byte[] Features);
    internal sealed record SamplingBlock(double Start, double Duration);
    internal sealed record SamplingPlan(double Rate, IReadOnlyList<SamplingBlock> Blocks, bool Complete);
    internal sealed record SeedSearch(IReadOnlyList<VideoLoopCandidate> Candidates, bool Limited, long ComparedPairs, int Regions);

    internal static SamplingPlan PlanSampling(double duration, double fps)
    {
        var rate = Math.Min(8, fps);
        var blockSeconds = SamplingBlockFrames / rate;
        var count = (int)Math.Min((MaximumSamples + SamplingBlockFrames - 1) / SamplingBlockFrames, Math.Ceiling(duration / blockSeconds));
        var complete = duration * rate <= MaximumSamples;
        var blocks = Enumerable.Range(0, count).Select(i =>
        {
            var start = complete ? i * blockSeconds : i * (duration - blockSeconds) / Math.Max(1, count - 1);
            return new SamplingBlock(start, Math.Min(blockSeconds, duration - start));
        }).ToArray();
        return new(rate, Array.AsReadOnly(blocks), complete);
    }

    private async Task<VideoLoopAnalysis> AnalyzeSearchAsync(string executable, string source, MediaProbeResult probe,
        double? requestedMinimum, LoopSensitivity sensitivity, IProgress<VideoLoopProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(source)) throw new FileNotFoundException("Video not found.", source);
        var sourceStamp = VideoLoopSourceStamp.Read(source);
        var duration = probe.Duration?.TotalSeconds ?? 0;
        var fps = probe.VideoFrameRate ?? 0;
        if (!probe.HasVideo || !double.IsFinite(fps) || fps <= 0 || !double.IsFinite(duration) || duration <= 0)
            throw new ArgumentException("A readable video with a known duration and frame rate is required.");
        if (fps > 240) throw new ArgumentException("Find Loop currently supports videos up to 240 FPS.");
        if (requestedMinimum is double value && (!double.IsFinite(value) || value < 0.1 || value > 600) || !Enum.IsDefined(sensitivity))
            throw new ArgumentException("Minimum loop duration must be between 0.1 and 600 seconds.");
        var minimum = requestedMinimum ?? AutoMinimum(duration, fps);
        var color = VideoLoopColorPolicy.Evaluate(probe);
        await color.RequireAvailableAsync(runner, executable, token).ConfigureAwait(false);
        if (duration + 0.0000001 < minimum) return new([], 0);
        var endpointPhase = Stopwatch.StartNew();
        progress?.Report(new(1, "Reading native video endpoints..."));
        using var timing = new VideoLoopTimingIndex(runner, executable, source, probe);
        var last = await timing.ResolveTimeAsync(duration, true, token).ConfigureAwait(false);
        duration = last.End;
        if (requestedMinimum is null) minimum = AutoMinimum(duration, fps);
        var endpointElapsed = endpointPhase.Elapsed;
        if (duration + 0.0000001 < minimum) return new([], 0);
        var plan = PlanSampling(duration, fps);
        var reader = new VideoLoopFrameReader(runner, executable, source,
            probe.VideoStartSeconds - probe.FormatStartSeconds, probe.VideoStartSeconds, colorPolicy: color);
        var compact = new List<SearchSample>();
        var maxResident = 0;
        var phase = Stopwatch.StartNew();
        progress?.Report(new(2, $"Fast pass: {plan.Rate:0.###} FPS in bounded blocks across the source..."));
        for (var blockIndex = 0; blockIndex < plan.Blocks.Count; blockIndex++)
        {
            token.ThrowIfCancellationRequested();
            var block = plan.Blocks[blockIndex];
            var pixels = await reader.ReadAsync(block.Start, block.Duration, plan.Rate, SamplingBlockFrames, token).ConfigureAwait(false);
            maxResident = Math.Max(maxResident, pixels.Count);
            foreach (var frame in pixels)
                if (compact.Count == 0 || frame.Time > compact[^1].Time)
                {
                    var descriptor = Descriptor(frame.Pixels);
                    if (compact.Count > 0 && descriptor.AsSpan().SequenceEqual(compact[^1].Features)) descriptor = compact[^1].Features;
                    compact.Add(new(frame.Time, descriptor));
                }
            pixels.Clear(); // RGB frames are not retained by the source-wide signature index.
            CheckSource();
            progress?.Report(new(2 + (blockIndex + 1) * 26 / plan.Blocks.Count,
                $"Fast pass: block {blockIndex + 1}/{plan.Blocks.Count}; {compact.Count} compact signatures."));
        }
        var samplingElapsed = phase.Elapsed;
        progress?.Report(new(30, "Fast pass: distributing spatial matches across regions and duration families..."));
        phase.Restart();
        var seedSearch = await Task.Run(() => SelectSeeds(compact, duration, minimum, plan.Rate, sensitivity, token), token).ConfigureAwait(false);
        var seedElapsed = phase.Elapsed;
        var limits = plan.Rate < fps ? VideoLoopSearchLimits.Sampling : VideoLoopSearchLimits.None;
        if (!plan.Complete) limits |= VideoLoopSearchLimits.TemporalCoverage;
        if (seedSearch.Limited) limits |= VideoLoopSearchLimits.SeedSelection;
        var candidates = new List<VideoLoopCandidate>();
        var refinedFrames = 0;
        var maxRefinementResident = 0;
        var size = RefinementSize(probe);
        var preciseReader = new VideoLoopFrameReader(runner, executable, source,
            probe.VideoStartSeconds - probe.FormatStartSeconds, probe.VideoStartSeconds, size.Width, size.Height, color);
        phase.Restart();
        var videoEnd = last.End;
        var radius = Math.Max(3 / fps, Math.Min(2, Math.Max(0.2, 1 / plan.Rate))) + VideoLoopSeamEvaluator.MaximumContextSeconds;
        // The whole-source seam has no frames outside the file; verify it explicitly.
        var firstFrames = await preciseReader.ReadAsync(0, 2 * radius + 0.15, null, MaximumRefinementFrames, token).ConfigureAwait(false);
        var lastFrames = await preciseReader.ReadAsync(Math.Max(0, last.Time - radius), videoEnd - Math.Max(0, last.Time - radius), null, MaximumRefinementFrames, token).ConfigureAwait(false);
        refinedFrames += firstFrames.Count + lastFrames.Count;
        maxRefinementResident = firstFrames.Count + lastFrames.Count;
        if (firstFrames.Count >= 2 && lastFrames.Count >= 2 && videoEnd - firstFrames[0].Time >= minimum &&
            BoundaryCandidate(firstFrames, 0, lastFrames, lastFrames.Count - 1, videoEnd, sensitivity) is { } whole)
            candidates.Add(whole);
        else if (firstFrames.Count == 1 && lastFrames.Count == 1 && firstFrames[0].Time == lastFrames[0].Time && videoEnd - firstFrames[0].Time >= minimum)
            candidates.Add(new(firstFrames[0].Time, videoEnd, 100) { Seam = new(0, 0, 0, 0, 0, true, 0, 1) });
        firstFrames.Clear(); lastFrames.Clear();
        ReportBatch(33, "Source endpoints checked.", 0);
        for (var index = 0; index < seedSearch.Candidates.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var seed = seedSearch.Candidates[index];
            if (seed.EndSeconds > videoEnd + 0.0000001) continue;
            var leftSeek = Math.Max(0, seed.StartSeconds - radius);
            var rightSeek = Math.Max(0, seed.EndSeconds - radius);
            var left = await preciseReader.ReadAsync(leftSeek, 2 * radius + 0.15, null, MaximumRefinementFrames, token).ConfigureAwait(false);
            var rightDuration = Math.Min(2 * radius + 0.15, videoEnd - rightSeek);
            var right = await preciseReader.ReadAsync(rightSeek, rightDuration, null, MaximumRefinementFrames, token).ConfigureAwait(false);
            refinedFrames += left.Count + right.Count;
            maxRefinementResident = Math.Max(maxRefinementResident, left.Count + right.Count);
            candidates.AddRange(await Task.Run(() => RefineVariants(left, right, seed, minimum, sensitivity, token), token).ConfigureAwait(false));
            if (seed.StartSeconds < radius || seed.EndSeconds > videoEnd - radius)
            {
                var includesLastFrame = right.Count > 0 && Math.Abs(right[^1].Time - last.Time) < 0.0000001;
                var boundaries = BoundaryVariants(left, right, minimum, videoEnd, includesLastFrame, sensitivity, token);
                candidates.AddRange(boundaries);
            }
            left.Clear(); right.Clear();
            var percent = 35 + (index + 1) * 60 / Math.Max(1, seedSearch.Candidates.Count);
            if ((index + 1) % 8 == 0 || index + 1 == seedSearch.Candidates.Count)
                ReportBatch(percent, $"Precise pass: {index + 1}/{seedSearch.Candidates.Count} distributed tracks checked.", index + 1);
            else progress?.Report(new(percent, $"Precise pass: checking track {index + 1}/{seedSearch.Candidates.Count}..."));
        }
        token.ThrowIfCancellationRequested();
        CheckSource();
        var final = Snapshot(seedSearch.Candidates.Count, true);
        AppLogger.LogStatic($"Find Loop: {size.Width}x{size.Height} refinement, {final.Candidates.Count} candidates, limits={final.Limits}.");
        foreach (var candidate in final.Candidates.Take(12))
            AppLogger.LogStatic($"Find Loop candidate {candidate.StartSeconds:R}..{candidate.EndSeconds:R}: {candidate.JunctionAssessment}; {candidate.Seam}");
        progress?.Report(new(100, $"Analysis complete: {final.Candidates.Count} candidate(s); coverage and budgets are reported.") { Results = final });
        return final;

        VideoLoopAnalysis Snapshot(int examined, bool completed)
        {
            var ranked = Rank(candidates);
            var resultLimits = limits | (ranked.Count > MaximumResults ? VideoLoopSearchLimits.CandidateList : VideoLoopSearchLimits.None);
            var kept = Array.AsReadOnly(ranked.Take(MaximumResults).ToArray());
            return new(kept, plan.Rate)
            {
                SearchCompleted = completed,
                Limits = resultLimits,
                Diagnostics = new()
                {
                    SampledFrames = compact.Count, RefinedFrames = refinedFrames, ExaminedSeeds = examined,
                    AcceptedCandidates = candidates.Count, PeakRawTemporaryBytes = Math.Max(reader.PeakTemporaryBytes, preciseReader.PeakTemporaryBytes),
                    SamplingElapsed = samplingElapsed, SeedSelectionElapsed = seedElapsed, RefinementElapsed = phase.Elapsed,
                    SamplingBlocks = plan.Blocks.Count, MaximumResidentSampleFrames = maxResident,
                    RetainedSignatureBytes = compact.Sum(s => (long)s.Features.Length + sizeof(double)),
                    ComparedSamplePairs = seedSearch.ComparedPairs, TemporalRegions = seedSearch.Regions,
                    CoveredSeconds = plan.Blocks.Sum(b => b.Duration), FullTemporalCoverage = plan.Complete,
                    EndpointElapsed = endpointElapsed, TimingFrames = timing.DecodedFrameCount,
                    RefinementWidth = size.Width, RefinementHeight = size.Height,
                    MaximumResidentRefinementFrames = maxRefinementResident
                }
            };
        }
        void ReportBatch(int percent, string message, int examined)
        {
            CheckSource();
            progress?.Report(new(percent, message) { Results = Snapshot(examined, false) });
        }
        void CheckSource()
        {
            try { if (VideoLoopSourceStamp.Read(source) == sourceStamp) return; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw new VideoLoopSourceChangedException();
        }
    }

    internal static List<VideoLoopCandidate> FindSeeds(IReadOnlyList<Frame> frames, double minimum, CancellationToken token)
        => SelectSeeds(frames.Select(f => new SearchSample(f.Time, Descriptor(f.Pixels))).ToArray(),
            frames.Count == 0 ? 0 : frames[^1].Time + 0.001, minimum, 8, LoopSensitivity.Normal, token).Candidates.ToList();

    internal static SeedSearch SelectSeeds(IReadOnlyList<SearchSample> samples, double duration, double minimum,
        double rate, LoopSensitivity sensitivity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var regions = Math.Clamp((int)Math.Ceiling(duration / 3), 1, 24);
        var buckets = new Dictionary<(int Region, int Family), List<VideoLoopCandidate>>();
        var bucketLimited = false;
        long compared = 0;
        var appearance = sensitivity switch { LoopSensitivity.Strict => 0.075, LoopSensitivity.Normal => 0.12, _ => 0.18 };
        var combined = sensitivity switch { LoopSensitivity.Strict => 0.11, LoopSensitivity.Normal => 0.16, _ => 0.22 };
        for (var i = 0; i < samples.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            for (var length = minimum; length < duration; length *= 2)
            {
                var b = LowerBound(samples, samples[i].Time + length);
                for (var neighbor = Math.Max(i + 1, b - 1); neighbor <= Math.Min(samples.Count - 1, b + 1); neighbor++) Consider(i, neighbor);
            }
        }
        for (var projection = 0; projection < 3; projection++)
        {
            var p = projection;
            var values = samples.Select(s => SpatialProjection(s.Features, p)).ToArray();
            var sorted = Enumerable.Range(0, samples.Count).OrderBy(i => values[i]).ThenBy(i => i).ToArray();
            var groupStart = new int[sorted.Length]; var groupEnd = new int[sorted.Length];
            for (var start = 0; start < sorted.Length;)
            {
                var end = start + 1;
                while (end < sorted.Length && values[sorted[end]] == values[sorted[start]]) end++;
                for (var n = start; n < end; n++) { groupStart[n] = start; groupEnd[n] = end; }
                start = end;
            }
            for (var n = 0; n < sorted.Length; n++)
            {
                token.ThrowIfCancellationRequested();
                for (var m = n + 1; m < Math.Min(sorted.Length, n + 9); m++) Consider(sorted[n], sorted[m]);
                // Equal signatures are also visited across time, not just at adjacent ranks.
                for (var q = 0; q < 8; q++)
                    Consider(sorted[n], sorted[groupStart[n] + q * (groupEnd[n] - groupStart[n] - 1) / 7]);
            }
        }
        var families = buckets.Keys.Select(k => k.Family).Distinct().OrderBy(i => i).ToArray();
        var spreadFamilies = Enumerable.Range(0, families.Length).Select(i => families[i % 2 == 0 ? i / 2 : families.Length - 1 - i / 2]).ToArray();
        var selected = new List<VideoLoopCandidate>();
        for (var slot = 0; slot < 2; slot++)
        foreach (var family in spreadFamilies)
        for (var r = 0; r < regions; r++)
        {
            token.ThrowIfCancellationRequested();
            var region = (r + family) % regions;
            if (!buckets.TryGetValue((region, family), out var bucket) || bucket.Count <= slot) continue;
            var candidate = bucket[slot];
            if (selected.Any(s => SameVariant(s, candidate))) continue;
            if (selected.Count == MaximumSeedTracks) return new(selected.AsReadOnly(), true, compared, regions);
            selected.Add(candidate);
        }
        return new(selected.AsReadOnly(), bucketLimited, compared, regions);

        void Consider(int a, int b)
        {
            if (a == b) return;
            if (a > b) (a, b) = (b, a);
            var length = samples[b].Time - samples[a].Time;
            if (length + 0.0000001 < minimum) return;
            compared++;
            var error = Difference(samples[a].Features, samples[b].Features);
            if (error > appearance) return;
            if (a + 1 < samples.Count && b + 1 < samples.Count && Continuous(a, a + 1) && Continuous(b, b + 1))
                error = Math.Max(error, Difference(samples[a + 1].Features, samples[b + 1].Features));
            if (a > 0 && b > 0 && Continuous(a - 1, a) && Continuous(b - 1, b))
                error += MotionDifference(samples[a - 1].Features, samples[a].Features, samples[b - 1].Features, samples[b].Features) * 0.5;
            if (error > combined) return;
            var key = (Math.Min(regions - 1, (int)(samples[a].Time * regions / Math.Max(duration, 0.001))),
                Math.Clamp((int)Math.Floor(Math.Log2(Math.Max(1, length / minimum))), 0, 15));
            if (!buckets.TryGetValue(key, out var bucket)) buckets[key] = bucket = [];
            if (bucket.Any(c => c.StartSeconds == samples[a].Time && c.EndSeconds == samples[b].Time)) return;
            var quality = 100 * (1 - error);
            var duplicate = bucket.FindIndex(c => Math.Abs(c.StartSeconds - samples[a].Time) < 0.7 &&
                Math.Abs(c.DurationSeconds - length) < Math.Max(0.15, Math.Min(0.5, Math.Min(c.DurationSeconds, length) * 0.02)));
            if (duplicate >= 0)
            {
                if (quality <= bucket[duplicate].Quality) return;
                bucket.RemoveAt(duplicate);
            }
            else if (bucket.Count == 2) bucketLimited = true;
            if (bucket.Count == 2 && (quality < bucket[^1].Quality || quality == bucket[^1].Quality && length >= bucket[^1].DurationSeconds)) return;
            bucket.Add(new(samples[a].Time, samples[b].Time, quality));
            bucket.Sort((x, y) => { var c = y.Quality.CompareTo(x.Quality); if (c != 0) return c; c = x.DurationSeconds.CompareTo(y.DurationSeconds); return c != 0 ? c : x.StartSeconds.CompareTo(y.StartSeconds); });
            if (bucket.Count > 2) bucket.RemoveAt(2);
        }
        bool Continuous(int a, int b) => samples[b].Time - samples[a].Time <= 1.5 / rate + 0.0000001;
    }

    private static int LowerBound(IReadOnlyList<SearchSample> samples, double time)
    {
        var a = 0; var b = samples.Count;
        while (a < b) { var m = a + (b - a) / 2; if (samples[m].Time < time) a = m + 1; else b = m; }
        return a;
    }

    internal static IReadOnlyList<VideoLoopCandidate> Rank(IEnumerable<VideoLoopCandidate> candidates)
    {
        var result = new List<VideoLoopCandidate>();
        // Ignore floating point noise in equivalent native-time cycles. Quality
        // still wins; duration only breaks equal scores, followed by source time.
        foreach (var candidate in candidates.OrderByDescending(c => Math.Round(c.Quality, 6))
            .ThenBy(c => Math.Round(c.DurationSeconds, 6)).ThenBy(c => c.StartSeconds))
            if (!result.Any(r => SameVariant(r, candidate) || r.Seam?.IsStatic == true && candidate.Seam?.IsStatic == true &&
                Math.Floor(Math.Log2(r.DurationSeconds)) == Math.Floor(Math.Log2(candidate.DurationSeconds)))) result.Add(candidate);
        return result.AsReadOnly();
    }

    private static bool SameVariant(VideoLoopCandidate a, VideoLoopCandidate b)
        => Math.Abs(a.DurationSeconds - b.DurationSeconds) < Math.Max(0.15, Math.Min(0.5, Math.Min(a.DurationSeconds, b.DurationSeconds) * 0.02)) &&
            Math.Abs(a.StartSeconds - b.StartSeconds) < 0.7;

    internal static IReadOnlyList<VideoLoopCandidate> BoundaryVariants(IReadOnlyList<Frame> left, IReadOnlyList<Frame> right,
        double minimum, double videoEnd, bool includesLastVideoFrame, LoopSensitivity sensitivity, CancellationToken token)
    {
        var accepted = new List<VideoLoopCandidate>();
        var evaluator = new VideoLoopSeamEvaluator(left, right);
        foreach (var pair in evaluator.BoundaryShortlist(minimum, videoEnd, includesLastVideoFrame, token))
            if (evaluator.Evaluate(pair.Start, pair.Last, pair.End, sensitivity, token) is { } candidate) accepted.Add(candidate);
        return Rank(accepted).Take(3).ToArray();
    }

    private static VideoLoopCandidate? BoundaryCandidate(IReadOnlyList<Frame> left, int a, IReadOnlyList<Frame> right,
        int b, double exclusiveEnd, LoopSensitivity sensitivity)
    {
        return new VideoLoopSeamEvaluator(left, right).Evaluate(a, b, exclusiveEnd, sensitivity, CancellationToken.None);
    }
}
