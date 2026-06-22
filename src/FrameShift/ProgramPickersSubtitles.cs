using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Windows.Forms;

namespace FrameShift;

internal static partial class Program
{
    private static bool EnsureAddSubtitlesToVideoOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Add Subtitles to Video"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        if (AddSubtitlesToVideoSettings.ContainsAnyOption(options))
        {
            if (!AddSubtitlesToVideoSettings.TryFromOptions(options, out var settings, out var settingsError) || settings is null)
            {
                return ShowAddSubtitlesToVideoPicker(inputPaths[0], options, logger);
            }

            if (!File.Exists(settings.SubtitleFilePath))
            {
                ShowCliError(MediaActionMessages.InputFileNotFound(settings.SubtitleFilePath));
                return false;
            }

            return true;
        }

        return ShowAddSubtitlesToVideoPicker(inputPaths[0], options, logger);
    }

    private static bool ShowAddSubtitlesToVideoPicker(string inputPath, Dictionary<string, string> options, AppLogger logger)
    {
        try
        {
            var initialMode = AddSubtitlesToVideoModes.Default;
            if (options.TryGetValue(ActionOptionKeys.SubtitleMode, out var modeValue) &&
                !string.IsNullOrWhiteSpace(modeValue) &&
                !AddSubtitlesToVideoModes.TryParse(modeValue, out initialMode))
            {
                initialMode = AddSubtitlesToVideoModes.Default;
            }

            options.TryGetValue(ActionOptionKeys.SubtitleFilePath, out var initialSubtitlePath);

            using var picker = new AddSubtitlesToVideoPickerForm(
                Path.GetFileName(inputPath),
                initialMode,
                initialSubtitlePath,
                Path.GetDirectoryName(inputPath));

            if (picker.ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            var selectedSettings = picker.SelectedSettings;
            if (selectedSettings.Mode == AddSubtitlesToVideoMode.BurnIntoVideo)
            {
                var editedSettings = OpenBurnEditor(inputPath, selectedSettings, logger);
                if (editedSettings is null)
                {
                    return false;
                }

                selectedSettings = editedSettings;
            }

            foreach (var option in selectedSettings.ToOptions())
            {
                options[option.Key] = option.Value;
            }

            logger.Log($"Program: Add Subtitles to Video options selected via dialog. mode={selectedSettings.Mode.ToOptionValue()}, subtitleFile={selectedSettings.SubtitleFilePath}.");
            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Add Subtitles to Video")));
            return false;
        }
    }

    private static AddSubtitlesToVideoSettings? OpenBurnEditor(string inputPath, AddSubtitlesToVideoSettings settings, AppLogger logger)
    {
        var toolLocator = new ToolLocator();
        var ffprobeRunner = new FfprobeRunner(logger);
        var ffprobePath = toolLocator.ResolveFfprobePath();
        var probeAttempt = ffprobeRunner.TryProbeMediaAsync(ffprobePath, inputPath, CancellationToken.None).GetAwaiter().GetResult();
        if (probeAttempt.Probe is null)
        {
            ShowCliError(probeAttempt.ErrorMessage ?? MediaActionMessages.ProbeFailed());
            return null;
        }

        using var editor = new AddSubtitlesToVideoBurnEditorForm(
            inputPath,
            toolLocator.ResolveFfmpegPath(),
            probeAttempt.Probe,
            new FfmpegRunner(logger),
            settings);

        return editor.ShowDialog() == DialogResult.OK
            ? editor.SelectedSettings
            : null;
    }
}
