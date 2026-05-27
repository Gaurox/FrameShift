using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Windows.Forms;

namespace FrameShift;

internal static partial class Program
{
    private static bool EnsureResizeVideoOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Resize Video"));
            return false;
        }

        if (options.ContainsKey(ActionOptionKeys.ResizeWidth) &&
            options.ContainsKey(ActionOptionKeys.ResizeHeight))
        {
            return true;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        var toolLocator = new ToolLocator();
        var ffprobeRunner = new FfprobeRunner(logger);
        try
        {
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

            if (probe.VideoWidth <= 0 || probe.VideoHeight <= 0)
            {
                ShowCliError(MediaActionMessages.VideoResolutionUnavailable());
                return false;
            }

            using var picker = new ResizeVideoForm(inputPaths[0], probe.VideoWidth, probe.VideoHeight);
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
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Resize Video")));
            return false;
        }
    }

    private static bool EnsureCompressVideoOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Compress Video"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!VideoCompressionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoCompressionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        if (options.TryGetValue(ActionOptionKeys.Profile, out var profileId))
        {
            if (!VideoCompressionCatalog.IsSupportedProfileId(profileId))
            {
                ShowCliError(MediaActionMessages.UnsupportedCompressionProfile(profileId));
                return false;
            }

            return true;
        }

        var toolLocator = new ToolLocator();
        var ffprobeRunner = new FfprobeRunner(logger);
        try
        {
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

            var sourceBytes = new FileInfo(inputPaths[0]).Length;
            var resolutionText = probe.VideoWidth > 0 && probe.VideoHeight > 0
                ? $"{probe.VideoWidth}x{probe.VideoHeight}"
                : "unknown";

            using var picker = new CompressVideoForm(inputPaths[0], sourceExtension, sourceBytes, resolutionText);
            if (picker.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(picker.SelectedProfileId))
            {
                return false;
            }

            options[ActionOptionKeys.Profile] = picker.SelectedProfileId!;
            if (picker.TargetBytes is long targetBytes && targetBytes > 0)
            {
                options[ActionOptionKeys.TargetSizeBytes] = targetBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Compress Video")));
            return false;
        }
    }

    private static bool EnsureCompressAudioOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Compress Audio"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!AudioConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, AudioConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        if (options.TryGetValue(ActionOptionKeys.Profile, out var profileId))
        {
            if (profileId is not (CompressAudioSettings.ProfileHigh or CompressAudioSettings.ProfileBalanced or CompressAudioSettings.ProfileSmall))
            {
                ShowCliError(MediaActionMessages.UnsupportedCompressionProfile(profileId));
                return false;
            }

            return true;
        }

        var toolLocator = new ToolLocator();
        var ffprobeRunner = new FfprobeRunner(logger);
        try
        {
            var ffprobePath = toolLocator.ResolveFfprobePath();
            var probeAttempt = ffprobeRunner.TryProbeMediaAsync(ffprobePath, inputPaths[0], CancellationToken.None).GetAwaiter().GetResult();

            if (probeAttempt.Probe is null)
            {
                ShowCliError(probeAttempt.ErrorMessage ?? MediaActionMessages.ProbeFailed());
                return false;
            }

            var probe = probeAttempt.Probe;
            if (!probe.HasAudio)
            {
                ShowCliError(MediaActionMessages.MissingAudioTrack());
                return false;
            }

            var sourceBytes = new FileInfo(inputPaths[0]).Length;

            using var picker = new CompressAudioForm(inputPaths[0], sourceExtension, sourceBytes, probe.PrimaryAudioSampleRate, probe.PrimaryAudioChannels);
            if (picker.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(picker.SelectedProfileId))
            {
                return false;
            }

            options[ActionOptionKeys.Profile] = picker.SelectedProfileId!;
            if (picker.TargetBytes is long targetBytes && targetBytes > 0)
            {
                options[ActionOptionKeys.TargetSizeBytes] = targetBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Compress Audio")));
            return false;
        }
    }

    private static bool EnsureCompressImageOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Compress Image"));
            return false;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!ImageConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageConversionCatalog.GetSupportedSourceFormatsText()));
            return false;
        }

        if (options.ContainsKey(ActionOptionKeys.Profile) ||
            options.ContainsKey(ActionOptionKeys.Target) ||
            options.ContainsKey(ActionOptionKeys.TargetSizeBytes))
        {
            if (!CompressImageSettings.TryFromOptions(options, out _, out var settingsError))
            {
                ShowCliError(settingsError ?? MediaActionMessages.CompressImageSettingsInvalid());
                return false;
            }

            return true;
        }

        try
        {
            var sourceBytes = new FileInfo(inputPaths[0]).Length;
            using var picker = new CompressImageForm(inputPaths[0], sourceExtension, sourceBytes);
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
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Compress Image")));
            return false;
        }
    }

    private static bool EnsureResizeImageOptions(List<string> inputPaths, Dictionary<string, string> options, AppLogger logger)
    {
        if (inputPaths.Count != 1)
        {
            ShowCliError(MediaActionMessages.ExactlyOneSourceFileRequired("Resize Image"));
            return false;
        }

        if (options.ContainsKey(ActionOptionKeys.ResizeWidth) &&
            options.ContainsKey(ActionOptionKeys.ResizeHeight))
        {
            return true;
        }

        var sourceExtension = Path.GetExtension(inputPaths[0]).ToLowerInvariant();
        if (!ImageCropSupport.IsSupportedExtension(sourceExtension))
        {
            ShowCliError(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageCropSupport.GetSupportedExtensionsText()));
            return false;
        }

        var toolLocator = new ToolLocator();
        var ffprobeRunner = new FfprobeRunner(logger);
        try
        {
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

            if (probe.VideoWidth <= 0 || probe.VideoHeight <= 0)
            {
                ShowCliError(MediaActionMessages.VideoResolutionUnavailable());
                return false;
            }

            using var picker = new ResizeImageForm(inputPaths[0], probe.VideoWidth, probe.VideoHeight);
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
            ShowCliError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Resize Image")));
            return false;
        }
    }
}
