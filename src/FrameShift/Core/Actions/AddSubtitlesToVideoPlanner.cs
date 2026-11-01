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
            ? L.Get("AddSubtitlesToVideoPlanner_Preparing_MKV_subtitle_track_e25c92fc")
            : L.Format("AddSubtitlesToVideoPlanner_Preparing_0_subtitle_track_8f46c609",TargetContainerId.ToUpperInvariant());

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
                L.Get("AddSubtitlesToVideoPlanner_MKV_can_keep_existing_streams_and_ad_54e56dad"));
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
                L.Get("AddSubtitlesToVideoPlanner_The_existing_container_can_keep_its__8b5f6ee5"));
        }

        return new AddSubtitlesToVideoPlan(
            ".mkv",
            "mkv",
            "subrip",
            UsesFallbackContainer: true,
            L.Format("AddSubtitlesToVideoPlanner_Container_0_is_not_retained_for_sele_af342ca6",normalizedExtension));
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
            return L.Get("AddSubtitlesToVideoPlanner_Source_contains_data_streams_or_atta_906eeb51");
        }

        return L.Get("AddSubtitlesToVideoPlanner_Source_contains_subtitle_streams_tha_ec588e02");
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
    public string PreparingText => L.Format("AddSubtitlesToVideoPlanner_Preparing_burn_in_render_to_0_2fbce182",TargetContainerId.ToUpperInvariant());

    public string PipelineDescription =>
        CopyAudio
            ? L.Format("AddSubtitlesToVideoPlanner_0_audio_copy_f7199d3a",VideoCodec)
            : $"{VideoCodec} + {AudioCodec ?? "no-audio"}";
}

internal static class AddSubtitlesToVideoBurnPlanner
{
    public static AddSubtitlesToVideoBurnPlan BuildPlan(string sourceExtension, MediaProbeResult probe)
    {
        var normalizedExtension = NormalizeExtension(sourceExtension);
        if (!VideoConversionCatalog.IsSupportedSourceExtension(normalizedExtension))
        {
            throw new InvalidOperationException(L.Format("AddSubtitlesToVideoPlanner_Unsupported_burn_target_container_0_2249e39b",normalizedExtension));
        }

        var targetId = normalizedExtension.TrimStart('.');
        var target = VideoConversionCatalog.GetTargetById(targetId);
        var profile = VideoConversionCatalog.GetProfileById("universal");
        var basePlan = VideoConversionPlanner.BuildPlan(target, profile, probe, nvencAvailable: false);
        var canCopyAudio = probe.HasAudio && AreAllAudioStreamsCompatible(targetId, probe.AudioCodecs);
        var warnings = new List<string>(basePlan.Warnings);
        if (probe.IsHdrLikely)
        {
            warnings.Add(L.Get("AddSubtitlesToVideoPlanner_HDR_source_detected_subtitle_burn_in_8cd40b25"));
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
