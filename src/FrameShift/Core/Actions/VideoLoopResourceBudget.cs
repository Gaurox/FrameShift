using System.ComponentModel;
using System.Runtime.InteropServices;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.Progress;

namespace FrameShift.Core.Actions;

internal sealed record VideoLoopResourcePlan(double TemporaryBytes, double OutputSeconds, string Description)
{
    internal static VideoLoopResourcePlan Create(int width, int height, double seconds, double fps,
        VideoLoopSettings settings, bool preview, bool audio, int sampleRate, int channels)
    {
        var raw = seconds * fps * width * height;
        var compact = settings.Mode == VideoLoopMode.Natural && !preview;
        var pcm = audio ? seconds * Math.Max(48000, sampleRate) * Math.Max(2, channels) * 2 : 0;
        var bytes = compact ? raw * 0.5 + pcm + 32 * 1024 * 1024
            : raw * 6 * (settings.Mode == VideoLoopMode.PingPong ? 4 : 1);
        return new(bytes, seconds * settings.Repetitions * (settings.Mode == VideoLoopMode.PingPong ? 2 : 1) / settings.Speed,
            compact ? "natural cycle" : preview ? "preview" : "forward/backward cycle");
    }
}

internal sealed class VideoLoopProcessTerminationException(string workspace, string destination)
    : IOException($"Media process termination could not be confirmed. Temporary files were retained at '{workspace}' and '{destination}'.")
{
    internal string Workspace { get; } = workspace;
    internal string Destination { get; } = destination;
}

internal sealed class VideoLoopCancellationException(CancellationScope scope, CancellationToken token) : OperationCanceledException(token)
{
    internal CancellationScope Scope { get; } = scope;
}

/// <summary>Phase estimates, live temporary-space guard and process-stop barrier for one owned render.</summary>
internal sealed class VideoLoopResourceBudget(string workspace, string destination, string inputPath, Func<string, long> availableSpace)
{
    internal const long MaximumTemporaryBytes = 8L * 1024 * 1024 * 1024;
    private const long FreeReserve = 16 * 1024 * 1024;

    internal static long GetAvailableSpace(string path)
    {
        var full = Path.GetFullPath(path);
        var directory = Directory.Exists(full) ? full : Path.GetDirectoryName(full)!;
        var native = directory.StartsWith(@"\\?\", StringComparison.Ordinal) ? directory
            : directory.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + directory[2..] : @"\\?\" + directory;
        if (!GetDiskFreeSpaceExW(native, out var available, out _, out _))
            throw new IOException("Cannot check available disk space for this loop.", new Win32Exception(Marshal.GetLastWin32Error()));
        return (long)Math.Min((ulong)long.MaxValue, available);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(string directory, out ulong available, out ulong total, out ulong free);

    internal void CheckPlan(VideoLoopResourcePlan plan, bool preview)
    {
        if (!double.IsFinite(plan.TemporaryBytes) || plan.TemporaryBytes > MaximumTemporaryBytes)
            throw new InvalidOperationException($"The {plan.Description} needs an estimated {plan.TemporaryBytes / (1024 * 1024 * 1024):0.##} GB of temporary space, above the 8 GB budget. " +
                (preview ? "Use Preview junction or select a shorter segment." : "Select a shorter segment."));
        CheckFree(workspace, plan.TemporaryBytes, $"Preparing {plan.Description}");
    }

    internal void CheckOutput(double bytes, string phase) => CheckFree(destination, bytes, phase);

    private void CheckFree(string path, double bytes, string phase)
    {
        if (!double.IsFinite(bytes) || bytes + FreeReserve > availableSpace(path) * 0.8)
            throw new IOException($"{phase}: not enough disk space (estimated additional {bytes / (1024 * 1024):0.##} MB). Select a shorter segment or free disk space.");
    }

    internal async Task<FfmpegRunResult> RunAsync(FfmpegRunner runner, string executable, IReadOnlyCollection<string> args,
        double duration, IProgressReporter? progress, CancellationToken token, Action<string>? observer = null)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var stopWatch = new CancellationTokenSource();
        Exception? resourceFailure = null;
        var watch = WatchAsync();
        FfmpegRunResult result;
        try
        {
            result = await runner.RunAsync(executable, args, TimeSpan.FromSeconds(duration), progress,
                inputPath, "Video Loop", "CPU", cancel.Token, observer).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (resourceFailure is not null) { throw resourceFailure; }
        finally { stopWatch.Cancel(); await watch.ConfigureAwait(false); }
        // Check this before cancellation: a failed OS stop must retain its files.
        if (!result.ProcessTerminationConfirmed) throw new VideoLoopProcessTerminationException(workspace, destination);
        if (resourceFailure is not null) throw resourceFailure;
        token.ThrowIfCancellationRequested();
        if (result.Canceled) throw new VideoLoopCancellationException(result.CancellationScope, token);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, "Loop rendering failed."));
        return result;

        async Task WatchAsync()
        {
            try
            {
                while (true)
                {
                    stopWatch.Token.ThrowIfCancellationRequested();
                    var actual = Directory.GetFiles(workspace).Sum(f => new FileInfo(f).Length);
                    if (actual > MaximumTemporaryBytes) throw new IOException("Loop rendering exceeded the 8 GB temporary-work budget.");
                    if (availableSpace(workspace) < FreeReserve || availableSpace(destination) < FreeReserve)
                        throw new IOException("Loop rendering: not enough disk space to continue safely.");
                    await Task.Delay(250, stopWatch.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stopWatch.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { resourceFailure = ex; cancel.Cancel(); }
        }
    }
}
