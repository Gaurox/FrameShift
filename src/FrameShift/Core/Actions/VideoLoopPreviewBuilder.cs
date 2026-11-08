using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Progress;

namespace FrameShift.Core.Actions;

internal enum VideoLoopPreviewKind { Junction, FullLoop }
internal enum VideoLoopPreviewContent { NaturalJunction, FullLoop, EndTurn, StartTurn }

internal sealed record VideoLoopPreviewClipPlan(VideoLoopCandidate Bounds, VideoLoopPreviewContent Content,
    int Width, int Height, int Repetitions, string Description)
{
    internal double? HeldTailStart { get; init; }
    internal double? HeldTailDuration { get; init; }
    internal IReadOnlyList<VideoLoopCandidate> Segments => Content == VideoLoopPreviewContent.NaturalJunction
        ? [new(HeldTailStart ?? Math.Max(Bounds.StartSeconds, Bounds.EndSeconds - Window), Bounds.EndSeconds, 0),
            new(Bounds.StartSeconds, Math.Min(Bounds.EndSeconds, Bounds.StartSeconds + Window), 0)]
        : [Bounds];
    private double Window => Math.Min(1.5, Bounds.DurationSeconds / 2);
}

internal sealed record VideoLoopPreviewClip(string Path, string Description);

/// <summary>One session-owned preview bundle. Dispose only after its decoder has stopped.</summary>
internal sealed record VideoLoopPreview(string Directory, IReadOnlyList<VideoLoopPreviewClip> Clips, int Width, int Height) : IDisposable
{
    public void Dispose()
    {
        try { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true); }
        catch (IOException ex) { Logging.AppLogger.LogStatic($"Loop preview cleanup failed: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { Logging.AppLogger.LogStatic($"Loop preview cleanup failed: {ex.Message}"); }
    }
}

internal sealed record VideoLoopPreviewKey(string Source, long Length, long LastWriteTicks, CutVideoSettings Cut,
    VideoLoopCandidate? Bounds, VideoLoopMode Mode, double Speed, VideoLoopPreviewKind Kind, string ColorProfile, int RenderVersion);

/// <summary>Short seam or complete-cycle proxy requests, independent of export resolution/audio/repetitions.</summary>
internal sealed class VideoLoopPreviewBuilder(FfmpegRunner runner, FfprobeRunner prober)
{
    internal const int MaximumDimension = 960;
    internal const int RenderVersion = 2;

    internal static VideoLoopPreviewKey Key(string source, MediaProbeResult probe, CutVideoSettings cut,
        VideoLoopSettings settings, VideoLoopCandidate? bounds, VideoLoopPreviewKind kind)
    {
        var file = new FileInfo(source);
        if (!file.Exists) throw new FileNotFoundException(L.Get("Loop_PreviewSourceUnavailable"), source);
        return new(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks, cut,
            bounds is null ? null : new(bounds.StartSeconds, bounds.EndSeconds, 0), settings.Mode, settings.Speed, kind,
            $"{probe.VideoPixelFormat}|{probe.VideoColorSpace}|{probe.VideoColorTransfer}|{probe.VideoColorPrimaries}|{probe.VideoBitDepth}|{probe.VideoColorRange}|{probe.VideoPeakLuminanceNits:R}|" +
            $"{probe.VideoWidth}|{probe.VideoHeight}|{probe.RotationDegrees}|{probe.VideoSampleAspectRatio:R}|" +
            $"{probe.FormatStartSeconds:R}|{probe.VideoStartSeconds:R}", RenderVersion);
    }

    internal static IReadOnlyList<VideoLoopPreviewClipPlan> Plan(MediaProbeResult probe, VideoLoopCandidate bounds,
        VideoLoopSettings settings, VideoLoopPreviewKind kind)
    {
        settings.Validate();
        VideoLoopColorPolicy.Evaluate(probe).RequireSupported();
        if (!Enum.IsDefined(kind) || !double.IsFinite(bounds.StartSeconds) || !double.IsFinite(bounds.EndSeconds) ||
            bounds.StartSeconds < 0 || bounds.DurationSeconds <= 0 || probe.VideoWidth <= 0 || probe.VideoHeight <= 0)
            throw new ArgumentException(L.Get("Loop_PreviewBoundsInvalid"));
        var sar = probe.VideoSampleAspectRatio;
        if (!double.IsFinite(sar) || sar <= 0) throw new ArgumentException(L.Get("Loop_PixelAspectInvalid"));
        var displayWidth = probe.DisplayVideoWidth * (probe.RequiresDisplayDimensionSwap ? 1 : sar);
        var displayHeight = probe.DisplayVideoHeight * (probe.RequiresDisplayDimensionSwap ? sar : 1);
        var scale = Math.Min(1, MaximumDimension / Math.Max(displayWidth, displayHeight));
        var width = Math.Max(2, (int)Math.Round(displayWidth * scale / 2) * 2);
        var height = Math.Max(2, (int)Math.Round(displayHeight * scale / 2) * 2);
        if (kind == VideoLoopPreviewKind.FullLoop)
            return [new(bounds, VideoLoopPreviewContent.FullLoop, width, height, 3, L.Get("Loop_FullClip"))];
        if (settings.Mode == VideoLoopMode.Natural)
            return [new(bounds, VideoLoopPreviewContent.NaturalJunction, width, height, 1, L.Get("Loop_NaturalClip"))];
        if (4 * bounds.DurationSeconds / settings.Speed <= 6)
            return [new(bounds, VideoLoopPreviewContent.FullLoop, width, height, 2, L.Get("Loop_PingPongClip"))];
        var window = Math.Min(Math.Max(1.5 * settings.Speed, 3 / (probe.VideoFrameRate ?? 1)), bounds.DurationSeconds);
        return [
            new(new(bounds.EndSeconds - window, bounds.EndSeconds, 0), VideoLoopPreviewContent.EndTurn, width, height, 1,
                L.Get("Loop_EndTurn")),
            new(new(bounds.StartSeconds, bounds.StartSeconds + window, 0), VideoLoopPreviewContent.StartTurn, width, height, 1,
                L.Get("Loop_StartTurn"))];
    }

