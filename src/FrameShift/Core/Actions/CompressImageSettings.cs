using System;
using System.Collections.Generic;
using System.Globalization;

namespace FrameShift.Core.Actions;

public sealed record CompressImageSettings(string ProfileId, string OutputFormat, long? TargetBytes)
{
    public const string ProfileHigh = "high";
    public const string ProfileBalanced = "balanced";
    public const string ProfileSmall = "small";

    public string OutputSuffix => TargetBytes.HasValue
        ? $"_compress_target_{OutputFormat}"
        : $"_compress_{ProfileId}_{OutputFormat}";

    public string OutputExtension => $".{OutputFormat}";

    public static bool IsTargetSizeSupportedForFormat(string format) =>
        format is "jpg" or "webp";

    public static string GetDefaultOutputFormat(string sourceExtension) => sourceExtension switch
    {
        ".jpg" or ".jpeg" => "jpg",
        ".webp" => "webp",
        _ => "png"
    };

    public IReadOnlyDictionary<string, string> ToOptions()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionOptionKeys.Profile] = ProfileId,
            [ActionOptionKeys.Target] = OutputFormat
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
        out CompressImageSettings? settings,
        out string? errorMessage)
    {
        settings = null;
        errorMessage = null;

        if (options is null ||
            !options.TryGetValue(ActionOptionKeys.Profile, out var profileId) ||
            string.IsNullOrWhiteSpace(profileId))
        {
            errorMessage = MediaActionMessages.CompressImageSettingsMissing();
            return false;
        }

        profileId = profileId.Trim().ToLowerInvariant();
        if (profileId is not (ProfileHigh or ProfileBalanced or ProfileSmall))
        {
            errorMessage = MediaActionMessages.CompressImageSettingsInvalid();
            return false;
        }

        if (!options.TryGetValue(ActionOptionKeys.Target, out var outputFormat) ||
            string.IsNullOrWhiteSpace(outputFormat))
        {
            errorMessage = MediaActionMessages.CompressImageSettingsMissing();
            return false;
        }

        outputFormat = outputFormat.Trim().ToLowerInvariant();
        if (outputFormat is not ("png" or "jpg" or "webp"))
        {
            errorMessage = MediaActionMessages.CompressImageSettingsInvalid();
            return false;
        }

        long? targetBytes = null;
        if (options.TryGetValue(ActionOptionKeys.TargetSizeBytes, out var targetText) &&
            !string.IsNullOrWhiteSpace(targetText))
        {
            if (!long.TryParse(targetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBytes) ||
                parsedBytes <= 0)
            {
                errorMessage = MediaActionMessages.CompressImageSettingsInvalid();
                return false;
            }

            targetBytes = parsedBytes;
        }

        settings = new CompressImageSettings(profileId, outputFormat, targetBytes);
        return true;
    }
}
