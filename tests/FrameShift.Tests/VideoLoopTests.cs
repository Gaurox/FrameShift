using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Core.Progress;
using Xunit;
using static FrameShift.Core.Actions.VideoLoopFrameReader;

namespace FrameShift.Tests;

public sealed class VideoLoopTests
{
    private static Frame Solid(double time, byte level)
        => new(time, Enumerable.Repeat(level, Width * Height * 3).ToArray());

    [Fact]
    public void StaticVideo_HasValidShortestLoop()
    {
        var frames = Enumerable.Range(0, 80).Select(i => Solid(i / 10d, 80)).ToList();
        var seeds = VideoLoopDetector.FindSeeds(frames, 1, CancellationToken.None);
        Assert.NotEmpty(seeds);
        var result = VideoLoopDetector.Refine(frames.Take(25).ToList(), frames.Skip(10).Take(25).ToList(), seeds[0], 1, LoopSensitivity.Strict, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(1, result!.DurationSeconds, 5);
        Assert.Equal(100, result.Quality);
    }

    [Fact]
    public void EvolvingLight_RejectsDistantFrames()
    {
        var frames = Enumerable.Range(0, 80).Select(i => Solid(i / 10d, (byte)(i * 3))).ToList();
        Assert.Empty(VideoLoopDetector.FindSeeds(frames, 2, CancellationToken.None));
    }

    [Fact]
    public void SpatialCandidateIndex_FindsLongCyclesWithConstantGlobalColorMean()
    {
        var frames = new List<Frame>();
        for (var i = 0; i < 202; i++)
        {
            var frame = Solid(i / 8d, 0);
            var phase = i % 80;
            var left = phase % 40; var top = phase / 40 * 8;
            for (var y = top; y < top + 8; y++)
            for (var x = left; x < left + 8; x++) frame.Pixels[(y * Width + x) * 3] = 255;
            frames.Add(frame);
        }
        var seeds = VideoLoopDetector.FindSeeds(frames, 1, CancellationToken.None);
        Assert.Contains(seeds, c => Math.Abs(c.DurationSeconds - 10) < 0.01);
    }

    [Fact]
    public void MatchingPose_OppositeMotion_IsRejected()
    {
        var left = Enumerable.Range(0, 9).Select(i => Solid(i / 10d, (byte)(40 + i * 20))).ToList();
        var right = Enumerable.Range(0, 9).Select(i => Solid(2 + i / 10d, (byte)(200 - i * 20))).ToList();
        Assert.Null(VideoLoopDetector.Refine(left, right, new(0.4, 2.4, 100), 1, LoopSensitivity.Normal, CancellationToken.None));
    }

    [Fact]
    public void StaticBackground_DoesNotHideChangingForeground()
    {
        var left = Enumerable.Range(0, 9).Select(i => Solid(i / 10d, 40)).ToList();
        var right = Enumerable.Range(0, 9).Select(i => Solid(2 + i / 10d, 40)).ToList();
        foreach (var frame in right)
            for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            for (var c = 0; c < 3; c++) frame.Pixels[(y * Width + x) * 3 + c] = 220;
        Assert.Null(VideoLoopDetector.Refine(left, right, new(0.4, 2.4, 100), 1, LoopSensitivity.Permissive, CancellationToken.None));
    }

    [Fact]
    public void ChaoticFrames_AreNotPresentedAsPerfectLoops()
    {
        var rng = new Random(20);
        var frames = Enumerable.Range(0, 80).Select(i => { var pixels = new byte[Width * Height * 3]; rng.NextBytes(pixels); return new Frame(i / 10d, pixels); }).ToList();
        Assert.Null(VideoLoopDetector.Refine(frames.Take(20).ToList(), frames.Skip(40).Take(20).ToList(), new(0.4, 4.4, 100), 1, LoopSensitivity.Permissive, CancellationToken.None));
    }

    [Fact]
    public void Ranking_PrefersQuality_AndRemovesNearDuplicates()
    {
        var result = VideoLoopDetector.Rank([new(1, 3, 95), new(1.02, 3.02, 99), new(5, 8, 99), new(9, 13, 100), new(0, 6, 100)]);
        Assert.Equal(4, result.Count);
        Assert.Equal(4, result[0].DurationSeconds);
        Assert.Equal(100, result[0].Quality);
        Assert.DoesNotContain(result, c => c.Quality == 95);
    }

    [Theory]
    [InlineData("natural", "1", "keep")]
    [InlineData("pingpong", "0.75", "mute")]
    [InlineData("pingpong", "0.5", "mute")]
    public void CliOptions_RoundTrip(string mode, string speed, string audio)
    {
        Assert.True(Program.TryParseArguments(["--action", "cut-video", "--loop-mode", mode, "--loop-speed", speed,
            "--loop-audio", audio, "--loop-repeat", "3", "--start-frame", "1", "--end-frame", "30", "vidéo été.mp4"], out _, out var paths, out var options, out var error), error);
        Assert.Single(paths);
        var settings = VideoLoopSettings.FromOptions(options)!;
        Assert.Equal(3, settings.Repetitions);
        Assert.Equal(settings, VideoLoopSettings.FromOptions(settings.ToOptions()));
    }

    [Theory]
    [InlineData("bad", "1", "mute", "1")]
    [InlineData("natural", "0.5", "mute", "1")]
    [InlineData("pingpong", "1", "keep", "1")]
    [InlineData("natural", "1", "mute", "11")]
    [InlineData("natural", "NaN", "mute", "1")]
    public void InvalidSettings_FailBeforeExport(string mode, string speed, string audio, string repetitions)
        => Assert.ThrowsAny<ArgumentException>(() => VideoLoopSettings.FromOptions(new Dictionary<string, string>
        { ["loop-mode"] = mode, ["loop-speed"] = speed, ["loop-audio"] = audio, ["loop-repeat"] = repetitions }));

    [Fact]
    public void Cli_MissingOrUnknownLoopOption_IsRejected()
    {
        Assert.False(Program.TryParseArguments(["--action", "cut-video", "--loop-speed"], out _, out _, out _, out _));
        Assert.False(Program.TryParseArguments(["--action", "cut-video", "--loop-fake", "1", "video.mp4"], out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => VideoLoopSettings.FromOptions(new Dictionary<string, string> { ["loop-speed"] = "1" }));
    }

    [Fact]
    public void SearchCancellation_IsObserved()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => VideoLoopDetector.FindSeeds([Solid(0, 40), Solid(1, 40)], 1, cts.Token));
    }

