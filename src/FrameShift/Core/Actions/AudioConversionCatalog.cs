using System;
using System.Collections.Generic;
using System.Linq;

namespace FrameShift.Core.Actions;

public sealed record AudioTarget(string Id, string DisplayName, string Description) : IConversionChoice;

public sealed record AudioProfile(string Id, string DisplayName, string Description) : IConversionChoice;

public sealed record AudioEncodingPlan(string ModeLabel, string Codec, IReadOnlyList<string> Args);

public static class AudioConversionCatalog
{
    private static readonly AudioTarget[] Targets =
    [
        new("mp3", "MP3", "General-purpose audio format with broad compatibility."),
        new("wav", "WAV", "Uncompressed PCM audio for editing and exchange."),
        new("flac", "FLAC", "Lossless audio for archiving."),
        new("m4a", "M4A", "AAC audio for library and Apple workflows."),
        new("ogg", "OGG", "Open audio format for compatible playback.")
    ];

    private static readonly AudioProfile[] Profiles =
    [
        new("standard", "Standard", "Recommended for most files. Good quality without oversized output."),
        new("high", "High quality", "Higher bitrate or quality settings when the target format supports it."),
        new("small", "Small file", "Lower bitrate or lighter settings to reduce output size.")
    ];

    public static IReadOnlyList<IConversionChoice> GetTargets() => Targets;

    public static IReadOnlyList<IConversionChoice> GetTargetsForSource(string sourceExtension)
    {
        var normalized = NormalizeSourceExtension(sourceExtension);
        return Targets.Where(target => !IsSameTargetAsSource(normalized, target.Id)).Cast<IConversionChoice>().ToArray();
    }

    public static IReadOnlyList<IConversionChoice> GetProfiles() => Profiles;

    public static string GetSupportedSourceFormatsText() => ".mp3, .wav, .wave, .flac, .m4a and .ogg";

    public static AudioTarget GetTargetById(string id)
    {
        foreach (var target in Targets)
        {
            if (string.Equals(target.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return target;
            }
        }

        return Targets[0];
    }

    public static AudioProfile GetProfileById(string id)
    {
        foreach (var profile in Profiles)
        {
            if (string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        return Profiles[0];
    }

    public static bool IsSupportedSourceExtension(string extension)
    {
        var normalized = NormalizeSourceExtension(extension);
        return normalized is ".mp3" or ".wav" or ".wave" or ".flac" or ".m4a" or ".ogg";
    }

    public static bool IsSupportedTarget(string id)
    {
        foreach (var target in Targets)
        {
            if (string.Equals(target.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsSameTargetAsSource(string sourceExtension, string targetId)
    {
        return IsEquivalentSourceTarget(NormalizeSourceExtension(sourceExtension), targetId);
    }

    public static AudioEncodingPlan GetEncodingPlan(string targetId, string profileId)
    {
        var target = GetTargetById(targetId);
        var profile = GetProfileById(profileId);
        var normalizedTarget = target.Id.ToLowerInvariant();
        var normalizedProfile = profile.Id.ToLowerInvariant();

        return normalizedTarget switch
        {
            "mp3" => new AudioEncodingPlan(
                "Audio",
                "libmp3lame",
                normalizedProfile switch
                {
                    "high" => ["-b:a", "320k"],
                    "small" => ["-b:a", "192k"],
                    _ => ["-b:a", "256k"]
                }),
            "wav" => new AudioEncodingPlan(
                "Audio",
                normalizedProfile == "high" ? "pcm_s24le" : "pcm_s16le",
                []),
            "flac" => new AudioEncodingPlan(
                "Audio",
                "flac",
                normalizedProfile switch
                {
                    "high" => ["-compression_level", "8"],
                    "small" => ["-compression_level", "3"],
                    _ => ["-compression_level", "5"]
                }),
            "m4a" => new AudioEncodingPlan(
                "Audio",
                "aac",
                normalizedProfile switch
                {
                    "high" => ["-b:a", "256k"],
                    "small" => ["-b:a", "128k"],
                    _ => ["-b:a", "192k"]
                }),
            "ogg" => new AudioEncodingPlan(
                "Audio",
                "libvorbis",
                normalizedProfile switch
                {
                    "high" => ["-q:a", "8"],
                    "small" => ["-q:a", "4"],
                    _ => ["-q:a", "6"]
                }),
            _ => throw new NotSupportedException($"Unsupported audio target '{targetId}'.")
        };
    }

    private static bool IsEquivalentSourceTarget(string sourceExtension, string targetId)
    {
        return sourceExtension switch
        {
            ".mp3" => targetId.Equals("mp3", StringComparison.OrdinalIgnoreCase),
            ".wav" or ".wave" => targetId.Equals("wav", StringComparison.OrdinalIgnoreCase),
            ".flac" => targetId.Equals("flac", StringComparison.OrdinalIgnoreCase),
            ".m4a" => targetId.Equals("m4a", StringComparison.OrdinalIgnoreCase),
            ".ogg" => targetId.Equals("ogg", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string NormalizeSourceExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        return extension.Trim().ToLowerInvariant();
    }
}
