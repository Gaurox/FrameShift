using System.Collections.Generic;
using System.Linq;

namespace FrameShift.Core.Actions;

public static class VideoCompressionCatalog
{
    private static readonly VideoCompressionProfile[] Profiles =
    [
        new("high", "High quality", "High quality keeps more detail.", "slow", 18, 256),
        new("balanced", "Balanced", "Balanced keeps a good tradeoff between quality and size.", "medium", 21, 192),
        new("small", "Small file", "Small file reduces size more aggressively.", "medium", 24, 128)
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

    public static string GetSupportedSourceFormatsText() => ".mp4, .mkv, .avi, .mov, .webm and .m4v";

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
