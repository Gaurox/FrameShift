using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FrameShift.Core.FFprobe;

namespace FrameShift.Core.Actions;

public enum JoinVideosPipeline
{
    DirectCopy,
    Normalize,
    Unsupported
}

public sealed record JoinVideosPlan(
    JoinVideosPipeline Pipeline,
    string DecisionReason,
    int TargetWidth,
    int TargetHeight,
    string TargetFrameRate,
    bool IncludeAudio)
{
    public bool IsSupported => Pipeline != JoinVideosPipeline.Unsupported;
}

public static class JoinVideosPlanner
{
    public static JoinVideosPlan BuildPlan(IReadOnlyList<JoinVideoProbeResult> probes, JoinVideosMode mode)
    {
        if (probes.Count < 2)
        {
            return Unsupported(MediaActionMessages.JoinVideosRequiresAtLeastTwoVideos());
        }

        if (probes.Any(probe => !probe.HasVideo))
        {
            return Unsupported(MediaActionMessages.MissingVideoTrack());
        }

        var first = probes[0];
        var width = MakeEven(first.DisplayVideoWidth);
        var height = MakeEven(first.DisplayVideoHeight);
        if (width <= 0 || height <= 0 || string.IsNullOrWhiteSpace(first.Video?.FrameRate) ||
            !probes.All(probe => probe.DurationSeconds > 0d))
        {
            return Unsupported(MediaActionMessages.JoinVideosMetadataIncomplete());
        }

        var directCopyCompatible = AreDirectCopyCompatible(probes);
        var hdrCount = probes.Count(probe => probe.IsHdrLikely);
        var containsHdr = hdrCount > 0;
        var mixedHdr = hdrCount > 0 && hdrCount < probes.Count;

        if (mode == JoinVideosMode.Copy)
        {
            return directCopyCompatible
                ? CopyPlan(first, "Forced copy mode: all stream signatures match.")
                : Unsupported(MediaActionMessages.JoinVideosCopyNotSafe());
        }

        if (mode == JoinVideosMode.Normalize)
        {
            return containsHdr
                ? Unsupported(MediaActionMessages.JoinVideosHdrNormalizationNotSupported())
                : NormalizePlan(width, height, first.Video!.FrameRate!, probes.Any(probe => probe.HasAudio), "Forced normalize mode.");
        }

        if (directCopyCompatible)
        {
            return CopyPlan(first, "All stream signatures match; joining without re-encoding.");
        }

        if (mixedHdr)
        {
            return Unsupported(MediaActionMessages.JoinVideosHdrMixNotSupported());
        }

        if (containsHdr)
        {
            return Unsupported(MediaActionMessages.JoinVideosHdrNormalizationNotSupported());
        }

        return NormalizePlan(
            width,
            height,
            first.Video!.FrameRate!,
            probes.Any(probe => probe.HasAudio),
            "The clips differ; normalizing to SDR H.264/AAC MP4.");
    }

    public static bool AreDirectCopyCompatible(IReadOnlyList<JoinVideoProbeResult> probes)
    {
        if (probes.Count < 2)
        {
            return false;
        }

        var first = probes[0];
        if (first.Video is null ||
            first.VideoStreamCount != 1 ||
            first.AudioStreamCount > 1 ||
            first.OtherStreamCount != 0 ||
            string.IsNullOrWhiteSpace(first.FormatName))
        {
            return false;
        }

        foreach (var probe in probes.Skip(1))
        {
            if (probe.Video is null ||
                probe.VideoStreamCount != 1 ||
                probe.AudioStreamCount != first.AudioStreamCount ||
                probe.OtherStreamCount != 0 ||
                !Same(probe.FormatName, first.FormatName) ||
                !SameVideo(probe.Video, first.Video) ||
                (first.Audio is not null && !SameAudio(probe.Audio, first.Audio)))
            {
                return false;
            }
        }

        return true;
    }

    public static double GetTotalDurationSeconds(IReadOnlyList<JoinVideoProbeResult> probes)
        => probes.Sum(probe => probe.DurationSeconds);

    public static long? GetEstimatedTotalFrames(IReadOnlyList<JoinVideoProbeResult> probes)
    {
        var frameRate = ParseFrameRate(probes.FirstOrDefault()?.Video?.FrameRate);
        if (frameRate is null)
        {
            return null;
        }

        var estimated = Math.Round(GetTotalDurationSeconds(probes) * frameRate.Value);
        return estimated > 0 && estimated <= long.MaxValue ? (long)estimated : null;
    }

    private static JoinVideosPlan CopyPlan(JoinVideoProbeResult first, string reason)
        => new(
            JoinVideosPipeline.DirectCopy,
            reason,
            first.DisplayVideoWidth,
            first.DisplayVideoHeight,
            first.Video?.FrameRate ?? string.Empty,
            first.HasAudio);

    private static JoinVideosPlan NormalizePlan(int width, int height, string frameRate, bool includeAudio, string reason)
        => new(JoinVideosPipeline.Normalize, reason, width, height, frameRate, includeAudio);

    private static JoinVideosPlan Unsupported(string reason)
        => new(JoinVideosPipeline.Unsupported, reason, 0, 0, string.Empty, false);

    private static bool SameVideo(JoinVideoStreamInfo left, JoinVideoStreamInfo right)
        => Same(left.CodecName, right.CodecName) &&
           Same(left.CodecTag, right.CodecTag) &&
           Same(left.Profile, right.Profile) &&
           Same(left.Level, right.Level) &&
           left.Width == right.Width &&
           left.Height == right.Height &&
           Same(left.PixelFormat, right.PixelFormat) &&
           Same(left.ColorSpace, right.ColorSpace) &&
           Same(left.ColorTransfer, right.ColorTransfer) &&
           Same(left.ColorPrimaries, right.ColorPrimaries) &&
           Same(left.ColorRange, right.ColorRange) &&
           Same(left.FieldOrder, right.FieldOrder) &&
           Same(left.TimeBase, right.TimeBase) &&
           Same(left.FrameRate, right.FrameRate) &&
           Same(left.SampleAspectRatio, right.SampleAspectRatio) &&
           Same(left.StartTime, right.StartTime) &&
           Same(left.ExtraDataHash, right.ExtraDataHash) &&
           left.RotationDegrees == right.RotationDegrees;

    private static bool SameAudio(JoinAudioStreamInfo? left, JoinAudioStreamInfo? right)
        => left is not null && right is not null &&
           Same(left.CodecName, right.CodecName) &&
           Same(left.CodecTag, right.CodecTag) &&
           Same(left.Profile, right.Profile) &&
           Same(left.SampleFormat, right.SampleFormat) &&
           Same(left.SampleRate, right.SampleRate) &&
           left.Channels == right.Channels &&
           Same(left.ChannelLayout, right.ChannelLayout) &&
           Same(left.TimeBase, right.TimeBase) &&
           Same(left.StartTime, right.StartTime) &&
           Same(left.ExtraDataHash, right.ExtraDataHash);

    private static bool Same(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static int MakeEven(int value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value % 2 == 0 ? value : value + 1;
    }

    private static double? ParseFrameRate(string? frameRate)
    {
        if (string.IsNullOrWhiteSpace(frameRate) || frameRate == "0/0")
        {
            return null;
        }

        var parts = frameRate.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
            numerator > 0 && denominator > 0)
        {
            return numerator / denominator;
        }

        return double.TryParse(frameRate, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;
    }
}
