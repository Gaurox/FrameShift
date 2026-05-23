using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class ConvertImageAction : IFrameShiftAction
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly ToolLocator _toolLocator;

    public ConvertImageAction(FfmpegRunner ffmpegRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "convert-image",
        "Convert Image",
        "Converts an image to another format.");

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
        if (!ImageConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageConversionCatalog.GetSupportedSourceFormatsText()));
        }

        var targetId = ConversionActionHelper.GetOption(request, ActionOptionKeys.Target, "png").ToLowerInvariant();
        if (!ImageConversionCatalog.IsSupportedTarget(targetId))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedTargetFormat(targetId));
        }

        var target = ImageConversionCatalog.GetTargetById(targetId);
        var profileId = ConversionActionHelper.GetOption(request, ActionOptionKeys.Profile, "standard").ToLowerInvariant();
        var profile = ImageConversionCatalog.GetProfileById(profileId);
        var ffmpegPath = _toolLocator.ResolveFfmpegPath();
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, $"_convert_{target.Id}", $".{target.Id}");

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.ProgressReporter?.ReportState("processing", $"Preparing image conversion to {target.DisplayName.ToLowerInvariant()} ({profile.DisplayName.ToLowerInvariant()}) on CPU...");

        try
        {
            var arguments = BuildArguments(request.InputPath, outputPath, target.Id, profile.Id);
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

    private static IReadOnlyList<string> BuildArguments(string inputPath, string outputPath, string targetId, string profileId)
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

        args.AddRange(ImageConversionCatalog.GetOutputArguments(targetId, profileId));
        args.Add(outputPath);
        return args;
    }
}
