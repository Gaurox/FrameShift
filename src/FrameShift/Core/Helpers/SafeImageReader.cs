using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SharpImage = SixLabors.ImageSharp.Image;

namespace FrameShift.Core.Helpers;

/// <summary>Only register the file decoders used by FrameShift, regardless of extension.</summary>
internal static class SafeImageReader
{
    private static readonly DecoderOptions Options = new()
    {
        Configuration = new Configuration(
            new PngConfigurationModule(), new JpegConfigurationModule(),
            new WebpConfigurationModule(), new BmpConfigurationModule())
    };

    public static Image<TPixel> Load<TPixel>(string path)
        where TPixel : unmanaged, IPixel<TPixel> => SharpImage.Load<TPixel>(Options, path);

    public static ImageInfo Identify(string path) => SharpImage.Identify(Options, path);
}
