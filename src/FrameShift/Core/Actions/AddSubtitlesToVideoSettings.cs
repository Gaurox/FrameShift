using System;
using System.Collections.Generic;
using System.IO;
using FrameShift.Core.AI.CreateSubtitles;

namespace FrameShift.Core.Actions;

internal sealed record AddSubtitlesToVideoSettings(
    string SubtitleFilePath,
    AddSubtitlesToVideoMode Mode = AddSubtitlesToVideoMode.SelectableTrack,
    AddSubtitlesToVideoBurnSettings? BurnSettings = null)
{
    public IReadOnlyDictionary<string, string> ToOptions()
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionOptionKeys.SubtitleFilePath] = SubtitleFilePath,
            [ActionOptionKeys.SubtitleMode] = Mode.ToOptionValue()
        };

        if (Mode == AddSubtitlesToVideoMode.BurnIntoVideo)
        {
            foreach (var option in (BurnSettings ?? AddSubtitlesToVideoBurnSettings.Default).ToOptions())
            {
                options[option.Key] = option.Value;
            }
        }

        return options;
    }

    public static bool ContainsAnyOption(IReadOnlyDictionary<string, string>? options)
    {
        return options is not null &&
               (options.ContainsKey(ActionOptionKeys.SubtitleFilePath) || options.ContainsKey(ActionOptionKeys.SubtitleMode));
    }

    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string>? options,
        out AddSubtitlesToVideoSettings settings,
        out string? error)
    {
        settings = default!;
        error = null;

        var mode = AddSubtitlesToVideoModes.Default;
        if (options is not null &&
            options.TryGetValue(ActionOptionKeys.SubtitleMode, out var modeValue) &&
            !string.IsNullOrWhiteSpace(modeValue) &&
            !AddSubtitlesToVideoModes.TryParse(modeValue, out mode))
        {
            error = MediaActionMessages.AddSubtitlesToVideoModeInvalid();
            return false;
        }

        if (options is null ||
            !options.TryGetValue(ActionOptionKeys.SubtitleFilePath, out var subtitleFilePath) ||
            string.IsNullOrWhiteSpace(subtitleFilePath))
        {
            error = MediaActionMessages.AddSubtitlesToVideoSettingsMissing();
            return false;
        }

        if (!IsSupportedSubtitleFilePath(subtitleFilePath, mode))
        {
            error = mode == AddSubtitlesToVideoMode.BurnIntoVideo
                ? MediaActionMessages.AddSubtitlesToVideoBurnSubtitleFormatInvalid()
                : MediaActionMessages.AddSubtitlesToVideoSelectableSubtitleFormatInvalid();
            return false;
        }

        if (!AddSubtitlesToVideoBurnSettings.TryFromOptions(options, out var burnSettings, out var burnError))
        {
            error = burnError ?? MediaActionMessages.AddSubtitlesToVideoBurnSettingsInvalid();
            return false;
        }

        settings = new AddSubtitlesToVideoSettings(subtitleFilePath.Trim(), mode, burnSettings);
        return true;
    }

    public static bool IsSupportedSubtitleExtension(string extension)
    {
        return string.Equals(extension?.Trim(), ".srt", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupportedSubtitleFilePath(string? subtitleFilePath, AddSubtitlesToVideoMode mode)
    {
        if (string.IsNullOrWhiteSpace(subtitleFilePath))
        {
            return false;
        }

        if (mode == AddSubtitlesToVideoMode.SelectableTrack)
        {
            return IsSupportedSubtitleExtension(Path.GetExtension(subtitleFilePath));
        }

        if (subtitleFilePath.EndsWith(CreateSubtitlesProjectSerializer.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var extension = Path.GetExtension(subtitleFilePath);
        return string.Equals(extension, ".srt", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".ass", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetSupportedSubtitleFormatsText(AddSubtitlesToVideoMode mode) => mode switch
    {
        AddSubtitlesToVideoMode.BurnIntoVideo => $".srt, .ass and {CreateSubtitlesProjectSerializer.FileExtension}",
        _ => ".srt"
    };
}
