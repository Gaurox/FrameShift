using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class CompressImageAction : IFrameShiftAction
{
    private static readonly int[] JpgCandidates = [2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 18, 20, 24, 28, 31];
    private static readonly int[] WebpCandidates = [95, 90, 86, 82, 78, 74, 70, 66, 62, 58, 54, 50, 46, 42, 38, 34, 30];

    private readonly FfmpegRunner _ffmpegRunner;
    private readonly ToolLocator _toolLocator;

    public CompressImageAction(FfmpegRunner ffmpegRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "compress-image",
        "Compress Image",
        "Compresses an image using a preset profile or a target file size.");

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

        var sourceExtension = NormalizeExtension(Path.GetExtension(request.InputPath));
        if (!ImageConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageConversionCatalog.GetSupportedSourceFormatsText()));
        }

        if (!CompressImageSettings.TryFromOptions(request.Options, out var settings, out var settingsError) || settings is null)
        {
            return new ActionExecutionResult(false, settingsError ?? MediaActionMessages.CompressImageSettingsMissing());
        }

        if (settings.TargetBytes.HasValue && !CompressImageSettings.IsTargetSizeSupportedForFormat(settings.OutputFormat))
        {
            return new ActionExecutionResult(false, MediaActionMessages.CompressImageTargetUnsupportedFormat(settings.OutputFormat));
        }

        var ffmpegPath = _toolLocator.ResolveFfmpegPath();
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, settings.OutputSuffix, settings.OutputExtension);

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.ProgressReporter?.ReportState("processing", "Preparing image compression on CPU...");

        try
        {
            if (settings.TargetBytes.HasValue)
            {
                return await ExecuteTargetSizeAsync(request, settings, ffmpegPath, outputPath, cancellationToken).ConfigureAwait(false);
            }

            return await ExecutePresetAsync(request, settings, ffmpegPath, outputPath, cancellationToken).ConfigureAwait(false);
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

    private async Task<ActionExecutionResult> ExecutePresetAsync(
        ActionRequest request,
        CompressImageSettings settings,
        string ffmpegPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var arguments = BuildProgressArguments(request.InputPath, outputPath, settings.OutputFormat, settings.ProfileId);
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
            var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, MediaActionMessages.FfmpegFailure("image"));
            request.ProgressReporter?.ReportState("failed", failureMessage);
            return new ActionExecutionResult(false, failureMessage, null, false);
        }

        request.ProgressReporter?.ReportProgress(1000, request.InputPath, Descriptor.DisplayName, "Completed.");
        request.ProgressReporter?.ReportState("done", "Completed.");
        return new ActionExecutionResult(true, MediaActionMessages.Completed(Descriptor.DisplayName), outputPath);
    }

    private async Task<ActionExecutionResult> ExecuteTargetSizeAsync(
        ActionRequest request,
        CompressImageSettings settings,
        string ffmpegPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var candidates = settings.OutputFormat == "jpg" ? JpgCandidates : WebpCandidates;
        var tempPath = Path.Combine(Path.GetTempPath(), $"frameshift_{Guid.NewGuid():N}{settings.OutputExtension}");

        int? bestUnderQuality = null;
        long bestUnderSize = 0;
        int? bestOverQuality = null;
        long bestOverSize = long.MaxValue;
        string lastStderr = string.Empty;

        try
        {
            foreach (var quality in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ConversionActionHelper.DeleteIfExists(tempPath);
                var candidateArgs = BuildCandidateArguments(request.InputPath, tempPath, settings.OutputFormat, quality);
                var candidateResult = await _ffmpegRunner.RunCaptureAsync(ffmpegPath, candidateArgs, cancellationToken).ConfigureAwait(false);

                lastStderr = candidateResult.StandardError;

                if (!candidateResult.Success || !File.Exists(tempPath))
                {
                    ConversionActionHelper.DeleteIfExists(tempPath);
                    var failMsg = ConversionActionHelper.GetFriendlyFfmpegError(lastStderr, MediaActionMessages.FfmpegFailure("image"));
                    request.ProgressReporter?.ReportState("failed", failMsg);
                    return new ActionExecutionResult(false, failMsg);
                }

                var size = new FileInfo(tempPath).Length;
                if (size <= settings.TargetBytes!.Value)
                {
                    if (bestUnderQuality is null || size > bestUnderSize)
                    {
                        bestUnderQuality = quality;
                        bestUnderSize = size;
                    }
                }
                else
                {
                    if (size < bestOverSize)
                    {
                        bestOverQuality = quality;
                        bestOverSize = size;
                    }
                }
            }
        }
        finally
        {
            ConversionActionHelper.DeleteIfExists(tempPath);
        }

        var chosenQuality = bestUnderQuality ?? bestOverQuality;
        if (chosenQuality is null)
        {
            var failMsg = MediaActionMessages.CompressImageTargetFailed();
            request.ProgressReporter?.ReportState("failed", failMsg);
            return new ActionExecutionResult(false, failMsg);
        }

        var finalArgs = BuildFinalArguments(request.InputPath, outputPath, settings.OutputFormat, chosenQuality.Value);
        var result = await _ffmpegRunner.RunAsync(
            ffmpegPath,
            finalArgs,
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
            var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, MediaActionMessages.FfmpegFailure("image"));
            request.ProgressReporter?.ReportState("failed", failureMessage);
            return new ActionExecutionResult(false, failureMessage, null, false);
        }

        request.ProgressReporter?.ReportProgress(1000, request.InputPath, Descriptor.DisplayName, "Completed.");
        request.ProgressReporter?.ReportState("done", "Completed.");
        return new ActionExecutionResult(true, MediaActionMessages.Completed(Descriptor.DisplayName), outputPath);
    }

    private static IReadOnlyList<string> BuildProgressArguments(string inputPath, string outputPath, string outputFormat, string profileId)
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
            "-frames:v", "1"
        };

        args.AddRange(GetPresetEncodingArgs(outputFormat, profileId));
        args.Add(outputPath);
        return args;
    }

    private static IReadOnlyList<string> BuildCandidateArguments(string inputPath, string tempPath, string outputFormat, int quality)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-i", inputPath,
            "-frames:v", "1"
        };

        args.AddRange(GetQualityEncodingArgs(outputFormat, quality));
        args.Add(tempPath);
        return args;
    }

    private static IReadOnlyList<string> BuildFinalArguments(string inputPath, string outputPath, string outputFormat, int quality)
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
            "-frames:v", "1"
        };

        args.AddRange(GetQualityEncodingArgs(outputFormat, quality));
        args.Add(outputPath);
        return args;
    }

    private static IReadOnlyList<string> GetPresetEncodingArgs(string outputFormat, string profileId)
    {
        return outputFormat switch
        {
            "jpg" => profileId switch
            {
                CompressImageSettings.ProfileHigh => ["-q:v", "2"],
                CompressImageSettings.ProfileSmall => ["-q:v", "12"],
                _ => ["-q:v", "6"]
            },
            "webp" => profileId switch
            {
                CompressImageSettings.ProfileHigh => ["-c:v", "libwebp", "-quality", "92", "-compression_level", "6"],
                CompressImageSettings.ProfileSmall => ["-c:v", "libwebp", "-quality", "68", "-compression_level", "6"],
                _ => ["-c:v", "libwebp", "-quality", "82", "-compression_level", "6"]
            },
            "png" => profileId == CompressImageSettings.ProfileSmall
                ? ["-c:v", "png", "-compression_level", "9", "-vf", "scale=iw:ih:flags=lanczos,format=rgb24"]
                : ["-c:v", "png", "-compression_level", "9"],
            _ => throw new NotSupportedException($"Unsupported image format '{outputFormat}' for compression.")
        };
    }

    private static IReadOnlyList<string> GetQualityEncodingArgs(string outputFormat, int quality)
    {
        return outputFormat switch
        {
            "jpg" => ["-q:v", quality.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            "webp" => ["-c:v", "libwebp", "-quality", quality.ToString(System.Globalization.CultureInfo.InvariantCulture), "-compression_level", "6"],
            _ => throw new NotSupportedException($"Quality-based encoding is not supported for '{outputFormat}'.")
        };
    }

    private static string NormalizeExtension(string extension) =>
        string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.Trim().ToLowerInvariant();
}
