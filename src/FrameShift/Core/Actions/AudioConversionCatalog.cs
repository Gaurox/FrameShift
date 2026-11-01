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
        new("mp3", "MP3", L.Get("AudioConversionCatalog_General_purpose_audio_format_with_br_3fb4d201")),
        new("wav", "WAV", L.Get("AudioConversionCatalog_Uncompressed_PCM_audio_for_editing_a_687723fd")),
        new("flac", "FLAC", L.Get("AudioConversionCatalog_Lossless_audio_for_archiving_dcfa76c8")),
        new("m4a", "M4A", L.Get("AudioConversionCatalog_AAC_audio_for_library_and_Apple_work_a58642a1")),
        new("ogg", "OGG", L.Get("AudioConversionCatalog_Open_audio_format_for_compatible_pla_c82df6f3"))
    ];

    private static readonly AudioProfile[] Profiles =
    [
        new("standard", L.Get("AudioConversionCatalog_Standard_89eefd94"), L.Get("AudioConversionCatalog_Recommended_for_most_files_Good_qual_bd293396")),
        new("high", L.Get("AudioConversionCatalog_High_quality_f9552ad2"), L.Get("AudioConversionCatalog_Higher_bitrate_or_quality_settings_w_470f4391")),
        new("small", L.Get("AudioConversionCatalog_Small_file_461660a8"), L.Get("AudioConversionCatalog_Lower_bitrate_or_lighter_settings_to_c71395e6"))
    ];

    public static IReadOnlyList<IConversionChoice> GetTargets() => Targets;

    public static IReadOnlyList<IConversionChoice> GetTargetsForSource(string sourceExtension)
    {
        var normalized = NormalizeSourceExtension(sourceExtension);
        return Targets.Where(target => !IsSameTargetAsSource(normalized, target.Id)).Cast<IConversionChoice>().ToArray();
    }

    public static IReadOnlyList<IConversionChoice> GetProfiles() => Profiles;

    public static string GetSupportedSourceFormatsText() => L.Get("AudioConversionCatalog_mp3_wav_wave_flac_m4a_and_ogg_92ede3e8");

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
            _ => throw new NotSupportedException(L.Format("AudioConversionCatalog_Unsupported_audio_target_0_db7f0d7b",targetId))
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
