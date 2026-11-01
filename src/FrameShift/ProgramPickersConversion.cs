using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrameShift.Core.Actions;
using FrameShift.Windows.Forms;

namespace FrameShift;

internal static partial class Program
{
    private static bool EnsureConvertVideoOptions(List<string> inputPaths, Dictionary<string, string> options)
    {
        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        return EnsureConversionOptions(
            inputPaths,
            options,
            L.Get("ProgramPickersConversion_FrameShift_Convert_Video_5b59db29"),
            L.Get("ProgramPickersConversion_Select_a_target_container_and_a_prof_9c3037fd"),
            VideoConversionCatalog.GetTargets(),
            VideoConversionCatalog.GetProfiles(),
            true);
    }

    private static bool EnsureConvertAudioOptions(List<string> inputPaths, Dictionary<string, string> options)
    {
        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!AudioConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, AudioConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        return EnsureConversionOptions(
            inputPaths,
            options,
            L.Get("ProgramPickersConversion_FrameShift_Convert_Audio_5533886d"),
            L.Get("ProgramPickersConversion_Select_an_output_format_and_a_profil_31b8e5ca"),
            AudioConversionCatalog.GetTargets(),
            AudioConversionCatalog.GetProfiles(),
            true);
    }

    private static bool EnsureConvertImageOptions(List<string> inputPaths, Dictionary<string, string> options)
    {
        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!ImageConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        return EnsureConversionOptions(
            inputPaths,
            options,
            L.Get("ProgramPickersConversion_FrameShift_Convert_Image_8258f3d7"),
            L.Get("ProgramPickersConversion_Select_an_output_format_and_a_profil_31b8e5ca"),
            ImageConversionCatalog.GetTargets(),
            ImageConversionCatalog.GetProfiles(),
            true);
    }

    private static bool EnsureExtractAudioOptions(List<string> inputPaths, Dictionary<string, string> options)
    {
        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        return EnsureConversionOptions(
            inputPaths,
            options,
            L.Get("ProgramPickersConversion_FrameShift_Extract_Audio_3f130bf9"),
            L.Get("ProgramPickersConversion_Choose_the_output_audio_format_8396bb3b"),
            ExtractAudioCatalog.GetTargets(),
            [],
            false,
            "Extract");
    }

    private static bool EnsureConversionOptions(
        List<string> inputPaths,
        Dictionary<string, string> options,
        string title,
        string description,
        IReadOnlyList<IConversionChoice> targets,
        IReadOnlyList<IConversionChoice> profiles,
        bool showProfiles,
        string primaryButtonText = "Convert")
    {
        if (options.ContainsKey(ActionOptionKeys.Target) && (!showProfiles || options.ContainsKey(ActionOptionKeys.Profile)))
        {
            return true;
        }

        if (targets.Count == 0)
        {
            ShowCliError(MediaActionMessages.NoAlternativeTargetFormatsAvailable());
            return false;
        }

        var sourceLabel = inputPaths.Count == 1
            ? FormatSelectionLabel(Path.GetFileName(inputPaths[0]))
            : L.Format("ProgramPickersConversion_0_selected_files_dd068a4b",inputPaths.Count);

        var initialTargetId = options.TryGetValue(ActionOptionKeys.Target, out var targetId) ? targetId : targets[0].Id;
        var initialProfileId = showProfiles && options.TryGetValue(ActionOptionKeys.Profile, out var profileId) ? profileId : profiles.FirstOrDefault()?.Id;

        using var picker = new ConversionPickerForm(
            title,
            sourceLabel,
            description,
            targets,
            profiles,
            initialTargetId,
            initialProfileId,
            primaryButtonText);

        if (picker.ShowDialog() != DialogResult.OK || picker.Selection is null)
        {
            return false;
        }

        options[ActionOptionKeys.Target] = picker.Selection.TargetId;
        if (showProfiles && !string.IsNullOrWhiteSpace(picker.Selection.ProfileId))
        {
            options[ActionOptionKeys.Profile] = picker.Selection.ProfileId!;
        }

        return true;
    }

    private static string FormatSelectionLabel(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return L.Get("ProgramPickersConversion_selected_file_d65a205d");
        }

        const int maxLength = 72;
        if (fileName.Length <= maxLength)
        {
            return fileName;
        }

        var prefixLength = 34;
        var suffixLength = 30;
        return $"{fileName[..prefixLength]}...{fileName[^suffixLength..]}";
    }
}
