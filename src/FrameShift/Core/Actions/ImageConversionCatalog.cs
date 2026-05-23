using System;
using System.Collections.Generic;
using System.Linq;

namespace FrameShift.Core.Actions;

public sealed record ImageTarget(string Id, string DisplayName, string Description) : IConversionChoice;

public sealed record ImageProfile(string Id, string DisplayName, string Description) : IConversionChoice;

public static class ImageConversionCatalog
{
    private static readonly ImageTarget[] Targets =
    [
        new("png", "PNG", "Lossless image format with wide support."),
        new("jpg", "JPG", "Compressed image format for photos and general sharing."),
        new("webp", "WEBP", "Modern image format with good compression."),
        new("bmp", "BMP", "Uncompressed bitmap format.")
    ];

    private static readonly ImageProfile[] Profiles =
    [
        new("standard", "Standard", "Balanced preset for most images."),
        new("high", "High quality", "Stronger compression or higher-quality output when available."),
        new("small", "Small file", "Favors smaller files over speed.")
    ];

    public static IReadOnlyList<IConversionChoice> GetTargets() => Targets;

    public static IReadOnlyList<IConversionChoice> GetProfiles() => Profiles;

    public static IReadOnlyList<IConversionChoice> GetTargetsForSource(string sourceExtension)
    {
        var normalized = NormalizeSourceExtension(sourceExtension);
        return Targets.Where(target => !IsSameTargetAsSource(normalized, target.Id)).Cast<IConversionChoice>().ToArray();
    }

    public static ImageTarget GetTargetById(string id)
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

    public static ImageProfile GetProfileById(string id)
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

    public static string GetSupportedSourceFormatsText() => ".png, .jpg, .jpeg, .webp and .bmp";

    public static bool IsSupportedSourceExtension(string extension)
    {
        var normalized = NormalizeSourceExtension(extension);
        return normalized is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp";
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

    public static IReadOnlyList<string> GetOutputArguments(string targetId)
    {
        return GetOutputArguments(targetId, "standard");
    }

    public static IReadOnlyList<string> GetOutputArguments(string targetId, string profileId)
    {
        var normalizedProfileId = GetProfileById(profileId).Id;
        return targetId.ToLowerInvariant() switch
        {
            "png" => normalizedProfileId switch
            {
                "high" => ["-compression_level", "9"],
                "small" => ["-compression_level", "3"],
                _ => ["-compression_level", "6"]
            },
            "jpg" => normalizedProfileId switch
            {
                "high" => ["-q:v", "1"],
                "small" => ["-q:v", "5"],
                _ => ["-q:v", "2"]
            },
            "webp" => normalizedProfileId switch
            {
                "high" => ["-c:v", "libwebp", "-quality", "95", "-compression_level", "3"],
                "small" => ["-c:v", "libwebp", "-quality", "80", "-compression_level", "6"],
                _ => ["-c:v", "libwebp", "-quality", "90", "-compression_level", "4"]
            },
            "bmp" => Array.Empty<string>(),
            _ => throw new NotSupportedException($"Unsupported image target '{targetId}'.")
        };
    }

    private static bool IsEquivalentSourceTarget(string sourceExtension, string targetId)
    {
        return sourceExtension switch
        {
            ".png" => targetId.Equals("png", StringComparison.OrdinalIgnoreCase),
            ".jpg" or ".jpeg" => targetId.Equals("jpg", StringComparison.OrdinalIgnoreCase),
            ".webp" => targetId.Equals("webp", StringComparison.OrdinalIgnoreCase),
            ".bmp" => targetId.Equals("bmp", StringComparison.OrdinalIgnoreCase),
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
