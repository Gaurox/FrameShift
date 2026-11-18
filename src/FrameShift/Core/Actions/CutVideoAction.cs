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

        VideoLoopSourceStamp sourceStamp;
        var useCuda = probe.VideoCodec == "av1";
        CutBounds? bounds = null;
        try
        {
            sourceStamp = VideoLoopSourceStamp.Read(request.InputPath);
            if (settings.RequestedTimeRange is { } times)
            {
                using var timing = new VideoLoopTimingIndex(_ffmpegRunner, ffmpegPath, request.InputPath, probe, useCuda);
                request.ProgressReporter?.ReportState("processing", L.Get("Loop_Indexing"));
                var last = await timing.ResolveCutTimeAsync(times.End, end: true, cancellationToken).ConfigureAwait(false);
                var first = await timing.ResolveCutTimeAsync(times.Start, end: false, cancellationToken).ConfigureAwait(false);
                settings = new CutVideoSettings(first.Number!.Value, last.Number!.Value, fps.Value);
                bounds = new CutBounds(first.Timestamp, last.Timestamp, last.End + probe.VideoStartSeconds);
            }

            if (VideoLoopSettings.FromOptions(request.Options) is { } loop)
            {
                VideoLoopCandidate? loopBounds = null;
                if (request.Options!.TryGetValue("loop-start", out var start) && request.Options.TryGetValue("loop-end", out var end))
                    loopBounds = new VideoLoopCandidate(double.Parse(start, CultureInfo.InvariantCulture), double.Parse(end, CultureInfo.InvariantCulture), 0);
                request.Logger.Log($"Cut Video loop: {request.QueueItemDescription}; candidate {request.Options.GetValueOrDefault("loop-candidate")}; " +
                    $"bounds {loopBounds?.StartSeconds:R} → {loopBounds?.EndSeconds:R}; {loop.Mode}, speed {loop.Speed}, repeats {loop.Repetitions}, audio {loop.KeepAudio}.");
                return await new VideoLoopExporter(_ffmpegRunner, _ffprobeRunner) { ExpectedSource = request.ExpectedLoopSource }.ExportAsync(ffmpegPath, ffprobePath,
                    request, probe, settings, loop, loopBounds, cancellationToken).ConfigureAwait(false);
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
            var trimSubtitles = sourceExtension != ".avi" && probe.HasSubtitles;
            if (bounds is null && (probe.HasAudio || trimSubtitles || probe.Chapters.Count != 0))
            {
                var (timingResult, selectionBounds) = await ReadCutBoundsAsync(ffmpegPath, request, settings, useCuda, cancellationToken).ConfigureAwait(false);
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
            // Keep subtitle packets available from the beginning, including cues already active at the cut.
            double? seek = !trimSubtitles && bounds is { CanSeek: true } known && known.Start - probe.FormatStartSeconds > 5
                ? known.Start - 2 : null;
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
                seek,
                useCuda,
                sourceStamp,
                request.ProgressReporter,
                cancellationToken).ConfigureAwait(false);

            if (primaryResult.Canceled)
            {
                return ReportCanceled(request, primaryResult.CancellationScope);
            }

            if (primaryResult.ProcessTerminationConfirmed && primaryResult.ExitCode == 0 && File.Exists(outputPath))
            {
                return ConversionActionHelper.CompleteOutput(output, request, Descriptor.DisplayName, cancellationToken);
            }

            if (encodingPlan.Fallback is not null && primaryResult.ProcessTerminationConfirmed)
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
                    seek,
                    useCuda: false,
                    sourceStamp,
                    request.ProgressReporter,
                    cancellationToken).ConfigureAwait(false);

                if (fallbackResult.Canceled)
                {
                    return ReportCanceled(request, fallbackResult.CancellationScope);
                }

                if (fallbackResult.ProcessTerminationConfirmed && fallbackResult.ExitCode == 0 && File.Exists(outputPath))
                {
                    return ConversionActionHelper.CompleteOutput(output, request, Descriptor.DisplayName, cancellationToken);
                }

                if (fallbackResult.ProcessTerminationConfirmed)
                    ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(
                    fallbackResult.StandardError,
                    L.Get("CutVideoAction_FFmpeg_failed_during_Cut_Video_proce_170c6576"));
                request.ProgressReporter?.ReportState("failed", failureMessage);
                return new ActionExecutionResult(false, failureMessage, null, false);
            }

            if (primaryResult.ProcessTerminationConfirmed)
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
        string ffmpegPath, ActionRequest request, CutVideoSettings settings, bool useCuda, CancellationToken cancellationToken)
    {
        var startFrame = settings.StartFrameZeroBased;
        var frameCount = settings.EndFrame - startFrame;
        // Read through the next frame to get the selected last frame's full display interval.
        // At EOF there is no next timestamp, so use the last frame's duration.
        var scanStart = Math.Max(0, startFrame - 1);
        var filter = FormattableString.Invariant($"trim=start_frame={scanStart}:end_frame={(long)settings.EndFrame + 1},showinfo=checksum=0,setpts=PTS-STARTPTS");
        string[] arguments =
        [
            "-hide_banner", "-loglevel", "info", "-progress", "pipe:1", "-nostats",
            .. CudaInputArgs(useCuda), "-copyts", "-i", request.InputPath, "-map", "0:V:0", "-an", "-sn", "-dn", "-vf", filter,
            "-fps_mode", "passthrough", "-f", "null", "-"
        ];

        VideoLoopTimestamp? first = null;
        VideoLoopTimestamp? previous = null;
        VideoLoopTimestamp last = default;
        VideoLoopTimestamp? next = null;
        var seen = scanStart - startFrame;
        var timing = new VideoLoopTimingCollector((int)Math.Min((long)frameCount + 2, int.MaxValue), stamp =>
        {
            var frame = seen++;
            if (frame < 0)
            {
                previous = stamp;
                return;
            }
            if (frame == frameCount)
            {
                next = stamp;
                return;
            }

            first ??= stamp;
            last = stamp;
        }, retainTimings: false);

        var message = L.Get("Loop_ReadTimingFailed");
        var mode = useCuda ? "CUDA" : "CPU";
        request.ProgressReporter?.ReportState("processing", L.Format("CutVideoAction_Preparing_cut_on_0_f63f727c", mode));
        var result = await _ffmpegRunner.RunAsync(
            ffmpegPath, arguments, null, request.ProgressReporter, request.InputPath,
            Descriptor.DisplayName, mode, cancellationToken, timing.Observe).ConfigureAwait(false);
        if (result.Canceled)
        {
            return (result, null);
        }

        if (useCuda && result.ProcessTerminationConfirmed && result.ExitCode != 0)
        {
            request.Logger.Log("CUDA cut timing failed. Retrying with software decoding.");
            return await ReadCutBoundsAsync(ffmpegPath, request, settings, false, cancellationToken).ConfigureAwait(false);
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
        var canSeek = seen >= frameCount && (previous is null || previous.Value.Seconds < first.Value.Seconds) &&
            (next is null || next.Value.Seconds > last.Seconds);
        return (result, new CutBounds(first.Value, last, end, canSeek));
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
        double? seek,
        bool useCuda,
        VideoLoopSourceStamp sourceStamp,
        IProgressReporter? progressReporter,
        CancellationToken cancellationToken)
    {
        var result = await EncodeAsync(seek, useCuda).ConfigureAwait(false);
        if (useCuda && CanRetry(result))
            result = await EncodeAsync(seek, cuda: false).ConfigureAwait(false);
        // If NVENC fails, let the software encoder try the same seek before scanning from the start.
        if (seek is not null && CanRetry(result) && plan.VideoCodec != "h264_nvenc")
            result = await EncodeAsync(seekAt: null, cuda: false).ConfigureAwait(false);
        return result;

        async Task<FfmpegRunResult> EncodeAsync(double? seekAt, bool cuda)
        {
            if (VideoLoopSourceStamp.Read(inputPath) != sourceStamp)
                throw new VideoLoopSourceChangedException();

            var expectedFrames = settings.EndFrame - settings.StartFrame + 1;
            var count = 0L;
            VideoLoopTimestamp first = default, last = default;
            var timing = seekAt is null ? null : new VideoLoopTimingCollector(int.MaxValue, stamp =>
            {
                if (count++ == 0) first = stamp;
                last = stamp;
            }, retainTimings: false);
            var arguments = BuildArguments(inputPath, output.WorkingPath, settings, plan, bounds, hasAudio, trimSubtitles,
                chaptersPath, seekAt, cuda);
            var result = await _ffmpegRunner.RunAsync(
                ffmpegPath, arguments,
                TimeSpan.FromSeconds(bounds is { } measured ? measured.End - measured.Start : settings.DurationSeconds),
                expectedFrames, progressReporter, inputPath, Descriptor.DisplayName, plan.ModeLabel, null,
                cancellationToken, timing is null ? null : timing.Observe).ConfigureAwait(false);
            if (!result.ProcessTerminationConfirmed)
            {
                output.RetainForUnconfirmedProcess();
            }
            else if (!result.Canceled && result.ExitCode == 0)
            {
                if (VideoLoopSourceStamp.Read(inputPath) != sourceStamp)
                    throw new VideoLoopSourceChangedException();
                if (seekAt is not null && bounds is { } expected &&
                    (count != expectedFrames || !SameTimestamp(first, expected.First) || !SameTimestamp(last, expected.Last)))
                    return result with { ExitCode = -1, StandardError = L.Get("Loop_ReadTimingFailed") };
            }
            return result;
        }

        static bool CanRetry(FfmpegRunResult result) =>
            !result.Canceled && result.ProcessTerminationConfirmed && result.ExitCode != 0;

        static bool SameTimestamp(VideoLoopTimestamp actual, VideoLoopTimestamp expected) =>
            actual.Pts == expected.Pts && actual.Numerator == expected.Numerator && actual.Denominator == expected.Denominator;
    }

    private static IReadOnlyList<string> BuildArguments(
        string inputPath,
        string outputPath,
        CutVideoSettings settings,
        EncodingPlan plan,
        CutBounds? bounds,
        bool hasAudio,
        bool trimSubtitles,
        string? chaptersPath,
        double? seek,
        bool useCuda)
    {
        // trim uses an exclusive zero-based end, matching the one-based inclusive EndFrame.
        var videoFilter = seek is not null && bounds is { } known
            ? $"trim=start_pts={known.First.Pts},trim=end_frame={settings.EndFrame - settings.StartFrameZeroBased},showinfo=checksum=0,setpts=PTS-STARTPTS"
            : $"trim=start_frame={settings.StartFrameZeroBased}:end_frame={settings.EndFrame},setpts=PTS-STARTPTS";

        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", seek is null ? "error" : "info",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y"
        };

        if (bounds is not null)
        {
            args.Add("-copyts");
        }

        args.AddRange(CudaInputArgs(useCuda));
        if (seek is { } timestamp)
            args.AddRange(["-seek_timestamp", "1", "-ss", VideoLoopFrameReader.Number(timestamp)]);
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

    private static string[] CudaInputArgs(bool useCuda) => useCuda ? ["-hwaccel", "cuda"] : [];

    private sealed record EncodingPlan(
        string ModeLabel,
        string VideoCodec,
        IReadOnlyList<string> VideoArgs,
        string AudioCodec,
        IReadOnlyList<string> AudioArgs);

    private sealed record EncodingPlanBundle(EncodingPlan Primary, EncodingPlan? Fallback);

    private readonly record struct CutBounds(VideoLoopTimestamp First, VideoLoopTimestamp Last, double End, bool CanSeek = true)
    {
        internal double Start => First.Seconds;
    }
}
