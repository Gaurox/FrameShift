using System.Collections.Generic;
using System.Linq;

namespace FrameShift.Core.Actions;

public sealed record VideoTarget(string Id, string DisplayName, string Description) : IConversionChoice;

public sealed record VideoProfile(string Id, string DisplayName, string Description) : IConversionChoice;

public static class VideoConversionCatalog
{
    private static readonly VideoTarget[] Targets =
    [
        new("mp4", "MP4", "General-purpose container with broad compatibility."),
        new("mkv", "MKV", "Flexible container for mixed codecs and subtitles."),
        new("avi", "AVI", "Legacy container for older playback devices."),
        new("mov", "MOV", "Apple-friendly container for editing workflows."),
        new("webm", "WEBM", "Web delivery with VP9/Opus."),
        new("m4v", "M4V", "MP4 variant for media library workflows.")
    ];

    private static readonly VideoProfile[] Profiles =
    [
        new("universal", "Universal", "Safe default for most files."),
        new("remux", "Remux", "Copy streams when the codecs are already compatible."),
        new("montage", "Montage", "Higher quality for editing."),
        new("youtube", "Web / YouTube", "Balanced quality for upload."),
        new("streaming", "Streaming light", "Smaller files for sharing."),
        new("tv", "TV / USB", "Safer playback on TVs and USB devices.")
    ];

    public static IReadOnlyList<IConversionChoice> GetTargets() => Targets;

    public static IReadOnlyList<IConversionChoice> GetProfiles() => Profiles;

    public static IReadOnlyList<IConversionChoice> GetTargetsForSource(string sourceExtension)
    {
        var normalized = NormalizeSourceExtension(sourceExtension);
        return Targets.Where(target => !IsSameTargetAsSource(normalized, target.Id)).Cast<IConversionChoice>().ToArray();
    }

    public static string GetSupportedSourceFormatsText() => ".mp4, .mkv, .avi, .mov, .webm and .m4v";

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
