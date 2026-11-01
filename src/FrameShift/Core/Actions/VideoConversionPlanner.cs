using System;
using System.Collections.Generic;
using System.Linq;
using FrameShift.Core.FFprobe;

namespace FrameShift.Core.Actions;

public static class VideoConversionPlanner
{
    public static string GetPreparingText(string profileId, string targetId)
    {
        var target = targetId.ToUpperInvariant();
        return profileId.ToLowerInvariant() switch
        {
            "remux" => L.Format("VideoConversionPlanner_Preparing_remux_to_0_bacf3014",target),
            "montage" => L.Format("VideoConversionPlanner_Preparing_montage_conversion_to_0_a2ce4e52",target),
            "youtube" => L.Format("VideoConversionPlanner_Preparing_web_conversion_to_0_c6937378",target),
            "streaming" => L.Format("VideoConversionPlanner_Preparing_streaming_conversion_to_0_1965bf1c",target),
            "tv" => L.Format("VideoConversionPlanner_Preparing_TV_compatible_conversion_t_2104baa8",target),
            _ => L.Format("VideoConversionPlanner_Preparing_universal_conversion_to_0_8e094eba",target)
        };
    }

    public static VideoConversionPlan BuildPlan(
        VideoTarget target,
        VideoProfile profile,
        MediaProbeResult probe,
        bool nvencAvailable)
    {
        var targetId = target.Id.ToLowerInvariant();
        var profileId = profile.Id.ToLowerInvariant();

        if (profileId == "remux")
        {
            var remux = BuildRemuxPlan(targetId, probe);
            if (remux is not null)
            {
                return remux;
            }

            profileId = "universal";
        }

        var streamPlan = BuildStreamPlan(targetId, probe);
        var encodingPlan = BuildEncodingPlan(targetId, profileId, nvencAvailable);

        return new VideoConversionPlan(
            targetId,
            profileId,
            encodingPlan.ModeLabel,
            encodingPlan.VideoCodec,
            encodingPlan.VideoArgs,
            probe.HasAudio ? encodingPlan.AudioCodec : null,
            probe.HasAudio ? encodingPlan.AudioArgs : Array.Empty<string>(),
            encodingPlan.GlobalArgs,
            streamPlan.MapArgs,
            streamPlan.SubtitleCodecArgs,
            streamPlan.Warnings,
            false);
    }

    public static string? ValidateRemuxCompatibility(string targetId, MediaProbeResult probe)
    {
        var videoCodec = probe.VideoCodec?.ToLowerInvariant();
        var audioCodecs = probe.AudioCodecs.Select(codec => codec.ToLowerInvariant()).ToArray();
        var subtitleCodecs = probe.SubtitleCodecs.Select(codec => codec.ToLowerInvariant()).ToArray();

        if (targetId is "webm")
        {
            if (!string.IsNullOrWhiteSpace(videoCodec) && videoCodec is not ("vp8" or "vp9" or "av1"))
            {
                return L.Format("VideoConversionPlanner_Remux_to_WEBM_is_not_possible_video__036635d3",videoCodec);
            }

            foreach (var codec in audioCodecs)
            {
                if (codec is not ("opus" or "vorbis"))
                {
                    return L.Format("VideoConversionPlanner_Remux_to_WEBM_is_not_possible_audio__38161e46",codec);
                }
            }

            foreach (var codec in subtitleCodecs)
            {
                if (codec is not "webvtt")
                {
                    return L.Format("VideoConversionPlanner_Remux_to_WEBM_is_not_possible_subtit_92aad9e8",codec);
                }
            }
        }

        if (targetId is "mp4" or "m4v" or "mov")
        {
            if (!string.IsNullOrWhiteSpace(videoCodec) && videoCodec is not ("h264" or "hevc" or "av1" or "mpeg4"))
            {
                return L.Format("VideoConversionPlanner_Remux_to_0_is_not_possible_video_cod_bd199b11",targetId.ToUpperInvariant(),videoCodec);
            }
        }

        return null;
    }

