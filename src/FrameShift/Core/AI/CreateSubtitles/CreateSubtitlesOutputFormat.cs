using System;
using System.Collections.Generic;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.AI.CreateSubtitles;

internal enum CreateSubtitlesOutputFormat
{
    StandardSrt,
    AdvancedAss,
    FrameShiftSubtitleProject
}

internal static class CreateSubtitlesOutputFormats
{
    public static CreateSubtitlesOutputFormat Default => CreateSubtitlesOutputFormat.StandardSrt;

    public static bool TryParse(string? value, out CreateSubtitlesOutputFormat format)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "srt":
            case "standard-srt":
                format = CreateSubtitlesOutputFormat.StandardSrt;
                return true;

            case "ass":
            case "advanced-ass":
                format = CreateSubtitlesOutputFormat.AdvancedAss;
                return true;

            case "project":
            case "frameshift-project":
            case "frameshift-subtitle-project":
                format = CreateSubtitlesOutputFormat.FrameShiftSubtitleProject;
                return true;

            default:
                format = Default;
                return false;
        }
    }

    public static string ToOptionValue(this CreateSubtitlesOutputFormat format) => format switch
    {
        CreateSubtitlesOutputFormat.StandardSrt => "srt",
        CreateSubtitlesOutputFormat.AdvancedAss => "ass",
        _ => "project"
    };

    public static string GetDisplayName(this CreateSubtitlesOutputFormat format) => format switch
    {
        CreateSubtitlesOutputFormat.StandardSrt => L.Get("CreateSubtitlesOutputFormat_Standard_SRT_076c290a"),
        CreateSubtitlesOutputFormat.AdvancedAss => L.Get("CreateSubtitlesOutputFormat_Advanced_ASS_Subtitle_2ed5f762"),
        _ => L.Get("CreateSubtitlesOutputFormat_FrameShift_Customization_Project_d045e460")
    };

    public static string GetDescription(this CreateSubtitlesOutputFormat format) => format switch
    {
        CreateSubtitlesOutputFormat.StandardSrt => L.Get("CreateSubtitlesOutputFormat_Maximum_compatibility_Text_and_timin_bc15f80c"),
        CreateSubtitlesOutputFormat.AdvancedAss => L.Get("CreateSubtitlesOutputFormat_Creates_a_finished_subtitle_file_wit_a5d99393"),
        _ => L.Get("CreateSubtitlesOutputFormat_Saves_all_word_timings_so_you_can_la_d9f0a83f")
    };

    public static string GetOutputExtension(this CreateSubtitlesOutputFormat format) => format switch
    {
        CreateSubtitlesOutputFormat.StandardSrt => ".srt",
        CreateSubtitlesOutputFormat.AdvancedAss => ".ass",
        _ => CreateSubtitlesProjectSerializer.FileExtension
    };

    public static string GetBuildMessage(this CreateSubtitlesOutputFormat format) => format switch
    {
        CreateSubtitlesOutputFormat.StandardSrt => L.Get("CreateSubtitlesOutputFormat_Building_SRT_subtitles_104bcbe4"),
        CreateSubtitlesOutputFormat.AdvancedAss => L.Get("CreateSubtitlesOutputFormat_Building_ASS_subtitles_93cc54eb"),
        _ => L.Get("CreateSubtitlesOutputFormat_Building_subtitle_project_db86efa7")
    };

    public static string FormatProject(
        SubtitleProject project,
        CreateSubtitlesOutputFormat format,
        CreateSubtitlesAssPreset assPreset = CreateSubtitlesAssPreset.Classic) => format switch
    {
        CreateSubtitlesOutputFormat.StandardSrt => CreateSubtitlesSrtFormatter.Format(project),
        CreateSubtitlesOutputFormat.AdvancedAss => CreateSubtitlesAssFormatter.Format(project, assPreset),
        _ => CreateSubtitlesProjectSerializer.Serialize(project)
    };

    public static string CreateOutputPath(string inputPath, CreateSubtitlesOutputFormat format) =>
        OutputPathHelper.CreateUniqueOutputPath(inputPath, string.Empty, format.GetOutputExtension());

    public static string GetDesiredOutputPath(string inputPath, CreateSubtitlesOutputFormat format) =>
        OutputPathHelper.GetOutputPath(inputPath, string.Empty, format.GetOutputExtension());

    public static IReadOnlyList<CreateSubtitlesOutputFormat> GetAll() =>
    [
        CreateSubtitlesOutputFormat.StandardSrt,
        CreateSubtitlesOutputFormat.AdvancedAss,
        CreateSubtitlesOutputFormat.FrameShiftSubtitleProject
    ];
}