    internal async Task<VideoLoopPreview> BuildAsync(string executable, string probeExecutable, string source,
        MediaProbeResult probe, CutVideoSettings cut, VideoLoopSettings settings, VideoLoopCandidate? exactBounds,
        VideoLoopPreviewKind kind, IProgressReporter? progress, CancellationToken token)
    {
        var exporter = new VideoLoopExporter(runner, prober);
        settings.Validate();
        await VideoLoopColorPolicy.Evaluate(probe).RequireAvailableAsync(runner, executable, token).ConfigureAwait(false);
        var bounds = exactBounds ?? await exporter.ResolveFrameBoundsAsync(executable, source, cut, probe, token).ConfigureAwait(false);
        var plans = Plan(probe, bounds, settings, kind).ToArray();
        if (plans[0].Content == VideoLoopPreviewContent.NaturalJunction)
            plans[0] = await ResolveHeldTailAsync(executable, source, probe, plans[0], progress, token).ConfigureAwait(false);
        var directory = Path.Combine(Path.GetTempPath(), $"frameshift_loop_preview_{Guid.NewGuid():N}");
        var preview = new VideoLoopPreview(directory, plans.Select((p, i) =>
            new VideoLoopPreviewClip(Path.Combine(directory, $"clip_{i}.mp4"), p.Description)).ToArray(), plans[0].Width, plans[0].Height);
        Directory.CreateDirectory(directory);
        try
        {
            for (var i = 0; i < plans.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                await exporter.RenderPreviewAsync(executable, probeExecutable, source, preview.Clips[i].Path, probe, cut,
                    settings with { KeepAudio = false, Repetitions = plans[i].Repetitions }, plans[i], progress, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            return preview;
        }
        catch (VideoLoopProcessTerminationException)
        {
            Logging.AppLogger.LogStatic($"Loop preview retained because process termination was not confirmed: {directory}");
            throw;
        }
        catch { preview.Dispose(); throw; }
    }

    private async Task<VideoLoopPreviewClipPlan> ResolveHeldTailAsync(string executable, string source, MediaProbeResult probe,
        VideoLoopPreviewClipPlan plan, IProgressReporter? progress, CancellationToken token)
    {
        var tail = plan.Segments[0];
        var reader = new VideoLoopFrameReader(runner, executable, source, probe.VideoStartSeconds - probe.FormatStartSeconds, probe.VideoStartSeconds);
        var frames = await reader.ReadAsync(tail.StartSeconds, tail.DurationSeconds, null, 1024, token).ConfigureAwait(false);
        if (frames.Count > 0) return plan;
        progress?.ReportState("processing", L.Get("Loop_ResolveHeldFrame"));
        // Sparse VFR may hold one image throughout the entire requested tail window.
        // Find that image in bounded blocks, then shorten its leading hold to the window.
        var lookback = tail.DurationSeconds;
        while (frames.Count == 0 && lookback < plan.Bounds.DurationSeconds)
        {
            lookback = Math.Min(plan.Bounds.DurationSeconds, lookback * 2);
            var seek = Math.Max(plan.Bounds.StartSeconds, plan.Bounds.EndSeconds - lookback);
            frames = await reader.ReadAsync(seek, plan.Bounds.EndSeconds - seek, null, 1024, token).ConfigureAwait(false);
        }
        if (frames.Count == 0) throw new InvalidOperationException(L.Get("Loop_NoJunctionFrame"));
        var last = frames[^1];
        while (frames.Count == 1024)
        {
            var seek = last.Time + 0.000001;
            frames = await reader.ReadAsync(seek, plan.Bounds.EndSeconds - seek, null, 1024, token).ConfigureAwait(false);
            if (frames.Count > 0) last = frames[^1];
        }
        return plan with { HeldTailStart = last.Time, HeldTailDuration = tail.DurationSeconds };
    }
}
