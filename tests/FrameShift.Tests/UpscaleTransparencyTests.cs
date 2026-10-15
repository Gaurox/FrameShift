using FrameShift.Core.AI.Upscale;
using FrameShift.Core.Helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace FrameShift.Tests;

public sealed class UpscaleTransparencyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"frameshift upscale transparence été {Guid.NewGuid():N}");

    // A self-contained ONNX Resize graph: dynamic NCHW RGB, nearest x2, opset 13 / IR 8.
    // Tests exercise the real processor and PNG publication without downloading model weights.
    private const string Nearest2xModel =
        "CAg6iAIKgAEKBWlucHV0CgAKBnNjYWxlcxIGb3V0cHV0IgZSZXNpemUqLwoeY29vcmRpbmF0ZV90cmFuc2Zvcm1hdGlvbl9tb2RlIgphc3ltbWV0cmljoAEDKhIKBG1vZGUiB25lYXJlc3SgAQMqGAoMbmVhcmVzdF9tb2RlIgVmbG9vcqABAxIaRnJhbWVTaGlmdCB0ZXN0IG5lYXJlc3QgeDIqHggEEAEiEAAAgD8AAIA/AAAAQAAAAEBCBnNjYWxlc1ohCgVpbnB1dBIYChYIARISCgIIAQoCCAMKAxIBaAoDEgF3YiQKBm91dHB1dBIaChgIARIUCgIIAQoCCAMKBBICb2gKBBICb3dCBAoAEA0=";

    [Theory]
    [InlineData(0, 2)]
    [InlineData(42, 2)]
    [InlineData(255, 2)]
    [InlineData(0, 1.5)]
    [InlineData(42, 1.5)]
    [InlineData(255, 1.5)]
    public async Task UniformAlphaSurvivesInferenceFinalResizeAndPngPublication(byte alpha, double factor)
    {
        var color = new Rgba32(32, 64, 96, alpha);
        using var source = new Image<Rgba32>(4, 4, color);
        Directory.CreateDirectory(_root);
        var input = Path.Combine(_root, "source été.png");
        source.SaveAsPng(input);
        var target = UpscaleFrameProcessor.ResolveFinalSize(4, 4, new UpscaleRequest(Factor: factor), 2);
        var desired = Path.Combine(_root, "source été" + target.Suffix + ".png");
        File.WriteAllText(desired, "foreign output");
        using var engine = new UpscaleEngine(CreateModel());

        var saved = await engine.UpscaleAsync(input, new UpscaleRequest(Factor: factor),
            new InlineProgress(_ => { }), CancellationToken.None);

        Assert.Equal(Path.Combine(_root, "source été" + target.Suffix + "_001.png"), saved);
        Assert.Equal("foreign output", File.ReadAllText(desired));
        using var decoded = SafeImageReader.Load<Rgba32>(saved);
        Assert.Equal(target.Width, decoded.Width);
        Assert.Equal(target.Height, decoded.Height);
        for (var y = 0; y < decoded.Height; y++)
        for (var x = 0; x < decoded.Width; x++)
            Assert.Equal(color, decoded[x, y]);
        using var original = SafeImageReader.Load<Rgba32>(input);
        Assert.Equal(color, original[0, 0]);
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void AlphaEdgesRemainAlignedAcrossTilesAndRgbIsUnchanged(double factor)
    {
        using var source = new Image<Rgba32>(520, 8, new Rgba32(32, 64, 96));
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            source[x, y] = new Rgba32(32, 64, 96, x < 160 ? (byte)0 : x < 360 ? (byte)128 : (byte)255);
        using var processor = new UpscaleFrameProcessor(CreateModel());

        using var output = processor.Upscale(source, new UpscaleRequest(Factor: factor), null, CancellationToken.None);

        Assert.Equal(new Rgba32(32, 64, 96, 0), output[0, 0]);
        Assert.Equal(new Rgba32(32, 64, 96, 128), output[output.Width / 2, output.Height / 2]);
        Assert.Equal(new Rgba32(32, 64, 96, 255), output[output.Width - 1, output.Height - 1]);
        for (var y = 0; y < output.Height; y++)
        for (var x = 0; x < output.Width; x++)
        {
            var pixel = output[x, y];
            Assert.Equal((byte)32, pixel.R);
            Assert.Equal((byte)64, pixel.G);
            Assert.Equal((byte)96, pixel.B);
            Assert.Equal(output[x, 0].A, pixel.A);
            if (factor == 1) Assert.Equal(source[x, y].A, pixel.A);
        }
    }

    [Fact]
    public void ExplicitTargetDimensionsPreserveHorizontalAndVerticalAlphaGeometry()
    {
        using var source = new Image<Rgba32>(8, 8, new Rgba32(32, 64, 96));
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            source[x, y] = new Rgba32(32, 64, 96, y < 4
                ? (x < 4 ? (byte)0 : (byte)64)
                : (x < 4 ? (byte)128 : (byte)255));
        using var processor = new UpscaleFrameProcessor(CreateModel());

        using var output = processor.Upscale(source,
            new UpscaleRequest(TargetWidth: 13, TargetHeight: 11), null, CancellationToken.None);

        Assert.Equal(13, output.Width);
        Assert.Equal(11, output.Height);
        Assert.Equal(new Rgba32(32, 64, 96, 0), output[0, 0]);
        Assert.Equal(new Rgba32(32, 64, 96, 64), output[12, 0]);
        Assert.Equal(new Rgba32(32, 64, 96, 128), output[0, 10]);
        Assert.Equal(new Rgba32(32, 64, 96, 255), output[12, 10]);
    }

    [Fact]
    public async Task CancellationAfterInferenceDoesNotPublishTransparentOutput()
    {
        Directory.CreateDirectory(_root);
        var input = Path.Combine(_root, "cancel été.png");
        using (var image = new Image<Rgba32>(4, 4, new Rgba32(32, 64, 96, 42)))
            image.SaveAsPng(input);
        using var engine = new UpscaleEngine(CreateModel());
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.UpscaleAsync(input,
            new UpscaleRequest(Factor: 2), new InlineProgress(p =>
            {
                if (p.Percent == 90) cancellation.Cancel();
            }), cancellation.Token));

        Assert.Equal(new[] { input }, Directory.GetFiles(_root, "*.png"));
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
    }

    [Fact]
    public void RgbVideoFramesRemainOpaque()
    {
        using var source = new Image<Rgb24>(4, 4, new Rgb24(32, 64, 96));
        using var processor = new UpscaleFrameProcessor(CreateModel());

        using var output = processor.Upscale(source, new UpscaleRequest(Factor: 2), null, CancellationToken.None);

        for (var y = 0; y < output.Height; y++)
        for (var x = 0; x < output.Width; x++)
            Assert.Equal(new Rgba32(32, 64, 96, 255), output[x, y]);
    }

    private UpscaleModelDefinition CreateModel()
    {
        Directory.CreateDirectory(_root);
        var bytes = Convert.FromBase64String(Nearest2xModel);
        File.WriteAllBytes(Path.Combine(_root, "nearest2x.onnx"), bytes);
        return new UpscaleModelDefinition("test-nearest2x", "Test nearest x2", _root, "nearest2x.onnx",
            "", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), bytes.Length,
            "Test fixture", ScaleFactor: 2, ForceCpu: true);
    }

    public void Dispose()
    {
        // Windows may retain a transient sharing lock after the native session
        // has been disposed (e.g. a file scanner). A persistent lock still fails.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 19) { Thread.Sleep(100); }
        }
    }

    private sealed class InlineProgress(Action<UpscaleProgress> report) : IProgress<UpscaleProgress>
    {
        public void Report(UpscaleProgress value) => report(value);
    }
}
