using Clipper2Lib;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FrameShift.Core.AI.Ocr;

internal static class OcrGeometry
{
    public static List<OcrPoint[]> Detect(float[] probability, int width, int height, double boxThreshold, CancellationToken token)
    {
        var mask = new byte[probability.Length];
        for (var i = 0; i < mask.Length; i++)
            mask[i] = probability[i] > .2f ? (byte)1 : (byte)0;
        var queue = new int[mask.Length];
        var boxes = new List<OcrPoint[]>();
        for (var start = 0; start < mask.Length && boxes.Count < 3000; start++)
        {
            if (start % width == 0)
                token.ThrowIfCancellationRequested();
            if (mask[start] != 1)
                continue;
            var head = 0;
            var tail = 1;
            queue[0] = start;
            mask[start] = 2;
            var boundary = new List<OcrPoint>();
            while (head < tail)
            {
                if ((head & 4095) == 0)
                    token.ThrowIfCancellationRequested();
                var position = queue[head++];
                var x = position % width;
                var y = position / width;
                var edge = false;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                        {
                            edge = true;
                            continue;
                        }

                        var next = ny * width + nx;
                        if (mask[next] == 0)
                            edge = true;
                        if (mask[next] != 1)
                            continue;
                        mask[next] = 2;
                        queue[tail++] = next;
                    }

                if (edge)
                    boundary.Add(new(x, y));
            }

            if (tail < 6 || boundary.Count < 3)
                continue;
            var rectangle = MinimumRectangle(boundary);
            if (rectangle.Length != 4)
                continue;
            var sideA = Distance(rectangle[0], rectangle[1]);
            var sideB = Distance(rectangle[1], rectangle[2]);
            if (Math.Min(sideA, sideB) < 3)
                continue;
            var left = Math.Max(0, (int)Math.Floor(rectangle.Min(p => p.X)));
            var right = Math.Min(width - 1, (int)Math.Ceiling(rectangle.Max(p => p.X)));
            var top = Math.Max(0, (int)Math.Floor(rectangle.Min(p => p.Y)));
            var bottom = Math.Min(height - 1, (int)Math.Ceiling(rectangle.Max(p => p.Y)));
            double score = 0;
            var count = 0;
            for (var y = top; y <= bottom; y++)
                for (var x = left; x <= right; x++)
                    if (Inside(new(x, y), rectangle))
                    {
                        score += probability[y * width + x];
                        count++;
                    }

            if (count == 0 || score / count < boxThreshold)
                continue;
            var polygon = new PathD(rectangle.Select(p => new PointD(p.X, p.Y)));
            var expanded = Clipper.InflatePaths(new PathsD { polygon }, sideA * sideB * 1.4 / (2 * (sideA + sideB)), JoinType.Round, EndType.Polygon);
            if (expanded.Count != 1)
                continue;
            var box = MinimumRectangle(expanded[0].Select(p => new OcrPoint(p.x, p.y)).ToList());
            if (box.Length == 4)
                boxes.Add(box.Select(p => new OcrPoint(Math.Clamp(p.X, 0, width - 1), Math.Clamp(p.Y, 0, height - 1))).ToArray());
        }

