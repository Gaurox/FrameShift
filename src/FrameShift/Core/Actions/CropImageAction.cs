using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class CropImageAction : IFrameShiftAction
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly ToolLocator _toolLocator;

    public CropImageAction(FfmpegRunner ffmpegRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "crop-image",
        "Crop Image",
        "Crops an image using a fixed rectangle selected in the crop window.");

    public async Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath))
        {
            return new ActionExecutionResult(false, MediaActionMessages.InputPathRequired());
        }

        if (!File.Exists(request.InputPath))
        {
            return new ActionExecutionResult(false, MediaActionMessages.InputFileNotFound(request.InputPath));
        }

        var sourceExtension = Path.GetExtension(request.InputPath).ToLowerInvariant();
        if (!ImageCropSupport.IsSupportedExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageCropSupport.GetSupportedExtensionsText()));
        }

        if (!VideoCropSettings.TryFromOptions(request.Options, out var crop, out var cropError))
        {
            return new ActionExecutionResult(false, cropError ?? MediaActionMessages.CropSettingsMissing());
        }

        crop = NormalizeCropRect(crop.X, crop.Y, crop.Width, crop.Height);

        var ffmpegPath = _toolLocator.ResolveFfmpegPath();
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, "_crop");

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.ProgressReporter?.ReportState("processing", "Preparing image crop on CPU...");

        try
        {
            var arguments = BuildArguments(request.InputPath, outputPath, sourceExtension, crop);
            var result = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                null,
                request.ProgressReporter,
                request.InputPath,
                Descriptor.DisplayName,
                "CPU",
                cancellationToken).ConfigureAwait(false);

            if (result.Canceled)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, result.CancellationScope);
            }

            if (result.ExitCode != 0)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ImageCropSupport.GetFriendlyError(result.StandardError);
                request.ProgressReporter?.ReportState("failed", failureMessage);
                return new ActionExecutionResult(false, failureMessage, null, false);
            }

            request.ProgressReporter?.ReportProgress(1000, request.InputPath, Descriptor.DisplayName, "Completed.");
            request.ProgressReporter?.ReportState("done", "Completed.");
            return new ActionExecutionResult(true, MediaActionMessages.Completed(Descriptor.DisplayName), outputPath);
        }
        catch (OperationCanceledException)
        {
            ConversionActionHelper.DeleteIfExists(outputPath);
            request.ProgressReporter?.ReportState("canceled", "Canceled.");
            return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, CancellationScope.All);
        }
        catch (Exception ex)
        {
            ConversionActionHelper.DeleteIfExists(outputPath);
            var failureMessage = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed(Descriptor.DisplayName));
            request.ProgressReporter?.ReportState("failed", failureMessage);
            return new ActionExecutionResult(false, failureMessage);
        }
    }

    private static IReadOnlyList<string> BuildArguments(
        string inputPath,
        string outputPath,
        string sourceExtension,
        VideoCropSettings crop)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-i", inputPath,
            "-vf", $"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}",
            "-frames:v", "1"
        };

        args.AddRange(ImageCropSupport.GetOutputArguments(sourceExtension));
        args.Add(outputPath);
        return args;
    }

    private static VideoCropSettings NormalizeCropRect(int x, int y, int width, int height)
    {
        if (x < 0)
        {
            x = 0;
        }

        if (y < 0)
        {
            y = 0;
        }

        if (width < 2)
        {
            width = 2;
        }

        if (height < 2)
        {
            height = 2;
        }

        return new VideoCropSettings(x, y, width, height);
    }

}
