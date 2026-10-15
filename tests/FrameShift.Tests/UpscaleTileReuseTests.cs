using System.Reflection;
using FrameShift.Core.AI.Upscale;
using Microsoft.ML.OnnxRuntime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace FrameShift.Tests;

public sealed class UpscaleTileReuseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"FrameShift tuiles été {Guid.NewGuid():N}");

    [Theory]
    [InlineData(256)] [InlineData(128)]
    public void SuccessfulSizeIsReusedAcrossFramesWithoutChangingPixels(int limit)
    {
        var attempts = new List<int>(); var loads = 0;
        using var processor = new UpscaleFrameProcessor(Model(), (size, _) =>
        {
            attempts.Add(size);
            if (size > limit) throw new OutOfMemoryException("Injected tile resource limit");
        }, (path, _) => { loads++; return CpuSession(path); });
        using var source = Source(520, 8);
        for (var frame = 0; frame < 4; frame++)
        {
            using var output = processor.Upscale(source, new(Factor: 2), null, CancellationToken.None);
            AssertNearestPixels(source, output);
        }
        var failedSizes = limit == 256 ? new[] { 512 } : new[] { 512, 256 };
        Assert.Equal(failedSizes.Concat(Enumerable.Repeat(limit, 4)), attempts);
        Assert.Equal(failedSizes.Length + 1, loads);
    }

    [Fact]
    public void ChangedGeometryStartsAtTheLargestSizeAndANewProcessorDoesNotShareTheChoice()
    {
        var attempts = new List<int>();
        void Limit(int size, string _) { attempts.Add(size); if (size == 512) throw new OutOfMemoryException(); }
        using var processor = new UpscaleFrameProcessor(Model(), Limit, (path, _) => CpuSession(path));
        using var first = Source(520, 8);
        using var second = Source(521, 8);
        using (processor.Upscale(first, new(), null, CancellationToken.None)) { }
        using (processor.Upscale(second, new(), null, CancellationToken.None)) { }
        using (processor.Upscale(first, new(), null, CancellationToken.None)) { }
        using var other = new UpscaleFrameProcessor(Model(), Limit, (path, _) => CpuSession(path));
        using (other.Upscale(first, new(), null, CancellationToken.None)) { }
        Assert.Equal(new[] { 512, 256, 512, 256, 512, 256, 512, 256 }, attempts);
    }

    [Fact]
    public void ProviderFallbackResetsTileChoiceAndThenReusesTheCpuChoice()
    {
        var attempts = new List<(string, int)>();
        var failProvider = false;
        // CPU sessions with injected provider labels exercise the transition
        // deterministically; this does not certify a real DirectML device.
        using var processor = new UpscaleFrameProcessor(Model(forceCpu: false), (size, provider) =>
        {
            attempts.Add((provider, size));
            if (provider == "DirectML" && failProvider) throw NativeError("Fail", "DirectML execution failed");
            if (provider == "DirectML" && size == 512) throw new OutOfMemoryException();
        }, (path, forceCpu) =>
        {
            var cpu = CpuSession(path);
            return (cpu.Session, forceCpu ? "CPU" : "DirectML");
        });
        using var source = Source(520, 8);
        using (processor.Upscale(source, new(), null, CancellationToken.None)) { }
        failProvider = true;
        using (var output = processor.Upscale(source, new(), null, CancellationToken.None)) AssertNearestPixels(source, output);
        using (processor.Upscale(source, new(), null, CancellationToken.None)) { }
        Assert.Equal(new[] { ("DirectML", 512), ("DirectML", 256), ("DirectML", 256), ("CPU", 512), ("CPU", 512) }, attempts);
    }

    [Fact]
    public void ProviderChangeDuringSessionRecreationAlsoResetsTheChoice()
    {
        var loads = 0; var attempts = new List<(string, int)>();
        using var processor = new UpscaleFrameProcessor(Model(forceCpu: false), (size, provider) =>
        {
            attempts.Add((provider, size));
            if (provider == "DirectML") throw new OutOfMemoryException();
        }, (path, _) =>
        {
            loads++; var cpu = CpuSession(path);
            return (cpu.Session, loads == 1 ? "DirectML" : "CPU");
        });
        using var source = Source(520, 8);
        using (processor.Upscale(source, new(), null, CancellationToken.None)) { }
        using (processor.Upscale(source, new(), null, CancellationToken.None)) { }
        Assert.Equal(new[] { ("DirectML", 512), ("CPU", 512), ("CPU", 512) }, attempts);
    }

    [Fact]
    public void RgbVideoPathReusesTheSameLearnedSizeAndStaysOpaque()
    {
        var attempts = new List<int>();
        using var processor = new UpscaleFrameProcessor(Model(), (size, _) =>
        { attempts.Add(size); if (size == 512) throw new OutOfMemoryException(); }, (path, _) => CpuSession(path));
        using var source = Source(520, 8);
        using (processor.Upscale(source, new(), null, CancellationToken.None)) { }
        using var videoFrame = source.CloneAs<Rgb24>();
        using var output = processor.Upscale(videoFrame, new(), null, CancellationToken.None);
        Assert.Equal(new[] { 512, 256, 256 }, attempts);
        for (var y = 0; y < output.Height; y++) for (var x = 0; x < output.Width; x++)
        {
            var pixel = videoFrame[x / 2, y / 2];
            Assert.Equal(new Rgba32(pixel.R, pixel.G, pixel.B, 255), output[x, y]);
        }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void InvalidModelInputsAreNotRetriedAtSmallerSizesOrOnAnotherProvider(bool forceCpu)
    {
        var attempts = new List<int>(); var loads = 0;
        using var processor = new UpscaleFrameProcessor(Model(forceCpu) with { InputTensorName = "absent_input" },
            (size, _) => attempts.Add(size), (path, cpu) =>
            {
                loads++; var session = CpuSession(path);
                return (session.Session, cpu ? "CPU" : "DirectML");
            });
        using var source = Source(520, 8);
        Assert.Throws<OnnxRuntimeException>(() => processor.Upscale(source, new(), null, CancellationToken.None));
        Assert.Equal(new[] { 512 }, attempts); Assert.Equal(1, loads);
    }

    [Fact]
    public void ModelInitializationFailureIsNotRetriedAsATileFailure()
    {
        var loads = 0; var runs = 0;
        using var processor = new UpscaleFrameProcessor(Model(), (_, _) => runs++, (_, _) =>
        { loads++; throw new InvalidDataException("Invalid model fixture"); });
        using var source = Source(8, 8);
        Assert.Throws<InvalidDataException>(() => processor.Upscale(source, new(), null, CancellationToken.None));
        Assert.Equal(1, loads); Assert.Equal(0, runs);
    }

    [Fact]
    public void CanceledRetryDoesNotRememberAnUnsuccessfulSizeAndTheProcessorCanRunAgain()
    {
        var attempts = new List<int>(); var cancel = true;
        using var cancellation = new CancellationTokenSource();
        using var processor = new UpscaleFrameProcessor(Model(), (size, _) =>
        {
            attempts.Add(size);
            if (size == 512) throw new OutOfMemoryException();
            if (cancel) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
        }, (path, _) => CpuSession(path));
        using var source = Source(520, 8);
        Assert.ThrowsAny<OperationCanceledException>(() => processor.Upscale(source, new(), null, cancellation.Token));
        cancel = false;
        using (processor.Upscale(source, new(), null, CancellationToken.None)) { }
        using (processor.Upscale(source, new(), null, CancellationToken.None)) { }
        Assert.Equal(new[] { 512, 256, 512, 256, 256 }, attempts);
    }

    [Fact]
    public void ExhaustedResourceRetriesAreBoundedAndDoNotLearnAFailedSize()
    {
        var attempts = new List<int>();
        using var processor = new UpscaleFrameProcessor(Model(), (size, _) => { attempts.Add(size); throw new OutOfMemoryException(); },
            (path, _) => CpuSession(path));
        using var source = Source(8, 8);
        for (var i = 0; i < 2; i++)
            Assert.Throws<OutOfMemoryException>(() => processor.Upscale(source, new(), null, CancellationToken.None));
        Assert.Equal(new[] { 512, 256, 128, 512, 256, 128 }, attempts);
    }

    [Theory]
    [InlineData("Fail", "BFCArena: Failed to allocate memory for requested buffer", true)]
    [InlineData("Fail", "std::bad_alloc", true)]
    [InlineData("Fail", "E_OUTOFMEMORY (0x8007000e)", true)]
    [InlineData("InvalidArgument", "Failed to allocate memory for invalid model input", false)]
    [InlineData("InvalidGraph", "Invalid graph", false)]
    [InlineData("NoSuchFile", "Failed to allocate memory while reading a missing model file", false)]
    [InlineData("Fail", "Kernel not implemented for this model", false)]
    public void NativeResourceErrorsAreDistinguishedFromPermanentModelErrors(string code, string message, bool expected)
        => Assert.Equal(expected, UpscaleFrameProcessor.IsTileResourceFailure(NativeError(code, message)));

    private UpscaleModelDefinition Model(bool forceCpu = true)
    {
        Directory.CreateDirectory(_root);
        var bytes = Convert.FromBase64String(UpscaleTransparencyTests.Nearest2xModel);
        var path = Path.Combine(_root, "nearest.onnx");
        if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
        return new("test", "Test", _root, "nearest.onnx", "", "", bytes.Length, "test", ScaleFactor: 2, ForceCpu: forceCpu);
    }

    private static (InferenceSession Session, string Provider) CpuSession(string path)
    {
        using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
        return (new InferenceSession(path, options), "CPU");
    }

    private static Image<Rgba32> Source(int width, int height)
    {
        var source = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            source[x, y] = new Rgba32((byte)(x % 256), (byte)(y * 13), (byte)(x * 7 % 256), 42);
        return source;
    }

    private static void AssertNearestPixels(Image<Rgba32> source, Image<Rgba32> output)
    {
        Assert.Equal((source.Width * 2, source.Height * 2), (output.Width, output.Height));
        for (var y = 0; y < output.Height; y++) for (var x = 0; x < output.Width; x++)
            Assert.Equal(source[x / 2, y / 2], output[x, y]);
    }

    private static OnnxRuntimeException NativeError(string code, string message)
    {
        var type = typeof(OnnxRuntimeException).Assembly.GetType("Microsoft.ML.OnnxRuntime.ErrorCode")!;
        return (OnnxRuntimeException)Activator.CreateInstance(typeof(OnnxRuntimeException), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [Enum.Parse(type, code), message], null)!;
    }

    public void Dispose()
    {
        for (var attempt = 0; ; attempt++)
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); return; }
            catch (IOException) when (attempt < 19) { Thread.Sleep(100); }
        }
    }
}
