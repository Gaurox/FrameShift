using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class ExtractAudioAction : IFrameShiftAction
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public ExtractAudioAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "extract-audio",
        L.Get("ExtractAudioAction_Extract_Audio_dfb515b5"),
        L.Get("ExtractAudioAction_Extracts_the_first_audio_track_from__a65ea239"));

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
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
        }

        var targetId = ConversionActionHelper.GetOption(request, ActionOptionKeys.Target, "mp3").ToLowerInvariant();
        if (!ExtractAudioCatalog.IsSupportedTarget(targetId))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedTargetFormat(targetId));
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
        if (!probe.HasAudio)
        {
            request.ProgressReporter?.ReportState("failed", MediaActionMessages.MissingAudioTrack());
            return new ActionExecutionResult(false, MediaActionMessages.MissingAudioTrack());
        }

        var plan = ExtractAudioCatalog.CreatePlan(targetId, probe);
        using var output = OutputOperation.ForFile(OutputPathHelper.GetOutputPath(request.InputPath, "_audio", plan.OutputExtension));
        var outputPath = output.WorkingPath;

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.Logger.Log($"Audio extraction strategy: {plan.StrategyDescription}");
        request.ProgressReporter?.ReportState("processing", L.Format("ExtractAudioAction_Preparing_audio_extraction_to_0_on_C_e0975867",targetId.ToUpperInvariant()));

        try
        {
            var arguments = BuildArguments(request.InputPath, outputPath, plan);
            var result = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                probe.Duration,
                request.ProgressReporter,
                request.InputPath,
                Descriptor.DisplayName,
                "CPU",
                cancellationToken).ConfigureAwait(false);

            if (result.Canceled)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.ProgressReporter?.ReportState("canceled", L.Get("ExtractAudioAction_Canceled_437433cc"));
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, result.CancellationScope);
            }

            if (result.ExitCode != 0 || !File.Exists(outputPath))
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, L.Get("ExtractAudioAction_FFmpeg_failed_during_audio_extractio_2f1a9fd9"));
                request.ProgressReporter?.ReportState("failed", failureMessage);
                return new ActionExecutionResult(false, failureMessage, null, false);
            }

            return ConversionActionHelper.CompleteOutput(output, request, Descriptor.DisplayName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ConversionActionHelper.DeleteIfExists(outputPath);
            request.ProgressReporter?.ReportState("canceled", L.Get("ExtractAudioAction_Canceled_437433cc"));
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

    private static IReadOnlyList<string> BuildArguments(string inputPath, string outputPath, ExtractAudioPlan plan)
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
            "-vn",
            "-sn",
            "-dn",
            "-map", "0:a:0?",
            "-c:a", plan.Codec
        };

        args.AddRange(plan.Args);
        args.Add(outputPath);
        return args;
    }
}
