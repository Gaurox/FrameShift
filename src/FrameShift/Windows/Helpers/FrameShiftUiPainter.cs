using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace FrameShift.Windows.Helpers;

public static class FrameShiftUiPainter
{
    public static void AttachRoundedBorder(Panel panel, Color borderColor, int radius, float penWidth = 1F)
    {
        AttachRoundedBorder(panel, () => FrameShiftTheme.ResolveCurrentBackgroundColor(borderColor), radius, penWidth);
    }

    public static void AttachRoundedBorder(Panel panel, Func<Color> borderColorProvider, int radius, float penWidth = 1F)
    {
        panel.SizeChanged += (_, _) => panel.Invalidate();
        panel.Paint += (_, e) => DrawRoundedBorder(panel, e.Graphics, borderColorProvider(), radius, penWidth);
    }

    public static void DrawRoundedBorder(Control control, Graphics graphics, Color borderColor, int radius, float penWidth = 1F)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(borderColor, penWidth);
        var rect = new Rectangle(0, 0, control.Width - 1, control.Height - 1);
        using var path = CreateRoundedPath(rect, radius);
        graphics.DrawPath(pen, path);
    }

    public static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(rect.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();

        return path;
    }

    public static Bitmap CreateCenteredIconBackground(string iconPath, Size canvasSize, Point offset)
    {
        using var icon = new Icon(iconPath);
        using var iconBitmap = icon.ToBitmap();
        var canvas = new Bitmap(canvasSize.Width, canvasSize.Height);

        using var graphics = Graphics.FromImage(canvas);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var availableWidth = Math.Max(1, canvasSize.Width - 8);
        var availableHeight = Math.Max(1, canvasSize.Height - 8);
        var scale = Math.Min(availableWidth / (float)iconBitmap.Width, availableHeight / (float)iconBitmap.Height);
        var drawWidth = Math.Max(1, (int)Math.Round(iconBitmap.Width * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(iconBitmap.Height * scale));
        var drawX = ((canvasSize.Width - drawWidth) / 2) + offset.X;
        var drawY = ((canvasSize.Height - drawHeight) / 2) + offset.Y;

        graphics.DrawImage(iconBitmap, new Rectangle(drawX, drawY, drawWidth, drawHeight));
        return canvas;
    }

    public static string ResolveIconPath(string preferredIconPath, string fallbackIconPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredIconPath) && File.Exists(preferredIconPath))
        {
            return preferredIconPath;
        }

        return !string.IsNullOrWhiteSpace(fallbackIconPath) && File.Exists(fallbackIconPath)
            ? fallbackIconPath
            : string.Empty;
    }
}
