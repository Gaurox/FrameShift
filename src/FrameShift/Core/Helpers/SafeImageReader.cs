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
        ColorProfileHandling = ColorProfileHandling.Preserve,
        Configuration = new Configuration(
            new PngConfigurationModule(), new JpegConfigurationModule(),
            new WebpConfigurationModule(), new BmpConfigurationModule())
    };
    private static readonly DecoderOptions FirstFrameOptions = new()
    {
        Configuration = Options.Configuration,
        ColorProfileHandling = ColorProfileHandling.Preserve,
        MaxFrames = 1
    };

    public static Image<TPixel> Load<TPixel>(string path)
        where TPixel : unmanaged, IPixel<TPixel> => SharpImage.Load<TPixel>(Options, path);

    public static ImageInfo Identify(string path) => SharpImage.Identify(Options, path);

    public static Image<TPixel> LoadFirstFrame<TPixel>(string path)
        where TPixel : unmanaged, IPixel<TPixel> => SharpImage.Load<TPixel>(FirstFrameOptions, path);

    public static Image<TPixel> LoadValidatedFirstFrame<TPixel>(
        string path, Action<int, int> validateSize, CancellationToken token = default)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        token.ThrowIfCancellationRequested();
        // Keep the same read handle between Identify and Decode: the source cannot
        // be replaced with a larger image after validation on Windows.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var info = SharpImage.Identify(FirstFrameOptions, stream);
        validateSize(info.Width, info.Height);
        token.ThrowIfCancellationRequested();
        stream.Position = 0;
        var image = SharpImage.Load<TPixel>(FirstFrameOptions, stream);
        try
        {
            token.ThrowIfCancellationRequested();
            validateSize(image.Width, image.Height);
            return image;
        }
        catch { image.Dispose(); throw; }
    }
}
