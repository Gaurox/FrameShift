using System.Drawing;
using FrameShift.Core.Helpers;
using Xunit;

namespace FrameShift.Tests;

public sealed class ImageAutoCropDetectorTests
{
    [Fact]
    public void TryDetectCropBounds_DetectsHorizontalBlackBars()
    {
        using var bitmap = new Bitmap(320, 180);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
            using var brush = new SolidBrush(Color.FromArgb(210, 225, 245));
            graphics.FillRectangle(brush, 0, 20, bitmap.Width, 140);
        }

        var success = ImageAutoCropDetector.TryDetectCropBounds(bitmap, out var cropBounds);

        Assert.True(success);
        Assert.Equal(0, cropBounds.X);
        Assert.InRange(cropBounds.Y, 15, 25);
        Assert.Equal(bitmap.Width, cropBounds.Width);
        Assert.InRange(cropBounds.Height, 140, 152);
    }

    [Fact]
    public void TryDetectCropBounds_DetectsDocumentBoundingBoxOnWhiteBackground()
    {
        using var bitmap = new Bitmap(500, 400);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var brush = new SolidBrush(Color.FromArgb(238, 238, 230));
            graphics.FillPolygon(
                brush,
                new Point[]
                {
                    new Point(130, 60),
                    new Point(385, 95),
                    new Point(350, 325),
                    new Point(105, 300)
                });
        }

        var success = ImageAutoCropDetector.TryDetectCropBounds(bitmap, out var cropBounds);

        Assert.True(success);
        Assert.InRange(cropBounds.X, 95, 140);
        Assert.InRange(cropBounds.Y, 50, 105);
        Assert.InRange(cropBounds.Right, 345, 395);
        Assert.InRange(cropBounds.Bottom, 295, 330);
    }
}
