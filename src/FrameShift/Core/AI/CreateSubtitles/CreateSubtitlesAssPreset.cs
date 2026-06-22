using System.Collections.Generic;

namespace FrameShift.Core.AI.CreateSubtitles;

internal enum CreateSubtitlesAssPreset
{
    Classic,
    WordHighlight,
    ProgressiveReveal
}

internal static class CreateSubtitlesAssPresets
{
    public static CreateSubtitlesAssPreset Default => CreateSubtitlesAssPreset.Classic;

    public static bool TryParse(string? value, out CreateSubtitlesAssPreset preset)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "classic":
                preset = CreateSubtitlesAssPreset.Classic;
                return true;

            case "word-highlight":
            case "highlight":
                preset = CreateSubtitlesAssPreset.WordHighlight;
                return true;

            case "progressive-reveal":
            case "reveal":
                preset = CreateSubtitlesAssPreset.ProgressiveReveal;
                return true;

            default:
                preset = Default;
                return false;
        }
    }

    public static string ToOptionValue(this CreateSubtitlesAssPreset preset) => preset switch
    {
        CreateSubtitlesAssPreset.Classic => "classic",
        CreateSubtitlesAssPreset.WordHighlight => "word-highlight",
        _ => "progressive-reveal"
    };

    public static string GetDisplayName(this CreateSubtitlesAssPreset preset) => preset switch
    {
        CreateSubtitlesAssPreset.Classic => "Classic",
        CreateSubtitlesAssPreset.WordHighlight => "Word Highlight",
        _ => "Progressive Reveal"
    };

    public static string GetDescription(this CreateSubtitlesAssPreset preset) => preset switch
    {
        CreateSubtitlesAssPreset.Classic => "Full sentence, current ASS rendering.",
        CreateSubtitlesAssPreset.WordHighlight => "Full sentence stays visible while the current word is highlighted.",
        _ => "Words appear progressively as the segment advances."
    };

    public static IReadOnlyList<CreateSubtitlesAssPreset> GetAll() =>
    [
        CreateSubtitlesAssPreset.Classic,
        CreateSubtitlesAssPreset.WordHighlight,
        CreateSubtitlesAssPreset.ProgressiveReveal
    ];
}
