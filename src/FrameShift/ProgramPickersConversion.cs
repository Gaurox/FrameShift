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
            "FrameShift - Convert Video",
            "Select a target container and a profile.",
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
            "FrameShift - Convert Audio",
            "Select an output format and a profile.",
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
            "FrameShift - Convert Image",
            "Select an output format and a profile.",
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
            "FrameShift - Extract Audio",
            "Choose the output audio format.",
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
            : $"{inputPaths.Count} selected files";

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
            return "selected file";
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
