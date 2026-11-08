using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using Xunit;

namespace FrameShift.Tests;

/// <summary>Deterministic low-resolution media; no private footage or binary asset dependency.</summary>
internal sealed class VideoLoopTestCorpus : IDisposable
{
    internal const int Fps = 15;
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), $"FrameShift loop corpus été {Guid.NewGuid():N}");
    internal string Source => Path.Combine(Root, "source cycle.mkv");
    internal ToolLocator Tools { get; } = new();
    internal FfmpegRunner Runner { get; } = new(new AppLogger());
    internal FfprobeRunner Prober { get; } = new(new AppLogger());
    internal MediaProbeResult Probe { get; private set; } = null!;

    internal async Task GenerateAsync(string scenario, double duration, CancellationToken token,
        bool lowResolution = false, string pixelFormat = "yuv444p")
    {
        Directory.CreateDirectory(Root);
        var period = scenario == "whole-source" ? duration : scenario == "longer-better" ? 4 : 1;
        var frames = VideoLoopFrameReader.Number(period * Fps);
        var phase = $"2*PI*mod(N,{frames})/{frames}";
        var filter = scenario switch
        {
            "many-regions" => "nullsrc=s=160x96:r=15,geq=r='32+mod(47*floor(N/45),160)+30*sin(2*PI*(X/W+mod(N,15)/15))':g='32+mod(83*floor(N/45),160)+20*cos(2*PI*(Y/H+mod(N,15)/15))':b='32+mod(121*floor(N/45),160)'",
            "four-regions" => "nullsrc=s=160x96:r=15,geq=r='128+90*sin(2*PI*(X/W+mod(N,15)/15))':g='32+48*floor(N/45)':b='96+40*cos(2*PI*(Y/H+mod(N,15)/15))'",
            "longer-better" => "nullsrc=s=160x96:r=15,geq=r='128+90*sin(2*PI*(X/W+mod(N,15)/15))+8*sin(2*PI*mod(N,60)/60)':g='128+40*cos(2*PI*(Y/H+mod(N,15)/15))+4*cos(2*PI*mod(N,60)/60)':b='64'",
            "whole-source" or "periodic" => $"nullsrc=s=160x96:r=15,geq=r='128+90*sin(2*PI*X/W+{phase})':g='128+70*cos(2*PI*Y/H+{phase})':b='96+40*sin({phase})'",
            _ => throw new ArgumentException("Unknown loop corpus scenario.", nameof(scenario))
        };
        if (lowResolution) filter = filter.Replace("s=160x96", "s=64x48", StringComparison.Ordinal);
        filter += ",setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709";
        var result = await Runner.RunAsync(Tools.ResolveFfmpegPath(),
            ["-hide_banner", "-loglevel", "error", "-progress", "pipe:1", "-nostats", "-y",
                "-f", "lavfi", "-i", filter, "-t", VideoLoopFrameReader.Number(duration),
                "-an", "-c:v", "ffv1", "-pix_fmt", pixelFormat, "-color_trc", "bt709",
                "-color_primaries", "bt709", "-colorspace", "bt709", Source],
            TimeSpan.FromSeconds(duration), null, Source, "Loop test corpus", "CPU", token);
        Assert.False(result.Canceled);
        Assert.True(result.ProcessTerminationConfirmed);
        Assert.True(result.ExitCode == 0, result.StandardError);
        Probe = (await Prober.TryProbeMediaAsync(Tools.ResolveFfprobePath(), Source, token)).Probe
            ?? throw new InvalidOperationException("Loop corpus probe failed.");
    }

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
