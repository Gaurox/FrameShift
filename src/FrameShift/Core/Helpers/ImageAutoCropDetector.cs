using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace FrameShift.Core.Helpers;

internal static class ImageAutoCropDetector
{
    private const int MaxAnalysisDimension = 320;
    private const int MinimumDetectedSize = 18;
    private const int SourcePaddingPixels = 0;
    private const int StrongEdgeThreshold = 28;
    private const int MinimumComponentPixels = 40;
    private const double MinimumComponentAreaRatio = 0.0025;
    private const double BorderOccupancyThreshold = 0.035;
    private const double SourceBorderOccupancyThreshold = 0.003;

    public static bool TryDetectCropBounds(Bitmap sourceBitmap, out Rectangle cropBounds)
    {
        cropBounds = Rectangle.Empty;

        if (sourceBitmap.Width < 2 || sourceBitmap.Height < 2)
        {
            return false;
        }

        using var analysisBitmap = CreateAnalysisBitmap(sourceBitmap, out var scaleX, out var scaleY);
        var pixels = BuildPixelData(analysisBitmap);
        var edgeStrength = BuildEdgeStrengthMap(pixels);
        var backgroundStats = CreateBackgroundStats(pixels);
        var contentMask = BuildContentMask(pixels, edgeStrength, backgroundStats);
        SuppressIsolatedNoise(contentMask);

        if (!TryFindLargestComponentBounds(contentMask, out var componentBounds))
        {
            return false;
        }

        componentBounds = TightenBounds(contentMask, componentBounds);
        cropBounds = ScaleToSourceBounds(componentBounds, sourceBitmap.Size, scaleX, scaleY);
        cropBounds = RefineSourceBounds(sourceBitmap, cropBounds);
        return cropBounds.Width >= 2 && cropBounds.Height >= 2;
    }

    private static Bitmap CreateAnalysisBitmap(Bitmap sourceBitmap, out double scaleX, out double scaleY)
    {
        var longestSide = Math.Max(sourceBitmap.Width, sourceBitmap.Height);
        var scale = longestSide > MaxAnalysisDimension
            ? MaxAnalysisDimension / (double)longestSide
            : 1d;

        var width = Math.Max(1, (int)Math.Round(sourceBitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(sourceBitmap.Height * scale));

        scaleX = sourceBitmap.Width / (double)width;
        scaleY = sourceBitmap.Height / (double)height;

        var analysisBitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(analysisBitmap);
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(sourceBitmap, 0, 0, width, height);
        return analysisBitmap;
    }

    private static PixelData[,] BuildPixelData(Bitmap bitmap)
    {
        var pixels = new PixelData[bitmap.Height, bitmap.Width];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var brightness = (byte)Math.Clamp(
                    (int)Math.Round((pixel.R * 0.299) + (pixel.G * 0.587) + (pixel.B * 0.114)),
                    0,
                    255);
                pixels[y, x] = new PixelData(pixel.R, pixel.G, pixel.B, brightness);
            }
        }

