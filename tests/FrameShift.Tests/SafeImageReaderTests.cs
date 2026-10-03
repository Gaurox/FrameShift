using FrameShift.Core.Helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace FrameShift.Tests;

public sealed class SafeImageReaderTests
{
    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".webp")]
    public void RenamedTiffIsRejectedDuringIdentifyAndLoad(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"frameshift_é {Guid.NewGuid():N}{extension}");
        try
        {
            using var image = new Image<Rgba32>(2, 2);
            image.SaveAsTiff(path);
            Assert.Throws<UnknownImageFormatException>(() => SafeImageReader.Identify(path));
            Assert.Throws<UnknownImageFormatException>(() => SafeImageReader.Load<Rgba32>(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".webp")]
    [InlineData(".bmp")]
    public void SupportedFormatsRemainReadable(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"frameshift_é {Guid.NewGuid():N}{extension}");
        try
        {
            using var original = new Image<Rgba32>(3, 2, new Rgba32(80, 120, 160));
            original.Save(path);
            var info = SafeImageReader.Identify(path);
            using var decoded = SafeImageReader.Load<Rgba32>(path);
            Assert.Equal(3, info.Width);
            Assert.Equal(2, decoded.Height);
            Assert.InRange((int)decoded[0, 0].R, 75, 85);
            Assert.InRange((int)decoded[0, 0].G, 115, 125);
            Assert.InRange((int)decoded[0, 0].B, 155, 165);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PngTransparencyIsPreserved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"frameshift_{Guid.NewGuid():N}.png");
        try
        {
            using var original = new Image<Rgba32>(1, 1, new Rgba32(80, 120, 160, 42));
            original.SaveAsPng(path);
            using var decoded = SafeImageReader.Load<Rgba32>(path);
            Assert.Equal(original[0, 0], decoded[0, 0]);
        }
        finally { File.Delete(path); }
    }
}
