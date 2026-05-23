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
            "remux" => $"Preparing remux to {target}...",
            "montage" => $"Preparing montage conversion to {target}...",
            "youtube" => $"Preparing web conversion to {target}...",
            "streaming" => $"Preparing streaming conversion to {target}...",
            "tv" => $"Preparing TV-compatible conversion to {target}...",
            _ => $"Preparing universal conversion to {target}..."
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
                return $"Remux to WEBM is not possible: video codec '{videoCodec}' is not WEBM-compatible.";
            }

            foreach (var codec in audioCodecs)
            {
                if (codec is not ("opus" or "vorbis"))
                {
                    return $"Remux to WEBM is not possible: audio codec '{codec}' is not WEBM-compatible.";
                }
            }

            foreach (var codec in subtitleCodecs)
            {
                if (codec is not "webvtt")
                {
                    return $"Remux to WEBM is not possible: subtitle codec '{codec}' is not WEBM-compatible.";
                }
            }
        }

        if (targetId is "mp4" or "m4v" or "mov")
        {
            if (!string.IsNullOrWhiteSpace(videoCodec) && videoCodec is not ("h264" or "hevc" or "av1" or "mpeg4"))
            {
                return $"Remux to {targetId.ToUpperInvariant()} is not possible: video codec '{videoCodec}' is not compatible.";
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
                warningList.Add("Some subtitles are not compatible with this output format and will be removed.");
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
                warningList.Add("Some subtitles are not compatible with WEBM and will be removed.");
            }
        }
        else if (targetId is "avi" && probe.SubtitleCodecs.Count > 0)
        {
            warningList.Add("Subtitles cannot be kept in AVI output and will be removed.");
        }

        if (probe.Streams.Any(stream => string.Equals(stream.CodecType, "data", StringComparison.OrdinalIgnoreCase) || string.Equals(stream.CodecType, "attachment", StringComparison.OrdinalIgnoreCase)))
        {
            warningList.Add("Extra data streams or attachments will be removed.");
        }

        var globalArgs = targetId is "mp4" or "m4v" or "mov"
            ? new[] { "-movflags", "+faststart" }
            : Array.Empty<string>();

        return new VideoConversionPlan(
            targetId,
            "remux",
            "Copy codecs",
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
            _ => throw new InvalidOperationException("Unsupported target format.")
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
                warnings.Add("Some subtitles are not compatible with this output format and will be removed.");
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
                warnings.Add("Some subtitles are not compatible with WEBM and will be removed.");
            }
        }
        else if (targetId is "avi" && probe.HasSubtitles)
        {
            warnings.Add("Subtitles cannot be kept in AVI output and will be removed.");
        }

        if (probe.Streams.Any(stream => string.Equals(stream.CodecType, "data", StringComparison.OrdinalIgnoreCase) || string.Equals(stream.CodecType, "attachment", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("Extra data streams or attachments will be removed.");
        }

        return (mapArgs, subtitleArgs, warnings);
    }

    private static bool IsTextSubtitle(string? codecName)
    {
        var normalized = codecName?.ToLowerInvariant();
        return normalized is "subrip" or "ass" or "ssa" or "webvtt" or "mov_text" or "text";
    }
}
