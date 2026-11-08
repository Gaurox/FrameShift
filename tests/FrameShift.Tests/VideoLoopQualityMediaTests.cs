using System.Text.Json;
using System.Drawing;
using System.Drawing.Imaging;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class VideoLoopQualityMediaTests
{
    [Fact]
    public async Task BetterLongCycle_RanksBeforeShortCycle_AndDecodedExportsConfirmTheDifference()
    {
        using var corpus = new VideoLoopTestCorpus();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await corpus.GenerateAsync("longer-better", 12, timeout.Token);
        var analysis = await new VideoLoopDetector(corpus.Runner).AnalyzeAsync(corpus.Tools.ResolveFfmpegPath(),
            corpus.Source, corpus.Probe, 1, LoopSensitivity.Normal, null, timeout.Token);
        Assert.NotEmpty(analysis.Candidates);
        Assert.True(analysis.Candidates[0].DurationSeconds > 3.9, JsonSerializer.Serialize(analysis.Candidates.Take(5)));
        var longer = analysis.Candidates.First(c => Math.Abs(c.DurationSeconds - 4) < 0.01 && c.EndSeconds < 11);
        var shorter = analysis.Candidates.First(c => c.DurationSeconds < 1.1 && c.EndSeconds < 11);
        Assert.True(longer.Seam!.Error < shorter.Seam!.Error);
        Assert.True(analysis.Candidates.ToList().IndexOf(longer) < analysis.Candidates.ToList().IndexOf(shorter));
        var evidence = EvidenceDirectory();
        var inspected = new List<object>();
        var exportErrors = new List<double[]>();
        foreach (var (name, candidate) in new[] { ("long-exact", longer), ("short-approximate", shorter) })
        {
            var output = Path.Combine(corpus.Root, name + ".mp4");
            await new VideoLoopExporter(corpus.Runner, corpus.Prober).RenderAsync(corpus.Tools.ResolveFfmpegPath(),
                corpus.Tools.ResolveFfprobePath(), corpus.Source, output, corpus.Probe, new(1, 180, 15),
                new(VideoLoopMode.Natural, Repetitions: 3, KeepAudio: false), candidate, null, timeout.Token);
            var reader = new VideoLoopFrameReader(corpus.Runner, corpus.Tools.ResolveFfmpegPath(), output);
            var decoded = await reader.ReadAsync(0, candidate.DurationSeconds * 3 + 0.2, null, 600, timeout.Token);
            var cycleFrames = (int)Math.Round(candidate.DurationSeconds * 15);
            Assert.Equal(cycleFrames * 3, decoded.Count);
            for (var junction = 1; junction <= 2; junction++)
            for (var i = -2; i <= 2; i++)
            {
                var original = ((i % cycleFrames) + cycleFrames) % cycleFrames;
                Assert.InRange(MeanDifference(decoded[original].Pixels, decoded[junction * cycleFrames + i].Pixels), 0, 2);
            }
            var expected = await new VideoLoopFrameReader(corpus.Runner, corpus.Tools.ResolveFfmpegPath(), corpus.Source)
                .ReadAsync(candidate.EndSeconds, 0.15, null, 2, timeout.Token);
            var start = await new VideoLoopFrameReader(corpus.Runner, corpus.Tools.ResolveFfmpegPath(), corpus.Source)
                .ReadAsync(candidate.StartSeconds, 0.15, null, 2, timeout.Token);
            var discontinuity = MeanDifference(expected[0].Pixels, decoded[cycleFrames].Pixels);
            // Subtract the same image's encoding/color error: a lossy H.264 roundtrip
            // is not itself a seam defect. Compare the two repeated transitions too.
            var seamResiduals = Enumerable.Range(1, 2).Select(j => Enumerable.Range(0, start[0].Pixels.Length).Average(i =>
                Math.Abs((decoded[j * cycleFrames].Pixels[i] - expected[0].Pixels[i]) -
                    (decoded[0].Pixels[i] - start[0].Pixels[i])))).ToArray();
            exportErrors.Add(seamResiduals);
            inspected.Add(new { Name = name, Candidate = candidate, Discontinuity = discontinuity, SeamResiduals = seamResiduals, Frames = decoded.Count });
            File.Copy(output, Path.Combine(evidence, name + ".mp4"), overwrite: true);
            SaveJunctionStrip(decoded, cycleFrames, Path.Combine(evidence, name + ".png"));
            if (name == "long-exact") Assert.Equal(0, MeanDifference(start[0].Pixels, expected[0].Pixels));
            else Assert.True(MeanDifference(start[0].Pixels, expected[0].Pixels) > 1.5);
        }
        await File.WriteAllTextAsync(Path.Combine(evidence, "quality-exports.json"),
            JsonSerializer.Serialize(new { Analysis = analysis, Exports = inspected }, new JsonSerializerOptions { WriteIndented = true }), timeout.Token);
        Assert.All(exportErrors[0], mismatch => Assert.InRange(mismatch, 0, 2));
        for (var j = 0; j < 2; j++) Assert.True(exportErrors[1][j] > exportErrors[0][j] + 0.5,
            $"Long/short decoded seam residuals: {JsonSerializer.Serialize(exportErrors)}");
    }

    [Fact]
    public async Task ShortSource_RemainsSearchableWithTheAutoMinimum()
    {
        using var corpus = new VideoLoopTestCorpus();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        await corpus.GenerateAsync("whole-source", 0.8, timeout.Token);
        var analysis = await new VideoLoopDetector(corpus.Runner).AnalyzeAsync(corpus.Tools.ResolveFfmpegPath(),
            corpus.Source, corpus.Probe, null, LoopSensitivity.Normal, null, timeout.Token);
        Assert.Contains(analysis.Candidates, c => c.StartSeconds == 0 && Math.Abs(c.EndSeconds - 0.8) < 0.001);
    }

    [Fact]
    public async Task AutoMinimum_UsesVideoEndEvenWhenAudioContinuesLonger()
    {
        using var corpus = new VideoLoopTestCorpus();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        await corpus.GenerateAsync("whole-source", 0.8, timeout.Token);
        var source = Path.Combine(corpus.Root, "vidéo courte audio long.mkv");
        var result = await corpus.Runner.RunAsync(corpus.Tools.ResolveFfmpegPath(),
            ["-hide_banner", "-loglevel", "error", "-progress", "pipe:1", "-nostats", "-y", "-i", corpus.Source,
                "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo:d=2", "-map", "0:v", "-map", "1:a",
                "-c:v", "copy", "-c:a", "pcm_s16le", source], null, null, source, "Auto minimum fixture", "CPU", timeout.Token);
        Assert.True(result.ExitCode == 0, result.StandardError);
        var probe = (await corpus.Prober.TryProbeMediaAsync(corpus.Tools.ResolveFfprobePath(), source, timeout.Token)).Probe!;
        Assert.True(probe.Duration!.Value.TotalSeconds > 1.9);
        var analysis = await new VideoLoopDetector(corpus.Runner).AnalyzeAsync(corpus.Tools.ResolveFfmpegPath(),
            source, probe, null, LoopSensitivity.Normal, null, timeout.Token);
        using var timing = new VideoLoopTimingIndex(corpus.Runner, corpus.Tools.ResolveFfmpegPath(), source, probe);
        var nativeEnd = (await timing.ResolveTimeAsync(2, true, timeout.Token)).End;
        Assert.InRange(nativeEnd, 0.798, 0.802); // Muxed packet durations use the native millisecond clock.
        Assert.Contains(analysis.Candidates, c => c.StartSeconds == 0 && c.EndSeconds == nativeEnd);
    }

    [Fact]
    public async Task HigherResolutionDecode_ExposesDetailLostInTheCoarsePass()
    {
        using var corpus = new VideoLoopTestCorpus();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        Directory.CreateDirectory(corpus.Root);
        var result = await corpus.Runner.RunAsync(corpus.Tools.ResolveFfmpegPath(),
            ["-hide_banner", "-loglevel", "error", "-progress", "pipe:1", "-nostats", "-y", "-f", "lavfi", "-i",
                "color=c=0x282828:s=1920x1080:r=10:d=1.2,drawbox=x=0:y=0:w=20:h=20:color=white:t=fill:enable='gte(t,0.6)'",
                "-an", "-c:v", "ffv1", corpus.Source], null, null, corpus.Source, "Fine detail fixture", "CPU", timeout.Token);
        Assert.True(result.ExitCode == 0, result.StandardError);
        var probe = (await corpus.Prober.TryProbeMediaAsync(corpus.Tools.ResolveFfprobePath(), corpus.Source, timeout.Token)).Probe!;
        var size = VideoLoopDetector.RefinementSize(probe);
        Assert.Equal((192, 108), size);
        var reports = new List<object>();
        foreach (var (width, height) in new[] { (64, 48), size })
        {
            var frames = await new VideoLoopFrameReader(corpus.Runner, corpus.Tools.ResolveFfmpegPath(), corpus.Source,
                width: width, height: height).ReadAsync(0, 1.2, null, 12, timeout.Token);
            Assert.Equal(12, frames.Count);
            Assert.All(frames, f => Assert.Equal(width * height * 3, f.Pixels.Length));
            var left = frames.Take(6).ToArray();
            var right = frames.Skip(6).Select(f => f with { Time = f.Time + 2 }).ToArray();
            var candidate = new VideoLoopSeamEvaluator(left, right).Evaluate(2, 1, right[2].Time, LoopSensitivity.Strict, timeout.Token);
            reports.Add(new { Width = width, Height = height, Candidate = candidate });
            if (width == 64) Assert.NotNull(candidate);
            else Assert.Null(candidate);
        }
        await File.WriteAllTextAsync(Path.Combine(EvidenceDirectory(), "fine-detail.json"), JsonSerializer.Serialize(reports), timeout.Token);
    }

    [Theory]
    [InlineData("240", 240, 1d)]
    [InlineData("24000/1001", 24, 1.001d)]
    public async Task NativeSearch_HandlesHighAndFractionalFrameRates(string rate, int cycleFrames, double period)
    {
        using var corpus = new VideoLoopTestCorpus();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        Directory.CreateDirectory(corpus.Root);
        var filter = $"nullsrc=s=128x72:r={rate}:d=2.1,geq=r='128+80*sin(2*PI*(X/W+N/{cycleFrames}))':g='128+50*cos(2*PI*(Y/H+N/{cycleFrames}))':b='64'";
        var result = await corpus.Runner.RunAsync(corpus.Tools.ResolveFfmpegPath(),
            ["-hide_banner", "-loglevel", "error", "-progress", "pipe:1", "-nostats", "-y", "-f", "lavfi", "-i", filter,
                "-an", "-c:v", "ffv1", "-pix_fmt", "yuv444p", corpus.Source], null, null, corpus.Source, "Native FPS fixture", "CPU", timeout.Token);
        Assert.True(result.ExitCode == 0, result.StandardError);
        var probe = (await corpus.Prober.TryProbeMediaAsync(corpus.Tools.ResolveFfprobePath(), corpus.Source, timeout.Token)).Probe!;
        var analysis = await new VideoLoopDetector(corpus.Runner).AnalyzeAsync(corpus.Tools.ResolveFfmpegPath(),
            corpus.Source, probe, 0.9, LoopSensitivity.Normal, null, timeout.Token);
        Assert.Contains(analysis.Candidates, c => Math.Abs(c.DurationSeconds - period) < 0.01);
        Assert.InRange(analysis.Diagnostics.MaximumResidentRefinementFrames, 1, VideoLoopDetector.MaximumRefinementFrames * 2);
        Assert.Equal(128, analysis.Diagnostics.RefinementWidth); Assert.Equal(72, analysis.Diagnostics.RefinementHeight);
    }

    private static double MeanDifference(byte[] a, byte[] b)
        => a.Zip(b, (x, y) => (double)Math.Abs(x - y)).Average();

    private static void SaveJunctionStrip(IReadOnlyList<VideoLoopFrameReader.Frame> frames, int count, string path)
    {
        using var strip = new Bitmap(4 * 192, 2 * 168);
        using var graphics = Graphics.FromImage(strip);
        graphics.Clear(Color.White);
        for (var junction = 1; junction <= 2; junction++)
        for (var col = 0; col < 4; col++)
        {
            var index = junction * count + col - 2;
            var frame = frames[index];
            using var image = new Bitmap(frame.PixelWidth, frame.PixelHeight);
            for (var y = 0; y < frame.PixelHeight; y++)
            for (var x = 0; x < frame.PixelWidth; x++)
            {
                var i = (y * frame.PixelWidth + x) * 3;
                image.SetPixel(x, y, Color.FromArgb(frame.Pixels[i], frame.Pixels[i + 1], frame.Pixels[i + 2]));
            }
            graphics.DrawImage(image, col * 192, (junction - 1) * 168, 192, 144);
            graphics.DrawString($"{(col < 2 ? "End" : "Start")}  {frame.Time:0.###} s", SystemFonts.DefaultFont, Brushes.Black,
                col * 192, (junction - 1) * 168 + 144);
        }
        strip.Save(path, ImageFormat.Png);
    }

    private static string EvidenceDirectory()
    {
        for (var root = new DirectoryInfo(AppContext.BaseDirectory); root is not null; root = root.Parent)
            if (File.Exists(Path.Combine(root.FullName, "src", "FrameShift", "FrameShift.csproj")))
            {
                var directory = Path.Combine(root.FullName, "scratch", "loop-validation", "improvements", "lot6-2026-10-29");
                Directory.CreateDirectory(directory);
                return directory;
            }
        throw new DirectoryNotFoundException("Loop quality evidence requires the source checkout.");
    }
}
