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
    private static bool EnsureConvertToIconOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Convert to Icon"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!ImageCropSupport.IsSupportedExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageCropSupport.GetSupportedExtensionsText()));
            return false;
        }

        if (ConvertToIconSettings.ContainsAnyOption(options))
        {
            if (!ConvertToIconSettings.TryFromOptions(options, out _, out var settingsError))
            {
                ShowCliError(settingsError ?? MediaActionMessages.ConvertToIconSettingsInvalid());
                return false;
            }

            return true;
        }

        var toolLocator = new ToolLocator();
        var ffmpegRunner = new FfmpegRunner(logger);
        try
        {
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            string? temporaryPreviewPath = null;
            var previewPath = inputPaths[0];

            if (string.Equals(sourceExtension, ".webp", StringComparison.OrdinalIgnoreCase))
            {
                temporaryPreviewPath = CreateTemporaryIconPreviewPng(ffmpegPath, ffmpegRunner, inputPaths[0]);
                previewPath = temporaryPreviewPath;
            }

            try
            {
                using var picker = new ConvertToIconForm(inputPaths[0], previewPath);
                if (picker.ShowDialog() != DialogResult.OK || picker.Selection is null)
                {
                    return false;
                }

                foreach (var item in picker.Selection.ToOptions())
                {
                    options[item.Key] = item.Value;
                }

                return true;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(temporaryPreviewPath) && File.Exists(temporaryPreviewPath))
                {
                    File.Delete(temporaryPreviewPath);
                }
            }
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Convert to Icon")));
            return false;
        }
    }

    private static bool EnsureCropVideoOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Crop Video"));
            return false;
        }

        if (options.ContainsKey(ActionOptionKeys.CropX) &&
            options.ContainsKey(ActionOptionKeys.CropY) &&
            options.ContainsKey(ActionOptionKeys.CropWidth) &&
            options.ContainsKey(ActionOptionKeys.CropHeight))
        {
            return true;
        }

        var toolLocator = new ToolLocator();
        var ffmpegRunner = new FfmpegRunner(logger);
        var ffprobeRunner = new FfprobeRunner(logger);
        try
        {
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            var ffprobePath = toolLocator.ResolveFfprobePath();
            var probeAttempt = ffprobeRunner.TryProbeMediaAsync(ffprobePath, inputPaths[0], CancellationToken.None).GetAwaiter().GetResult();

            if (probeAttempt.Probe is null)
            {
                ShowCliError(probeAttempt.ErrorMessage ?? MediaActionMessages.ProbeFailed());
                return false;
            }

            var probe = probeAttempt.Probe;
            if (!probe.HasVideo)
            {
                ShowCliError(MediaActionMessages.MissingVideoTrack());
                return false;
            }

            using var picker = new CropVideoForm(inputPaths[0], ffmpegPath, probe, ffmpegRunner);
            if (picker.ShowDialog() != DialogResult.OK || picker.Selection is null)
            {
                return false;
            }

            foreach (var item in picker.Selection.ToOptions())
            {
                options[item.Key] = item.Value;
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Crop Video")));
            return false;
        }
    }

    private static bool EnsureCropImageOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Crop Image"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!ImageCropSupport.IsSupportedExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageCropSupport.GetSupportedExtensionsText()));
            return false;
        }

        if (options.ContainsKey(ActionOptionKeys.CropX) &&
            options.ContainsKey(ActionOptionKeys.CropY) &&
            options.ContainsKey(ActionOptionKeys.CropWidth) &&
            options.ContainsKey(ActionOptionKeys.CropHeight))
        {
            return true;
        }

        var toolLocator = new ToolLocator();
        var ffmpegRunner = new FfmpegRunner(logger);
        try
        {
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            using var picker = new CropImageForm(inputPaths[0], ffmpegPath, ffmpegRunner);
            if (picker.ShowDialog() != DialogResult.OK || picker.Selection is null)
            {
                return false;
            }

            foreach (var item in picker.Selection.ToOptions())
            {
                options[item.Key] = item.Value;
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Crop Image")));
            return false;
        }
    }

    private static bool EnsureRotateFlipVideoOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Rotate / Flip Video"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        if (RotateFlipSettings.ContainsAnyOption(options))
        {
            if (!RotateFlipSettings.TryFromOptions(options, out _, out var settingsError))
            {
                ShowCliError(settingsError ?? MediaActionMessages.RotateFlipSettingsMissing());
                return false;
            }

            return true;
        }

        var toolLocator = new ToolLocator();
        var ffmpegRunner = new FfmpegRunner(logger);
        var ffprobeRunner = new FfprobeRunner(logger);
        try
        {
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            var ffprobePath = toolLocator.ResolveFfprobePath();
            var probeAttempt = ffprobeRunner.TryProbeMediaAsync(ffprobePath, inputPaths[0], CancellationToken.None).GetAwaiter().GetResult();

            if (probeAttempt.Probe is null)
            {
                ShowCliError(probeAttempt.ErrorMessage ?? MediaActionMessages.ProbeFailed());
                return false;
            }

            var probe = probeAttempt.Probe;
            if (!probe.HasVideo)
            {
                ShowCliError(MediaActionMessages.MissingVideoTrack());
                return false;
            }

            using var picker = new RotateFlipVideoForm(inputPaths[0], ffmpegPath, probe, ffmpegRunner);
            if (picker.ShowDialog() != DialogResult.OK || picker.Selection is null)
            {
                return false;
            }

            foreach (var item in picker.Selection.ToOptions())
            {
                options[item.Key] = item.Value;
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Rotate / Flip Video")));
            return false;
        }
    }

    private static bool EnsureRotateFlipImageOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Rotate / Flip Image"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!ImageCropSupport.IsSupportedExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageCropSupport.GetSupportedExtensionsText()));
            return false;
        }

        if (RotateFlipSettings.ContainsAnyOption(options))
        {
            if (!RotateFlipSettings.TryFromOptions(options, out _, out var settingsError))
            {
                ShowCliError(settingsError ?? MediaActionMessages.RotateFlipSettingsMissing());
                return false;
            }

            return true;
        }

        var toolLocator = new ToolLocator();
        var ffmpegRunner = new FfmpegRunner(logger);
        try
        {
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            using var picker = new RotateFlipImageForm(inputPaths[0], ffmpegPath, ffmpegRunner);
            if (picker.ShowDialog() != DialogResult.OK || picker.Selection is null)
            {
                return false;
            }

            foreach (var item in picker.Selection.ToOptions())
            {
                options[item.Key] = item.Value;
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Rotate / Flip Image")));
            return false;
        }
    }

    private static string CreateTemporaryIconPreviewPng(string ffmpegPath, FfmpegRunner ffmpegRunner, string inputPath)
    {
        var previewPath = Path.Combine(Path.GetTempPath(), $"frameshift_icon_preview_{Guid.NewGuid():N}.png");
        var result = ffmpegRunner.RunCaptureAsync(
            ffmpegPath,
            [
                "-hide_banner",
                "-loglevel", "error",
                "-y",
                "-i", inputPath,
                "-frames:v", "1",
                "-update", "1",
                previewPath
            ],
            CancellationToken.None).GetAwaiter().GetResult();

        if (!result.Success || !File.Exists(previewPath))
        {
            if (File.Exists(previewPath))
            {
                File.Delete(previewPath);
            }

            throw new InvalidOperationException(ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, MediaActionMessages.ConvertToIconFailed()));
        }

        return previewPath;
    }
}
