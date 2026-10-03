using FrameShift.Core.Helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
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

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".webp")]
    public void EmbeddedIccIsPreservedWithoutChangingPixelColors(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"frameshift_é {Guid.NewGuid():N}{extension}");
        try
        {
            using var original = new Image<Rgba32>(3, 2, new Rgba32(80, 120, 160));
            original.Metadata.IccProfile = new IccProfile(TruncatedIccProfile());
            original.Save(path);
            using var decoded = SafeImageReader.Load<Rgba32>(path);
            Assert.NotNull(decoded.Metadata.IccProfile);
            Assert.Equal(TruncatedIccProfile(), decoded.Metadata.IccProfile.ToByteArray());
            Assert.InRange((int)decoded[0, 0].R, 75, 85);
            Assert.InRange((int)decoded[0, 0].G, 115, 125);
            Assert.InRange((int)decoded[0, 0].B, 155, 165);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TruncatedIccDoesNotAllocateFromUnvalidatedGridDimensions()
    {
        var profile = new IccProfile(TruncatedIccProfile());
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Empty(profile.Entries);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 10_000_000);
    }

    // 200-byte truncated A2B0/mpet/CLUT fixture, 15 channels, grid=3.
    // Regression for https://github.com/SixLabors/ImageSharp/security/advisories/GHSA-gwg2-r3hj-4w44
    private static byte[] TruncatedIccProfile()
    {
        var bytes = new byte[200];
        (int Offset, uint Value)[] fields =
        [
            (0, 200), (4, 0x74657374), (8, 0x04000000), (12, 0x6D6E7472),
            (16, 0x52474220), (20, 0x58595A20), (36, 0x61637370),
            (128, 1), (132, 0x41324230), (136, 144), (140, 56),
            (144, 0x6D706574), (156, 1), (160, 24), (164, 32), (168, 0x636C7574),
            (172, 0x000F000F)
        ];
        foreach (var field in fields)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(field.Offset), field.Value);
        bytes.AsSpan(176, 15).Fill(3);
        return bytes;
    }
}