        return pixels;
    }

    private static int[,] BuildEdgeStrengthMap(PixelData[,] pixels)
    {
        var height = pixels.GetLength(0);
        var width = pixels.GetLength(1);
        var edgeStrength = new int[height, width];

        for (var y = 0; y < height; y++)
        {
            var nextY = Math.Min(height - 1, y + 1);
            for (var x = 0; x < width; x++)
            {
                var nextX = Math.Min(width - 1, x + 1);
                var horizontal = Math.Abs(pixels[y, x].Brightness - pixels[y, nextX].Brightness);
                var vertical = Math.Abs(pixels[y, x].Brightness - pixels[nextY, x].Brightness);
                edgeStrength[y, x] = horizontal + vertical;
            }
        }

        return edgeStrength;
    }

    private static BackgroundStats CreateBackgroundStats(PixelData[,] pixels)
    {
        var height = pixels.GetLength(0);
        var width = pixels.GetLength(1);
        var borderDepth = Math.Max(2, Math.Min(12, Math.Min(width, height) / 20));
        var borderPixels = new List<PixelData>(borderDepth * ((width * 2) + (height * 2)));

        for (var y = 0; y < borderDepth; y++)
        {
            for (var x = 0; x < width; x++)
            {
                borderPixels.Add(pixels[y, x]);
                borderPixels.Add(pixels[height - 1 - y, x]);
            }
        }

        for (var x = 0; x < borderDepth; x++)
        {
            for (var y = borderDepth; y < height - borderDepth; y++)
            {
                borderPixels.Add(pixels[y, x]);
                borderPixels.Add(pixels[y, width - 1 - x]);
            }
        }

        var medianRed = GetMedian(borderPixels.Select(pixel => (int)pixel.R));
        var medianGreen = GetMedian(borderPixels.Select(pixel => (int)pixel.G));
        var medianBlue = GetMedian(borderPixels.Select(pixel => (int)pixel.B));
        var medianBrightness = GetMedian(borderPixels.Select(pixel => (int)pixel.Brightness));

        var colorDeviation = borderPixels
            .Select(pixel => Math.Abs(pixel.R - medianRed) + Math.Abs(pixel.G - medianGreen) + Math.Abs(pixel.B - medianBlue))
            .ToArray();
        Array.Sort(colorDeviation);

        var brightnessDeviation = borderPixels
            .Select(pixel => Math.Abs(pixel.Brightness - medianBrightness))
            .ToArray();
        Array.Sort(brightnessDeviation);

        var colorTolerance = Math.Max(20, colorDeviation[colorDeviation.Length / 2] * 4 + 6);
        var brightnessTolerance = Math.Max(12, brightnessDeviation[brightnessDeviation.Length / 2] * 4 + 4);

        return new BackgroundStats(medianRed, medianGreen, medianBlue, medianBrightness, colorTolerance, brightnessTolerance);
    }

    private static bool[,] BuildContentMask(PixelData[,] pixels, int[,] edgeStrength, BackgroundStats backgroundStats)
    {
        var height = pixels.GetLength(0);
        var width = pixels.GetLength(1);
        var mask = new bool[height, width];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[y, x];
                var colorDistance = Math.Abs(pixel.R - backgroundStats.MedianR) +
                                    Math.Abs(pixel.G - backgroundStats.MedianG) +
                                    Math.Abs(pixel.B - backgroundStats.MedianB);
                var brightnessDistance = Math.Abs(pixel.Brightness - backgroundStats.MedianBrightness);
                var hasStrongColorDifference = colorDistance >= backgroundStats.ColorTolerance;
                var hasStrongBrightnessDifference = brightnessDistance >= backgroundStats.BrightnessTolerance;
                var hasStrongEdge = edgeStrength[y, x] >= StrongEdgeThreshold;

                mask[y, x] = hasStrongColorDifference || (hasStrongBrightnessDifference && hasStrongEdge);
            }
        }

        return mask;
    }

    private static void SuppressIsolatedNoise(bool[,] mask)
    {
        var height = mask.GetLength(0);
        var width = mask.GetLength(1);
        var snapshot = (bool[,])mask.Clone();

        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                if (!snapshot[y, x])
                {
                    continue;
                }

                var neighbors = 0;
                for (var ny = y - 1; ny <= y + 1; ny++)
                {
                    for (var nx = x - 1; nx <= x + 1; nx++)
                    {
                        if ((nx != x || ny != y) && snapshot[ny, nx])
                        {
                            neighbors++;
                        }
                    }
                }

                if (neighbors <= 1)
                {
                    mask[y, x] = false;
                }
            }
        }
    }

    private static bool TryFindLargestComponentBounds(bool[,] mask, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;

        var height = mask.GetLength(0);
        var width = mask.GetLength(1);
        var visited = new bool[height, width];
        var minimumArea = Math.Max(MinimumComponentPixels, (int)Math.Round(width * height * MinimumComponentAreaRatio));

        var bestScore = 0;
        var bestBounds = Rectangle.Empty;
        var queue = new Queue<Point>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!mask[y, x] || visited[y, x])
                {
                    continue;
                }

                visited[y, x] = true;
                queue.Enqueue(new Point(x, y));

                var pixelCount = 0;
                var minX = x;
                var maxX = x;
                var minY = y;
                var maxY = y;

                while (queue.Count > 0)
                {
                    var point = queue.Dequeue();
                    pixelCount++;

                    if (point.X < minX)
                    {
                        minX = point.X;
                    }

                    if (point.X > maxX)
                    {
                        maxX = point.X;
                    }

                    if (point.Y < minY)
                    {
                        minY = point.Y;
                    }

                    if (point.Y > maxY)
                    {
                        maxY = point.Y;
                    }

                    for (var offsetY = -1; offsetY <= 1; offsetY++)
                    {
                        for (var offsetX = -1; offsetX <= 1; offsetX++)
                        {
                            if (offsetX == 0 && offsetY == 0)
                            {
                                continue;
                            }

                            var nextX = point.X + offsetX;
                            var nextY = point.Y + offsetY;
                            if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height)
                            {
                                continue;
                            }

                            if (!mask[nextY, nextX] || visited[nextY, nextX])
                            {
                                continue;
                            }

                            visited[nextY, nextX] = true;
                            queue.Enqueue(new Point(nextX, nextY));
                        }
                    }
                }

                if (pixelCount < minimumArea)
                {
                    continue;
                }

                var candidateBounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
                var score = pixelCount;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestBounds = candidateBounds;
                }
            }
        }

        if (bestScore <= 0 || bestBounds.Width < MinimumDetectedSize || bestBounds.Height < MinimumDetectedSize)
        {
            return false;
        }

        bounds = bestBounds;
        return true;
    }

    private static Rectangle ScaleToSourceBounds(Rectangle analysisBounds, Size sourceSize, double scaleX, double scaleY)
    {
        var scaledLeft = Math.Max(0, (int)Math.Floor(analysisBounds.Left * scaleX) - SourcePaddingPixels);
        var scaledTop = Math.Max(0, (int)Math.Floor(analysisBounds.Top * scaleY) - SourcePaddingPixels);
        var scaledRight = Math.Min(sourceSize.Width - 1, (int)Math.Ceiling(analysisBounds.Right * scaleX) + SourcePaddingPixels);
        var scaledBottom = Math.Min(sourceSize.Height - 1, (int)Math.Ceiling(analysisBounds.Bottom * scaleY) + SourcePaddingPixels);

        var width = Math.Max(2, scaledRight - scaledLeft + 1);
        var height = Math.Max(2, scaledBottom - scaledTop + 1);

        if (scaledLeft + width > sourceSize.Width)
        {
            width = sourceSize.Width - scaledLeft;
        }

        if (scaledTop + height > sourceSize.Height)
        {
            height = sourceSize.Height - scaledTop;
        }

        return new Rectangle(scaledLeft, scaledTop, width, height);
    }

    private static Rectangle TightenBounds(bool[,] mask, Rectangle bounds)
    {
        var left = bounds.Left;
        var top = bounds.Top;
        var right = bounds.Right - 1;
        var bottom = bounds.Bottom - 1;

        while (left < right && GetColumnOccupancy(mask, left, top, bottom) < BorderOccupancyThreshold)
        {
            left++;
        }

        while (right > left && GetColumnOccupancy(mask, right, top, bottom) < BorderOccupancyThreshold)
        {
            right--;
        }

        while (top < bottom && GetRowOccupancy(mask, top, left, right) < BorderOccupancyThreshold)
        {
            top++;
        }

        while (bottom > top && GetRowOccupancy(mask, bottom, left, right) < BorderOccupancyThreshold)
        {
            bottom--;
        }

        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private static Rectangle RefineSourceBounds(Bitmap sourceBitmap, Rectangle bounds)
    {
        var backgroundStats = CreateSourceBackgroundStats(sourceBitmap);
        var left = Math.Clamp(bounds.Left, 0, sourceBitmap.Width - 1);
        var top = Math.Clamp(bounds.Top, 0, sourceBitmap.Height - 1);
        var right = Math.Clamp(bounds.Right - 1, left, sourceBitmap.Width - 1);
        var bottom = Math.Clamp(bounds.Bottom - 1, top, sourceBitmap.Height - 1);

        while (left < right && GetSourceColumnOccupancy(sourceBitmap, left, top, bottom, backgroundStats) < SourceBorderOccupancyThreshold)
        {
            left++;
        }

        while (right > left && GetSourceColumnOccupancy(sourceBitmap, right, top, bottom, backgroundStats) < SourceBorderOccupancyThreshold)
        {
            right--;
        }

        while (top < bottom && GetSourceRowOccupancy(sourceBitmap, top, left, right, backgroundStats) < SourceBorderOccupancyThreshold)
        {
            top++;
        }

        while (bottom > top && GetSourceRowOccupancy(sourceBitmap, bottom, left, right, backgroundStats) < SourceBorderOccupancyThreshold)
        {
            bottom--;
        }

        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private static SourceBackgroundStats CreateSourceBackgroundStats(Bitmap sourceBitmap)
    {
        var borderDepth = Math.Max(2, Math.Min(24, Math.Min(sourceBitmap.Width, sourceBitmap.Height) / 20));
        var sampleStep = Math.Max(1, Math.Max(sourceBitmap.Width, sourceBitmap.Height) / 500);
        var samples = new List<PixelData>();

        for (var y = 0; y < borderDepth; y += sampleStep)
        {
            for (var x = 0; x < sourceBitmap.Width; x += sampleStep)
            {
                samples.Add(GetSourcePixelData(sourceBitmap.GetPixel(x, y)));
                samples.Add(GetSourcePixelData(sourceBitmap.GetPixel(x, sourceBitmap.Height - 1 - y)));
            }
        }

        for (var x = 0; x < borderDepth; x += sampleStep)
        {
            for (var y = borderDepth; y < sourceBitmap.Height - borderDepth; y += sampleStep)
            {
                samples.Add(GetSourcePixelData(sourceBitmap.GetPixel(x, y)));
                samples.Add(GetSourcePixelData(sourceBitmap.GetPixel(sourceBitmap.Width - 1 - x, y)));
            }
        }

        var medianRed = GetMedian(samples.Select(pixel => (int)pixel.R));
        var medianGreen = GetMedian(samples.Select(pixel => (int)pixel.G));
        var medianBlue = GetMedian(samples.Select(pixel => (int)pixel.B));
        var medianBrightness = GetMedian(samples.Select(pixel => (int)pixel.Brightness));

        var colorDeviation = samples
            .Select(pixel => Math.Abs(pixel.R - medianRed) + Math.Abs(pixel.G - medianGreen) + Math.Abs(pixel.B - medianBlue))
            .ToArray();
        Array.Sort(colorDeviation);

        var brightnessDeviation = samples
            .Select(pixel => Math.Abs(pixel.Brightness - medianBrightness))
            .ToArray();
        Array.Sort(brightnessDeviation);

        var colorTolerance = Math.Max(18, colorDeviation[colorDeviation.Length / 2] * 3 + 6);
        var brightnessTolerance = Math.Max(8, brightnessDeviation[brightnessDeviation.Length / 2] * 3 + 4);
        return new SourceBackgroundStats(medianRed, medianGreen, medianBlue, medianBrightness, colorTolerance, brightnessTolerance);
    }

    private static double GetSourceColumnOccupancy(Bitmap sourceBitmap, int x, int top, int bottom, SourceBackgroundStats backgroundStats)
    {
        var active = 0;
        var total = Math.Max(1, bottom - top + 1);
        for (var y = top; y <= bottom; y++)
        {
            if (IsSourceContentPixel(GetSourcePixelData(sourceBitmap.GetPixel(x, y)), backgroundStats))
            {
                active++;
            }
        }

        return active / (double)total;
    }

    private static double GetSourceRowOccupancy(Bitmap sourceBitmap, int y, int left, int right, SourceBackgroundStats backgroundStats)
    {
        var active = 0;
        var total = Math.Max(1, right - left + 1);
        for (var x = left; x <= right; x++)
        {
            if (IsSourceContentPixel(GetSourcePixelData(sourceBitmap.GetPixel(x, y)), backgroundStats))
            {
                active++;
            }
        }

        return active / (double)total;
    }

    private static bool IsSourceContentPixel(PixelData pixel, SourceBackgroundStats backgroundStats)
    {
        var colorDistance = Math.Abs(pixel.R - backgroundStats.MedianR) +
                            Math.Abs(pixel.G - backgroundStats.MedianG) +
                            Math.Abs(pixel.B - backgroundStats.MedianB);
        var brightnessDistance = Math.Abs(pixel.Brightness - backgroundStats.MedianBrightness);
        return colorDistance >= backgroundStats.ColorTolerance ||
               brightnessDistance >= backgroundStats.BrightnessTolerance;
    }

    private static PixelData GetSourcePixelData(Color pixel)
    {
        var brightness = (byte)Math.Clamp(
            (int)Math.Round((pixel.R * 0.299) + (pixel.G * 0.587) + (pixel.B * 0.114)),
            0,
            255);
        return new PixelData(pixel.R, pixel.G, pixel.B, brightness);
    }

    private static double GetColumnOccupancy(bool[,] mask, int x, int top, int bottom)
    {
        var active = 0;
        var total = Math.Max(1, bottom - top + 1);
        for (var y = top; y <= bottom; y++)
        {
            if (mask[y, x])
            {
                active++;
            }
        }

        return active / (double)total;
    }

    private static double GetRowOccupancy(bool[,] mask, int y, int left, int right)
    {
        var active = 0;
        var total = Math.Max(1, right - left + 1);
        for (var x = left; x <= right; x++)
        {
            if (mask[y, x])
            {
                active++;
            }
        }

        return active / (double)total;
    }

    private static int GetMedian(IEnumerable<int> values)
    {
        var array = values.OrderBy(value => value).ToArray();
        return array.Length == 0 ? 0 : array[array.Length / 2];
    }

    private readonly record struct PixelData(byte R, byte G, byte B, byte Brightness);

    private readonly record struct BackgroundStats(
        int MedianR,
        int MedianG,
        int MedianB,
        int MedianBrightness,
        int ColorTolerance,
        int BrightnessTolerance);

    private readonly record struct SourceBackgroundStats(
        int MedianR,
        int MedianG,
        int MedianB,
        int MedianBrightness,
        int ColorTolerance,
        int BrightnessTolerance);
}
