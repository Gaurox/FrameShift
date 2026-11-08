using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Core.Progress;
using Xunit;

namespace FrameShift.Tests;

internal sealed class VideoLoopExportFixture : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), $"FrameShift export d'été {Guid.NewGuid():N}");
    internal string Source => Path.Combine(Root, "source vidéo.mkv");
    internal ToolLocator Tools { get; } = new();
    internal FfmpegRunner Runner { get; } = new(new AppLogger());
    internal FfprobeRunner Prober { get; } = new(new AppLogger());
    internal MediaProbeResult Probe { get; private set; } = null!;
    internal VideoLoopExportFixture() => Directory.CreateDirectory(Root);
    internal async Task GenerateAsync(string filter = "testsrc2=s=160x96:r=10", double seconds = 3, string? videoFilter = null, bool audio = false)
    {
        var args = new List<string> { "-f", "lavfi", "-i", filter };
        if (audio) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000"]);
        if (videoFilter is not null) args.AddRange(["-vf", videoFilter]);
        args.AddRange(["-t", VideoLoopFrameReader.Number(seconds), "-fps_mode", "passthrough", "-c:v", "ffv1", "-pix_fmt", "yuv444p"]);
        if (audio) args.AddRange(["-c:a", "pcm_s16le"]);
        args.Add(Source);
        await RunAsync(args);
        Probe = await ReadProbeAsync(Source);
    }
    internal async Task RunAsync(IReadOnlyCollection<string> arguments)
    {
        var result = await Runner.RunAsync(Tools.ResolveFfmpegPath(), new[] { "-hide_banner", "-loglevel", "error", "-progress", "pipe:1", "-nostats", "-n" }.Concat(arguments).ToArray(),
            null, null, Source, "Loop resource fixture", "CPU", CancellationToken.None);
        Assert.True(result.ProcessTerminationConfirmed);
        Assert.True(result.ExitCode == 0, result.StandardError);
    }
    internal async Task<MediaProbeResult> ReadProbeAsync(string source) =>
        (await Prober.TryProbeMediaAsync(Tools.ResolveFfprobePath(), source, CancellationToken.None)).Probe!;
    internal Task RenderAsync(string output, VideoLoopSettings settings, VideoLoopCandidate bounds, IProgressReporter? progress = null,
        CancellationToken token = default, VideoLoopExporter? exporter = null) =>
        (exporter ?? new(Runner, Prober)).RenderAsync(Tools.ResolveFfmpegPath(), Tools.ResolveFfprobePath(), Source, output, Probe,
            new(1, 30, Probe.VideoFrameRate!.Value), settings, bounds, progress, token);
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}

internal sealed class LoopExportTestProgress(Action<int, string?>? progress = null, Action<string?>? state = null) : IProgressReporter
{
    internal Func<string, bool>? QueueCancel { get; init; }
    public bool IsCancellationRequested => false;
    public bool IsQueueItemRemovalRequested(string path) => false;
    public bool IsQueueItemCancellationRequested(string path) => QueueCancel?.Invoke(path) == true;
    public void ReportQueue(IReadOnlyList<string> items) { }
    public void ReportQueueItem(string path, string status, string? message = null) { }
    public void ReportState(string status, string? message = null) => state?.Invoke(message);
    public void ReportProgress(int value, string? file, string? action, string? message, string? etaText = null) => progress?.Invoke(value, file);
}
