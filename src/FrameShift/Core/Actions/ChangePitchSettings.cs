using System;
using System.Collections.Generic;
using System.Globalization;

namespace FrameShift.Core.Actions;

public sealed record ChangePitchSettings(double Semitones, bool KeepDuration)
{
    public const double MinSemitones = -24.0;
    public const double MaxSemitones = 24.0;

    public double PitchFactor => Math.Pow(2.0, Semitones / 12.0);

    public double EstimatedOutputDuration(double sourceDurationSeconds)
    {
        return KeepDuration ? sourceDurationSeconds : sourceDurationSeconds / PitchFactor;
    }

    public string OutputSuffix
    {
        get
        {
            var rounded = (int)Math.Round(Semitones);
            return rounded switch
            {
                > 0 => $"_pitch_plus{rounded}st",
                < 0 => $"_pitch_minus{Math.Abs(rounded)}st",
                _   => "_pitch_0st"
            };
        }
    }

    public IReadOnlyDictionary<string, string> ToOptions()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionOptionKeys.PitchSemitones] = Semitones.ToString("0.######", CultureInfo.InvariantCulture),
            [ActionOptionKeys.PitchKeepDuration] = KeepDuration ? "true" : "false"
        };
    }

    public static bool ContainsAnyOption(IReadOnlyDictionary<string, string>? options)
    {
        return options is not null && options.ContainsKey(ActionOptionKeys.PitchSemitones);
    }

    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string>? options,
        out ChangePitchSettings? settings,
        out string? errorMessage)
    {
        settings = null;
        errorMessage = null;

        if (options is null || !options.TryGetValue(ActionOptionKeys.PitchSemitones, out var semiText) ||
            string.IsNullOrWhiteSpace(semiText))
        {
            errorMessage = MediaActionMessages.ChangePitchSettingsMissing();
            return false;
        }

        if (!double.TryParse(semiText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var semitones))
        {
            errorMessage = MediaActionMessages.ChangePitchSettingsInvalid();
            return false;
        }

        if (semitones < MinSemitones || semitones > MaxSemitones)
        {
            errorMessage = MediaActionMessages.ChangePitchSemitonesOutOfRange();
            return false;
        }

        var keepDuration = true;
        if (options.TryGetValue(ActionOptionKeys.PitchKeepDuration, out var keepText) &&
            !string.IsNullOrWhiteSpace(keepText))
        {
            keepDuration = !string.Equals(keepText.Trim(), "false", StringComparison.OrdinalIgnoreCase);
        }

        var candidate = new ChangePitchSettings(semitones, keepDuration);
        if (candidate.PitchFactor <= 0)
        {
            errorMessage = MediaActionMessages.ChangePitchSettingsInvalid();
            return false;
        }

        settings = candidate;
        return true;
    }
}
