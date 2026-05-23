using System;
using System.Drawing;

namespace FrameShift.Windows.Helpers;

public static class PreviewLayoutHelper
{
    public static RectangleF GetCenteredImageBounds(Size availableSize, Size bitmapSize)
    {
        var availableWidth = Math.Max(1, availableSize.Width);
        var availableHeight = Math.Max(1, availableSize.Height);
        var bitmapWidth = Math.Max(1, bitmapSize.Width);
        var bitmapHeight = Math.Max(1, bitmapSize.Height);
        var scale = Math.Min(availableWidth / (double)bitmapWidth, availableHeight / (double)bitmapHeight);

        var displayWidth = Math.Max(1, (int)Math.Round(bitmapWidth * scale));
        var displayHeight = Math.Max(1, (int)Math.Round(bitmapHeight * scale));
        var imageX = Math.Max(0, (availableWidth - displayWidth) / 2);
        var imageY = Math.Max(0, (availableHeight - displayHeight) / 2);
        return new RectangleF(imageX, imageY, displayWidth, displayHeight);
    }
}
