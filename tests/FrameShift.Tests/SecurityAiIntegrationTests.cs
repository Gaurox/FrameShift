using FrameShift.Core.AI.RemoveBackground;
using FrameShift.Core.AI.Upscale;
using FrameShift.Core.AI.VideoInterpolation;
using FrameShift.Core.AI.VideoUpscale;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Xunit.Abstractions;

namespace FrameShift.Tests;

// Opt in with FRAMESHIFT_SECURITY_AI_MODELS pointing at existing models. Never download assets.
public sealed class SecurityAiIntegrationTests(ITestOutputHelper log) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"frameshift IA sécurité {Guid.NewGuid():N}");
    private static string ModelsRoot => Environment.GetEnvironmentVariable("FRAMESHIFT_SECURITY_AI_MODELS")!;
    private string CreateInput(string filename)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, filename);
        using var image = new Image<Rgba32>(32, 32, new Rgba32(80, 120, 160, 200));
        image.Save(path);
        return path;
    }

    [LocalModelTheory("upscale-video-onnx/realesr_general_x4v3.onnx")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealUpscalePreservesCollisionsAndHandlesEarlyAndLateCancellation(bool forceCpu)
    {
        var input = CreateInput("été.png");
        var desired = Path.Combine(_root, "été_upscaled_2x.png");
        File.WriteAllText(desired, "foreign");
        var model = UpscaleModelCatalog.GetById("realesr-general-x4v3")!;
        model = model with { Folder = Path.Combine(ModelsRoot, model.Folder), ForceCpu = forceCpu, LegacyFolder = null };
        using var engine = new UpscaleEngine(model);
        using var late = new CancellationTokenSource();
        var saved = await engine.UpscaleAsync(input, new UpscaleRequest(Factor: 2),
            new InlineProgress<UpscaleProgress>(p =>
            {
                if (p.Percent == 100) { late.Cancel(); throw new InvalidOperationException("Late UI failure"); }
            }), late.Token);
        log.WriteLine($"Upscale provider: {engine.Provider}; forceCpu={forceCpu}");
        if (forceCpu) Assert.Equal("CPU", engine.Provider);
        Assert.Equal(Path.Combine(_root, "été_upscaled_2x_001.png"), saved);
        using (var decoded = SafeImageReader.Load<Rgba32>(saved)) Assert.Equal(64, decoded.Width);
        Assert.Equal("foreign", File.ReadAllText(desired));

        using var early = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.UpscaleAsync(input,
            new UpscaleRequest(Factor: 2), new InlineProgress<UpscaleProgress>(p =>
            {
                if (p.Percent == 95) early.Cancel();
            }), early.Token));
        Assert.False(File.Exists(Path.Combine(_root, "été_upscaled_2x_002.png")));
        Assert.True(File.Exists(saved));
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
    }

    [LocalModelTheory("upscale-video-onnx/realesr_general_x4v3.onnx")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealVideoUpscaleReadsAndWritesBmpFrames(bool forceCpu)
    {
        var input = CreateInput("000001.bmp");
        var output = Path.Combine(_root, "output");
        var model = UpscaleModelCatalog.GetById("realesr-general-x4v3")!;
        model = model with { Folder = Path.Combine(ModelsRoot, model.Folder), ForceCpu = forceCpu, LegacyFolder = null };
        using var engine = new VideoUpscaleEngine(model);
        var result = await engine.ProcessAsync(_root, output, new UpscaleRequest(Factor: 2), null, CancellationToken.None);
        log.WriteLine($"Video upscale provider: {result.Provider}; forceCpu={forceCpu}");
        if (forceCpu) Assert.Equal("CPU", result.Provider);
        Assert.Equal(1, result.ProcessedFrameCount);
        Assert.False(File.Exists(input));
        using var decoded = SafeImageReader.Load<Rgba32>(Path.Combine(output, "000001.bmp"));
        Assert.Equal(64, decoded.Width);
        Assert.Equal(64, decoded.Height);
    }

    [LocalModelFact("rife/rife_v426_x2.onnx")]
    public void RealRifeReadsAndWritesBmpFrames()
    {
        CreateInput("000001.bmp");
        CreateInput("000002.bmp");
        var output = Path.Combine(_root, "output");
        using var engine = new RifeFrameInterpolationEngine(Path.Combine(ModelsRoot, "rife", "rife_v426_x2.onnx"));
        Assert.Equal(3, engine.InterpolateAsync(_root, output, null, CancellationToken.None));
        log.WriteLine($"RIFE provider: {engine.Provider}");
        foreach (var frame in Directory.GetFiles(output, "*.bmp"))
        {
            using var decoded = SafeImageReader.Load<Rgb24>(frame);
            Assert.Equal(32, decoded.Width);
            Assert.Equal(32, decoded.Height);
        }
    }

    [LocalModelTheory("birefnet_lite-onnx/model_fp16.onnx")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealBackgroundRemovalPreservesExistingPng(bool forceCpu)
    {
        var input = CreateInput("été.png");
        var desired = Path.Combine(_root, "été_nobg.png");
        File.WriteAllText(desired, "foreign");
        var model = BackgroundRemovalModelCatalog.GetById("fast")!;
        model = model with { Folder = Path.Combine(ModelsRoot, model.Folder), ForceCpu = forceCpu };
        using var engine = new BackgroundRemovalEngine(model);
        var saved = await engine.RemoveBackgroundAsync(input, new InlineProgress<InferenceProgress>(_ => { }), CancellationToken.None);
        log.WriteLine($"Background removal provider: {engine.Provider}; forceCpu={forceCpu}");
        if (forceCpu) Assert.Equal("CPU", engine.Provider);
        Assert.Equal(Path.Combine(_root, "été_nobg_001.png"), saved);
        using var decoded = SafeImageReader.Load<Rgba32>(saved);
        Assert.Equal(32, decoded.Width);
        Assert.Equal(32, decoded.Height);
        Assert.Equal("foreign", File.ReadAllText(desired));
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
    }

    [LocalModelTheory("upscale-video-onnx/realesr_general_x4v3.onnx")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealRawVideoUpscalePublishesWithoutReplacement(bool forceCpu)
    {
        var (input, runner, tools, probe) = await CreateVideo();
        var desired = Path.Combine(_root, "été_upscaled.mp4");
        File.WriteAllText(desired, "foreign");
        var model = UpscaleModelCatalog.GetById("realesr-general-x4v3")!;
        model = model with { Folder = Path.Combine(ModelsRoot, model.Folder), ForceCpu = forceCpu, LegacyFolder = null };
        using (var output = OutputOperation.ForFile(desired))
        {
            var pipeline = new UpscaleRawVideoPipeline(runner, new AppLogger());
            var result = await pipeline.RunAsync(tools.ResolveFfmpegPath(), input, output.WorkingPath,
                model, probe, new UpscaleRequest(Factor: 2), "libx264", ["-preset", "ultrafast"], false, null, CancellationToken.None);
            log.WriteLine($"Rawvideo upscale provider: {result.Provider}; forceCpu={forceCpu}");
            if (forceCpu) Assert.Equal("CPU", result.Provider);
            Assert.Equal(2, result.SourceFrameCount);
            Assert.Equal(2, result.OutputFrameCount);
            var saved = output.Publish(CancellationToken.None);
            var decoded = await new FfprobeRunner(new AppLogger()).TryProbeMediaAsync(tools.ResolveFfprobePath(), saved, CancellationToken.None);
            Assert.NotNull(decoded.Probe);
            Assert.Equal(64, decoded.Probe.DisplayVideoWidth);
        }
        Assert.Equal("foreign", File.ReadAllText(desired));
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
    }

    [LocalModelFact("rife/rife_v426_x2.onnx")]
    public async Task RealRawVideoRifePublishesWithoutReplacement()
    {
        var (input, runner, tools, probe) = await CreateVideo();
        var desired = Path.Combine(_root, "été_rife.mp4");
        File.WriteAllText(desired, "foreign");
        using (var output = OutputOperation.ForFile(desired))
        {
            var pipeline = new RifeRawVideoPipeline(runner, new AppLogger());
            var result = await pipeline.RunAsync(tools.ResolveFfmpegPath(), tools.ResolveFfprobePath(), input,
                output.WorkingPath, Path.Combine(ModelsRoot, "rife", "rife_v426_x2.onnx"), probe,
                new RifeInterpolateVideoSettings("rife-v4.26-x2", 2, 1), false, 44100, "x2", "libx264",
                ["-preset", "ultrafast"], null, CancellationToken.None);
            log.WriteLine($"Rawvideo RIFE provider: {result.Provider}");
            Assert.Equal(2, result.SourceFrameCount);
            Assert.Equal(3, result.OutputFrameCount);
            var saved = output.Publish(CancellationToken.None);
            Assert.NotNull((await new FfprobeRunner(new AppLogger()).TryProbeMediaAsync(
                tools.ResolveFfprobePath(), saved, CancellationToken.None)).Probe);
        }
        Assert.Equal("foreign", File.ReadAllText(desired));
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
    }

    private async Task<(string Input, FfmpegRunner Runner, ToolLocator Tools, MediaProbeResult Probe)> CreateVideo()
    {
        Directory.CreateDirectory(_root);
        var input = Path.Combine(_root, "été.mp4");
        var runner = new FfmpegRunner(new AppLogger());
        var tools = new ToolLocator();
        var result = await runner.RunCaptureAsync(tools.ResolveFfmpegPath(),
            ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=blue:s=32x32:r=2:d=1", "-c:v", "libx264", input], CancellationToken.None);
        Assert.True(result.Success, result.StandardError);
        var probe = await new FfprobeRunner(new AppLogger()).TryProbeMediaAsync(tools.ResolveFfprobePath(), input, CancellationToken.None);
        Assert.NotNull(probe.Probe);
        return (input, runner, tools, probe.Probe);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

public sealed class LocalModelTheoryAttribute : TheoryAttribute
{
    public LocalModelTheoryAttribute(string relativePath) => Skip = LocalModelFactAttribute.GetSkipReason(relativePath);
}

public sealed class LocalModelFactAttribute : FactAttribute
{
    public LocalModelFactAttribute(string relativePath) => Skip = GetSkipReason(relativePath);
    internal static string? GetSkipReason(string relativePath)
    {
        var root = Environment.GetEnvironmentVariable("FRAMESHIFT_SECURITY_AI_MODELS");
        return root is not null && File.Exists(Path.Combine(root, relativePath))
            ? null : $"Set FRAMESHIFT_SECURITY_AI_MODELS to existing local models ({relativePath}).";
    }
}
