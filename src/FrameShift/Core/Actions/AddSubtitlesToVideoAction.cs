using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class AddSubtitlesToVideoAction : IFrameShiftAction
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public AddSubtitlesToVideoAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "add-subtitles-video",
        "Add Subtitles to Video",
        "Adds subtitles either as a selectable track or burned into the video.");

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
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
        }

        if (!AddSubtitlesToVideoSettings.TryFromOptions(request.Options, out var settings, out var settingsError) || settings is null)
        {
            return new ActionExecutionResult(false, settingsError ?? MediaActionMessages.AddSubtitlesToVideoSettingsMissing());
        }

        if (!File.Exists(settings.SubtitleFilePath))
        {
            return new ActionExecutionResult(false, MediaActionMessages.InputFileNotFound(settings.SubtitleFilePath));
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

        return settings.Mode == AddSubtitlesToVideoMode.BurnIntoVideo
            ? await ExecuteBurnIntoVideoAsync(request, settings, sourceExtension, probe, ffmpegPath, cancellationToken).ConfigureAwait(false)
            : await ExecuteSelectableTrackAsync(request, settings, sourceExtension, probe, ffmpegPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ActionExecutionResult> ExecuteSelectableTrackAsync(
        ActionRequest request,
        AddSubtitlesToVideoSettings settings,
        string sourceExtension,
        MediaProbeResult probe,
        string ffmpegPath,
        CancellationToken cancellationToken)
    {
        var plan = AddSubtitlesToVideoPlanner.BuildPlan(sourceExtension, probe);
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, "_subtitled", plan.TargetExtension);

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.Logger.Log($"Add subtitles plan: {plan.PipelineDescription}. {plan.DecisionReason}");
        request.ProgressReporter?.ReportState("processing", plan.PreparingText);

        try
        {
            var arguments = BuildArguments(request.InputPath, settings.SubtitleFilePath, outputPath, plan, probe);
            var result = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                probe.Duration,
                probe.EstimatedVideoFrameCount,
                request.ProgressReporter,
                request.InputPath,
                Descriptor.DisplayName,
                "CPU",
                plan.PipelineDescription,
                cancellationToken).ConfigureAwait(false);

            if (result.Canceled)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, result.CancellationScope);
            }

            if (result.ExitCode != 0 || !File.Exists(outputPath))
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(
                    result.StandardError,
                    MediaActionMessages.AddSubtitlesToVideoFailed());
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
    }

    private async Task<ActionExecutionResult> ExecuteBurnIntoVideoAsync(
        ActionRequest request,
        AddSubtitlesToVideoSettings settings,
        string sourceExtension,
        MediaProbeResult probe,
        string ffmpegPath,
        CancellationToken cancellationToken)
    {
        var plan = AddSubtitlesToVideoBurnPlanner.BuildPlan(sourceExtension, probe);
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, "_subtitled_burned", plan.TargetExtension);
        AddSubtitlesToVideoPreparedSubtitleInput? preparedInput = null;

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.Logger.Log($"Burn subtitles plan: {plan.PipelineDescription}");
        request.ProgressReporter?.ReportState("processing", plan.PreparingText);

        if (plan.Warnings.Count > 0)
        {
            foreach (var warning in plan.Warnings)
            {
                request.Logger.Log(warning);
            }
        }

        try
        {
            preparedInput = await AddSubtitlesToVideoSubtitleSourceLoader
                .PrepareAssInputAsync(settings.SubtitleFilePath, probe, settings.BurnSettings ?? AddSubtitlesToVideoBurnSettings.Default, cancellationToken)
                .ConfigureAwait(false);
            request.Logger.Log(preparedInput.PreparationSummary);

            var arguments = BuildBurnArguments(
                request.InputPath,
                preparedInput.AssFilePath,
                outputPath,
                plan,
                probe.HasAudio);
            var result = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                probe.Duration,
                probe.EstimatedVideoFrameCount,
                request.ProgressReporter,
                request.InputPath,
                Descriptor.DisplayName,
                "CPU",
                plan.PipelineDescription,
                cancellationToken).ConfigureAwait(false);

            if (result.Canceled)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, result.CancellationScope);
            }

            if (result.ExitCode != 0 || !File.Exists(outputPath))
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(
                    result.StandardError,
                    MediaActionMessages.BurnSubtitlesToVideoFailed());
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
            DeletePreparedSubtitleInput(preparedInput);
        }
    }

    internal static IReadOnlyList<string> BuildArguments(
        string inputPath,
        string subtitleFilePath,
        string outputPath,
        AddSubtitlesToVideoPlan plan,
        MediaProbeResult probe)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-i", inputPath,
            "-i", subtitleFilePath,
            "-map_metadata", "0",
            "-map_chapters", "0",
            "-map", "0",
            "-map", "1:0",
            "-c", "copy"
        };

        if (string.Equals(plan.TargetContainerId, "mkv", StringComparison.OrdinalIgnoreCase))
        {
            var existingSubtitleCount = probe.Streams.Count(stream =>
                string.Equals(stream.CodecType, "subtitle", StringComparison.OrdinalIgnoreCase));
            arguments.AddRange([
                $"-c:s:{existingSubtitleCount.ToString(CultureInfo.InvariantCulture)}",
                plan.SubtitleCodec
            ]);
        }
        else
        {
            arguments.AddRange(["-c:s", plan.SubtitleCodec]);
        }

        arguments.Add(outputPath);
        return arguments;
    }

    internal static IReadOnlyList<string> BuildBurnArguments(
        string inputPath,
        string assSubtitleFilePath,
        string outputPath,
        AddSubtitlesToVideoBurnPlan plan,
        bool hasAudio)
    {
        return BuildBurnArguments(inputPath, assSubtitleFilePath, outputPath, plan, hasAudio, clipWindow: null);
    }

    internal static IReadOnlyList<string> BuildBurnArguments(
        string inputPath,
        string assSubtitleFilePath,
        string outputPath,
        AddSubtitlesToVideoBurnPlan plan,
        bool hasAudio,
        AddSubtitlesToVideoBurnClipWindow? clipWindow)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-i", inputPath,
            "-map_metadata", "0",
            "-map_chapters", "0"
        };

        if (clipWindow is not null)
        {
            arguments.AddRange([
                "-ss",
                clipWindow.StartSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                "-t",
                clipWindow.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture)
            ]);
        }

        arguments.AddRange(plan.MapArgs);
        arguments.Add("-dn");
        arguments.AddRange(["-vf", BuildAssVideoFilter(assSubtitleFilePath)]);
        arguments.AddRange(["-c:v", plan.VideoCodec]);
        arguments.AddRange(plan.VideoArgs);

        if (!hasAudio)
        {
            arguments.Add("-an");
        }
        else if (plan.CopyAudio)
        {
            arguments.AddRange(["-c:a", "copy"]);
        }
        else if (!string.IsNullOrWhiteSpace(plan.AudioCodec))
        {
            arguments.AddRange(["-c:a", plan.AudioCodec!]);
            arguments.AddRange(plan.AudioArgs);
        }
        else
        {
            arguments.Add("-an");
        }

        if (plan.SubtitleCodecArgs.Count > 0)
        {
            arguments.AddRange(plan.SubtitleCodecArgs);
        }

        if (plan.GlobalArgs.Count > 0)
        {
            arguments.AddRange(plan.GlobalArgs);
        }

        arguments.Add(outputPath);
        return arguments;
    }

    internal static string BuildAssVideoFilter(string assSubtitleFilePath)
    {
        return $"ass=filename='{EscapeFilterPath(assSubtitleFilePath)}'";
    }

    internal static string EscapeFilterPath(string path)
    {
        var normalized = Path.GetFullPath(path).Replace('\\', '/');
        return normalized
            .Replace(":", "\\:", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal);
    }

    private static void DeletePreparedSubtitleInput(AddSubtitlesToVideoPreparedSubtitleInput? preparedInput)
    {
        if (preparedInput is null || !preparedInput.DeleteAfterUse)
        {
            return;
        }

        ConversionActionHelper.DeleteIfExists(preparedInput.AssFilePath);
    }

    private static string NormalizeExtension(string extension)
    {
        return string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.Trim().ToLowerInvariant();
    }
}

internal sealed record AddSubtitlesToVideoBurnClipWindow(double StartSeconds, double DurationSeconds);
