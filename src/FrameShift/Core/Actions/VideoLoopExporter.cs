using System.Globalization;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Progress;
using static FrameShift.Core.Actions.VideoLoopFrameReader;

namespace FrameShift.Core.Actions;

/// <summary>One precise render path for both previews and published loops.</summary>
public sealed class VideoLoopExporter(FfmpegRunner runner, FfprobeRunner probeRunner)
{
    internal Func<string, long> AvailableSpace { get; init; } = VideoLoopResourceBudget.GetAvailableSpace;
    internal VideoLoopSourceStamp? ExpectedSource { get; init; }

    public async Task<ActionExecutionResult> ExportAsync(string executable, string probeExecutable,
        ActionRequest request, MediaProbeResult probe, CutVideoSettings cut, VideoLoopSettings settings,
        VideoLoopCandidate? exactBounds, CancellationToken token)
    {
        settings.Validate();
        await VideoLoopColorPolicy.Evaluate(probe).RequireAvailableAsync(runner, executable, token).ConfigureAwait(false);
        using var output = OutputOperation.ForFile(OutputPathHelper.GetOutputPath(request.InputPath,
            settings.Mode == VideoLoopMode.Natural ? "_loop" : "_pingpong", ".mp4"));
        try
        {
            await RenderAsync(executable, probeExecutable, request.InputPath, output.WorkingPath, probe,
                cut, settings, exactBounds, request.ProgressReporter, token).ConfigureAwait(false);
        }
        catch (VideoLoopProcessTerminationException) { output.RetainForUnconfirmedProcess(); throw; }
        return ConversionActionHelper.CompleteOutput(output, request, "Video Loop", token);
    }

    public async Task RenderAsync(string executable, string probeExecutable, string source, string destination,
        MediaProbeResult probe, CutVideoSettings cut, VideoLoopSettings settings, VideoLoopCandidate? exactBounds,
        IProgressReporter? progress, CancellationToken token)
        => await RenderCoreAsync(executable, probeExecutable, source, destination, probe, cut, settings, exactBounds,
            null, progress, token).ConfigureAwait(false);

    internal Task RenderPreviewAsync(string executable, string probeExecutable, string source, string destination,
        MediaProbeResult probe, CutVideoSettings cut, VideoLoopSettings settings, VideoLoopPreviewClipPlan preview,
        IProgressReporter? progress, CancellationToken token)
        => RenderCoreAsync(executable, probeExecutable, source, destination, probe, cut, settings, preview.Bounds,
            preview, progress, token);