    [Fact]
    public void NativeTimestampParsing_DoesNotRoundLongHighFpsBoundaries()
    {
        var timing = Assert.Single(ParseTimings("config in time_base: 1/24000, frame_rate: 24000/1001\n" +
            "n: 0 pts: 960001001 pts_time:40000 duration:1001 duration_time:0.0417083"));
        Assert.Equal(960001001d / 24000, timing.Time, 8);
        Assert.Equal(1001d / 24000, timing.Duration, 10);
    }
}

public sealed class VideoLoopMediaTests
{
    private readonly ToolLocator _tools = new();
    private readonly FfmpegRunner _runner = new(new AppLogger());
    private readonly FfprobeRunner _probe = new(new AppLogger());

    private async Task<MediaProbeResult> Probe(string file)
        => (await _probe.TryProbeMediaAsync(_tools.ResolveFfprobePath(), file, CancellationToken.None)).Probe
            ?? throw new InvalidOperationException("Fixture probe failed.");

    private async Task Generate(string file, string filter, double duration, bool audio = false, string? codec = null)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", filter };
        if (audio) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000"]);
        args.AddRange(["-t", Number(duration), "-c:v", codec ?? "libx264", "-pix_fmt", "yuv420p"]);
        if (audio) args.AddRange(["-c:a", "aac"]);
        args.Add(file);
        var result = await _runner.RunAsync(_tools.ResolveFfmpegPath(), args, null, null, file, "Loop fixture", "CPU", CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
    }

