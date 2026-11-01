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
        CreateSubtitlesAssPreset.Classic => L.Get("CreateSubtitlesAssPreset_Classic_c572f120"),
        CreateSubtitlesAssPreset.WordHighlight => L.Get("CreateSubtitlesAssPreset_Word_Highlight_d3f9e56e"),
        _ => L.Get("CreateSubtitlesAssPreset_Progressive_Reveal_4004e769")
    };

    public static string GetDescription(this CreateSubtitlesAssPreset preset) => preset switch
    {
        CreateSubtitlesAssPreset.Classic => L.Get("CreateSubtitlesAssPreset_Full_sentence_current_ASS_rendering_6b55673b"),
        CreateSubtitlesAssPreset.WordHighlight => L.Get("CreateSubtitlesAssPreset_Full_sentence_stays_visible_while_th_3bfa55cf"),
        _ => L.Get("CreateSubtitlesAssPreset_Words_appear_progressively_as_the_se_3933eb3e")
    };

    public static IReadOnlyList<CreateSubtitlesAssPreset> GetAll() =>
    [
        CreateSubtitlesAssPreset.Classic,
        CreateSubtitlesAssPreset.WordHighlight,
        CreateSubtitlesAssPreset.ProgressiveReveal
    ];
}
