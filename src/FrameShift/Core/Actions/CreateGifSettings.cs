using System;
using System.Collections.Generic;
using System.Globalization;

namespace FrameShift.Core.Actions;

public sealed record CreateGifSettings(
    double StartSeconds,
    double DurationSeconds,
    string ResolutionKey,
    int Fps,
    string QualityKey)
{
    public const string DefaultResolutionKey = "720";
    public const int DefaultFps = 12;
    public const string DefaultQualityKey = "balanced";

    public double EndSeconds => StartSeconds + DurationSeconds;

    public IReadOnlyDictionary<string, string> ToOptions()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionOptionKeys.StartTime] = FormatSeconds(StartSeconds),
            [ActionOptionKeys.Duration] = FormatSeconds(DurationSeconds),
            [ActionOptionKeys.GifResolution] = ResolutionKey,
            [ActionOptionKeys.GifFps] = Fps.ToString(CultureInfo.InvariantCulture),
            [ActionOptionKeys.GifQuality] = QualityKey
        };
    }

    public bool IsCustomRange(double sourceDurationSeconds)
    {
        return StartSeconds > 0.001d || (sourceDurationSeconds - EndSeconds) > 0.001d;
    }

    public static IReadOnlyList<CreateGifPresetItem> GetResolutionItems()
    {
        return
        [
            new CreateGifPresetItem("original", "Original width"),
            new CreateGifPresetItem("1280", "1280 px"),
            new CreateGifPresetItem("960", "960 px"),
            new CreateGifPresetItem("720", "720 px"),
            new CreateGifPresetItem("540", "540 px"),
            new CreateGifPresetItem("360", "360 px")
        ];
    }

    public static IReadOnlyList<CreateGifPresetItem> GetFpsItems()
    {
        return
        [
            new CreateGifPresetItem("8", "8 fps"),
            new CreateGifPresetItem("10", "10 fps"),
            new CreateGifPresetItem("12", "12 fps"),
            new CreateGifPresetItem("15", "15 fps"),
            new CreateGifPresetItem("18", "18 fps"),
            new CreateGifPresetItem("20", "20 fps")
        ];
    }

    public static IReadOnlyList<CreateGifPresetItem> GetQualityItems()
    {
        return
        [
            new CreateGifPresetItem("high", "High"),
            new CreateGifPresetItem("balanced", "Balanced"),
            new CreateGifPresetItem("small", "Small file")
        ];
    }

    public static bool ContainsAnyOption(IReadOnlyDictionary<string, string>? options)
    {
        if (options is null)
        {
            return false;
        }

        return options.ContainsKey(ActionOptionKeys.StartTime) ||
               options.ContainsKey(ActionOptionKeys.EndTime) ||
               options.ContainsKey(ActionOptionKeys.Duration) ||
               options.ContainsKey(ActionOptionKeys.GifResolution) ||
               options.ContainsKey(ActionOptionKeys.GifFps) ||
               options.ContainsKey(ActionOptionKeys.GifQuality);
    }

    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string>? options,
        double sourceDurationSeconds,
        out CreateGifSettings? settings,
        out string? errorMessage)
    {
        settings = null;
        errorMessage = null;

        if (sourceDurationSeconds <= 0)
        {
            errorMessage = MediaActionMessages.DurationUnavailable();
            return false;
        }

        if (options is null)
        {
            errorMessage = MediaActionMessages.CreateGifSettingsMissing();
            return false;
        }

        var resolutionKey = GetOptionValue(options, ActionOptionKeys.GifResolution) ?? DefaultResolutionKey;
        if (!IsSupportedResolutionKey(resolutionKey))
        {
            errorMessage = MediaActionMessages.CreateGifResolutionInvalid();
            return false;
        }

        var qualityKey = GetOptionValue(options, ActionOptionKeys.GifQuality) ?? DefaultQualityKey;
        if (!IsSupportedQualityKey(qualityKey))
        {
            errorMessage = MediaActionMessages.CreateGifQualityInvalid();
            return false;
        }

        var fps = DefaultFps;
        var fpsText = GetOptionValue(options, ActionOptionKeys.GifFps);
        if (!string.IsNullOrWhiteSpace(fpsText))
        {
            if (!int.TryParse(fpsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out fps) ||
                !IsSupportedFps(fps))
            {
                errorMessage = MediaActionMessages.CreateGifFpsInvalid();
                return false;
            }
        }

        var startSeconds = 0d;
        var startText = GetOptionValue(options, ActionOptionKeys.StartTime);
        if (!string.IsNullOrWhiteSpace(startText))
        {
            if (!CutAudioSettings.TryParseTimeText(startText, out startSeconds))
            {
                errorMessage = MediaActionMessages.CreateGifStartTimeInvalid();
                return false;
            }
        }

        if (startSeconds < 0)
        {
            errorMessage = MediaActionMessages.CreateGifStartTimeInvalid();
            return false;
        }

        if (startSeconds >= sourceDurationSeconds)
        {
            errorMessage = MediaActionMessages.CreateGifStartOutsideSource();
            return false;
        }

        var endText = GetOptionValue(options, ActionOptionKeys.EndTime);
        var durationText = GetOptionValue(options, ActionOptionKeys.Duration);
        double durationSeconds;
        if (!string.IsNullOrWhiteSpace(endText))
        {
            if (!CutAudioSettings.TryParseTimeText(endText, out var endSeconds))
            {
                errorMessage = MediaActionMessages.CreateGifEndTimeInvalid();
                return false;
            }

            durationSeconds = endSeconds - startSeconds;
        }
        else if (!string.IsNullOrWhiteSpace(durationText))
        {
            if (!CutAudioSettings.TryParseTimeText(durationText, out durationSeconds))
            {
                errorMessage = MediaActionMessages.CreateGifDurationInvalid();
                return false;
            }
        }
        else
        {
            durationSeconds = sourceDurationSeconds - startSeconds;
        }

        if (durationSeconds <= 0)
        {
            errorMessage = MediaActionMessages.CreateGifDurationInvalid();
            return false;
        }

        if ((startSeconds + durationSeconds) > (sourceDurationSeconds + 0.001d))
        {
            errorMessage = MediaActionMessages.CreateGifRangeInvalid();
            return false;
        }

        settings = new CreateGifSettings(
            startSeconds,
            durationSeconds,
            resolutionKey,
            fps,
            qualityKey);
        return true;
    }

    public static bool IsSupportedResolutionKey(string? resolutionKey)
    {
        return resolutionKey is "original" or "1280" or "960" or "720" or "540" or "360";
    }

    public static bool IsSupportedFps(int fps)
    {
        return fps is 8 or 10 or 12 or 15 or 18 or 20;
    }

    public static bool IsSupportedQualityKey(string? qualityKey)
    {
        return qualityKey is "high" or "balanced" or "small";
    }

    private static string? GetOptionValue(IReadOnlyDictionary<string, string> options, string key)
    {
        foreach (var option in options)
        {
            if (!string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return string.IsNullOrWhiteSpace(option.Value)
                ? null
                : option.Value.Trim();
        }

        return null;
    }

    private static string FormatSeconds(double seconds)
    {
        return seconds.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

public readonly record struct CreateGifPresetItem(string Key, string Label);
