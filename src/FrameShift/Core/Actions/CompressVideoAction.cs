using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class CompressVideoAction : IFrameShiftAction
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public CompressVideoAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "compress-video",
        "Compress Video",
        "Compresses a video using a standard, widely compatible codec profile.");

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
        if (!VideoCompressionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoCompressionCatalog.GetSupportedSourceFormatsText()));
        }

        var profileId = ConversionActionHelper.GetOption(request, ActionOptionKeys.Profile, "balanced");
        if (!VideoCompressionCatalog.IsSupportedProfileId(profileId))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedCompressionProfile(profileId));
        }

        var profile = VideoCompressionCatalog.GetProfileById(profileId);
        long? targetBytes = null;
        if (ConversionActionHelper.GetOption(request, ActionOptionKeys.TargetSizeBytes, string.Empty) is string targetBytesText &&
            !string.IsNullOrWhiteSpace(targetBytesText))
        {
            if (!long.TryParse(targetBytesText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedTargetBytes) || parsedTargetBytes <= 0)
            {
                return new ActionExecutionResult(false, MediaActionMessages.CompressionTargetSizeInvalid());
            }

            targetBytes = parsedTargetBytes;
        }

        if (targetBytes is null && request.Options?.ContainsKey(ActionOptionKeys.TargetSizeBytes) == true)
        {
            return new ActionExecutionResult(false, MediaActionMessages.CompressionTargetSizeMissing());
        }

        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, targetBytes is null ? $"_compress_{profile.Id}" : "_compress_target");
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

        VideoCompressionPlan plan;
        try
        {
            plan = VideoCompressionPlanner.BuildPlan(profile, probe, sourceExtension, targetBytes);
        }
        catch (InvalidOperationException ex)
        {
            var message = string.IsNullOrWhiteSpace(ex.Message)
                ? MediaActionMessages.Failed(Descriptor.DisplayName)
                : ex.Message;
            request.ProgressReporter?.ReportState("failed", message);
            return new ActionExecutionResult(false, message);
        }

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.Logger.Log($"Video compression profile: {plan.ProfileName} ({plan.ProfileId}).");
        request.ProgressReporter?.ReportState("processing", VideoCompressionPlanner.GetPreparingText(plan.ProfileId, plan.UseTargetSize));

        if (plan.Warnings.Count > 0)
        {
            foreach (var warning in plan.Warnings)
            {
                request.Logger.Log(warning);
            }
        }

        try
        {
            var arguments = BuildArguments(request.InputPath, outputPath, plan, probe.HasAudio);
            var runResult = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                probe.Duration,
                probe.EstimatedVideoFrameCount,
                request.ProgressReporter,
                request.InputPath,
                Descriptor.DisplayName,
                plan.ModeLabel,
                cancellationToken).ConfigureAwait(false);

            if (runResult.Canceled)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, runResult.CancellationScope);
            }

            if (runResult.ExitCode != 0)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(runResult.StandardError, MediaActionMessages.FfmpegFailure("video"));
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
        VideoCompressionPlan plan,
        bool hasAudio)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-i", inputPath,
            "-dn",
            "-c:v", plan.VideoCodec
        };

        args.AddRange(plan.MapArgs);
        args.AddRange(plan.VideoArgs);

        if (hasAudio && !string.IsNullOrWhiteSpace(plan.AudioCodec))
        {
            args.Add("-c:a");
            args.Add(plan.AudioCodec!);
            args.AddRange(plan.AudioArgs);
        }
        else
        {
            args.Add("-an");
        }

        if (plan.SubtitleCodecArgs.Count > 0)
        {
            args.AddRange(plan.SubtitleCodecArgs);
        }

        if (plan.GlobalArgs.Count > 0)
        {
            args.AddRange(plan.GlobalArgs);
        }

        args.Add(outputPath);
        return args;
    }
}
