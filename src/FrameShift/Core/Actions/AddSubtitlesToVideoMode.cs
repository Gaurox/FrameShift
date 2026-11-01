using System;

namespace FrameShift.Core.Actions;

internal enum AddSubtitlesToVideoMode
{
    SelectableTrack,
    BurnIntoVideo
}

internal static class AddSubtitlesToVideoModes
{
    public static AddSubtitlesToVideoMode Default => AddSubtitlesToVideoMode.SelectableTrack;

    public static bool TryParse(string? value, out AddSubtitlesToVideoMode mode)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "selectable":
            case "selectable-track":
            case "track":
                mode = AddSubtitlesToVideoMode.SelectableTrack;
                return true;

            case "burn":
            case "burn-in":
            case "burn-into-video":
                mode = AddSubtitlesToVideoMode.BurnIntoVideo;
                return true;

            default:
                mode = Default;
                return false;
        }
    }

    public static string ToOptionValue(this AddSubtitlesToVideoMode mode) => mode switch
    {
        AddSubtitlesToVideoMode.BurnIntoVideo => "burn",
        _ => "selectable"
    };

    public static string GetDisplayName(this AddSubtitlesToVideoMode mode) => mode switch
    {
        AddSubtitlesToVideoMode.BurnIntoVideo => L.Get("AddSubtitlesToVideoMode_Burn_Subtitles_Into_Video_50d88f09"),
        _ => L.Get("AddSubtitlesToVideoMode_Selectable_Subtitle_Track_4fadfa69")
    };
}
