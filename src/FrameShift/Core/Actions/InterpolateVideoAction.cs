using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class InterpolateVideoAction : IFrameShiftAction
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public InterpolateVideoAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "interpolate-video",
        "Interpolate Video",
        "Interpolates video frames to a higher (or different) frame rate.");

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
        if (!IsSupportedExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(
                sourceExtension, GetSupportedFormatsText()));
        }

        if (!InterpolateVideoSettings.TryFromOptions(request.Options, out var settings, out var settingsError) || settings is null)
        {
            return new ActionExecutionResult(false, settingsError ?? MediaActionMessages.InterpolateVideoSettingsMissing());
        }

        var ffmpegPath = _toolLocator.ResolveFfmpegPath();
        var ffprobePath = _toolLocator.ResolveFfprobePath();

        var probeAttempt = await _ffprobeRunner.TryProbeMediaAsync(ffprobePath, request.InputPath, cancellationToken).ConfigureAwait(false);
        if (probeAttempt.Probe is null)
        {
            var probeError = probeAttempt.ErrorMessage ?? MediaActionMessages.ProbeFailed();
            request.ProgressReporter?.ReportState("failed", probeError);
            return new ActionExecutionResult(false, probeError);
        }

        var probe = probeAttempt.Probe;
        if (!probe.HasVideo)
        {
            request.ProgressReporter?.ReportState("failed", MediaActionMessages.MissingVideoTrack());
            return new ActionExecutionResult(false, MediaActionMessages.MissingVideoTrack());
        }

        if (probe.Duration is null || probe.Duration.Value.TotalSeconds <= 0)
        {
            request.ProgressReporter?.ReportState("failed", MediaActionMessages.DurationUnavailable());
            return new ActionExecutionResult(false, MediaActionMessages.DurationUnavailable());
        }

        var durationSeconds = probe.Duration.Value.TotalSeconds;
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, settings.OutputSuffix, sourceExtension);
        var nvencAvailable = await _ffmpegRunner.IsEncoderAvailableAsync(ffmpegPath, "h264_nvenc", cancellationToken).ConfigureAwait(false);
        var plan = InterpolateVideoPlanner.BuildPlan(sourceExtension, nvencAvailable);
        var cpuFallbackPlan = plan.ModeLabel.Equals("GPU", StringComparison.OrdinalIgnoreCase)
            ? InterpolateVideoPlanner.BuildPlan(sourceExtension, false)
            : null;
        var pipelineDescription = InterpolateVideoPlanner.BuildPipelineDescription(plan, settings.TargetFps);

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.Logger.Log($"TargetFps: {settings.TargetFps.ToString("0.###", CultureInfo.InvariantCulture)}");
        request.Logger.Log($"Interpolation pipeline: {pipelineDescription}");
        request.ProgressReporter?.ReportState("processing", $"Preparing interpolation on {plan.ModeLabel}...");

        try
        {
            var arguments = InterpolateVideoPlanner.BuildArguments(request.InputPath, outputPath, settings, plan);
            var result = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                TimeSpan.FromSeconds(durationSeconds),
                probe.EstimatedVideoFrameCount,
                request.ProgressReporter,
                request.InputPath,
                Descriptor.DisplayName,
                plan.ModeLabel,
                pipelineDescription,
                cancellationToken).ConfigureAwait(false);

            if (!result.Canceled && result.ExitCode != 0 && cpuFallbackPlan is not null)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                var fallbackPipelineDescription = InterpolateVideoPlanner.BuildPipelineDescription(cpuFallbackPlan, settings.TargetFps);
                request.Logger.Log("GPU interpolation encode path failed. Retrying with CPU fallback.");
                request.Logger.Log($"Interpolation pipeline: {fallbackPipelineDescription}");
                request.ProgressReporter?.ReportState("processing", $"GPU unavailable. Retrying on CPU... | {fallbackPipelineDescription}");

                var fallbackArguments = InterpolateVideoPlanner.BuildArguments(request.InputPath, outputPath, settings, cpuFallbackPlan);
                result = await _ffmpegRunner.RunAsync(
                    ffmpegPath,
                    fallbackArguments,
                    TimeSpan.FromSeconds(durationSeconds),
                    probe.EstimatedVideoFrameCount,
                    request.ProgressReporter,
                    request.InputPath,
                    Descriptor.DisplayName,
                    cpuFallbackPlan.ModeLabel,
                    fallbackPipelineDescription,
                    cancellationToken).ConfigureAwait(false);
            }

            if (result.Canceled)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, result.CancellationScope);
            }

            if (result.ExitCode != 0 || !File.Exists(outputPath))
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, "FFmpeg failed during interpolation.");
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

    private static string NormalizeExtension(string extension)
    {
        return string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.Trim().ToLowerInvariant();
    }

    private static bool IsSupportedExtension(string extension)
    {
        return extension is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".m4v";
    }

    private static string GetSupportedFormatsText()
    {
        return ".mp4, .mkv, .avi, .mov, .webm, .m4v";
    }
}
