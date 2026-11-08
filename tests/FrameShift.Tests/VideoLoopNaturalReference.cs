using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using static FrameShift.Core.Actions.VideoLoopFrameReader;

namespace FrameShift.Tests;

/// <summary>Test-only snapshot of the pre-lot-7 natural FFV1/PCM path.</summary>
internal static class VideoLoopNaturalReference
{
    internal static async Task<long> RenderAsync(FfmpegRunner runner, string executable, string source, string output,
        MediaProbeResult probe, VideoLoopCandidate bounds, int repeats, bool audio, CancellationToken token)
    {
        var workspace = Path.Combine(Path.GetDirectoryName(output)!, "reference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var safe = true;
        try
        {
            var first = probe.VideoStartSeconds + bounds.StartSeconds;
            var end = probe.VideoStartSeconds + bounds.EndSeconds;
            var cycle = Path.Combine(workspace, "cycle.mkv");
            var forward = new List<string> { "-ss", Number(Math.Max(0, first - probe.FormatStartSeconds - 0.1)), "-copyts", "-i", source,
                "-map", "0:v:0", "-vf", $"trim=start={Number(first)}:end={Number(end)},setpts=PTS-STARTPTS", "-c:v", "ffv1", "-level", "3", "-fps_mode", "passthrough" };
            if (audio) forward.AddRange(["-map", "0:a:0", "-af", $"atrim=start={Number(first)}:end={Number(end)},asetpts=PTS-{Number(first)}/TB,aresample=async=1:first_pts=0,apad,atrim=duration={Number(bounds.DurationSeconds)}", "-c:a", "pcm_s16le"]);
            else forward.Add("-an");
            forward.AddRange(["-t", Number(bounds.DurationSeconds), cycle]);
            await Run(forward);
            var bytes = new FileInfo(cycle).Length;
            var list = Path.Combine(workspace, "cycles.txt");
            await File.WriteAllLinesAsync(list, Enumerable.Range(0, repeats).SelectMany(_ => new[] { "file 'cycle.mkv'", $"duration {Number(bounds.DurationSeconds)}" }), token);
            var final = new List<string> { "-f", "concat", "-safe", "1", "-i", list, "-map", "0:v:0", "-vf", "setpts=PTS-STARTPTS", "-fps_mode", "passthrough",
                "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-bf", "0", "-pix_fmt", probe.DisplayVideoWidth % 2 == 0 && probe.DisplayVideoHeight % 2 == 0 ? "yuv420p" : "yuv444p",
                "-bsf:v", $"setts=duration='if(eq(NEXT_PTS,NOPTS),max(1,round({Number(bounds.DurationSeconds * repeats)}/TB-PTS)),DURATION)'" };
            if (audio) final.AddRange(["-map", "0:a:0", "-c:a", "aac", "-b:a", "256k"]);
            else final.Add("-an");
            final.AddRange(["-map_metadata", "-1", "-movflags", "+faststart", output]);
            await Run(final);
            return bytes;
        }
        finally { if (safe) Directory.Delete(workspace, true); }

        async Task Run(List<string> args)
        {
            var result = await runner.RunAsync(executable, new[] { "-hide_banner", "-loglevel", "error", "-progress", "pipe:1", "-nostats", "-n" }.Concat(args).ToArray(),
                null, null, source, "Loop FFV1 reference", "CPU", token);
            safe = result.ProcessTerminationConfirmed;
            if (!safe) throw new IOException("Reference process did not stop; test workspace retained.");
            token.ThrowIfCancellationRequested();
            if (result.ExitCode != 0) throw new InvalidOperationException(result.StandardError);
        }
    }
}