    private static string Folder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"FrameShift boucle été {Guid.NewGuid():N}");
        Directory.CreateDirectory(path); return path;
    }

    [Fact]
    public async Task CyclicAnimation_TwoPassSearch_FindsKnownPeriodAwayFromFirstFrame()
    {
        var root = Folder();
        try
        {
            var source = Path.Combine(root, "cycle animé.mp4");
            await Generate(source, "nullsrc=s=160x96:r=15,geq=r='128+100*sin(2*PI*(X/W+N/30))':g='128+100*cos(2*PI*(Y/H+N/30))':b='64+40*sin(2*PI*N/30)'", 6);
            var result = await new VideoLoopDetector(_runner).AnalyzeAsync(_tools.ResolveFfmpegPath(), source, await Probe(source), 1,
                LoopSensitivity.Normal, null, CancellationToken.None);
            Assert.NotEmpty(result.Candidates);
            // The best seam can be a longer cycle after perceptual ranking.
            Assert.Contains(result.Candidates, c => c.DurationSeconds is > 1.99 and < 2.01 && c.StartSeconds > 0);
            Assert.Equal("Promising", result.Candidates[0].JunctionAssessment);
            Assert.True(result.Candidates[0].Quality >= result.Candidates.First(c => c.DurationSeconds is > 1.99 and < 2.01).Quality - 0.000001);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("mp4", null, true)]
    [InlineData("mkv", null, false)]
    [InlineData("mov", null, true)]
    [InlineData("webm", "libvpx-vp9", false)]
    public async Task NaturalExport_ThreeCycles_HasExactFramesGeometryAudioAndTransitions(string extension, string? codec, bool audio)
    {
        var root = Folder();
        try
        {
            var source = Path.Combine(root, $"portrait été.{extension}");
            await Generate(source, "nullsrc=s=96x160:r=10,geq=r='N*18':g='N*12':b='N*8'", 1.2, audio, codec);
            var info = await Probe(source);
            var output = Path.Combine(root, "natural.mp4");
            await new VideoLoopExporter(_runner, _probe).RenderAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), source, output,
                info, new(3, 8, 10), new(VideoLoopMode.Natural, 1, 3, audio), null, null, CancellationToken.None);
            var rendered = await Probe(output);
            Assert.Equal(96, rendered.DisplayVideoWidth); Assert.Equal(160, rendered.DisplayVideoHeight);
            Assert.Equal(audio, rendered.HasAudio);
            var frames = await new VideoLoopFrameReader(_runner, _tools.ResolveFfmpegPath(), output).ReadAsync(0, 5, null, 100, CancellationToken.None);
            Assert.Equal(18, frames.Count);
            Assert.InRange(rendered.Duration!.Value.TotalSeconds, 1.79, 1.83);
            for (var i = 0; i < 6; i++)
            {
                Assert.InRange(MeanDifference(frames[i].Pixels, frames[i + 6].Pixels), 0, 2);
                Assert.InRange(MeanDifference(frames[i].Pixels, frames[i + 12].Pixels), 0, 2);
            }
            Assert.True(frames.Zip(frames.Skip(1), (a, b) => b.Time > a.Time).All(x => x));
            // Both encoded seams follow the same last→first transition; no held/extra frame.
            Assert.InRange(Math.Abs(MeanDifference(frames[5].Pixels, frames[6].Pixels) - MeanDifference(frames[11].Pixels, frames[12].Pixels)), 0, 1);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0.75)]
    [InlineData(0.5)]
    public async Task PingPong_EndpointsAreNotDuplicated_AndSpeedPreservesAllFrames(double speed)
    {
        var root = Folder();
        try
        {
            var source = Path.Combine(root, "aller retour.mp4");
            await Generate(source, "nullsrc=s=160x96:r=10,geq=r='N*20':g='N*15':b='N*10'", 1.2, true);
            var output = Path.Combine(root, "pingpong.mp4");
            await new VideoLoopExporter(_runner, _probe).RenderAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), source, output,
                await Probe(source), new(2, 10, 10), new(VideoLoopMode.PingPong, speed, 3, false), null, null, CancellationToken.None);
            var frames = await new VideoLoopFrameReader(_runner, _tools.ResolveFfmpegPath(), output).ReadAsync(0, 20, null, 100, CancellationToken.None);
            var expected = Enumerable.Range(1, 9).Concat(Enumerable.Range(2, 7).Reverse()).ToArray();
            Assert.Equal(48, frames.Count);
            Assert.False((await Probe(output)).HasAudio);
            for (var i = 0; i < frames.Count; i++)
                Assert.InRange(frames[i].Pixels.Where((_, c) => c % 3 == 0).Average(v => (double)v), expected[i % 16] * 20 - 7, expected[i % 16] * 20 + 7);
            Assert.InRange((await Probe(output)).Duration!.Value.TotalSeconds, 4.8 / speed - 0.05, 4.8 / speed + 0.05);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task VfrNaturalExport_PreservesVariableFrameIntervalsAndExactEnd(int repeats)
    {
        var root = Folder();
        try
        {
            var source = Path.Combine(root, "variable.mkv");
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=10:d=3",
                "-vf", "select='if(lt(n,10),1,not(mod(n,2)))'", "-fps_mode", "passthrough", "-c:v", "ffv1", source };
            Assert.Equal(0, (await _runner.RunAsync(_tools.ResolveFfmpegPath(), args, null, null, source, "VFR fixture", "CPU", CancellationToken.None)).ExitCode);
            var info = await Probe(source);
            var output = Path.Combine(root, "vfr-loop.mp4");
            await new VideoLoopExporter(_runner, _probe).RenderAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), source, output,
                info, new(1, 16, info.VideoFrameRate!.Value), new(VideoLoopMode.Natural, 1, repeats, false), new(0, 2.2, 0), null, CancellationToken.None);
            var frames = await new VideoLoopFrameReader(_runner, _tools.ResolveFfmpegPath(), output).ReadAsync(0, 10, null, 100, CancellationToken.None);
            Assert.Equal(16 * repeats, frames.Count);
            Assert.InRange(frames[1].Time - frames[0].Time, 0.09, 0.11);
            Assert.InRange(frames[12].Time - frames[11].Time, 0.19, 0.21);
            if (repeats > 1) Assert.InRange(frames[16].Time - frames[15].Time, 0.19, 0.21);
            Assert.InRange((await Probe(output)).Duration!.Value.TotalSeconds, 2.2 * repeats - 0.003, 2.2 * repeats + 0.003);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CancellationAndUniqueExport_PreserveSourceAndCleanOwnedFiles()
    {
        var root = Folder();
        try
        {
            var source = Path.Combine(root, "statique été.mp4");
            await Generate(source, "color=c=blue:s=160x96:r=10", 3);
            var info = await Probe(source);
            var exporter = new VideoLoopExporter(_runner, _probe);
            var original = await File.ReadAllBytesAsync(source);
            var request = new ActionRequest(source, new AppLogger(), null);
            var first = await exporter.ExportAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), request, info,
                new(1, 10, 10), new(VideoLoopMode.Natural, 1, 1, false), null, CancellationToken.None);
            var second = await exporter.ExportAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), request, info,
                new(1, 10, 10), new(VideoLoopMode.Natural, 1, 1, false), null, CancellationToken.None);
            Assert.True(first.Success); Assert.True(second.Success); Assert.NotEqual(first.OutputPath, second.OutputPath);
            using var cts = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporter.RenderAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), source,
                Path.Combine(root, "canceled.mp4"), info, new(1, 30, 10), new(VideoLoopMode.Natural, 1, 3, false), null, new CancelOnProgress(cts), cts.Token));
            Assert.False(File.Exists(Path.Combine(root, "canceled.mp4")));
            Assert.Empty(Directory.GetDirectories(root, ".frameshift-*"));
            Assert.Equal(original, await File.ReadAllBytesAsync(source));
        }
        finally { Directory.Delete(root, true); }
    }

    private static double MeanDifference(byte[] a, byte[] b) => a.Zip(b, (x, y) => (double)Math.Abs(x - y)).Average();

    [Fact]
    public async Task RotatingObject_FindsTwoSecondPeriod()
    {
        var root = Folder();
        try
        {
            var file = Path.Combine(root, "rotation.mp4");
            await Generate(file, "nullsrc=s=160x96:r=15,geq=r='if(lt(hypot(X-80,Y-48),35),128+100*cos(atan2(Y-48,X-80)-2*PI*N/30),30)':g='80':b='60'", 6);
            var result = await new VideoLoopDetector(_runner).AnalyzeAsync(_tools.ResolveFfmpegPath(), file, await Probe(file), 1,
                LoopSensitivity.Normal, null, CancellationToken.None);
            Assert.NotEmpty(result.Candidates);
            Assert.Contains(result.Candidates, c => c.DurationSeconds is > 1.99 and < 2.01);
            Assert.Equal("Promising", result.Candidates[0].JunctionAssessment);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CorruptedAudioOnlyAndShortVideos_AreHandledCleanly()
    {
        var root = Folder();
        try
        {
            var corrupt = Path.Combine(root, "corrompue.mp4");
            await File.WriteAllTextAsync(corrupt, "not a media file");
            var action = new CutVideoAction(_runner, _probe, _tools);
            Assert.False((await action.ExecuteAsync(new(corrupt, new AppLogger(), null, new CutVideoSettings(1, 2, 10).ToOptions()), CancellationToken.None)).Success);
            var audioOnly = Path.Combine(root, "audio seul.mp4");
            Assert.Equal(0, (await _runner.RunAsync(_tools.ResolveFfmpegPath(), ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-c:a", "aac", audioOnly], null, null, audioOnly, "Audio-only fixture", "CPU", CancellationToken.None)).ExitCode);
            Assert.False((await action.ExecuteAsync(new(audioOnly, new AppLogger(), null), CancellationToken.None)).Success);
            var shortVideo = Path.Combine(root, "courte.mp4");
            await Generate(shortVideo, "color=c=blue:s=160x96:r=10", 0.4);
            var result = await new VideoLoopDetector(_runner).AnalyzeAsync(_tools.ResolveFfmpegPath(), shortVideo, await Probe(shortVideo), 1,
                LoopSensitivity.Normal, null, CancellationToken.None);
            Assert.Empty(result.Candidates);
            Assert.Empty(Directory.GetDirectories(root, ".frameshift-*"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("10", 13.2)]
    [InlineData("24000/1001", 5.5055)]
    public async Task MultiChunkPingPong_HasExpectedFrameCount(string rate, double duration)
    {
        var root = Folder();
        try
        {
            var source = Path.Combine(root, "multi chunks.mp4");
            await Generate(source, $"testsrc2=s=160x96:r={rate}", duration);
            var info = await Probe(source);
            var output = Path.Combine(root, "reverse.mp4");
            await new VideoLoopExporter(_runner, _probe).RenderAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), source, output,
                info, new(1, 132, info.VideoFrameRate!.Value), new(VideoLoopMode.PingPong, 1, 1, false), null, null, CancellationToken.None);
            var frames = await new VideoLoopFrameReader(_runner, _tools.ResolveFfmpegPath(), output).ReadAsync(0, 30, null, 300, CancellationToken.None);
            Assert.Equal(262, frames.Count);
            for (var i = 1; i < 131; i++) Assert.InRange(MeanDifference(frames[i].Pixels, frames[262 - i].Pixels), 0, 6);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RotationMetadata_IsAppliedOnceAndKeptInDisplayGeometry()
    {
        var root = Folder();
        try
        {
            var source = Path.Combine(root, "landscape.mp4");
            await Generate(source, "testsrc2=s=160x96:r=10", 2);
            var rotated = Path.Combine(root, "rotated.mp4");
            Assert.Equal(0, (await _runner.RunAsync(_tools.ResolveFfmpegPath(), ["-hide_banner", "-loglevel", "error", "-y", "-display_rotation:v:0", "90", "-i", source,
                "-c", "copy", rotated], null, null, source, "Rotation fixture", "CPU", CancellationToken.None)).ExitCode);
            var info = await Probe(rotated); Assert.True(info.HasDisplayRotation);
            var output = Path.Combine(root, "orientation.mp4");
            await new VideoLoopExporter(_runner, _probe).RenderAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), rotated, output,
                info, new(1, 10, 10), new(VideoLoopMode.Natural, 1, 1, false), null, null, CancellationToken.None);
            var result = await Probe(output);
            Assert.Equal(96, result.DisplayVideoWidth); Assert.Equal(160, result.DisplayVideoHeight);
            Assert.Equal(0, result.RotationDegrees);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("video.mp4")]
    [InlineData("image exemples/ComfyUI_00723_.mp4")]
    public async Task LocalRealFootage_AnalysisPreviewAndExport(string relativeSource)
    {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var source = Path.Combine(repo, "tests/input", relativeSource);
        // Local private footage is not a prerequisite of the portable suite. Synthetic media
        // above provides mandatory coverage; this optional qualification records its availability.
        if (!File.Exists(source)) return;
        var evidence = Path.Combine(repo, "scratch/loop-validation/real", Path.GetFileNameWithoutExtension(source));
        Directory.CreateDirectory(evidence);
        var info = await Probe(source);
        var result = await new VideoLoopDetector(_runner).AnalyzeAsync(_tools.ResolveFfmpegPath(), source, info, 1,
            LoopSensitivity.Normal, null, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(evidence, "analysis.json"), System.Text.Json.JsonSerializer.Serialize(result));
        var bounds = result.Candidates.FirstOrDefault() ?? new VideoLoopCandidate(0, Math.Min(2, info.Duration!.Value.TotalSeconds), 0);
        var settings = new VideoLoopSettings(result.Candidates.Count > 0 ? VideoLoopMode.Natural : VideoLoopMode.PingPong, 1, 3, false);
        var output = OutputPathHelper.CreateUniqueFilePath(Path.Combine(evidence, "preview-three-cycles.mp4"));
        await new VideoLoopExporter(_runner, _probe).RenderAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), source, output,
            info, new(1, Math.Max(3, (int)(bounds.EndSeconds * info.VideoFrameRate!.Value)), info.VideoFrameRate!.Value), settings, bounds, null, CancellationToken.None);
        var frames = await new VideoLoopFrameReader(_runner, _tools.ResolveFfmpegPath(), output).ReadAsync(0, 30, null, 1024, CancellationToken.None);
        Assert.True(frames.Count > 3); Assert.True((await Probe(output)).HasVideo);
        var cycleFrames = frames.Count / 3;
        using var strip = new System.Drawing.Bitmap(640, 280);
        using var graphics = System.Drawing.Graphics.FromImage(strip);
        graphics.Clear(System.Drawing.Color.White);
        for (var junction = 1; junction <= 2; junction++)
        for (var col = 0; col < 4; col++)
        {
            var index = junction * cycleFrames + col - 2;
            var frame = frames[index];
            using var image = new System.Drawing.Bitmap(Width, Height);
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var pixel = (y * Width + x) * 3;
                image.SetPixel(x, y, System.Drawing.Color.FromArgb(frame.Pixels[pixel], frame.Pixels[pixel + 1], frame.Pixels[pixel + 2]));
            }
            graphics.DrawImage(image, col * 160, (junction - 1) * 140, 160, 120);
            graphics.DrawString($"Frame {index}, {frame.Time:0.###} s", System.Drawing.SystemFonts.DefaultFont, System.Drawing.Brushes.Black, col * 160, (junction - 1) * 140 + 120);
        }
        strip.Save(Path.Combine(evidence, "junctions.png"));
    }

    [Fact]
    public async Task DelayedAudio_KeepsInitialSilenceAndTimestampOffset()
    {
        var root = Folder();
        try
        {
            var video = Path.Combine(root, "video.mp4");
            await Generate(video, "testsrc2=s=160x96:r=10", 3);
            var delayed = Path.Combine(root, "delayed audio.mp4");
            Assert.Equal(0, (await _runner.RunAsync(_tools.ResolveFfmpegPath(), ["-hide_banner", "-loglevel", "error", "-y", "-i", video,
                "-itsoffset", "0.5", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy", "-c:a", "aac", delayed],
                null, null, video, "Delayed audio fixture", "CPU", CancellationToken.None)).ExitCode);
            var output = Path.Combine(root, "audio-loop.mp4");
            await new VideoLoopExporter(_runner, _probe).RenderAsync(_tools.ResolveFfmpegPath(), _tools.ResolveFfprobePath(), delayed, output,
                await Probe(delayed), new(1, 10, 10), new(VideoLoopMode.Natural, 1, 3, true), null, null, CancellationToken.None);
            var pcm = Path.Combine(root, "audio.pcm");
            Assert.Equal(0, (await _runner.RunAsync(_tools.ResolveFfmpegPath(), ["-hide_banner", "-loglevel", "error", "-y", "-i", output,
                "-map", "0:a:0", "-ar", "10000", "-ac", "1", "-f", "s16le", pcm], null, null, output, "Audio check", "CPU", CancellationToken.None)).ExitCode);
            var data = await File.ReadAllBytesAsync(pcm);
            double Energy(double from, double to) => Enumerable.Range((int)(from * 10000), (int)((to - from) * 10000))
                .Average(i => (double)Math.Abs((int)BitConverter.ToInt16(data, i * 2)));
            Assert.True(Energy(0, 0.3) < 10);
            Assert.True(Energy(0.6, 0.9) > 100);
            Assert.True(Energy(1, 1.3) < 10);
            Assert.InRange((await Probe(output)).Duration!.Value.TotalSeconds, 2.99, 3.04);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class CancelOnProgress(CancellationTokenSource cts) : IProgressReporter
    {
        public bool IsCancellationRequested => cts.IsCancellationRequested;
        public bool IsQueueItemRemovalRequested(string path) => false;
        public bool IsQueueItemCancellationRequested(string path) => false;
        public void ReportQueue(IReadOnlyList<string> items) { }
        public void ReportQueueItem(string path, string state, string? message = null) { }
        public void ReportState(string state, string? message = null) { }
        public void ReportProgress(int value, string? file, string? action, string? message, string? etaText = null) => cts.Cancel();
    }
}
