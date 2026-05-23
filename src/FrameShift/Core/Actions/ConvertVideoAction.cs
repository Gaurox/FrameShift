using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class ConvertVideoAction : IFrameShiftAction
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public ConvertVideoAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "convert-video",
        "Convert Video",
        "Converts a video to another container and codec profile.");

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

        var targetId = ConversionActionHelper.GetOption(request, ActionOptionKeys.Target, "mp4").ToLowerInvariant();
        if (!VideoConversionCatalog.IsSupportedTarget(targetId))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedTargetFormat(targetId));
        }

        var profileId = ConversionActionHelper.GetOption(request, ActionOptionKeys.Profile, "universal").ToLowerInvariant();
        var target = VideoConversionCatalog.GetTargetById(targetId);
        var profile = VideoConversionCatalog.GetProfileById(profileId);

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

        var nvencAvailable = await _ffmpegRunner.IsEncoderAvailableAsync(ffmpegPath, "h264_nvenc", cancellationToken).ConfigureAwait(false);

        if (profile.Id.Equals("remux", StringComparison.OrdinalIgnoreCase))
        {
            var remuxCompatibility = VideoConversionPlanner.ValidateRemuxCompatibility(target.Id, probe);
            if (!string.IsNullOrWhiteSpace(remuxCompatibility))
            {
                request.Logger.Log(remuxCompatibility);
                profile = VideoConversionCatalog.GetProfileById("universal");
            }
        }

        var plan = VideoConversionPlanner.BuildPlan(target, profile, probe, nvencAvailable);
        var cpuFallbackPlan = plan.ModeLabel.Equals("GPU", StringComparison.OrdinalIgnoreCase)
            ? VideoConversionPlanner.BuildPlan(target, profile, probe, false)
            : null;
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(request.InputPath, $"_convert_{target.Id}", $".{target.Id}");
        var pipelineDescription = BuildPipelineDescription(plan, probe, cpuFallbackPlan is not null);

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.Logger.Log($"Video pipeline: {pipelineDescription}");
        request.ProgressReporter?.ReportState("processing", $"{VideoConversionPlanner.GetPreparingText(plan.ProfileId, plan.TargetId).TrimEnd('.')} on {plan.ModeLabel}...");

        if (plan.Warnings.Count > 0)
        {
            foreach (var warning in plan.Warnings)
            {
                request.Logger.Log(warning);
            }
        }

        try
        {
            var arguments = BuildArguments(
                request.InputPath,
                outputPath,
                plan,
                probe,
                probe.HasAudio,
                useHardwareDecode: cpuFallbackPlan is not null);
            var result = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                probe.Duration,
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
                request.Logger.Log("GPU conversion failed. Retrying with CPU fallback.");
                var fallbackPipelineDescription = BuildPipelineDescription(cpuFallbackPlan, probe, false);
                request.Logger.Log($"Video pipeline: {fallbackPipelineDescription}");
                request.ProgressReporter?.ReportState("processing", $"GPU unavailable. Retrying on CPU... | {fallbackPipelineDescription}");

                var fallbackArguments = BuildArguments(
                    request.InputPath,
                    outputPath,
                    cpuFallbackPlan,
                    probe,
                    probe.HasAudio,
                    useHardwareDecode: false);
                result = await _ffmpegRunner.RunAsync(
                    ffmpegPath,
                    fallbackArguments,
                    probe.Duration,
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

            if (result.ExitCode != 0)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, MediaActionMessages.FfmpegFailure("video"));
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
        VideoConversionPlan plan,
        MediaProbeResult probe,
        bool hasAudio,
        bool useHardwareDecode)
    {
        var cuvidDecoder = useHardwareDecode ? GetCuvidDecoder(probe.VideoCodec) : null;
        var useCudaPipeline = useHardwareDecode &&
            !string.IsNullOrWhiteSpace(cuvidDecoder) &&
            IsNvencCodec(plan.VideoCodec);

        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
        };

        if (useCudaPipeline)
        {
            args.AddRange(["-hwaccel", "cuda", "-hwaccel_output_format", "cuda", "-c:v", cuvidDecoder!]);
        }
        else if (useHardwareDecode)
        {
            args.AddRange(["-hwaccel", "cuda"]);
        }

        args.AddRange(["-i", inputPath]);
        args.AddRange(plan.MapArgs);
        args.Add("-dn");
        if (useCudaPipeline)
        {
            args.AddRange(["-vf", "scale_cuda=format=yuv420p"]);
        }

        args.Add("-c:v");
        args.Add(plan.VideoCodec);
        args.AddRange(useCudaPipeline ? RemovePixelFormatArgs(plan.VideoArgs) : plan.VideoArgs);

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

    private static string? GetCuvidDecoder(string? videoCodec)
    {
        return videoCodec?.ToLowerInvariant() switch
        {
            "av1" => "av1_cuvid",
            "h264" => "h264_cuvid",
            "hevc" => "hevc_cuvid",
            "mjpeg" => "mjpeg_cuvid",
            "mpeg1video" => "mpeg1_cuvid",
            "mpeg2video" => "mpeg2_cuvid",
            "mpeg4" => "mpeg4_cuvid",
            "vc1" => "vc1_cuvid",
            "vp8" => "vp8_cuvid",
            "vp9" => "vp9_cuvid",
            _ => null
        };
    }

    private static bool IsNvencCodec(string videoCodec)
    {
        return videoCodec.EndsWith("_nvenc", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPipelineDescription(VideoConversionPlan plan, MediaProbeResult probe, bool preferHardwareDecode)
    {
        var stages = new List<string>();

        if (preferHardwareDecode)
        {
            var cuvidDecoder = GetCuvidDecoder(probe.VideoCodec);
            if (!string.IsNullOrWhiteSpace(cuvidDecoder) && IsNvencCodec(plan.VideoCodec))
            {
                stages.Add(cuvidDecoder);
                stages.Add("scale_cuda");
            }
            else
            {
                stages.Add("cuda decode");
            }
        }
        else
        {
            stages.Add("cpu decode");
        }

        stages.Add(plan.VideoCodec);
        return string.Join(" -> ", stages);
    }

    private static IReadOnlyList<string> RemovePixelFormatArgs(IReadOnlyList<string> args)
    {
        var filteredArgs = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], "-pix_fmt", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            filteredArgs.Add(args[i]);
        }

        return filteredArgs;
    }

}
