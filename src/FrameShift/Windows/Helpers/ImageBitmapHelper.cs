using System.Drawing;
using System.IO;

namespace FrameShift.Windows.Helpers;

public static class ImageBitmapHelper
{
    public static (int Width, int Height) GetImageInfo(string path)
    {
        using var stream = File.OpenRead(path);
        using var image = Image.FromStream(stream, false, false);
        return (image.Width, image.Height);
    }

    public static Bitmap LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        using var image = Image.FromStream(stream, false, false);
        return new Bitmap(image);
    }
}
