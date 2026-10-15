using System.Buffers.Binary;
using FrameShift.Core.AI.RemoveBackground;
using FrameShift.Core.AI.RemoveObject;
using FrameShift.Core.AI.Upscale;
using FrameShift.Core.Helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace FrameShift.Tests;

public sealed class ImageMemoryValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"FrameShift mémoire été {Guid.NewGuid():N}");
    public ImageMemoryValidationTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("upscale")] [InlineData("background")] [InlineData("object")]
    public async Task ExcessiveDimensionsAreRejectedBeforeDecodingOrLoadingModels(string engine)
    {
        var path = Path.Combine(_root, "grande image é.bmp");
        WriteHeaderOnlyBmp(path, 100000, 100000);
        var info = SafeImageReader.Identify(path);
        Assert.Equal(100000, info.Width);
        Assert.Equal(100000, info.Height);
        InvalidOperationException error;
        switch (engine)
        {
            case "upscale":
                using (var processor = new UpscaleEngine(new("test", "Test", _root, "absent.onnx", "", "", 0, "test")))
                    error = await Assert.ThrowsAsync<InvalidOperationException>(() => processor.UpscaleAsync(path, new(), new InlineProgress<UpscaleProgress>(), CancellationToken.None));
                break;
            case "background":
                using (var processor = new BackgroundRemovalEngine(new("test", "Test", _root, "absent.onnx", "", "", 0, "test", 32)))
                    error = await Assert.ThrowsAsync<InvalidOperationException>(() => processor.RemoveBackgroundAsync(path, new InlineProgress<InferenceProgress>(), CancellationToken.None));
                break;
            default:
                using (var processor = new ObjectRemovalEngine(new("test", "Test", _root, "absent.onnx", "", "", 0, "test")))
                    error = await Assert.ThrowsAsync<InvalidOperationException>(() => processor.InpaintAsync(path, new bool[1, 1], new InlineProgress<InpaintProgress>(), CancellationToken.None));
                break;
        }
        Assert.Contains("too large", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { path }, Directory.GetFiles(_root));
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task NativeUpscaleBudgetIsCheckedBeforeDecodeEvenWithASmallFinalTarget()
    {
        var path = Path.Combine(_root, "native trop grande.bmp");
        WriteHeaderOnlyBmp(path, 3000, 3000); // Valid input budget, but native x4 is 144 million pixels.
        using var processor = new UpscaleEngine(new("test", "Test", _root, "absent.onnx", "", "", 0, "test"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => processor.UpscaleAsync(path,
            new(TargetWidth: 3000, TargetHeight: 3000), new InlineProgress<UpscaleProgress>(), CancellationToken.None));
        Assert.Contains("output too large", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(2000, 2000, 4)] [InlineData(4000, 4000, 2)]
    public void ExactNativeOutputBudgetIsAcceptedWithoutAllocatingPixels(int width, int height, int scale)
        => UpscaleFrameProcessor.ValidateOutputSize(width, height, new(), scale);

    [Theory]
    [InlineData(2001, 2000, 4)] [InlineData(4001, 4000, 2)]
    [InlineData(int.MaxValue, int.MaxValue, 4)] [InlineData(1, 1, int.MaxValue)]
    [InlineData(0, 100, 4)] [InlineData(10, 10, 0)]
    public void InvalidOrExcessiveOutputIsRejectedWithoutOverflow(int width, int height, int scale)
        => Assert.Throws<InvalidOperationException>(() => UpscaleFrameProcessor.ValidateOutputSize(width, height, new(), scale));

    [Theory]
    [InlineData(".png")] [InlineData(".webp")]
    public void StaticLoadingDecodesOnlyTheFirstAnimationFrame(string extension)
    {
        var path = Path.Combine(_root, "animation" + extension);
        using (var image = new Image<Rgba32>(4, 3, new Rgba32(50, 80, 120, 42)))
        using (var second = new Image<Rgba32>(4, 3, new Rgba32(200, 30, 40)))
        {
            image.Frames.AddFrame(second.Frames.RootFrame);
            image.Save(path);
        }
        using var all = SafeImageReader.Load<Rgba32>(path);
        Assert.Equal(2, all.Frames.Count);
        var validations = 0;
        using var first = SafeImageReader.LoadValidatedFirstFrame<Rgba32>(path, (width, height) =>
        { Assert.Equal((4, 3), (width, height)); validations++; });
        Assert.Equal(2, validations);
        Assert.Single(first.Frames);
        Assert.Equal(all[0, 0], first[0, 0]);
    }

    [Fact]
    public void ValidatedReaderPreservesAlphaProfileAndGeometry()
    {
        var path = Path.Combine(_root, "couleurs é.png");
        using (var image = new Image<Rgba32>(4, 3, new Rgba32(50, 80, 120, 42)))
        {
            image.Metadata.IccProfile = new IccProfile();
            image.SaveAsPng(path);
        }
        using var ordinary = SafeImageReader.Load<Rgba32>(path);
        using var validated = SafeImageReader.LoadValidatedFirstFrame<Rgba32>(path, (_, _) => { });
        Assert.Equal((ordinary.Width, ordinary.Height), (validated.Width, validated.Height));
        Assert.Equal(ordinary[0, 0], validated[0, 0]);
        Assert.Equal(ordinary.Metadata.IccProfile!.ToByteArray(), validated.Metadata.IccProfile!.ToByteArray());
    }

    [Fact]
    public void SizeValidationAndCancellationRunBeforePixelDecodeAndReleaseTheHandle()
    {
        var path = Path.Combine(_root, "header only.bmp");
        WriteHeaderOnlyBmp(path, 10, 10);
        using var cancel = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() => SafeImageReader.LoadValidatedFirstFrame<Rgba32>(path, (_, _) => cancel.Cancel(), cancel.Token));
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public void ValidatedReaderAlsoRejectsARenamedDisallowedFormat()
    {
        var path = Path.Combine(_root, "renommé.png");
        using (var image = new Image<Rgba32>(2, 2)) image.SaveAsTiff(path);
        Assert.Throws<UnknownImageFormatException>(() => SafeImageReader.LoadValidatedFirstFrame<Rgba32>(path, (_, _) => { }));
    }

    private static void WriteHeaderOnlyBmp(string path, int width, int height)
    {
        var header = new byte[54];
        "BM"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(2), 54);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(28), 24);
        File.WriteAllBytes(path, header);
    }

    private sealed class InlineProgress<T> : IProgress<T> { public void Report(T value) { } }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
