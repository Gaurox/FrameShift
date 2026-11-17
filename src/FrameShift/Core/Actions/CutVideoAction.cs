using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Progress;

namespace FrameShift.Core.Actions;

public sealed class CutVideoAction : IFrameShiftAction
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public CutVideoAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "cut-video",
        L.Get("CutVideoAction_Cut_Video_2106db79"),
        L.Get("CutVideoAction_Cuts_a_video_between_two_frame_bound_2c39a62a"));

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

        if (request.ExpectedLoopSource is { } expected && VideoLoopSourceStamp.Read(request.InputPath) != expected)
            return new(false, new VideoLoopSourceChangedException().Message);

        var sourceExtension = Path.GetExtension(request.InputPath).ToLowerInvariant();
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
        }

        var ffmpegPath = _toolLocator.ResolveFfmpegPath();
        var ffprobePath = _toolLocator.ResolveFfprobePath();
        var probeAttempt = await _ffprobeRunner.TryProbeMediaAsync(ffprobePath, request.InputPath, cancellationToken, includeChapters: true).ConfigureAwait(false);
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

        var fps = probe.VideoFrameRate;
        if (fps is null || fps <= 0)
        {
            return new ActionExecutionResult(false, MediaActionMessages.VideoFrameRateUnavailable());
        }

        var totalFrames = probe.EstimatedVideoFrameCount is long frameCount && frameCount > 0
            ? (int)Math.Min(int.MaxValue, frameCount)
            : CutVideoMath.EstimateFrameCount(probe.Duration, fps.Value);

        if (totalFrames <= 0)
        {
            return new ActionExecutionResult(false, MediaActionMessages.VideoFrameCountUnavailable());
        }

        if (!CutVideoSettings.TryFromOptions(request.Options, fps.Value, totalFrames, out var settings, out var settingsError) || settings is null)
        {
            return new ActionExecutionResult(false, settingsError ?? MediaActionMessages.CutVideoSettingsMissing());
        }

        try
        {
            if (settings.RequestedTimeRange is { } times)
            {
                using var timing = new VideoLoopTimingIndex(_ffmpegRunner, ffmpegPath, request.InputPath, probe);
                request.ProgressReporter?.ReportState("processing", L.Get("Loop_Indexing"));
                var first = await timing.ResolveCutTimeAsync(times.Start, end: false, cancellationToken).ConfigureAwait(false);
                var last = await timing.ResolveCutTimeAsync(times.End, end: true, cancellationToken).ConfigureAwait(false);
                settings = new CutVideoSettings(first.Number!.Value, last.Number!.Value, fps.Value);
            }

            if (VideoLoopSettings.FromOptions(request.Options) is { } loop)
            {
                VideoLoopCandidate? bounds = null;
                if (request.Options!.TryGetValue("loop-start", out var start) && request.Options.TryGetValue("loop-end", out var end))
                    bounds = new VideoLoopCandidate(double.Parse(start, CultureInfo.InvariantCulture), double.Parse(end, CultureInfo.InvariantCulture), 0);
                request.Logger.Log($"Cut Video loop: {request.QueueItemDescription}; candidate {request.Options.GetValueOrDefault("loop-candidate")}; " +
                    $"bounds {bounds?.StartSeconds:R} → {bounds?.EndSeconds:R}; {loop.Mode}, speed {loop.Speed}, repeats {loop.Repetitions}, audio {loop.KeepAudio}.");
                return await new VideoLoopExporter(_ffmpegRunner, _ffprobeRunner) { ExpectedSource = request.ExpectedLoopSource }.ExportAsync(ffmpegPath, ffprobePath,
                    request, probe, settings, loop, bounds, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (VideoLoopCancellationException ex) { return new(false, L.Get("Loop_ExportCanceled"), null, true, ex.Scope); }
        catch (OperationCanceledException) { return ReportCanceled(request, CancellationScope.All); }
        catch (Exception ex)
        {
            request.Logger.Log($"Cut selection or loop export failed: {ex}");
            request.ProgressReporter?.ReportState("failed", ex.Message);
            return new(false, ConversionActionHelper.GetFriendlyExceptionMessage(ex, L.Get("Loop_ExportFailed")));
        }

        using var output = OutputOperation.ForFile(OutputPathHelper.GetOutputPath(
            request.InputPath,
            $"__frame_{settings.StartFrame}_to_{settings.EndFrame}",
            sourceExtension));
        var outputPath = output.WorkingPath;

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.Logger.Log($"Cut video strategy: re-encode to {sourceExtension} for frame-accurate trimming.");

        try
        {
            var sourceStamp = VideoLoopSourceStamp.Read(request.InputPath);
            var trimSubtitles = sourceExtension != ".avi" && probe.HasSubtitles;
            CutBounds? bounds = null;
            if (probe.HasAudio || trimSubtitles || probe.Chapters.Count != 0)
            {
                request.ProgressReporter?.ReportState("processing", L.Format("CutVideoAction_Preparing_cut_on_0_f63f727c", "CPU"));
                var (timingResult, selectionBounds) = await ReadCutBoundsAsync(ffmpegPath, request, settings, cancellationToken).ConfigureAwait(false);
                if (timingResult.Canceled)
                {
                    return ReportCanceled(request, timingResult.CancellationScope);
                }

                bounds = selectionBounds;
            }

            var chaptersPath = bounds is { } measured && probe.Chapters.Count != 0
                ? await VideoChapterMetadata.WriteAsync(output.WorkspacePath, probe.Chapters, measured.Start, measured.End,
                    1, cancellationToken).ConfigureAwait(false)
                : null;
            var encodingPlan = await GetEncodingPlanAsync(sourceExtension, cancellationToken).ConfigureAwait(false);
            request.ProgressReporter?.ReportState("processing", L.Format("CutVideoAction_Preparing_cut_on_0_f63f727c", encodingPlan.Primary.ModeLabel));
            var primaryResult = await RunEncodingAttemptAsync(
                ffmpegPath,
                request.InputPath,
                output,
                settings,
                encodingPlan.Primary,
                bounds,
                probe.HasAudio,
                trimSubtitles,
                chaptersPath,
                sourceStamp,
                request.ProgressReporter,
                cancellationToken).ConfigureAwait(false);

            if (primaryResult.Canceled)
            {
                return ReportCanceled(request, primaryResult.CancellationScope);
            }

            if (primaryResult.ExitCode == 0 && File.Exists(outputPath))
            {
                return ConversionActionHelper.CompleteOutput(output, request, Descriptor.DisplayName, cancellationToken);
            }

            if (encodingPlan.Fallback is not null)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.ProgressReporter?.ReportState("processing", L.Format("CutVideoAction_Retrying_cut_on_0_11e479fe",encodingPlan.Fallback.ModeLabel));

                var fallbackResult = await RunEncodingAttemptAsync(
                    ffmpegPath,
                    request.InputPath,
                    output,
                    settings,
                    encodingPlan.Fallback,
                    bounds,
                    probe.HasAudio,
                    trimSubtitles,
                    chaptersPath,
                    sourceStamp,
                    request.ProgressReporter,
                    cancellationToken).ConfigureAwait(false);

                if (fallbackResult.Canceled)
                {
                    return ReportCanceled(request, fallbackResult.CancellationScope);
                }

                if (fallbackResult.ExitCode == 0 && File.Exists(outputPath))
                {
                    return ConversionActionHelper.CompleteOutput(output, request, Descriptor.DisplayName, cancellationToken);
                }

                ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(
                    fallbackResult.StandardError,
                    L.Get("CutVideoAction_FFmpeg_failed_during_Cut_Video_proce_170c6576"));
                request.ProgressReporter?.ReportState("failed", failureMessage);
                return new ActionExecutionResult(false, failureMessage, null, false);
            }

            ConversionActionHelper.DeleteIfExists(outputPath);
            var primaryFailureMessage = ConversionActionHelper.GetFriendlyFfmpegError(
                primaryResult.StandardError,
                L.Get("CutVideoAction_FFmpeg_failed_during_Cut_Video_proce_170c6576"));
            request.ProgressReporter?.ReportState("failed", primaryFailureMessage);
            return new ActionExecutionResult(false, primaryFailureMessage, null, false);
        }
        catch (OperationCanceledException)
        {
            ConversionActionHelper.DeleteIfExists(outputPath);
            return ReportCanceled(request, CancellationScope.All);
        }
        catch (Exception ex)
        {
            ConversionActionHelper.DeleteIfExists(outputPath);
            request.Logger.Log($"Cut video failed: {ex}");
            var failureMessage = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed(Descriptor.DisplayName));
            request.ProgressReporter?.ReportState("failed", failureMessage);
            return new ActionExecutionResult(false, failureMessage);
        }
    }

    private ActionExecutionResult ReportCanceled(ActionRequest request, CancellationScope scope)
    {
        request.ProgressReporter?.ReportState("canceled", L.Get("CutVideoAction_Canceled_437433cc"));
        return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, scope);
    }

    private async Task<(FfmpegRunResult Result, CutBounds? Bounds)> ReadCutBoundsAsync(
        string ffmpegPath, ActionRequest request, CutVideoSettings settings, CancellationToken cancellationToken)
    {
        var startFrame = settings.StartFrameZeroBased;
        var frameCount = settings.EndFrame - startFrame;
        // Read through the next frame to get the selected last frame's full display interval.
        // At EOF there is no next timestamp, so use the last frame's duration.
        var filter = FormattableString.Invariant($"trim=start_frame={startFrame}:end_frame={(long)settings.EndFrame + 1},showinfo=checksum=0,setpts=PTS-STARTPTS");
        string[] arguments =
        [
            "-hide_banner", "-loglevel", "info", "-progress", "pipe:1", "-nostats",
            "-copyts", "-i", request.InputPath, "-map", "0:V:0", "-an", "-sn", "-dn", "-vf", filter,
            "-fps_mode", "passthrough", "-f", "null", "-"
        ];

        VideoLoopTimestamp? first = null;
        VideoLoopTimestamp last = default;
        VideoLoopTimestamp? next = null;
        var seen = 0;
        var timing = new VideoLoopTimingCollector((int)Math.Min((long)frameCount + 1, int.MaxValue), stamp =>
        {
            if (seen++ == frameCount)
            {
                next = stamp;
                return;
            }

            first ??= stamp;
            last = stamp;
        }, retainTimings: false);

        var message = L.Get("Loop_ReadTimingFailed");
        var result = await _ffmpegRunner.RunAsync(
            ffmpegPath, arguments, null, request.ProgressReporter, request.InputPath,
            Descriptor.DisplayName, "CPU", cancellationToken, timing.Observe).ConfigureAwait(false);
        if (result.Canceled)
        {
            return (result, null);
        }

        if (result.ExitCode != 0 || !result.ProcessTerminationConfirmed)
        {
            throw new InvalidOperationException(ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, message));
        }

        if (first is null)
        {
            throw new InvalidOperationException(L.Get("Loop_NoSelectionFrame"));
        }

        var lastDuration = last.DurationSeconds > 0 ? last.DurationSeconds : 1 / settings.Fps;
        var end = next is { } following && following.Seconds > last.Seconds ? following.Seconds : last.Seconds + lastDuration;
        if (end <= first.Value.Seconds)
        {
            throw new InvalidOperationException(message);
        }

        request.Logger.Log(FormattableString.Invariant($"Cut video bounds: {first.Value.Seconds:R} to {end:R} seconds on the source timeline."));
        return (result, new CutBounds(first.Value.Seconds, end));
    }

    private async Task<FfmpegRunResult> RunEncodingAttemptAsync(
        string ffmpegPath,
        string inputPath,
        OutputOperation output,
        CutVideoSettings settings,
        EncodingPlan plan,
        CutBounds? bounds,
        bool hasAudio,
        bool trimSubtitles,
        string? chaptersPath,
        VideoLoopSourceStamp sourceStamp,
        IProgressReporter? progressReporter,
        CancellationToken cancellationToken)
    {
        if (VideoLoopSourceStamp.Read(inputPath) != sourceStamp)
        {
            throw new VideoLoopSourceChangedException();
        }

        var arguments = BuildArguments(inputPath, output.WorkingPath, settings, plan, bounds, hasAudio, trimSubtitles, chaptersPath);
        var result = await _ffmpegRunner.RunAsync(
            ffmpegPath,
            arguments,
            TimeSpan.FromSeconds(bounds is { } measured ? measured.End - measured.Start : settings.DurationSeconds),
            EstimateOutputFrameCount(settings),
            progressReporter,
            inputPath,
            Descriptor.DisplayName,
            plan.ModeLabel,
            cancellationToken).ConfigureAwait(false);
        if (!result.ProcessTerminationConfirmed)
        {
            output.RetainForUnconfirmedProcess();
        }
        else if (!result.Canceled && result.ExitCode == 0 && VideoLoopSourceStamp.Read(inputPath) != sourceStamp)
        {
            throw new VideoLoopSourceChangedException();
        }

        return result;
    }

    private static IReadOnlyList<string> BuildArguments(
        string inputPath,
        string outputPath,
        CutVideoSettings settings,
        EncodingPlan plan,
        CutBounds? bounds,
        bool hasAudio,
        bool trimSubtitles,
        string? chaptersPath)
    {
        // trim uses an exclusive zero-based end, matching the one-based inclusive EndFrame.
        var videoFilter = $"trim=start_frame={settings.StartFrameZeroBased}:end_frame={settings.EndFrame},setpts=PTS-STARTPTS";

        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y"
        };

        if (bounds is not null)
        {
            args.Add("-copyts");
        }

        args.AddRange(["-i", inputPath]);
        if (chaptersPath is not null)
            args.AddRange(["-f", "ffmetadata", "-i", chaptersPath]);

        args.AddRange(["-map", "0:V:0", "-map", "0:a?", "-vf", videoFilter, "-c:v", plan.VideoCodec,
            "-map_chapters", chaptersPath is null ? "-1" : "1"]);
        if (trimSubtitles)
            args.AddRange(["-map", "0:s?"]);
        if (Path.GetExtension(outputPath).Equals(".mkv", StringComparison.OrdinalIgnoreCase))
            args.AddRange(["-map", "0:t?", "-c:t", "copy"]);
        args.AddRange(plan.VideoArgs);

        // AVI uses a constant-rate time grid. The other containers can retain native frame times.
        if (!Path.GetExtension(outputPath).Equals(".avi", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(["-fps_mode", "passthrough", "-enc_time_base:v", "filter"]);
        }

        if (bounds is { } measured)
        {
            var start = VideoLoopFrameReader.Number(measured.Start);
            var end = VideoLoopFrameReader.Number(measured.End);
            if (hasAudio)
            {
                var duration = VideoLoopFrameReader.Number(measured.End - measured.Start);
                var audioFilter = $"atrim=start={start}:end={end},asetpts=PTS-({start})/TB," +
                    $"aresample=async=1:first_pts=0,apad,atrim=duration={duration}";
                args.AddRange(["-af", audioFilter, "-c:a", plan.AudioCodec]);
                // The bundled AAC encoder's default coder can stall even on short cuts.
                if (plan.AudioCodec == "aac")
                    args.AddRange(["-aac_coder", "fast"]);
                args.AddRange(plan.AudioArgs);
            }

            if (trimSubtitles)
            {
                // Drop disjoint packets, then clip and rebase overlapping cues without changing the payload.
                var filter = $"noise=amount=0:drop='if(gt(duration,0),lte((pts+duration)*tb,{start}),lt(pts*tb,{start}))+gte(pts*tb,{end})'," +
                    $"setts=pts='max(PTS*TB-({start}),0)/TB':dts='max(DTS*TB-({start}),0)/TB':" +
                    $"duration='max(0,(min((PTS+DURATION)*TB,{end})-max(PTS*TB,{start}))/TB)'";
                args.AddRange(["-c:s", "copy", "-bsf:s", filter]);
            }
        }
        if (!hasAudio)
        {
            args.Add("-an");
        }

        args.Add(outputPath);
        return args;
    }

    private async Task<EncodingPlanBundle> GetEncodingPlanAsync(string extension, CancellationToken cancellationToken)
    {
        return extension.ToLowerInvariant() switch
        {
            ".mp4" => await GetNvencPlanAsync(
                cancellationToken,
                new EncodingPlan("CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart"], "aac", ["-b:a", "320k"]),
                new EncodingPlan("NVIDIA GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p", "-movflags", "+faststart"], "aac", ["-b:a", "320k"])).ConfigureAwait(false),
            ".mkv" => await GetNvencPlanAsync(
                cancellationToken,
                new EncodingPlan("CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p"], "aac", ["-b:a", "320k"]),
                new EncodingPlan("NVIDIA GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p"], "aac", ["-b:a", "320k"])).ConfigureAwait(false),
            ".avi" => new EncodingPlanBundle(
                new EncodingPlan("CPU", "mpeg4", ["-q:v", "2"], "libmp3lame", ["-b:a", "320k"]),
                null),
            ".mov" => await GetNvencPlanAsync(
                cancellationToken,
                new EncodingPlan("CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart"], "aac", ["-b:a", "320k"]),
                new EncodingPlan("NVIDIA GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p", "-movflags", "+faststart"], "aac", ["-b:a", "320k"])).ConfigureAwait(false),
            ".webm" => new EncodingPlanBundle(
                new EncodingPlan("CPU", "libvpx-vp9", ["-crf", "31", "-b:v", "0", "-deadline", "good", "-cpu-used", "2", "-row-mt", "1"], "libopus", ["-b:a", "192k"]),
                null),
            ".m4v" => await GetNvencPlanAsync(
                cancellationToken,
                new EncodingPlan("CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart"], "aac", ["-b:a", "320k"]),
                new EncodingPlan("NVIDIA GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p", "-movflags", "+faststart"], "aac", ["-b:a", "320k"])).ConfigureAwait(false),
            _ => throw new NotSupportedException(L.Get("CutVideoAction_Unsupported_input_format_Only_mp4_mk_b0d46086"))
        };
    }

    private async Task<EncodingPlanBundle> GetNvencPlanAsync(CancellationToken cancellationToken, EncodingPlan cpuPlan, EncodingPlan gpuPlan)
    {
        var ffmpegPath = _toolLocator.ResolveFfmpegPath();
        var nvencAvailable = await _ffmpegRunner.IsEncoderAvailableAsync(ffmpegPath, "h264_nvenc", cancellationToken).ConfigureAwait(false);
        return nvencAvailable
            ? new EncodingPlanBundle(gpuPlan, cpuPlan)
            : new EncodingPlanBundle(cpuPlan, null);
    }

    private static long? EstimateOutputFrameCount(CutVideoSettings settings)
    {
        var frames = (long)Math.Max(1, settings.EndFrame - settings.StartFrame + 1);
        return frames;
    }

    private sealed record EncodingPlan(
        string ModeLabel,
        string VideoCodec,
        IReadOnlyList<string> VideoArgs,
        string AudioCodec,
        IReadOnlyList<string> AudioArgs);

    private sealed record EncodingPlanBundle(EncodingPlan Primary, EncodingPlan? Fallback);

    private readonly record struct CutBounds(double Start, double End);
}
