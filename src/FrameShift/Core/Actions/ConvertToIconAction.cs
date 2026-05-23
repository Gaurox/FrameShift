using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class ConvertToIconAction : IFrameShiftAction
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly ToolLocator _toolLocator;

    public ConvertToIconAction(FfmpegRunner ffmpegRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "convert-to-icon",
        "Convert to Icon",
        "Creates a Windows .ico file from a supported image source.");

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

        if (!ConvertToIconSettings.TryFromOptions(request.Options, out var settings, out var settingsError) || settings is null)
        {
            return new ActionExecutionResult(false, settingsError ?? MediaActionMessages.ConvertToIconSettingsMissing());
        }

        var ffmpegPath = _toolLocator.ResolveFfmpegPath();
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, "_icon", ".ico");
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"frameshift_icon_{Guid.NewGuid():N}");

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.ProgressReporter?.ReportState("processing", "Preparing icon conversion on CPU...");

        try
        {
            Directory.CreateDirectory(tempDirectory);

            var pngFiles = new List<string>(settings.Sizes.Count);
            for (var index = 0; index < settings.Sizes.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var size = settings.Sizes[index];
                var pngPath = Path.Combine(tempDirectory, $"icon_{size}.png");
                request.ProgressReporter?.ReportState("processing", $"Generating {size}x{size} icon layer ({index + 1}/{settings.Sizes.Count}) on CPU...");

                var pngResult = await _ffmpegRunner.RunAsync(
                    ffmpegPath,
                    BuildPngArguments(request.InputPath, pngPath, size, settings.FitMode, settings.Background),
                    null,
                    request.ProgressReporter,
                    request.InputPath,
                    Descriptor.DisplayName,
                    "CPU",
                    cancellationToken).ConfigureAwait(false);

                if (pngResult.Canceled)
                {
                    ConversionActionHelper.DeleteIfExists(outputPath);
                    request.ProgressReporter?.ReportState("canceled", "Canceled.");
                    return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, pngResult.CancellationScope);
                }

                if (pngResult.ExitCode != 0 || !File.Exists(pngPath))
                {
                    ConversionActionHelper.DeleteIfExists(outputPath);
                    var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(pngResult.StandardError, MediaActionMessages.ConvertToIconFailed());
                    request.ProgressReporter?.ReportState("failed", failureMessage);
                    return new ActionExecutionResult(false, failureMessage);
                }

                pngFiles.Add(pngPath);
            }

            request.ProgressReporter?.ReportState("processing", "Assembling icon file on CPU...");
            var iconResult = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                BuildIconArguments(pngFiles, outputPath),
                null,
                request.ProgressReporter,
                request.InputPath,
                Descriptor.DisplayName,
                "CPU",
                cancellationToken).ConfigureAwait(false);

            if (iconResult.Canceled)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, iconResult.CancellationScope);
            }

            if (iconResult.ExitCode != 0 || !File.Exists(outputPath))
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(iconResult.StandardError, MediaActionMessages.ConvertToIconFailed());
                request.ProgressReporter?.ReportState("failed", failureMessage);
                return new ActionExecutionResult(false, failureMessage);
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
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    internal static IReadOnlyList<string> BuildPngArguments(string inputPath, string outputPath, int size, string fitMode, string background)
    {
        var backgroundColor = GetBackgroundColor(background);
        var filter = string.Equals(fitMode, "fill", StringComparison.OrdinalIgnoreCase)
            ? $"scale={size}:{size}:force_original_aspect_ratio=increase,crop={size}:{size},setsar=1,format=rgba"
            : $"scale={size}:{size}:force_original_aspect_ratio=decrease,pad={size}:{size}:(ow-iw)/2:(oh-ih)/2:color={backgroundColor},setsar=1,format=rgba";

        return
        [
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-i", inputPath,
            "-frames:v", "1",
            "-vf", filter,
            "-update", "1",
            outputPath
        ];
    }

    internal static IReadOnlyList<string> BuildIconArguments(IReadOnlyList<string> pngFiles, string outputPath)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y"
        };

        for (var index = 0; index < pngFiles.Count; index++)
        {
            arguments.Add("-i");
            arguments.Add(pngFiles[index]);
        }

        for (var index = 0; index < pngFiles.Count; index++)
        {
            arguments.Add("-map");
            arguments.Add($"{index}:v");
        }

        arguments.Add(outputPath);
        return arguments;
    }

    private static string GetBackgroundColor(string background)
    {
        return background.ToLowerInvariant() switch
        {
            "white" => "white",
            "black" => "black",
            _ => "0x00000000"
        };
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
