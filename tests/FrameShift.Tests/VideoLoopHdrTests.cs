using System.Text.Json;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using Xunit;
using static FrameShift.Core.Actions.VideoLoopFrameReader;

namespace FrameShift.Tests;

public sealed class VideoLoopHdrTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"FrameShift HDR été {Guid.NewGuid():N}");
    private readonly ToolLocator _tools = new();
    private readonly FfmpegRunner _runner = new(new AppLogger());
    private readonly FfprobeRunner _prober = new(new AppLogger());
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromMinutes(3));
    private CancellationToken Token => _timeout.Token;
    private string Source => Path.Combine(_root, "rampe lumière.mkv");
    public VideoLoopHdrTests() => Directory.CreateDirectory(_root);
    public void Dispose() { _timeout.Dispose(); Directory.Delete(_root, true); }

    [Fact]
    public async Task BundledSoftwareFilters_AreAvailable()
    {
        var result = await _runner.RunCaptureAsync(_tools.ResolveFfmpegPath(), ["-hide_banner", "-filters"], Token);
        var evidence = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scratch", "loop-validation", "improvements", "lot8");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "ffmpeg-filters.txt"), result.StandardOutput + result.StandardError, Token);
        Assert.True(result.Success);
        await VideoLoopColorPolicy.Evaluate(VideoLoopColorPolicyTests.Probe("smpte2084", "bt2020", space: "bt2020nc"))
            .RequireAvailableAsync(_runner, _tools.ResolveFfmpegPath(), Token);
    }

    private async Task<MediaProbeResult> Generate(string transfer, bool vfr = false, bool fullRange = false)
    {
        // Equal-energy grey patches in scene-linear BT.2020: 0 to 1000 nits.
        // Lower third also includes colored patches to catch matrix/primary mistakes.
        var levels = "if(lt(X,20),0,if(lt(X,40),0.01,if(lt(X,60),0.1,if(lt(X,80),0.5,if(lt(X,100),1,if(lt(X,120),2,if(lt(X,140),4,10)))))))";
        var hdr = transfer is "smpte2084" or "arib-std-b67";
        var linear = hdr ? levels : $"({levels})/10";
        var filter = "nullsrc=s=160x96:r=10,format=gbrpf32le," +
            $"geq=r='{linear}':g='if(gte(Y,64),({linear})*0.4,{linear})':b='if(gte(Y,64),({linear})*0.2,{linear})'," +
            $"zscale=pin=bt2020:tin=linear:min=gbr:rin=full:p=bt2020:t={transfer}:m=bt2020nc:r={(fullRange ? "full" : "limited")}:npl=100,format=yuv444p10le";
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-filter_threads", "1", "-f", "lavfi", "-i", filter };
        if (vfr) args.AddRange(["-vf", "select='not(eq(mod(n,4),3))'"]);
        args.AddRange(["-t", "2", "-fps_mode", "passthrough", "-c:v", "ffv1", "-color_trc", transfer,
            "-color_primaries", "bt2020", "-colorspace", "bt2020nc", "-color_range", fullRange ? "pc" : "tv", Source]);
        var result = await _runner.RunAsync(_tools.ResolveFfmpegPath(), args, null, null, Source, "HDR fixture", "CPU", Token);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return await Probe(Source);
    }

    private async Task<MediaProbeResult> Probe(string path) =>
        (await _prober.TryProbeMediaAsync(_tools.ResolveFfprobePath(), path, Token)).Probe!;

    private Task<List<Frame>> Frames(string path, VideoLoopColorPolicy? color = null) =>
        new VideoLoopFrameReader(_runner, _tools.ResolveFfmpegPath(), path, width: 160, height: 96, colorPolicy: color)
            .ReadAsync(0, 10, null, 100, Token);

    private static double Difference(Frame a, Frame b) => a.Pixels.Zip(b.Pixels, (x, y) => (double)Math.Abs(x - y)).Average();
    private static int Pixel(Frame frame, int x, int y, int channel = 0) => frame.Pixels[(y * 160 + x) * 3 + channel];

    [Theory]
    [InlineData("smpte2084", false)]
    [InlineData("arib-std-b67", false)]
    [InlineData("smpte2084", true)]
    [InlineData("bt2020-10", false)]
    [InlineData("bt2020-12", false)]
    [InlineData("bt709", false)]
    public async Task Ramp_AnalysisPreviewAndExportShareColor_AndRetainNativeVfr(string transfer, bool fullRange)
    {
        var info = await Generate(transfer, vfr: true, fullRange);
        Assert.Equal(fullRange ? "pc" : "tv", info.VideoColorRange);
        var color = VideoLoopColorPolicy.Evaluate(info);
        Assert.True(color.CanRender, color.Message);
        var expected = await Frames(Source, color);
        var native = await Frames(Source);
        Assert.Equal(native.Select(f => f.Time), expected.Select(f => f.Time));
        using (var timing = new VideoLoopTimingIndex(_runner, _tools.ResolveFfmpegPath(), Source, info))
        {
            var first = await timing.ResolveFrameAsync(1, Token);
            using var still = await FrameShift.Windows.Helpers.PreviewFrameHelper.CaptureNativeFrameAsync(
                _tools.ResolveFfmpegPath(), _runner, Source, info, first.Timestamp, Token);
            for (var i = 0; i < 8; i++)
                Assert.InRange(Math.Abs(still.GetPixel(10 + 20 * i, 32).R - Pixel(expected[0], 10 + 20 * i, 32)), 0, 3);
        }
        var output = Path.Combine(_root, "sortie SDR.mp4");
        await new VideoLoopExporter(_runner, _prober).RenderAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(),
            Source, output, info, new(1, 20, 10), new(VideoLoopMode.Natural, Repetitions: 3, KeepAudio: false),
            new(0, 2, 0), null, Token);
        var rendered = await Frames(output);
        Assert.Equal(expected.Count * 3, rendered.Count);
        for (var i = 0; i < rendered.Count; i++)
        {
            Assert.InRange(Difference(expected[i % expected.Count], rendered[i]), 0, 4);
            Assert.InRange(Math.Abs(rendered[i].Time - (expected[i % expected.Count].Time + 2 * (i / expected.Count))), 0, 0.002);
        }
        var probe = await Probe(output);
        Assert.Equal("bt709", probe.VideoColorPrimaries); Assert.Equal("bt709", probe.VideoColorTransfer);
        Assert.Equal("bt709", probe.VideoColorSpace); Assert.Equal("tv", probe.VideoColorRange);
        Assert.Equal("yuv420p", probe.VideoPixelFormat); Assert.False(probe.IsHdrLikely);
        Assert.InRange(probe.Duration!.Value.TotalSeconds, 5.999, 6.001);
        using var preview = await new VideoLoopPreviewBuilder(_runner, _prober).BuildAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(),
            Source, info, new(1, 20, 10), new(VideoLoopMode.Natural), new(0, 2, 0), VideoLoopPreviewKind.FullLoop, null, Token);
        var proxy = await Frames(Assert.Single(preview.Clips).Path);
        Assert.Equal(rendered.Count, proxy.Count);
        for (var i = 0; i < rendered.Count; i++) Assert.InRange(Difference(rendered[i], proxy[i]), 0, 4);

        var patches = Enumerable.Range(0, 8).Select(i => Pixel(expected[0], 10 + i * 20, 32)).ToArray();
        if (color.ToneMapsHdr)
        {
            var diagnostics = new List<float[]>();
            foreach (var stage in new[] { color.Filter[..color.Filter.IndexOf(",zscale=p=bt709", StringComparison.Ordinal)],
                color.Filter[..color.Filter.IndexOf(",tonemap=", StringComparison.Ordinal)],
                color.Filter[..color.Filter.IndexOf(",zscale=t=bt709", StringComparison.Ordinal)] })
            {
                var raw = Path.Combine(_root, "stage.rgb");
                var result = await _runner.RunAsync(_tools.ResolveFfmpegPath(), ["-hide_banner", "-loglevel", "error", "-y", "-filter_threads", "1", "-i", Source,
                    "-vf", stage, "-frames:v", "1", "-pix_fmt", "gbrpf32le", "-f", "rawvideo", raw], null, null, Source, "HDR curve", "CPU", Token);
                Assert.True(result.ExitCode == 0, result.StandardError);
                var bytes = await File.ReadAllBytesAsync(raw, Token);
                diagnostics.Add(Enumerable.Range(0, 8).Select(i => BitConverter.ToSingle(bytes, (32 * 160 + 10 + 20 * i) * 4)).ToArray());
            }
            var inputLevels = new[] { 0d, 0.01, 0.1, 0.5, 1, 2, 4, 10 };
            static double Hable(double x) => (x * (0.15 * x + 0.05) + 0.004) / (x * (0.15 * x + 0.5) + 0.06) - 0.02 / 0.3;
            for (var i = 0; i < inputLevels.Length; i++)
            {
                Assert.InRange(Math.Abs(diagnostics[0][i] - inputLevels[i]), 0, Math.Max(0.002, inputLevels[i] * 0.015));
                Assert.InRange(Math.Abs(diagnostics[1][i] - diagnostics[0][i]), 0, 0.0001);
                // Independent curve check catches inappropriate RGB luma coefficients.
                Assert.InRange(Math.Abs(diagnostics[2][i] - Hable(diagnostics[0][i]) / Hable(10)), 0, 0.001);
            }
        }
        Assert.InRange(patches[0], 0, 3);
        for (var i = 1; i < patches.Length; i++) Assert.True(patches[i] > patches[i - 1], JsonSerializer.Serialize(patches));
        Assert.InRange(patches[^1], 245, 255);
        // Most patches must retain detail below white, rather than a depth-only clip.
        Assert.All(patches.Skip(1).Take(6), value => Assert.InRange(value, 5, 244));
        Assert.InRange(Math.Abs(Pixel(expected[0], 90, 32) - Pixel(expected[0], 90, 32, 1)), 0, 3);
        Assert.True(Pixel(expected[0], 90, 80) > Pixel(expected[0], 90, 80, 1) + 15);
        var evidence = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../scratch/loop-validation/improvements/lot8"));
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, $"ramp-{transfer}-{fullRange}.json"), JsonSerializer.Serialize(new
        { Transfer = transfer, FullRange = fullRange, Patches = patches, FrameCount = expected.Count, ExportFrames = rendered.Count,
            MaximumExportMeanError = rendered.Select((f, i) => Difference(f, expected[i % expected.Count])).Max(),
            MaximumPreviewMeanError = rendered.Select((f, i) => Difference(f, proxy[i])).Max(), Policy = color, OutputProbe = probe }, new JsonSerializerOptions { WriteIndented = true }), Token);
        File.Copy(output, Path.Combine(evidence, $"ramp-{transfer}-{fullRange}-sdr.mp4"), true);
        var png = Path.Combine(evidence, $"ramp-{transfer}-{fullRange}-sdr.png");
        var image = await _runner.RunAsync(_tools.ResolveFfmpegPath(), ["-hide_banner", "-loglevel", "error", "-y", "-i", output, "-frames:v", "1", png],
            null, null, output, "SDR visual reference", "CPU", Token);
        Assert.Equal(0, image.ExitCode);
    }

    [Theory]
    [InlineData("smpte2084")]
    [InlineData("arib-std-b67")]
    public async Task Hdr_SearchAndJunctionAndReverse_WorkWithoutChangingTheSelection(string transfer)
    {
        var info = await Generate(transfer);
        var result = await new VideoLoopDetector(_runner).AnalyzeAsync(_tools.ResolveFfmpegPath(), Source, info, null, LoopSensitivity.Normal, null, Token);
        Assert.True(result.SearchCompleted); Assert.NotEmpty(result.Candidates);
        var bounds = new VideoLoopCandidate(0, 2, 0);
        using var junction = await new VideoLoopPreviewBuilder(_runner, _prober).BuildAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(),
            Source, info, new(1, 20, 10), new(VideoLoopMode.Natural), bounds, VideoLoopPreviewKind.Junction, null, Token);
        var junctionFrames = await Frames(Assert.Single(junction.Clips).Path);
        Assert.Equal(20, junctionFrames.Count);
        var output = Path.Combine(_root, "reverse.mp4");
        await new VideoLoopExporter(_runner, _prober).RenderAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(),
            Source, output, info, new(1, 20, 10), new(VideoLoopMode.PingPong, KeepAudio: false), bounds, null, Token);
        var reverse = await Frames(output); Assert.Equal(38, reverse.Count);
        Assert.InRange(Difference(junctionFrames[0], reverse[0]), 0, 4);
        var probe = await Probe(output); Assert.Equal("bt709", probe.VideoColorTransfer); Assert.False(probe.HasAudio);
    }

    [Theory]
    [InlineData("smpte2084")]
    [InlineData("arib-std-b67")]
    public async Task Hdr_CancellationDuringConversion_CleansOutputAndReapsDecoder(string transfer)
    {
        var info = await Generate(transfer);
        using var cancel = new CancellationTokenSource();
        var encoding = false; var canceledDuringEncoding = false;
        var progress = new LoopExportTestProgress((_, _) =>
        {
            if (encoding) { canceledDuringEncoding = true; cancel.Cancel(); }
        }, message => { if (message?.StartsWith("Encoding") == true) encoding = true; });
        var request = new ActionRequest(Source, new AppLogger(), progress);
        var workspaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exporter = new VideoLoopExporter(_runner, _prober) { AvailableSpace = path =>
        {
            if (Path.GetFileName(path).StartsWith("frameshift_loop_render_", StringComparison.Ordinal)) workspaces.Add(path);
            return VideoLoopResourceBudget.GetAvailableSpace(path);
        } };
        await Assert.ThrowsAsync<OperationCanceledException>(() => exporter.ExportAsync(
            _tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), request, info, new(1, 20, 10),
            new(VideoLoopMode.Natural, KeepAudio: false), new(0, 2, 0), cancel.Token));
        Assert.True(canceledDuringEncoding);
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
        Assert.Empty(Directory.GetFiles(_root, "*_loop*.mp4"));
        Assert.NotEmpty(workspaces); Assert.All(workspaces, path => Assert.False(Directory.Exists(path)));
        using var exclusive = File.Open(Source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task Hdr_StaticPeakMetadata_IsReadAndRemovedFromSdrFrames()
    {
        await Generate("smpte2084");
        var hevc = Path.Combine(_root, "mastering.mkv");
        var result = await _runner.RunAsync(_tools.ResolveFfmpegPath(), ["-hide_banner", "-loglevel", "error", "-y", "-i", Source,
            "-c:v", "libx265", "-pix_fmt", "yuv420p10le", "-x265-params",
            "pools=1:frame-threads=1:master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1):max-cll=4000,1000", hevc],
            null, null, Source, "HDR metadata", "CPU", Token);
        Assert.True(result.ExitCode == 0, result.StandardError);
        var info = await Probe(hevc);
        Assert.Equal(4000d, info.VideoPeakLuminanceNits);
        var output = Path.Combine(_root, "SDR metadata.mp4");
        await new VideoLoopExporter(_runner, _prober).RenderAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(),
            hevc, output, info, new(1, 20, 10), new(VideoLoopMode.Natural, KeepAudio: false), new(0, 2, 0), null, Token);
        var frames = await _prober.RunProbeAsync(_tools.ResolveFfprobePath(), ["-v", "error", "-select_streams", "v:0",
            "-show_frames", "-show_entries", "frame=color_transfer,color_primaries,color_space:frame_side_data=side_data_type", "-of", "json", output], Token);
        Assert.True(frames.Success, frames.StandardError);
        Assert.DoesNotContain("Mastering", frames.Output); Assert.DoesNotContain("Content light", frames.Output);
        Assert.DoesNotContain("smpte2084", frames.Output); Assert.DoesNotContain("bt2020", frames.Output);
        Assert.Null((await Probe(output)).VideoPeakLuminanceNits);
    }
}
