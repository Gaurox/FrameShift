using System.Collections.Generic;
using System.Linq;

namespace FrameShift.Core.Actions;

public static class VideoCompressionCatalog
{
    private static readonly VideoCompressionProfile[] Profiles =
    [
        new("high", L.Get("VideoCompressionCatalog_High_quality_f9552ad2"), L.Get("VideoCompressionCatalog_High_quality_keeps_more_detail_0d896921"), "slow", 18, 256),
        new("balanced", L.Get("VideoCompressionCatalog_Balanced_b9f90b2b"), L.Get("VideoCompressionCatalog_Balanced_keeps_a_good_tradeoff_betwe_ed19f459"), "medium", 21, 192),
        new("small", L.Get("VideoCompressionCatalog_Small_file_461660a8"), L.Get("VideoCompressionCatalog_Small_file_reduces_size_more_aggress_e253d537"), "medium", 24, 128)
    ];

    public static IReadOnlyList<IConversionChoice> GetProfiles() => Profiles;

    public static VideoCompressionProfile GetProfileById(string id)
    {
        foreach (var profile in Profiles)
        {
            if (string.Equals(profile.Id, id, System.StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        return Profiles[1];
    }

    public static bool IsSupportedSourceExtension(string extension)
    {
        var normalized = NormalizeSourceExtension(extension);
        return normalized is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".m4v";
    }

    public static string GetSupportedSourceFormatsText() => L.Get("VideoCompressionCatalog_mp4_mkv_avi_mov_webm_and_m4v_6a51bea4");

    public static bool IsSupportedProfileId(string id)
    {
        return Profiles.Any(profile => string.Equals(profile.Id, id, System.StringComparison.OrdinalIgnoreCase));
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
