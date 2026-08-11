using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FrameShift.Core.Actions;

public enum JoinVideosMode
{
    Auto,
    Copy,
    Normalize
}

public sealed class JoinVideosSettings
{
    public List<string> InputPaths { get; set; } = [];

    public JoinVideosMode Mode { get; set; } = JoinVideosMode.Auto;

    public string ToOptionPayload() => JsonSerializer.Serialize(this);

    public static bool TryParseMode(string? value, out JoinVideosMode mode)
    {
        mode = JoinVideosMode.Auto;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "auto" => true,
            "copy" => SetMode(JoinVideosMode.Copy, out mode),
            "normalize" => SetMode(JoinVideosMode.Normalize, out mode),
            _ => false
        };
    }

    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string>? options,
        out JoinVideosSettings? settings,
        out string? error)
    {
        settings = null;
        error = null;

        if (options is null ||
            !options.TryGetValue(ActionOptionKeys.JoinVideosSettings, out var payload) ||
            string.IsNullOrWhiteSpace(payload))
        {
            error = MediaActionMessages.JoinVideosSettingsMissing();
            return false;
        }

        try
        {
            settings = JsonSerializer.Deserialize<JoinVideosSettings>(payload);
        }
        catch (JsonException)
        {
            error = MediaActionMessages.JoinVideosSettingsInvalid();
            return false;
        }

        if (settings is null ||
            settings.InputPaths.Count < 2 ||
            settings.InputPaths.Any(string.IsNullOrWhiteSpace) ||
            !Enum.IsDefined(settings.Mode))
        {
            error = MediaActionMessages.JoinVideosRequiresAtLeastTwoVideos();
            return false;
        }

        return true;
    }

    private static bool SetMode(JoinVideosMode value, out JoinVideosMode mode)
    {
        mode = value;
        return true;
    }
}
