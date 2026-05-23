using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FrameShift.Core.FFprobe;

namespace FrameShift.Core.Actions;

public static class VideoCompressionPlanner
{
    public static string GetPreparingText(string profileId, bool useTargetSize)
    {
        if (useTargetSize)
        {
            return "Preparing target size compression...";
        }

        return profileId.ToLowerInvariant() switch
        {
            "high" => "Preparing high quality compression...",
            "balanced" => "Preparing balanced compression...",
            "small" => "Preparing small file compression...",
            _ => "Preparing video compression..."
        };
    }

    public static VideoCompressionPlan BuildPlan(
        VideoCompressionProfile profile,
        MediaProbeResult probe,
        string sourceExtension,
        long? targetBytes)
    {
        var normalizedExtension = NormalizeExtension(sourceExtension);
        var target = targetBytes.HasValue
            ? BuildTargetSizePlan(normalizedExtension, profile, probe, targetBytes.Value)
            : null;

        return BuildPlanInternal(normalizedExtension, profile, probe, target);
    }

    private static VideoCompressionPlan BuildPlanInternal(
        string sourceExtension,
        VideoCompressionProfile profile,
        MediaProbeResult probe,
        VideoCompressionTarget? target)
    {
        var mapArgs = new List<string>
        {
            "-map", "0:v:0?",
            "-map", "0:a?"
        };

        var subtitleCodecArgs = new List<string>();
        var warnings = new List<string>();

        var textSubtitleStreams = probe.Streams
            .Where(stream => string.Equals(stream.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase))
            .Where(stream => IsTextSubtitle(stream.CodecName))
            .ToArray();

        foreach (var stream in textSubtitleStreams)
        {
            if (sourceExtension == ".avi")
            {
                continue;
            }

            mapArgs.Add("-map");
            mapArgs.Add($"0:{stream.Index}");
        }

        if (textSubtitleStreams.Length > 0 && sourceExtension != ".avi")
        {
            subtitleCodecArgs.AddRange(GetSubtitleCodecArgs(sourceExtension));
        }

        if (sourceExtension == ".avi")
        {
            warnings.Add("Subtitles cannot be kept in AVI output and will be removed.");
        }
        else if (probe.Streams.Any(stream => string.Equals(stream.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase) && !IsTextSubtitle(stream.CodecName)))
        {
            warnings.Add(GetSubtitleWarning(sourceExtension));
        }

        if (probe.Streams.Any(stream => string.Equals(stream.CodecType, "data", StringComparison.OrdinalIgnoreCase) || string.Equals(stream.CodecType, "attachment", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("Extra data streams or attachments will be removed.");
        }

        var (videoCodec, videoArgs, audioCodec, audioArgs, globalArgs) = target is null
            ? BuildProfileEncoding(sourceExtension, profile, probe.HasAudio)
            : BuildTargetEncoding(sourceExtension, profile, probe.HasAudio, target);

        return new VideoCompressionPlan(
            profile.Id,
            profile.DisplayName,
            target is null ? "CPU" : "Target size",
            sourceExtension,
            target is not null,
            target?.TargetBytes,
            videoCodec,
            videoArgs,
            audioCodec,
            audioArgs,
            globalArgs,
            mapArgs,
            subtitleCodecArgs,
            warnings);
    }

    private static (string VideoCodec, IReadOnlyList<string> VideoArgs, string? AudioCodec, IReadOnlyList<string> AudioArgs, IReadOnlyList<string> GlobalArgs) BuildProfileEncoding(
        string sourceExtension,
        VideoCompressionProfile profile,
        bool hasAudio)
    {
        return sourceExtension switch
        {
            ".webm" => (
                "libvpx-vp9",
                [
                    "-crf", GetWebmCrf(profile.Id).ToString(CultureInfo.InvariantCulture),
                    "-b:v", "0",
                    "-deadline", "good",
                    "-cpu-used", "2",
                    "-row-mt", "1"
                ],
                hasAudio ? "libopus" : null,
                hasAudio ? ["-b:a", $"{GetDefaultAudioBitrateKbps(sourceExtension, profile.Id)}k"] : Array.Empty<string>(),
                Array.Empty<string>()),
            ".avi" => (
                "mpeg4",
                ["-q:v", GetAviQv(profile.Id).ToString(CultureInfo.InvariantCulture)],
                hasAudio ? "libmp3lame" : null,
                hasAudio ? ["-b:a", $"{GetDefaultAudioBitrateKbps(sourceExtension, profile.Id)}k"] : Array.Empty<string>(),
                Array.Empty<string>()),
            ".mkv" => (
                "libx264",
                ["-preset", profile.FFmpegPreset, "-crf", profile.VideoCrf.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p"],
                hasAudio ? "aac" : null,
                hasAudio ? ["-b:a", $"{profile.AudioBitrateKbps}k"] : Array.Empty<string>(),
                Array.Empty<string>()),
            ".mov" or ".m4v" or ".mp4" => (
                "libx264",
                ["-preset", profile.FFmpegPreset, "-crf", profile.VideoCrf.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p", "-profile:v", "high"],
                hasAudio ? "aac" : null,
                hasAudio ? ["-b:a", $"{profile.AudioBitrateKbps}k"] : Array.Empty<string>(),
                ["-movflags", "+faststart"]),
            _ => throw new InvalidOperationException("Unsupported file format.")
        };
    }

    private static (string VideoCodec, IReadOnlyList<string> VideoArgs, string? AudioCodec, IReadOnlyList<string> AudioArgs, IReadOnlyList<string> GlobalArgs) BuildTargetEncoding(
        string sourceExtension,
        VideoCompressionProfile profile,
        bool hasAudio,
        VideoCompressionTarget target)
    {
        var audioCodec = GetAudioCodec(sourceExtension);
        IReadOnlyList<string> audioArgs = hasAudio
            ? [ "-b:a", $"{target.AudioBitrateKbps}k" ]
            : Array.Empty<string>();

        return sourceExtension switch
        {
            ".webm" => (
                "libvpx-vp9",
                [
                    "-b:v", $"{target.VideoBitrateKbps}k",
                    "-maxrate", $"{target.VideoBitrateKbps}k",
                    "-bufsize", $"{target.BufferSizeKbps}k",
                    "-deadline", "good",
                    "-cpu-used", "2",
                    "-row-mt", "1"
                ],
                hasAudio ? audioCodec : null,
                hasAudio ? audioArgs : Array.Empty<string>(),
                Array.Empty<string>()),
            ".avi" => (
                "mpeg4",
                ["-b:v", $"{target.VideoBitrateKbps}k"],
                hasAudio ? audioCodec : null,
                hasAudio ? audioArgs : Array.Empty<string>(),
                Array.Empty<string>()),
            ".mkv" => (
                "libx264",
                [
                    "-preset", profile.FFmpegPreset,
                    "-b:v", $"{target.VideoBitrateKbps}k",
                    "-maxrate", $"{target.VideoBitrateKbps}k",
                    "-bufsize", $"{target.BufferSizeKbps}k",
                    "-pix_fmt", "yuv420p"
                ],
                hasAudio ? audioCodec : null,
                hasAudio ? audioArgs : Array.Empty<string>(),
                Array.Empty<string>()),
            ".mov" or ".m4v" or ".mp4" => (
                "libx264",
                [
                    "-preset", profile.FFmpegPreset,
                    "-b:v", $"{target.VideoBitrateKbps}k",
                    "-maxrate", $"{target.VideoBitrateKbps}k",
                    "-bufsize", $"{target.BufferSizeKbps}k",
                    "-pix_fmt", "yuv420p",
                    "-profile:v", "high"
                ],
                hasAudio ? audioCodec : null,
                hasAudio ? audioArgs : Array.Empty<string>(),
                ["-movflags", "+faststart"]),
            _ => throw new InvalidOperationException("Unsupported file format.")
        };
    }

    private static VideoCompressionTarget BuildTargetSizePlan(
        string sourceExtension,
        VideoCompressionProfile profile,
        MediaProbeResult probe,
        long targetBytes)
    {
        if (probe.Duration is null || probe.Duration.Value.TotalSeconds <= 0)
        {
            throw new InvalidOperationException(MediaActionMessages.DurationUnavailable());
        }

        if (targetBytes <= 0)
        {
            throw new InvalidOperationException(MediaActionMessages.CompressionTargetSizeInvalid());
        }

        var totalKbps = (int)Math.Floor(((targetBytes * 8.0) / probe.Duration.Value.TotalSeconds) / 1000.0 * 0.97);
        if (totalKbps < 64)
        {
            throw new InvalidOperationException(MediaActionMessages.CompressionTargetTooSmall());
        }

        var audioKbps = GetDefaultAudioBitrateKbps(sourceExtension, profile.Id);
        var minVideoKbps = sourceExtension == ".webm" ? 120 : 180;

        if ((totalKbps - audioKbps) < minVideoKbps)
        {
            audioKbps = Math.Max(48, totalKbps - minVideoKbps);
        }

        var videoKbps = Math.Max(minVideoKbps, totalKbps - audioKbps);
        var bufferKbps = Math.Max(videoKbps * 2, videoKbps + 128);
        return new VideoCompressionTarget(targetBytes, videoKbps, audioKbps, bufferKbps);
    }

    private static string GetAudioCodec(string sourceExtension)
    {
        return sourceExtension switch
        {
            ".webm" => "libopus",
            ".avi" => "libmp3lame",
            _ => "aac"
        };
    }

    private static int GetDefaultAudioBitrateKbps(string sourceExtension, string profileId)
    {
        return sourceExtension switch
        {
            ".webm" => profileId switch
            {
                "high" => 128,
                "small" => 64,
                _ => 96
            },
            ".avi" => profileId switch
            {
                "high" => 192,
                "small" => 96,
                _ => 128
            },
            _ => profileId switch
            {
                "high" => 192,
                "small" => 128,
                _ => 160
            }
        };
    }

    private static int GetWebmCrf(string profileId)
    {
        return profileId switch
        {
            "high" => 30,
            "small" => 40,
            _ => 34
        };
    }

    private static int GetAviQv(string profileId)
    {
        return profileId switch
        {
            "high" => 4,
            "small" => 12,
            _ => 8
        };
    }

    private static string[] GetSubtitleCodecArgs(string sourceExtension)
    {
        return sourceExtension switch
        {
            ".webm" => ["-c:s", "webvtt"],
            ".mkv" => ["-c:s", "copy"],
            _ => ["-c:s", "mov_text"]
        };
    }

    private static string GetSubtitleWarning(string sourceExtension)
    {
        return sourceExtension switch
        {
            ".webm" => "Some subtitles are not compatible with WEBM and will be removed.",
            _ => "Some subtitles are not compatible with this output format and will be removed."
        };
    }

    private static bool IsTextSubtitle(string? codecName)
    {
        var normalized = codecName?.ToLowerInvariant();
        return normalized is "subrip" or "ass" or "ssa" or "webvtt" or "mov_text" or "text";
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        return extension.Trim().ToLowerInvariant();
    }
}
