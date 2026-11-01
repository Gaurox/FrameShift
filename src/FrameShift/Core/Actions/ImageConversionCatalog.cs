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
        new("png", "PNG", L.Get("ImageConversionCatalog_Lossless_image_format_with_wide_supp_857589f9")),
        new("jpg", "JPG", L.Get("ImageConversionCatalog_Compressed_image_format_for_photos_a_5410c9a9")),
        new("webp", "WEBP", L.Get("ImageConversionCatalog_Modern_image_format_with_good_compre_15d5fd38")),
        new("bmp", "BMP", L.Get("ImageConversionCatalog_Uncompressed_bitmap_format_35aed712"))
    ];

    private static readonly ImageProfile[] Profiles =
    [
        new("standard", L.Get("ImageConversionCatalog_Standard_89eefd94"), L.Get("ImageConversionCatalog_Balanced_preset_for_most_images_60d8ac30")),
        new("high", L.Get("ImageConversionCatalog_High_quality_f9552ad2"), L.Get("ImageConversionCatalog_Stronger_compression_or_higher_quali_d4c8f401")),
        new("small", L.Get("ImageConversionCatalog_Small_file_461660a8"), L.Get("ImageConversionCatalog_Favors_smaller_files_over_speed_1e5e4947"))
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

    public static string GetSupportedSourceFormatsText() => L.Get("ImageConversionCatalog_png_jpg_jpeg_webp_and_bmp_394c0690");

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
            _ => throw new NotSupportedException(L.Format("ImageConversionCatalog_Unsupported_image_target_0_7c661285",targetId))
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
