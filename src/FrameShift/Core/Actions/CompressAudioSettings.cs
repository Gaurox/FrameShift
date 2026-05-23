using System;
using System.Collections.Generic;
using System.Globalization;

namespace FrameShift.Core.Actions;

public sealed record CompressAudioSettings(string ProfileId, long? TargetBytes)
{
    public const string ProfileHigh = "high";
    public const string ProfileBalanced = "balanced";
    public const string ProfileSmall = "small";

    public string OutputSuffix => TargetBytes.HasValue
        ? "_compress_target"
        : $"_compress_{ProfileId}";

    public static bool IsTargetSizeSupportedForExtension(string extension) =>
        extension is ".mp3" or ".m4a" or ".ogg";

    public IReadOnlyDictionary<string, string> ToOptions()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionOptionKeys.Profile] = ProfileId
        };

        if (TargetBytes.HasValue)
        {
            dict[ActionOptionKeys.TargetSizeBytes] = TargetBytes.Value.ToString(CultureInfo.InvariantCulture);
        }

        return dict;
    }

    public static bool ContainsAnyOption(IReadOnlyDictionary<string, string>? options) =>
        options is not null && options.ContainsKey(ActionOptionKeys.Profile);

    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string>? options,
        out CompressAudioSettings? settings,
        out string? errorMessage)
    {
        settings = null;
        errorMessage = null;

        if (options is null ||
            !options.TryGetValue(ActionOptionKeys.Profile, out var profileId) ||
            string.IsNullOrWhiteSpace(profileId))
        {
            errorMessage = MediaActionMessages.CompressAudioSettingsMissing();
            return false;
        }

        profileId = profileId.Trim().ToLowerInvariant();
        if (profileId is not (ProfileHigh or ProfileBalanced or ProfileSmall))
        {
            errorMessage = MediaActionMessages.CompressAudioSettingsInvalid();
            return false;
        }

        long? targetBytes = null;
        if (options.TryGetValue(ActionOptionKeys.TargetSizeBytes, out var targetText) &&
            !string.IsNullOrWhiteSpace(targetText))
        {
            if (!long.TryParse(targetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBytes) ||
                parsedBytes <= 0)
            {
                errorMessage = MediaActionMessages.CompressAudioSettingsInvalid();
                return false;
            }

            targetBytes = parsedBytes;
        }

        settings = new CompressAudioSettings(profileId, targetBytes);
        return true;
    }
}
