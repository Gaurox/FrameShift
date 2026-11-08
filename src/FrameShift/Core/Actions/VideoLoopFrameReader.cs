using System.Globalization;
using System.Text.RegularExpressions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.Progress;

namespace FrameShift.Core.Actions;

/// <summary>Bounded low-resolution decoding through the existing process owner.</summary>
internal sealed class VideoLoopFrameReader(FfmpegRunner runner, string executable, string source, double videoOffset = 0,
    double videoStartSeconds = 0, int width = VideoLoopFrameReader.Width, int height = VideoLoopFrameReader.Height,
    VideoLoopColorPolicy? colorPolicy = null)
{
    internal sealed record Frame(double Time, byte[] Pixels)
    {
        internal double Duration { get; init; }
        internal int PixelWidth { get; init; } = Width;
        internal int PixelHeight { get; init; } = Height;
    }
    internal const int Width = 64;
    internal const int Height = 48;
    internal long PeakTemporaryBytes { get; private set; }

    internal async Task<List<Frame>> ReadAsync(double seek, double duration, double? sampleRate,
        int maxFrames, CancellationToken token, IProgressReporter? progress = null)
    {
        var file = Path.Combine(Path.GetTempPath(), $"frameshift_loop_{Guid.NewGuid():N}.rgb");
        try
        {
            if (colorPolicy is not null) await colorPolicy.RequireAvailableAsync(runner, executable, token).ConfigureAwait(false);
            var args = new List<string> { "-hide_banner", "-loglevel", "info", "-progress", "pipe:1", "-nostats", "-y" };
            if (colorPolicy?.ConvertsToSdr == true) args.AddRange(["-threads", "2", "-filter_threads", "1"]);
            if (seek + videoOffset > 0) args.AddRange(["-ss", Number(Math.Max(0, seek + videoOffset - (sampleRate is null ? 0.001 : 0)))]);
            if (sampleRate is null) args.Add("-copyts");
            args.AddRange(["-i", source, "-t", Number(duration), "-map", "0:v:0", "-an", "-sn", "-dn"]);
            var filter = $"scale={width}:{height}:flags=area,setsar=1,format=rgb24";
            if (colorPolicy?.ConvertsToSdr == true) filter = colorPolicy.Filter + "," + filter;
            if (sampleRate is double fps) filter = $"fps={Number(fps)}:round=near," + filter;
            else filter = $"trim=start={Number(videoStartSeconds + seek)}:end={Number(videoStartSeconds + seek + duration)}," + filter;
            filter += ",showinfo";
            if (sampleRate is null) filter += ",setpts=PTS-STARTPTS";
            args.AddRange(["-vf", filter, "-frames:v", maxFrames.ToString(CultureInfo.InvariantCulture),
                "-fps_mode", "passthrough", "-f", "rawvideo", file]);
            var timing = new VideoLoopTimingCollector(maxFrames);
            var result = await runner.RunAsync(executable, args, TimeSpan.FromSeconds(duration), progress, source, "Find Loop", "CPU", token, timing.Observe).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (result.Canceled) throw new OperationCanceledException(token);
            if (result.ExitCode != 0) throw new InvalidOperationException(ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, "Loop decoding failed."));
            var times = timing.Times;
            var frames = new List<Frame>();
            await using var stream = File.OpenRead(file);
            PeakTemporaryBytes = Math.Max(PeakTemporaryBytes, stream.Length);
            while (stream.Position < stream.Length)
            {
                token.ThrowIfCancellationRequested();
                var pixels = new byte[checked(width * height * 3)];
                await stream.ReadExactlyAsync(pixels, token).ConfigureAwait(false);
                if (frames.Count >= times.Count) throw new InvalidOperationException("Loop frame timestamps are unavailable.");
                frames.Add(new Frame(sampleRate is null ? times[frames.Count] - videoStartSeconds : seek + times[frames.Count], pixels)
                {
                    Duration = frames.Count < timing.Timings.Count ? timing.Timings[frames.Count].Duration : 0,
                    PixelWidth = width, PixelHeight = height
                });
            }
            return frames;
        }
        finally { ConversionActionHelper.DeleteIfExists(file); }
    }

    internal static List<double> ParseTimes(string log) => Regex.Matches(log, @"\bn:\s*\d+\s+pts:\s*\S+\s+pts_time:\s*([-+\d.eE]+)")
        .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
    internal static List<(double Time, double Duration)> ParseTimings(string log)
    {
        var config = Regex.Match(log, @"config in time_base:\s*(\d+)/(\d+)");
        double? timeBase = config.Success ? double.Parse(config.Groups[1].Value, CultureInfo.InvariantCulture) /
            double.Parse(config.Groups[2].Value, CultureInfo.InvariantCulture) : null;
        return Regex.Matches(log,
            @"\bn:\s*\d+\s+pts:\s*(\S+)\s+pts_time:\s*([-+\d.eE]+)\s+duration:\s*(\S+)\s+duration_time:\s*([-+\d.eE]+)")
            .Select(m =>
            {
                // pts_time uses rounded %g text. Integer ticks retain frame accuracy
                // when manually trimming high-FPS frames far into a long source.
                var time = timeBase is double tb && long.TryParse(m.Groups[1].Value, out var ticks)
                    ? ticks * tb : double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                var duration = timeBase is double dtb && long.TryParse(m.Groups[3].Value, out var durationTicks)
                    ? durationTicks * dtb : double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                return (time, duration);
            }).ToList();
    }
    internal static string Number(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
}