    private static VideoConversionPlan? BuildRemuxPlan(string targetId, MediaProbeResult probe)
    {
        var warningList = new List<string>();
        var mapArgs = new List<string>
        {
            "-map", "0:v:0?",
            "-map", "0:a?"
        };

        var subtitleArgs = new List<string>();
        if (targetId is "mkv")
        {
            mapArgs.Add("-map");
            mapArgs.Add("0:s?");
            subtitleArgs.AddRange(["-c:s", "copy"]);
        }
        else if (targetId is "mp4" or "m4v" or "mov")
        {
            var textSubtitles = probe.Streams
                .Where(stream => string.Equals(stream.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase))
                .Where(stream => IsTextSubtitle(stream.CodecName))
                .Select(stream => stream.Index)
                .ToArray();
            foreach (var index in textSubtitles)
            {
                mapArgs.Add("-map");
                mapArgs.Add($"0:{index}");
            }

            if (textSubtitles.Length > 0)
            {
                subtitleArgs.AddRange(["-c:s", "mov_text"]);
            }

            if (probe.Streams.Any(stream => string.Equals(stream.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase) && !IsTextSubtitle(stream.CodecName)))
            {
                warningList.Add(L.Get("VideoConversionPlanner_Some_subtitles_are_not_compatible_wi_db624e10"));
            }
        }
        else if (targetId is "webm")
        {
            var textSubtitles = probe.Streams
                .Where(stream => string.Equals(stream.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase))
                .Where(stream => IsTextSubtitle(stream.CodecName))
                .Select(stream => stream.Index)
                .ToArray();
            foreach (var index in textSubtitles)
            {
                mapArgs.Add("-map");
                mapArgs.Add($"0:{index}");
            }

            if (textSubtitles.Length > 0)
            {
                subtitleArgs.AddRange(["-c:s", "webvtt"]);
            }

            if (probe.Streams.Any(stream => string.Equals(stream.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase) && !IsTextSubtitle(stream.CodecName)))
            {
                warningList.Add(L.Get("VideoConversionPlanner_Some_subtitles_are_not_compatible_wi_9fd1fdfc"));
            }
        }
        else if (targetId is "avi" && probe.SubtitleCodecs.Count > 0)
        {
            warningList.Add(L.Get("VideoConversionPlanner_Subtitles_cannot_be_kept_in_AVI_outp_cf84577d"));
        }

        if (probe.Streams.Any(stream => string.Equals(stream.CodecType, "data", StringComparison.OrdinalIgnoreCase) || string.Equals(stream.CodecType, "attachment", StringComparison.OrdinalIgnoreCase)))
        {
            warningList.Add(L.Get("VideoConversionPlanner_Extra_data_streams_or_attachments_wi_2f6bb184"));
        }

        var globalArgs = targetId is "mp4" or "m4v" or "mov"
            ? new[] { "-movflags", "+faststart" }
            : Array.Empty<string>();

        return new VideoConversionPlan(
            targetId,
            "remux",
            L.Get("VideoConversionPlanner_Copy_codecs_96a529da"),
            "copy",
            Array.Empty<string>(),
            "copy",
            Array.Empty<string>(),
            globalArgs,
            mapArgs,
            subtitleArgs,
            warningList,
            true);
    }

    private static (string ModeLabel, string VideoCodec, IReadOnlyList<string> VideoArgs, string AudioCodec, IReadOnlyList<string> AudioArgs, IReadOnlyList<string> GlobalArgs) BuildEncodingPlan(
        string targetId,
        string profileId,
        bool nvencAvailable)
    {
        return targetId switch
        {
            "mp4" => BuildMp4Plan(profileId, nvencAvailable),
            "mkv" => BuildMkvPlan(profileId, nvencAvailable),
            "avi" => BuildAviPlan(profileId),
            "mov" => BuildMovPlan(profileId, nvencAvailable),
            "webm" => BuildWebmPlan(profileId),
            "m4v" => BuildM4VPlan(profileId, nvencAvailable),
            _ => throw new InvalidOperationException(L.Get("VideoConversionPlanner_Unsupported_target_format_75eb855c"))
        };
    }