        return boxes;
    }

    public static double Distance(OcrPoint a, OcrPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    private static double Cross(OcrPoint a, OcrPoint b, OcrPoint c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static bool Inside(OcrPoint p, OcrPoint[] box) => Enumerable.Range(0, 4).All(i => Cross(box[i], box[(i + 1) % 4], p) >= -.01);
    public static OcrPoint[] MinimumRectangle(IReadOnlyList<OcrPoint> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        if (sorted.Length < 3)
            return[];
        var hull = new List<OcrPoint>();
        foreach (var p in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        var lower = hull.Count;
        for (var i = sorted.Length - 2; i >= 0; i--)
        {
            var p = sorted[i];
            while (hull.Count > lower && Cross(hull[^2], hull[^1], p) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        hull.RemoveAt(hull.Count - 1);
        double best = double.MaxValue;
        OcrPoint[] result = [];
        for (var i = 0; i < hull.Count; i++)
        {
            var angle = Math.Atan2(hull[(i + 1) % hull.Count].Y - hull[i].Y, hull[(i + 1) % hull.Count].X - hull[i].X);
            var c = Math.Cos(angle);
            var s = Math.Sin(angle);
            var xs = hull.Select(p => p.X * c + p.Y * s).ToArray();
            var ys = hull.Select(p => -p.X * s + p.Y * c).ToArray();
            var minX = xs.Min();
            var maxX = xs.Max();
            var minY = ys.Min();
            var maxY = ys.Max();
            var area = (maxX - minX) * (maxY - minY);
            if (area >= best)
                continue;
            best = area;
            OcrPoint Back(double x, double y) => new(x * c - y * s, x * s + y * c);
            result = [Back(minX, minY), Back(maxX, minY), Back(maxX, maxY), Back(minX, maxY)];
        }

        if (result.Length == 0)
            return[];
        var first = Enumerable.Range(0, 4).MinBy(i => result[i].X + result[i].Y);
        return Enumerable.Range(0, 4).Select(i => result[(first + i) % 4]).ToArray();
    }

    public static Image<Rgb24> Rectify(Image<Rgb24> source, OcrPoint[] p)
    {
        var width = Math.Clamp((int)Math.Round(Math.Max(Distance(p[0], p[1]), Distance(p[3], p[2]))), 1, 16384);
        var height = Math.Clamp((int)Math.Round(Math.Max(Distance(p[0], p[3]), Distance(p[1], p[2]))), 1, 16384);
        // Bound crops, preserving their aspect ratio; recognition uses a fixed height of 48.
        var ratio = Math.Min(1, Math.Sqrt(4_000_000d / (width * (double)height)));
        width = Math.Max(1, (int)(width * ratio));
        height = Math.Max(1, (int)(height * ratio));
        var result = new Image<Rgb24>(width, height);
        var dx1 = p[1].X - p[2].X;
        var dx2 = p[3].X - p[2].X;
        var dx3 = p[0].X - p[1].X + p[2].X - p[3].X;
        var dy1 = p[1].Y - p[2].Y;
        var dy2 = p[3].Y - p[2].Y;
        var dy3 = p[0].Y - p[1].Y + p[2].Y - p[3].Y;
        var determinant = dx1 * dy2 - dx2 * dy1;
        var g = Math.Abs(determinant) < 1e-9 ? 0 : (dx3 * dy2 - dx2 * dy3) / determinant;
        var h = Math.Abs(determinant) < 1e-9 ? 0 : (dx1 * dy3 - dx3 * dy1) / determinant;
        var a = p[1].X - p[0].X + g * p[1].X;
        var b = p[3].X - p[0].X + h * p[3].X;
        var d = p[1].Y - p[0].Y + g * p[1].Y;
        var e = p[3].Y - p[0].Y + h * p[3].Y;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var u = (x + .5) / width;
                var v = (y + .5) / height;
                var divisor = g * u + h * v + 1;
                var sx = Math.Clamp((a * u + b * v + p[0].X) / divisor, 0, source.Width - 1);
                var sy = Math.Clamp((d * u + e * v + p[0].Y) / divisor, 0, source.Height - 1);
                var ix = (int)sx;
                var iy = (int)sy;
                var fx = sx - ix;
                var fy = sy - iy;
                var tl = source[ix, iy];
                var tr = source[Math.Min(ix + 1, source.Width - 1), iy];
                var bl = source[ix, Math.Min(iy + 1, source.Height - 1)];
                var br = source[Math.Min(ix + 1, source.Width - 1), Math.Min(iy + 1, source.Height - 1)];
                byte Blend(byte v0, byte v1, byte v2, byte v3) => (byte)Math.Clamp(v0 * (1 - fx) * (1 - fy) + v1 * fx * (1 - fy) + v2 * (1 - fx) * fy + v3 * fx * fy, 0, 255);
                result[x, y] = new(Blend(tl.R, tr.R, bl.R, br.R), Blend(tl.G, tr.G, bl.G, br.G), Blend(tl.B, tr.B, bl.B, br.B));
            }

        return result;
    }

    public static bool Overlaps(OcrLine a, OcrLine b, double threshold = .5)
    {
        var intersection = Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left)) * Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top));
        return intersection / Math.Max(1, Math.Min((a.Right - a.Left) * (a.Bottom - a.Top), (b.Right - b.Left) * (b.Bottom - b.Top))) > threshold;
    }

    internal static List<OcrPoint[]> MergeTiles(List<OcrPoint[]> polygons)
    {
        var merged = new List<OcrPoint[]>();
        foreach (var polygon in polygons)
        {
            var current = new OcrLine("", null, polygon);
            var index = merged.FindIndex(existing =>
            {
                var previous = new OcrLine("", null, existing);
                var height = Math.Max(1, Math.Min(previous.Bottom - previous.Top, current.Bottom - current.Top));
                var vertical = Math.Min(previous.Bottom, current.Bottom) - Math.Max(previous.Top, current.Top);
                var horizontal = Math.Min(previous.Right, current.Right) - Math.Max(previous.Left, current.Left);
                var u0 = new OcrPoint(existing[1].X - existing[0].X, existing[1].Y - existing[0].Y);
                var u1 = new OcrPoint(polygon[1].X - polygon[0].X, polygon[1].Y - polygon[0].Y);
                var direction = (u0.X * u1.X + u0.Y * u1.Y) / Math.Max(1, Distance(existing[0], existing[1]) * Distance(polygon[0], polygon[1]));
                return horizontal > height * .5 && vertical > height * .7 && direction > .98;
            });
            if (index < 0)
                merged.Add(polygon);
            else
                merged[index] = MinimumRectangle(merged[index].Concat(polygon).ToArray());
        }

        return merged;
    }
}
