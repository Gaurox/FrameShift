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
        CreateSubtitlesOutputFormat.StandardSrt => "Standard SRT",
        CreateSubtitlesOutputFormat.AdvancedAss => "Advanced ASS Subtitle",
        _ => "FrameShift Customization Project"
    };

    public static string GetDescription(this CreateSubtitlesOutputFormat format) => format switch
    {
        CreateSubtitlesOutputFormat.StandardSrt => "Maximum compatibility. Text and timing by subtitle block, without custom styling.",
        CreateSubtitlesOutputFormat.AdvancedAss => "Creates a finished subtitle file with the selected style and dynamic effect already applied.",
        _ => "Saves all word timings so you can later customize font, size, colors, position and effects when adding subtitles to a video."
    };

    public static string GetOutputExtension(this CreateSubtitlesOutputFormat format) => format switch
    {
        CreateSubtitlesOutputFormat.StandardSrt => ".srt",
        CreateSubtitlesOutputFormat.AdvancedAss => ".ass",
        _ => CreateSubtitlesProjectSerializer.FileExtension
    };

    public static string GetBuildMessage(this CreateSubtitlesOutputFormat format) => format switch
    {
        CreateSubtitlesOutputFormat.StandardSrt => "Building SRT subtitles...",
        CreateSubtitlesOutputFormat.AdvancedAss => "Building ASS subtitles...",
        _ => "Building subtitle project..."
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

    public static IReadOnlyList<CreateSubtitlesOutputFormat> GetAll() =>
    [
        CreateSubtitlesOutputFormat.StandardSrt,
        CreateSubtitlesOutputFormat.AdvancedAss,
        CreateSubtitlesOutputFormat.FrameShiftSubtitleProject
    ];
}