    private async Task RenderCoreAsync(string executable, string probeExecutable, string source, string destination,
        MediaProbeResult probe, CutVideoSettings cut, VideoLoopSettings settings, VideoLoopCandidate? exactBounds,
        VideoLoopPreviewClipPlan? preview, IProgressReporter? progress, CancellationToken token)
    {
        settings.Validate();
        var color = VideoLoopColorPolicy.Evaluate(probe);
        await color.RequireAvailableAsync(runner, executable, token).ConfigureAwait(false);
        if (!probe.HasVideo || probe.VideoWidth <= 0 || probe.VideoHeight <= 0 || !double.IsFinite(cut.Fps) || cut.Fps <= 0)
            throw new ArgumentException("A readable video is required for loop export.");
        if (cut.Fps > 240) throw new ArgumentException("Loop export currently supports videos up to 240 FPS.");
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("The loop destination already exists. Choose a unique output path.");
        var bounds = exactBounds ?? await ResolveFrameBoundsAsync(executable, source, cut, probe, token).ConfigureAwait(false);
        if (!double.IsFinite(bounds.StartSeconds) || !double.IsFinite(bounds.EndSeconds) || bounds.StartSeconds < 0 || bounds.DurationSeconds <= 0 ||
            bounds.EndSeconds > (probe.Duration?.TotalSeconds ?? 0) + 1 / cut.Fps)
            throw new ArgumentException("Loop boundaries must describe a non-empty segment inside the source.");
        var compactNatural = preview is null && settings.Mode == VideoLoopMode.Natural;
        var keepAudio = preview is null && settings.Mode == VideoLoopMode.Natural && settings.KeepAudio && probe.HasAudio;
        var segments = preview?.Segments ?? [bounds];
        var renderWidth = preview?.Width ?? probe.VideoWidth;
        var renderHeight = preview?.Height ?? probe.VideoHeight;
        var renderSeconds = segments.Sum(s => s.DurationSeconds);
        if (preview?.HeldTailDuration is { } heldDuration)
            renderSeconds += heldDuration - segments[0].DurationSeconds;
        var plan = VideoLoopResourcePlan.Create(renderWidth, renderHeight, renderSeconds, cut.Fps, settings,
            preview is not null, keepAudio, probe.PrimaryAudioSampleRate, probe.PrimaryAudioChannels);
        var workspace = Path.Combine(Path.GetTempPath(), $"frameshift_loop_render_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        var budget = new VideoLoopResourceBudget(workspace, destination, source, AvailableSpace);
        var mayClean = true;
        var ownsDestination = false;
        try
        {
            budget.CheckPlan(plan, preview is not null);
            var stamp = ExpectedSource ?? VideoLoopSourceStamp.Read(source);
            if (VideoLoopSourceStamp.Read(source) != stamp) throw new VideoLoopSourceChangedException();
            if (compactNatural)
            {
                progress?.ReportState("processing", "Resolving the loop boundary...");
                bounds = await ResolveNaturalStartAsync(executable, source, probe, bounds, budget, progress, token).ConfigureAwait(false);
                segments = [bounds];
            }
            if (VideoLoopSourceStamp.Read(source) != stamp) throw new VideoLoopSourceChangedException();
            Logging.AppLogger.LogStatic($"Video Loop {plan.Description}: estimated temporary {plan.TemporaryBytes:0} bytes; expected output {plan.OutputSeconds:0.######} seconds.");
            var forwards = new List<(string File, double Duration)>();
            foreach (var segment in segments)
            {
                progress?.ReportState("processing", compactNatural ? "Encoding the natural loop cycle..." : "Preparing the loop selection...");
                var file = Path.Combine(workspace, $"forward_{forwards.Count}." + (compactNatural ? "mov" : "mkv"));
                var duration = await RenderForwardAsync(executable, source, file, probe, segment, cut.Fps,
                    settings.Mode, keepAudio, preview, compactNatural, budget, progress, token).ConfigureAwait(false);
                if (forwards.Count == 0 && preview?.HeldTailDuration is { } held) duration = held;
                forwards.Add((file, duration));
            }
            var forward = forwards[0].File;
            var cycleDuration = forwards.Sum(f => f.Duration);
            var cycle = forward;
            if (settings.Mode == VideoLoopMode.PingPong)
            {
                var renderProbe = preview is null ? probe : probe with { VideoWidth = renderWidth, VideoHeight = renderHeight };
                var generated = preview?.Content is VideoLoopPreviewContent.EndTurn or VideoLoopPreviewContent.StartTurn
                    ? await CreateTurnAsync(executable, probeExecutable, forward, workspace, renderProbe, cut.Fps, preview.Content, budget, progress, token).ConfigureAwait(false)
                    : await CreatePingPongCycleAsync(executable, probeExecutable, forward, workspace, renderProbe, cut.Fps, budget, progress, token).ConfigureAwait(false);
                cycle = generated.File;
                cycleDuration = generated.Duration;
            }

            if (VideoLoopSourceStamp.Read(source) != stamp) throw new VideoLoopSourceChangedException();
            progress?.ReportState("processing", "Assembling continuous loop repetitions...");
            var actualCycleBytes = Directory.GetFiles(workspace).Sum(f => new FileInfo(f).Length);
            var outputSeconds = cycleDuration * settings.Repetitions / settings.Speed;
            budget.CheckOutput(compactNatural ? new FileInfo(cycle).Length * (double)settings.Repetitions * 1.1 + outputSeconds * 32000
                : actualCycleBytes * settings.Repetitions + 1024 * 1024, "Assembling loop repetitions");
            Logging.AppLogger.LogStatic($"Video Loop: actual temporary {actualCycleBytes} bytes; output duration {outputSeconds:R} seconds; compact video copy={compactNatural}.");
            var final = Prefix();
            if (settings.Mode == VideoLoopMode.Natural)
            {
                // Explicit duration preserves the last VFR frame's hold until the exclusive boundary.
                var repeats = Path.Combine(workspace, "repeats.txt");
                await File.WriteAllLinesAsync(repeats, Enumerable.Range(0, settings.Repetitions)
                    .SelectMany(_ => forwards.SelectMany(f => new[] { $"file '{Path.GetFileName(f.File)}'", $"duration {Number(f.Duration)}" })), token).ConfigureAwait(false);
                final.AddRange(["-f", "concat", "-safe", "1", "-i", repeats]);
            }
            else final.AddRange(["-stream_loop", (settings.Repetitions - 1).ToString(CultureInfo.InvariantCulture), "-i", cycle]);
            if (compactNatural) final.AddRange(["-map", "0:v:0", "-c:v", "copy", "-video_track_timescale", "1000000"]);
            else final.AddRange([
                "-map", "0:v:0", "-vf", $"setpts=(PTS-STARTPTS)/{Number(settings.Speed)}", "-fps_mode", "passthrough",
                "-c:v", "libx264", "-preset", preview is null ? "medium" : "veryfast", "-crf", "18", "-pix_fmt",
                preview is not null || probe.DisplayVideoWidth % 2 == 0 && probe.DisplayVideoHeight % 2 == 0 ? "yuv420p" : "yuv444p"]);
            if (settings.Mode == VideoLoopMode.PingPong)
                final.AddRange(["-r", Number(cut.Fps * settings.Speed), "-fps_mode", "cfr"]);
            else
            {
                if (preview is not null)
                    final.AddRange(["-enc_time_base:v", "1:1000000", "-video_track_timescale", "1000000"]);
                // Presentation-ordered packets keep MP4's VFR end time unambiguous.
                // The final frame must last until the exclusive selection boundary,
                // exactly like each junction inside the continuous preview artifact.
                if (!compactNatural) final.AddRange(["-bf", "0"]);
                final.AddRange(["-bsf:v", LastPacketDuration(cycleDuration * settings.Repetitions)]);
            }
            if (keepAudio) final.AddRange(["-map", "0:a:0", "-c:a", "aac", "-b:a", "256k"]);
            else final.Add("-an");
            color.AddOutputArguments(final);
            final.AddRange(["-map_metadata", "-1", "-movflags", "+faststart", destination]);
            using (new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            ownsDestination = true;
            await RunAsync(executable, final, outputSeconds, budget, progress, token).ConfigureAwait(false);
            if (VideoLoopSourceStamp.Read(source) != stamp) throw new VideoLoopSourceChangedException();
        }
        catch (VideoLoopProcessTerminationException) { mayClean = false; throw; }
        catch (TimeoutException) { mayClean = false; throw new VideoLoopProcessTerminationException(workspace, destination); }
        catch { if (ownsDestination) ConversionActionHelper.DeleteIfExists(destination); throw; }
        finally
        {
            try { if (mayClean) Directory.Delete(workspace, recursive: true); }
            catch (IOException ex) { Logging.AppLogger.LogStatic($"Loop temporary cleanup failed: {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { Logging.AppLogger.LogStatic($"Loop temporary cleanup failed: {ex.Message}"); }
        }
    }

    private async Task<VideoLoopCandidate> ResolveNaturalStartAsync(string executable, string source, MediaProbeResult probe,
        VideoLoopCandidate bounds, VideoLoopResourceBudget budget, IProgressReporter? progress, CancellationToken token)
    {
        var start = probe.VideoStartSeconds + bounds.StartSeconds;
        var args = Prefix();
        args[args.IndexOf("error")] = "info";
        args.AddRange(["-threads", "2", "-filter_threads", "1", "-ss", Number(Math.Max(0, start - probe.FormatStartSeconds - 0.1)),
            "-copyts", "-i", source, "-map", "0:v:0", "-an", "-sn", "-dn", "-vf",
            $"trim=start={Number(start)}:end={Number(probe.VideoStartSeconds + bounds.EndSeconds)},showinfo",
            "-frames:v", "1", "-fps_mode", "passthrough", "-f", "null", "-"]);
        var timing = new VideoLoopTimingCollector(1);
        await budget.RunAsync(runner, executable, args, bounds.DurationSeconds, progress, token, timing.Observe).ConfigureAwait(false);
        if (timing.Timings.Count == 0) throw new ArgumentException("The selection contains no video frame.");
        return bounds with { StartSeconds = timing.Timings[0].Time - probe.VideoStartSeconds };
    }

    private async Task<double> RenderForwardAsync(string executable, string source, string destination,
        MediaProbeResult probe, VideoLoopCandidate bounds, double fps, VideoLoopMode mode, bool keepAudio,
        VideoLoopPreviewClipPlan? preview, bool compactNatural, VideoLoopResourceBudget budget, IProgressReporter? progress, CancellationToken token)
    {
        var start = probe.VideoStartSeconds + bounds.StartSeconds;
        var end = probe.VideoStartSeconds + bounds.EndSeconds;
        var args = Prefix();
        args[args.IndexOf("error")] = "info";
        var color = VideoLoopColorPolicy.Evaluate(probe);
        if (compactNatural || color.ConvertsToSdr) args.AddRange(["-threads", "2", "-filter_threads", "1"]);
        // Seek just before the selection, but trim against original video timestamps.
        // This avoids losing a fractional-FPS boundary to microsecond seek rounding.
        args.AddRange(["-ss", Number(Math.Max(0, start - probe.FormatStartSeconds - 0.1)), "-copyts", "-i", source,
            "-map", "0:v:0", "-vf", $"trim=start={Number(start)}:end={Number(end)}," + (compactNatural ? "" : "showinfo,") + "setpts=PTS-STARTPTS" +
            (color.ConvertsToSdr ? "," + color.Filter : "") +
            (preview is null ? "" : $",scale={preview.Width}:{preview.Height}:flags=area,setsar=1") +
            (mode == VideoLoopMode.PingPong ? $",fps={Number(fps)}" : "")]);
        if (compactNatural) args.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", "18", "-bf", "0",
            "-pix_fmt", probe.DisplayVideoWidth % 2 == 0 && probe.DisplayVideoHeight % 2 == 0 ? "yuv420p" : "yuv444p",
            "-threads", "4", "-x264-params", "rc-lookahead=8:sync-lookahead=4", "-enc_time_base:v", "1:1000000",
            "-video_track_timescale", "1000000", "-bsf:v", LastPacketDuration(bounds.DurationSeconds)]);
        else args.AddRange(["-c:v", "ffv1", "-level", "3"]);
        args.AddRange(["-fps_mode", "passthrough"]);
        if (keepAudio) args.AddRange(["-map", "0:a:0", "-af",
            $"atrim=start={Number(start)}:end={Number(end)},asetpts=PTS-{Number(start)}/TB," +
            $"aresample=async=1:first_pts=0,apad,atrim=duration={Number(bounds.DurationSeconds)}", "-c:a", "pcm_s16le"]);
        else args.Add("-an");
        color.AddOutputArguments(args);
        args.AddRange(["-map_metadata", "-1", "-sn", "-dn", "-t", Number(bounds.DurationSeconds), destination]);
        var timing = new VideoLoopTimingCollector(1);
        await budget.RunAsync(runner, executable, args, bounds.DurationSeconds, progress, token, compactNatural ? null : timing.Observe).ConfigureAwait(false);
        if (compactNatural) return bounds.DurationSeconds;
        if (timing.Timings.Count == 0) throw new InvalidOperationException("The loop selection contains no decoded frame.");
        // A short preview window may start between native frames. Preserve the hold
        // up to its exclusive end rather than stretching it back to the requested seek.
        return end - timing.Timings[0].Time;
    }

    private async Task<int> ReadFrameCountAsync(string probeExecutable, string file, CancellationToken token)
    {
        var result = await probeRunner.RunProbeAsync(probeExecutable,
            ["-v", "error", "-select_streams", "v:0", "-count_frames", "-show_entries", "stream=nb_read_frames", "-of", "csv=p=0", file], token).ConfigureAwait(false);
        if (!result.Success || !int.TryParse(result.Output.Trim(), out var count) || count < 2)
            throw new InvalidOperationException("Forward/backward preview requires at least two decoded frames.");
        return count;
    }

    private async Task<(string File, double Duration)> CreateTurnAsync(string executable, string probeExecutable, string forward,
        string workspace, MediaProbeResult probe, double fps, VideoLoopPreviewContent content, VideoLoopResourceBudget budget, IProgressReporter? progress, CancellationToken token)
    {
        var count = await ReadFrameCountAsync(probeExecutable, forward, token).ConfigureAwait(false);
        var parts = new List<string>();
        if (content == VideoLoopPreviewContent.EndTurn) parts.Add(forward);
        await AppendReversePartsAsync(executable, forward, workspace, probe, fps, parts, 0,
            content == VideoLoopPreviewContent.EndTurn ? count - 1 : count, budget, progress, token).ConfigureAwait(false);
        if (content == VideoLoopPreviewContent.StartTurn)
        {
            var rest = Path.Combine(workspace, "after_start_turn.mkv");
            var args = Prefix();
            args.AddRange(["-i", forward, "-an", "-vf", $"trim=start_frame=1,setpts=N/({Number(fps)}*TB)",
                "-c:v", "ffv1", "-level", "3", "-fps_mode", "passthrough", rest]);
            await RunAsync(executable, args, (count - 1) / fps, budget, progress, token).ConfigureAwait(false);
            parts.Add(rest);
        }
        var duration = (2 * count - 1) / fps;
        return (await ConcatPartsAsync(executable, parts, workspace, duration, budget, progress, token).ConfigureAwait(false), duration);
    }

    private async Task<(string File, double Duration)> CreatePingPongCycleAsync(string executable, string probeExecutable, string forward,
        string workspace, MediaProbeResult probe, double fps, VideoLoopResourceBudget budget, IProgressReporter? progress, CancellationToken token)
    {
        var count = await ReadFrameCountAsync(probeExecutable, forward, token).ConfigureAwait(false);
        if (count < 3)
            throw new InvalidOperationException("Ping-Pong requires at least three decoded frames.");
        var parts = new List<string> { forward };
        // N frames forward, then N-2 frames backwards: neither turning point is duplicated.
        await AppendReversePartsAsync(executable, forward, workspace, probe, fps, parts, 1, count - 1, budget, progress, token).ConfigureAwait(false);
        var duration = (2 * count - 2) / fps;
        return (await ConcatPartsAsync(executable, parts, workspace, duration, budget, progress, token).ConfigureAwait(false), duration);
    }

    private async Task AppendReversePartsAsync(string executable, string forward, string workspace,
        MediaProbeResult probe, double fps, List<string> parts, int first, int lastExclusive, VideoLoopResourceBudget budget,
        IProgressReporter? progress, CancellationToken token)
    {
        var chunkSize = (int)Math.Clamp(128L * 1024 * 1024 / ((long)probe.VideoWidth * probe.VideoHeight * 8), 1, 120);
        progress?.ReportState("processing", "Preparing backward frames...");
        for (var end = lastExclusive; end > first; )
        {
            token.ThrowIfCancellationRequested();
            var start = Math.Max(first, end - chunkSize);
            var reverseFile = Path.Combine(workspace, $"reverse_{parts.Count:0000}.mkv");
            var args = Prefix();
            // Matroska stores millisecond timestamps. Seek just before the target so a
            // fractional-FPS timestamp rounded down does not discard that first frame.
            args.AddRange(["-ss", Number(Math.Max(0, start / fps - 0.001)), "-t", Number((end - start) / fps + 0.002),
                "-i", forward, "-map", "0:v:0", "-an", "-vf",
                $"trim=end_frame={end - start},reverse,setpts=N/({Number(fps)}*TB)", "-c:v", "ffv1", "-level", "3", "-fps_mode", "passthrough", reverseFile]);
            await RunAsync(executable, args, (end - start) / fps, budget, progress, token).ConfigureAwait(false);
            parts.Add(reverseFile);
            end = start;
        }
    }

    private async Task<string> ConcatPartsAsync(string executable, IReadOnlyList<string> parts,
        string workspace, double duration, VideoLoopResourceBudget budget, IProgressReporter? progress, CancellationToken token)
    {
        // Generated basenames only; no user paths are interpolated into concat syntax.
        var list = Path.Combine(workspace, "parts.txt");
        await File.WriteAllLinesAsync(list, parts.Select(p => $"file '{Path.GetFileName(p)}'"), token).ConfigureAwait(false);
        var cycle = Path.Combine(workspace, "cycle.mkv");
        var concat = Prefix();
        concat.AddRange(["-f", "concat", "-safe", "1", "-i", list, "-map", "0:v:0", "-c", "copy", cycle]);
        await RunAsync(executable, concat, duration, budget, progress, token).ConfigureAwait(false);
        return cycle;
    }

    internal async Task<VideoLoopCandidate> ResolveFrameBoundsAsync(string executable, string source,
        CutVideoSettings cut, MediaProbeResult probe, CancellationToken token)
    {
        using var index = new VideoLoopTimingIndex(runner, executable, source, probe);
        var selection = await index.ResolveFramesAsync(cut.StartFrame, cut.EndFrame, token).ConfigureAwait(false);
        return selection.Bounds;
    }

    private async Task RunAsync(string executable, List<string> args, double duration,
        VideoLoopResourceBudget budget, IProgressReporter? progress, CancellationToken token)
    {
        await budget.RunAsync(runner, executable, args, duration, progress, token).ConfigureAwait(false);
    }

    private static string LastPacketDuration(double seconds)
        => $"setts=duration='if(eq(NEXT_PTS,NOPTS),max(1,round({Number(seconds)}/TB-PTS)),DURATION)'";

    private static List<string> Prefix() => ["-hide_banner", "-loglevel", "error", "-stats_period", "0.25", "-progress", "pipe:1", "-nostats", "-y"];
}
