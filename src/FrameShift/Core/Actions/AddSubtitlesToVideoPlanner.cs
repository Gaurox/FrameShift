using System;
using System.Collections.Generic;
using System.Linq;
using FrameShift.Core.FFprobe;

namespace FrameShift.Core.Actions;

internal sealed record AddSubtitlesToVideoPlan(
    string TargetExtension,
    string TargetContainerId,
    string SubtitleCodec,
    bool UsesFallbackContainer,
    string DecisionReason)
{
    public string PreparingText =>
        UsesFallbackContainer
            ? "Preparing MKV subtitle track..."
            : $"Preparing {TargetContainerId.ToUpperInvariant()} subtitle track...";

    public string PipelineDescription =>
        $"{TargetContainerId.ToUpperInvariant()} + {SubtitleCodec}";
}

internal static class AddSubtitlesToVideoPlanner
{
    public static AddSubtitlesToVideoPlan BuildPlan(string sourceExtension, MediaProbeResult probe)
    {
        var normalizedExtension = NormalizeExtension(sourceExtension);

        if (normalizedExtension == ".mkv")
        {
            return new AddSubtitlesToVideoPlan(
                ".mkv",
                "mkv",
                "subrip",
                UsesFallbackContainer: false,
                "MKV can keep existing streams and add the SRT track natively.");
        }

        if (normalizedExtension is ".mp4" or ".mov" or ".m4v")
        {
            if (HasIncompatibleStreamsForMovText(probe))
            {
                return new AddSubtitlesToVideoPlan(
                    ".mkv",
                    "mkv",
                    "subrip",
                    UsesFallbackContainer: true,
                    BuildFallbackReason(probe));
            }

            return new AddSubtitlesToVideoPlan(
                normalizedExtension,
                normalizedExtension.TrimStart('.'),
                "mov_text",
                UsesFallbackContainer: false,
                "The existing container can keep its streams cleanly with MOV_TEXT subtitles.");
        }

        return new AddSubtitlesToVideoPlan(
            ".mkv",
            "mkv",
            "subrip",
            UsesFallbackContainer: true,
            $"Container '{normalizedExtension}' is not retained for selectable SRT subtitle tracks. Falling back to MKV.");
    }

    private static bool HasIncompatibleStreamsForMovText(MediaProbeResult probe)
    {
        return probe.Streams.Any(stream =>
                   string.Equals(stream.CodecType, "data", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(stream.CodecType, "attachment", StringComparison.OrdinalIgnoreCase)) ||
               probe.Streams.Any(stream =>
                   string.Equals(stream.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase) &&
                   !IsTextSubtitle(stream.CodecName));
    }

    private static string BuildFallbackReason(MediaProbeResult probe)
    {
        if (probe.Streams.Any(stream =>
                string.Equals(stream.CodecType, "data", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(stream.CodecType, "attachment", StringComparison.OrdinalIgnoreCase)))
        {
            return "Source contains data streams or attachments that are not preserved cleanly in MP4/MOV. Falling back to MKV.";
        }

        return "Source contains subtitle streams that are not compatible with MOV_TEXT. Falling back to MKV.";
    }

    private static bool IsTextSubtitle(string? codecName)
    {
        var normalized = codecName?.ToLowerInvariant();
        return normalized is "subrip" or "ass" or "ssa" or "webvtt" or "mov_text" or "text" or "srt";
    }

    private static string NormalizeExtension(string extension)
    {
        return string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.Trim().ToLowerInvariant();
    }
}

internal sealed record AddSubtitlesToVideoBurnPlan(
    string TargetExtension,
    string TargetContainerId,
    string VideoCodec,
    IReadOnlyList<string> VideoArgs,
    bool CopyAudio,
    string? AudioCodec,
    IReadOnlyList<string> AudioArgs,
    IReadOnlyList<string> GlobalArgs,
    IReadOnlyList<string> MapArgs,
    IReadOnlyList<string> SubtitleCodecArgs,
    IReadOnlyList<string> Warnings)
{
    public string PreparingText => $"Preparing burn-in render to {TargetContainerId.ToUpperInvariant()}...";

    public string PipelineDescription =>
        CopyAudio
            ? $"{VideoCodec} + audio copy"
            : $"{VideoCodec} + {AudioCodec ?? "no-audio"}";
}

internal static class AddSubtitlesToVideoBurnPlanner
{
    public static AddSubtitlesToVideoBurnPlan BuildPlan(string sourceExtension, MediaProbeResult probe)
    {
        var normalizedExtension = NormalizeExtension(sourceExtension);
        if (!VideoConversionCatalog.IsSupportedSourceExtension(normalizedExtension))
        {
            throw new InvalidOperationException($"Unsupported burn target container '{normalizedExtension}'.");
        }

        var targetId = normalizedExtension.TrimStart('.');
        var target = VideoConversionCatalog.GetTargetById(targetId);
        var profile = VideoConversionCatalog.GetProfileById("universal");
        var basePlan = VideoConversionPlanner.BuildPlan(target, profile, probe, nvencAvailable: false);
        var canCopyAudio = probe.HasAudio && AreAllAudioStreamsCompatible(targetId, probe.AudioCodecs);
        var warnings = new List<string>(basePlan.Warnings);
        if (probe.IsHdrLikely)
        {
            warnings.Add("HDR source detected: subtitle burn-in re-encodes the video and may alter colorimetry.");
        }

        return new AddSubtitlesToVideoBurnPlan(
            normalizedExtension,
            targetId,
            basePlan.VideoCodec,
            basePlan.VideoArgs,
            CopyAudio: canCopyAudio,
            AudioCodec: canCopyAudio ? null : basePlan.AudioCodec,
            AudioArgs: canCopyAudio ? Array.Empty<string>() : basePlan.AudioArgs,
            GlobalArgs: basePlan.GlobalArgs,
            MapArgs: basePlan.MapArgs,
            SubtitleCodecArgs: basePlan.SubtitleCodecArgs,
            Warnings: warnings);
    }

    private static bool AreAllAudioStreamsCompatible(string targetId, IReadOnlyList<string> audioCodecs)
    {
        if (audioCodecs.Count == 0)
        {
            return false;
        }

        return audioCodecs.All(codec => IsAudioCodecCompatible(targetId, codec));
    }

    private static bool IsAudioCodecCompatible(string targetId, string? codecName)
    {
        var normalized = codecName?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return targetId switch
        {
            "mkv" => true,
            "webm" => normalized is "opus" or "vorbis",
            "avi" => normalized is "mp3" or "ac3" or "pcm_s16le" or "pcm_s24le" or "pcm_u8",
            "mov" => normalized is "aac" or "alac" or "ac3" or "eac3" or "mp3" or "pcm_s16le" or "pcm_s24le" or "pcm_s32le" or "pcm_f32le" or "pcm_f64le",
            _ => normalized is "aac" or "alac" or "ac3" or "eac3" or "mp3"
        };
    }

    private static string NormalizeExtension(string extension)
    {
        return string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.Trim().ToLowerInvariant();
    }
}
