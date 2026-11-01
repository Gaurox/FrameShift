using System.Collections.Generic;
using System.Linq;

namespace FrameShift.Core.Actions;

public sealed record VideoTarget(string Id, string DisplayName, string Description) : IConversionChoice;

public sealed record VideoProfile(string Id, string DisplayName, string Description) : IConversionChoice;

public static class VideoConversionCatalog
{
    private static readonly VideoTarget[] Targets =
    [
        new("mp4", "MP4", L.Get("VideoConversionCatalog_General_purpose_container_with_broad_8b67d207")),
        new("mkv", "MKV", L.Get("VideoConversionCatalog_Flexible_container_for_mixed_codecs__47f88cd6")),
        new("avi", "AVI", L.Get("VideoConversionCatalog_Legacy_container_for_older_playback__2cc4d4eb")),
        new("mov", "MOV", L.Get("VideoConversionCatalog_Apple_friendly_container_for_editing_7dbba323")),
        new("webm", "WEBM", L.Get("VideoConversionCatalog_Web_delivery_with_VP9_Opus_a60659ba")),
        new("m4v", "M4V", L.Get("VideoConversionCatalog_MP4_variant_for_media_library_workfl_0e71a35c"))
    ];

    private static readonly VideoProfile[] Profiles =
    [
        new("universal", L.Get("VideoConversionCatalog_Universal_3ed12bb0"), L.Get("VideoConversionCatalog_Safe_default_for_most_files_c6340c67")),
        new("remux", L.Get("VideoConversionCatalog_Remux_34c14dc3"), L.Get("VideoConversionCatalog_Copy_streams_when_the_codecs_are_alr_be402ef3")),
        new("montage", L.Get("VideoConversionCatalog_Montage_c1a09d22"), L.Get("VideoConversionCatalog_Higher_quality_for_editing_c0f3f12d")),
        new("youtube", L.Get("VideoConversionCatalog_Web_YouTube_c2825477"), L.Get("VideoConversionCatalog_Balanced_quality_for_upload_b2328c7d")),
        new("streaming", L.Get("VideoConversionCatalog_Streaming_light_dafa2764"), L.Get("VideoConversionCatalog_Smaller_files_for_sharing_dee0e00e")),
        new("tv", L.Get("VideoConversionCatalog_TV_USB_744b8d3a"), L.Get("VideoConversionCatalog_Safer_playback_on_TVs_and_USB_device_356c3387"))
    ];

    public static IReadOnlyList<IConversionChoice> GetTargets() => Targets;

    public static IReadOnlyList<IConversionChoice> GetProfiles() => Profiles;

    public static IReadOnlyList<IConversionChoice> GetTargetsForSource(string sourceExtension)
    {
        var normalized = NormalizeSourceExtension(sourceExtension);
        return Targets.Where(target => !IsSameTargetAsSource(normalized, target.Id)).Cast<IConversionChoice>().ToArray();
    }

    public static string GetSupportedSourceFormatsText() => L.Get("VideoConversionCatalog_mp4_mkv_avi_mov_webm_and_m4v_6a51bea4");

    public static bool IsSupportedSourceExtension(string extension)
    {
        var normalized = NormalizeSourceExtension(extension);
        return normalized is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".m4v";
    }

    public static bool IsSameTargetAsSource(string sourceExtension, string targetId)
    {
        var normalized = NormalizeSourceExtension(sourceExtension);
        return normalized switch
        {
            ".mp4" => targetId.Equals("mp4", System.StringComparison.OrdinalIgnoreCase),
            ".mkv" => targetId.Equals("mkv", System.StringComparison.OrdinalIgnoreCase),
            ".avi" => targetId.Equals("avi", System.StringComparison.OrdinalIgnoreCase),
            ".mov" => targetId.Equals("mov", System.StringComparison.OrdinalIgnoreCase),
            ".webm" => targetId.Equals("webm", System.StringComparison.OrdinalIgnoreCase),
            ".m4v" => targetId.Equals("m4v", System.StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    public static VideoTarget GetTargetById(string id)
    {
        foreach (var target in Targets)
        {
            if (string.Equals(target.Id, id, System.StringComparison.OrdinalIgnoreCase))
            {
                return target;
            }
        }

        return Targets[0];
    }

    public static VideoProfile GetProfileById(string id)
    {
        foreach (var profile in Profiles)
        {
            if (string.Equals(profile.Id, id, System.StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        return Profiles[0];
    }

    public static bool IsSupportedTarget(string id)
    {
        foreach (var target in Targets)
        {
            if (string.Equals(target.Id, id, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
