using System;
using System.Collections.Generic;
using System.Globalization;

namespace FrameShift.Core.Actions;

public sealed record RifeInterpolateVideoSettings(
    string ModelId,
    int TargetMultiplier,
    int PlaybackDivisor,
    bool RemoveAudioInSlowMotion = false,
    bool KeepPitchInSlowMotion = true)
{
    public bool IsSlowMotion => PlaybackDivisor > 1;

    public double GetOutputFrameRate(double sourceFps)
    {
        return sourceFps * TargetMultiplier / PlaybackDivisor;
    }

    public bool PreservesAudio => !IsSlowMotion || !RemoveAudioInSlowMotion;

    public bool KeepPitch => !IsSlowMotion || (PreservesAudio && KeepPitchInSlowMotion);

    public string OutputSuffix => PlaybackDivisor == 1
        ? $"_rife_x{TargetMultiplier}"
        : $"_rife_x{TargetMultiplier}_slowx{PlaybackDivisor}";

    public IReadOnlyDictionary<string, string> ToOptions()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionOptionKeys.InterpolateModelId] = ModelId,
            [ActionOptionKeys.InterpolateMultiplier] = TargetMultiplier.ToString(CultureInfo.InvariantCulture),
            [ActionOptionKeys.InterpolatePlaybackDivisor] = PlaybackDivisor.ToString(CultureInfo.InvariantCulture),
            [ActionOptionKeys.InterpolateSlowMotionRemoveAudio] = RemoveAudioInSlowMotion.ToString(),
            [ActionOptionKeys.InterpolateSlowMotionKeepPitch] = KeepPitchInSlowMotion.ToString()
        };
    }

    public static bool ContainsAnyOption(IReadOnlyDictionary<string, string>? options)
    {
        return options is not null &&
            (options.ContainsKey(ActionOptionKeys.InterpolateModelId) ||
             options.ContainsKey(ActionOptionKeys.InterpolateMultiplier) ||
             options.ContainsKey(ActionOptionKeys.InterpolatePlaybackDivisor));
    }

    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string>? options,
        out RifeInterpolateVideoSettings? settings,
        out string? errorMessage)
    {
        settings = null;
        errorMessage = null;

        if (options is null ||
            !options.TryGetValue(ActionOptionKeys.InterpolateModelId, out var modelId) ||
            string.IsNullOrWhiteSpace(modelId) ||
            !options.TryGetValue(ActionOptionKeys.InterpolateMultiplier, out var multiplierText) ||
            string.IsNullOrWhiteSpace(multiplierText))
        {
            errorMessage = MediaActionMessages.RifeInterpolateVideoSettingsMissing();
            return false;
        }

        if (!int.TryParse(multiplierText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var multiplier) || multiplier < 2)
        {
            errorMessage = MediaActionMessages.RifeInterpolateVideoSettingsInvalid();
            return false;
        }

        var removeAudioInSlowMotion = false;
        if (options.TryGetValue(ActionOptionKeys.InterpolateSlowMotionRemoveAudio, out var removeAudioText) &&
            !string.IsNullOrWhiteSpace(removeAudioText) &&
            !bool.TryParse(removeAudioText, out removeAudioInSlowMotion))
        {
            errorMessage = MediaActionMessages.RifeInterpolateVideoSettingsInvalid();
            return false;
        }

        var keepPitchInSlowMotion = true;
        if (options.TryGetValue(ActionOptionKeys.InterpolateSlowMotionKeepPitch, out var keepPitchText) &&
            !string.IsNullOrWhiteSpace(keepPitchText) &&
            !bool.TryParse(keepPitchText, out keepPitchInSlowMotion))
        {
            errorMessage = MediaActionMessages.RifeInterpolateVideoSettingsInvalid();
            return false;
        }

        var playbackDivisor = 1;
        if (options.TryGetValue(ActionOptionKeys.InterpolatePlaybackDivisor, out var playbackText) &&
            !string.IsNullOrWhiteSpace(playbackText))
        {
            if (!int.TryParse(playbackText, NumberStyles.Integer, CultureInfo.InvariantCulture, out playbackDivisor) ||
                (playbackDivisor is not 1 and not 2 and not 4))
            {
                errorMessage = MediaActionMessages.RifeInterpolateVideoSettingsInvalid();
                return false;
            }
        }

        settings = new RifeInterpolateVideoSettings(
            modelId.Trim(),
            multiplier,
            playbackDivisor,
            removeAudioInSlowMotion,
            keepPitchInSlowMotion);
        return true;
    }
}