    private static (string ModeLabel, string VideoCodec, IReadOnlyList<string> VideoArgs, string AudioCodec, IReadOnlyList<string> AudioArgs, IReadOnlyList<string> GlobalArgs) BuildMp4Plan(string profileId, bool nvencAvailable)
    {
        return profileId switch
        {
            "montage" => BuildPlanWithOptionalNvenc(
                nvencAvailable,
                "CPU",
                "libx264",
                ["-preset", "medium", "-crf", "14", "-pix_fmt", "yuv420p"],
                "aac",
                ["-b:a", "320k"],
                ["-movflags", "+faststart"],
                "GPU",
                "h264_nvenc",
                ["-preset", "p5", "-cq", "17", "-pix_fmt", "yuv420p"]),
            "youtube" => BuildPlanWithOptionalNvenc(nvencAvailable, "CPU", "libx264", ["-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p"], "aac", ["-b:a", "320k"], ["-movflags", "+faststart"], "GPU", "h264_nvenc", ["-preset", "p5", "-cq", "19", "-pix_fmt", "yuv420p"]),
            "streaming" => BuildPlanWithOptionalNvenc(nvencAvailable, "CPU", "libx264", ["-preset", "medium", "-crf", "23", "-pix_fmt", "yuv420p"], "aac", ["-b:a", "160k"], ["-movflags", "+faststart"], "GPU", "h264_nvenc", ["-preset", "p5", "-cq", "24", "-pix_fmt", "yuv420p"]),
            "tv" => BuildPlanWithOptionalNvenc(nvencAvailable, "CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-profile:v", "high", "-level", "4.1"], "aac", ["-b:a", "192k"], ["-movflags", "+faststart"], "GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p", "-profile:v", "high", "-level", "4.1"]),
            _ => BuildPlanWithOptionalNvenc(nvencAvailable, "CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p"], "aac", ["-b:a", "320k"], ["-movflags", "+faststart"], "GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p"])
        };
    }

    private static (string ModeLabel, string VideoCodec, IReadOnlyList<string> VideoArgs, string AudioCodec, IReadOnlyList<string> AudioArgs, IReadOnlyList<string> GlobalArgs) BuildMkvPlan(string profileId, bool nvencAvailable)
    {
        return profileId switch
        {
            "montage" => BuildPlanWithOptionalNvenc(nvencAvailable, "CPU", "libx264", ["-preset", "medium", "-crf", "14", "-pix_fmt", "yuv420p"], "flac", Array.Empty<string>(), Array.Empty<string>(), "GPU", "h264_nvenc", ["-preset", "p5", "-cq", "17", "-pix_fmt", "yuv420p"]),
            "youtube" => BuildPlanWithOptionalNvenc(nvencAvailable, "CPU", "libx264", ["-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p"], "aac", ["-b:a", "320k"], Array.Empty<string>(), "GPU", "h264_nvenc", ["-preset", "p5", "-cq", "19", "-pix_fmt", "yuv420p"]),
            "streaming" => BuildPlanWithOptionalNvenc(nvencAvailable, "CPU", "libx264", ["-preset", "medium", "-crf", "23", "-pix_fmt", "yuv420p"], "aac", ["-b:a", "128k"], Array.Empty<string>(), "GPU", "h264_nvenc", ["-preset", "p5", "-cq", "24", "-pix_fmt", "yuv420p"]),
            "tv" => BuildPlanWithOptionalNvenc(nvencAvailable, "CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-profile:v", "high", "-level", "4.1"], "aac", ["-b:a", "192k"], Array.Empty<string>(), "GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p", "-profile:v", "high", "-level", "4.1"]),
            _ => BuildPlanWithOptionalNvenc(nvencAvailable, "CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p"], "aac", ["-b:a", "192k"], Array.Empty<string>(), "GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p"])
        };
    }

    private static (string ModeLabel, string VideoCodec, IReadOnlyList<string> VideoArgs, string AudioCodec, IReadOnlyList<string> AudioArgs, IReadOnlyList<string> GlobalArgs) BuildAviPlan(string profileId)
    {
        return profileId switch
        {
            "montage" => ("CPU", "mpeg4", ["-q:v", "1", "-vtag", "XVID"], "pcm_s16le", Array.Empty<string>(), Array.Empty<string>()),
            "youtube" => ("CPU", "mpeg4", ["-q:v", "2", "-vtag", "XVID"], "libmp3lame", ["-b:a", "192k"], Array.Empty<string>()),
            "streaming" => ("CPU", "mpeg4", ["-q:v", "5", "-vtag", "XVID"], "libmp3lame", ["-b:a", "128k"], Array.Empty<string>()),
            "tv" => ("CPU", "mpeg4", ["-q:v", "2", "-vtag", "XVID"], "pcm_s16le", Array.Empty<string>(), Array.Empty<string>()),
            _ => ("CPU", "mpeg4", ["-q:v", "2", "-vtag", "XVID"], "pcm_s16le", Array.Empty<string>(), Array.Empty<string>())
        };
    }

    private static (string ModeLabel, string VideoCodec, IReadOnlyList<string> VideoArgs, string AudioCodec, IReadOnlyList<string> AudioArgs, IReadOnlyList<string> GlobalArgs) BuildMovPlan(string profileId, bool nvencAvailable)
    {
        return profileId switch
        {
            "montage" => ("CPU", "libx264", ["-preset", "medium", "-crf", "16", "-pix_fmt", "yuv420p"], "pcm_s16le", Array.Empty<string>(), ["-movflags", "+faststart"]),
            "youtube" => BuildPlanWithOptionalNvenc(nvencAvailable, "CPU", "libx264", ["-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p"], "aac", ["-b:a", "320k"], ["-movflags", "+faststart"], "GPU", "h264_nvenc", ["-preset", "p5", "-cq", "19", "-pix_fmt", "yuv420p"]),
            "streaming" => BuildPlanWithOptionalNvenc(nvencAvailable, "CPU", "libx264", ["-preset", "medium", "-crf", "23", "-pix_fmt", "yuv420p"], "aac", ["-b:a", "160k"], ["-movflags", "+faststart"], "GPU", "h264_nvenc", ["-preset", "p5", "-cq", "24", "-pix_fmt", "yuv420p"]),
            "tv" => BuildPlanWithOptionalNvenc(nvencAvailable, "CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-profile:v", "high", "-level", "4.1"], "aac", ["-b:a", "192k"], ["-movflags", "+faststart"], "GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p", "-profile:v", "high", "-level", "4.1"]),
            _ => BuildPlanWithOptionalNvenc(nvencAvailable, "CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p"], "aac", ["-b:a", "320k"], ["-movflags", "+faststart"], "GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p"])
        };
    }

    private static (string ModeLabel, string VideoCodec, IReadOnlyList<string> VideoArgs, string AudioCodec, IReadOnlyList<string> AudioArgs, IReadOnlyList<string> GlobalArgs) BuildWebmPlan(string profileId)
    {
        return profileId switch
        {
            "montage" => ("CPU", "libvpx-vp9", ["-crf", "18", "-b:v", "0", "-deadline", "good", "-cpu-used", "1", "-row-mt", "1"], "libopus", ["-b:a", "256k"], Array.Empty<string>()),
            "youtube" => ("CPU", "libvpx-vp9", ["-crf", "28", "-b:v", "0", "-deadline", "good", "-cpu-used", "2", "-row-mt", "1"], "libopus", ["-b:a", "192k"], Array.Empty<string>()),
            "streaming" => ("CPU", "libvpx-vp9", ["-crf", "34", "-b:v", "0", "-deadline", "good", "-cpu-used", "3", "-row-mt", "1"], "libopus", ["-b:a", "128k"], Array.Empty<string>()),
            "tv" => ("CPU", "libvpx-vp9", ["-crf", "30", "-b:v", "0", "-deadline", "good", "-cpu-used", "2", "-row-mt", "1"], "libopus", ["-b:a", "160k"], Array.Empty<string>()),
            _ => ("CPU", "libvpx-vp9", ["-crf", "31", "-b:v", "0", "-deadline", "good", "-cpu-used", "2", "-row-mt", "1"], "libopus", ["-b:a", "192k"], Array.Empty<string>())
        };
    }

    private static (string ModeLabel, string VideoCodec, IReadOnlyList<string> VideoArgs, string AudioCodec, IReadOnlyList<string> AudioArgs, IReadOnlyList<string> GlobalArgs) BuildM4VPlan(string profileId, bool nvencAvailable)
    {
        return profileId is "montage"
            ? ("CPU", "libx264", ["-preset", "medium", "-crf", "16", "-pix_fmt", "yuv420p"], "aac", ["-b:a", "320k"], ["-movflags", "+faststart"])
            : BuildMp4Plan(profileId, nvencAvailable);
    }

    private static (string ModeLabel, string VideoCodec, IReadOnlyList<string> VideoArgs, string AudioCodec, IReadOnlyList<string> AudioArgs, IReadOnlyList<string> GlobalArgs) BuildPlanWithOptionalNvenc(
        bool nvencAvailable,
        string cpuMode,
        string cpuVideoCodec,
        IReadOnlyList<string> cpuVideoArgs,
        string cpuAudioCodec,
        IReadOnlyList<string> cpuAudioArgs,
        IReadOnlyList<string> cpuGlobalArgs,
        string gpuMode,
        string gpuVideoCodec,
        IReadOnlyList<string> gpuVideoArgs)
    {
        if (nvencAvailable)
        {
            return (gpuMode, gpuVideoCodec, gpuVideoArgs, cpuAudioCodec, cpuAudioArgs, cpuGlobalArgs);
        }

        return (cpuMode, cpuVideoCodec, cpuVideoArgs, cpuAudioCodec, cpuAudioArgs, cpuGlobalArgs);
    }

    private static (IReadOnlyList<string> MapArgs, IReadOnlyList<string> SubtitleCodecArgs, IReadOnlyList<string> Warnings) BuildStreamPlan(string targetId, MediaProbeResult probe)
    {
        var mapArgs = new List<string>
        {
            "-map", "0:v:0?",
            "-map", "0:a?"
        };
        var subtitleArgs = new List<string>();
        var warnings = new List<string>();

        var textSubtitles = probe.Streams.Where(stream => string.Equals(stream.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase) && IsTextSubtitle(stream.CodecName)).ToArray();
        var nonTextSubtitles = probe.Streams.Where(stream => string.Equals(stream.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase) && !IsTextSubtitle(stream.CodecName)).ToArray();

        if (targetId is "mkv")
        {
            mapArgs.Add("-map");
            mapArgs.Add("0:s?");
            subtitleArgs.AddRange(["-c:s", "copy"]);
        }
        else if (targetId is "mp4" or "m4v" or "mov")
        {
            foreach (var stream in textSubtitles)
            {
                mapArgs.Add("-map");
                mapArgs.Add($"0:{stream.Index}");
            }

            if (textSubtitles.Length > 0)
            {
                subtitleArgs.AddRange(["-c:s", "mov_text"]);
            }

            if (nonTextSubtitles.Length > 0)
            {
                warnings.Add(L.Get("VideoConversionPlanner_Some_subtitles_are_not_compatible_wi_db624e10"));
            }
        }
        else if (targetId is "webm")
        {
            foreach (var stream in textSubtitles)
            {
                mapArgs.Add("-map");
                mapArgs.Add($"0:{stream.Index}");
            }

            if (textSubtitles.Length > 0)
            {
                subtitleArgs.AddRange(["-c:s", "webvtt"]);
            }

            if (nonTextSubtitles.Length > 0)
            {
                warnings.Add(L.Get("VideoConversionPlanner_Some_subtitles_are_not_compatible_wi_9fd1fdfc"));
            }
        }
        else if (targetId is "avi" && probe.HasSubtitles)
        {
            warnings.Add(L.Get("VideoConversionPlanner_Subtitles_cannot_be_kept_in_AVI_outp_cf84577d"));
        }

        if (probe.Streams.Any(stream => string.Equals(stream.CodecType, "data", StringComparison.OrdinalIgnoreCase) || string.Equals(stream.CodecType, "attachment", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add(L.Get("VideoConversionPlanner_Extra_data_streams_or_attachments_wi_2f6bb184"));
        }

        return (mapArgs, subtitleArgs, warnings);
    }

    private static bool IsTextSubtitle(string? codecName)
    {
        var normalized = codecName?.ToLowerInvariant();
        return normalized is "subrip" or "ass" or "ssa" or "webvtt" or "mov_text" or "text";
    }
}
