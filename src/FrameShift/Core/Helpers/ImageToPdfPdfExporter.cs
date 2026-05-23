using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using FrameShift.Core.Actions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SixLabors.ImageSharp.Formats.Png;

namespace FrameShift.Core.Helpers;

public sealed class ImageToPdfPdfExporter
{
    public void Export(ImageToPdfSettings settings, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var page = ImageToPdfGeometry.GetPageDefinition(
            settings.PageFormat,
            settings.CustomPageWidthCm,
            settings.CustomPageHeightCm);

        using var document = new PdfDocument();
        var pdfPage = document.AddPage();
        pdfPage.Width = XUnit.FromPoint(page.WidthPoints);
        pdfPage.Height = XUnit.FromPoint(page.HeightPoints);

        using var graphics = XGraphics.FromPdfPage(pdfPage);
        foreach (var item in settings.Items)
        {
            if (string.IsNullOrWhiteSpace(item.SourcePath) || !File.Exists(item.SourcePath))
            {
                throw new FileNotFoundException("Source image not found.", item.SourcePath);
            }

            XImage image;
            MemoryStream? cropStream = null;
            Bitmap? croppedBitmap = null;
            try
            {
                var crop = item.GetCrop();
                var hasCrop = !ImageToPdfGeometry.IsDefaultCrop(crop);
                var extension = Path.GetExtension(item.SourcePath).ToLowerInvariant();
                var needsRasterize = hasCrop || extension == ".webp";

                if (needsRasterize)
                {
                    Bitmap sourceBitmap;
                    if (extension == ".webp")
                    {
                        // GDI+ ne lit pas le WebP — on passe par ImageSharp
                        using var webpMs = new MemoryStream();
                        using (var imgSharp = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(item.SourcePath))
                            imgSharp.Save(webpMs, new PngEncoder());
                        webpMs.Position = 0;
                        sourceBitmap = new Bitmap(webpMs);
                    }
                    else
                    {
                        sourceBitmap = new Bitmap(item.SourcePath);
                    }

                    using (sourceBitmap)
                    {
                        Bitmap renderSource = sourceBitmap;
                        if (hasCrop)
                        {
                            var sourceRect = ImageToPdfGeometry.GetBitmapSourceRectFromCrop(sourceBitmap.Size, crop);
                            croppedBitmap = new Bitmap(sourceRect.Width, sourceRect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                            using var cropGraphics = Graphics.FromImage(croppedBitmap);
                            cropGraphics.DrawImage(sourceBitmap, new Rectangle(0, 0, croppedBitmap.Width, croppedBitmap.Height), sourceRect, GraphicsUnit.Pixel);
                            renderSource = croppedBitmap;
                        }
                        cropStream = new MemoryStream();
                        renderSource.Save(cropStream, System.Drawing.Imaging.ImageFormat.Png);
                        cropStream.Position = 0;
                    }
                    image = XImage.FromStream(cropStream);
                }
                else
                {
                    image = XImage.FromFile(item.SourcePath);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException(MediaActionMessages.ImageFileInaccessible(item.SourcePath), ex);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException(MediaActionMessages.ImageFileInaccessible(item.SourcePath), ex);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(MediaActionMessages.ImageInvalid(item.SourcePath), ex);
            }
            catch (ExternalException ex)
            {
                throw new InvalidOperationException(MediaActionMessages.ImageInvalid(item.SourcePath), ex);
            }
            catch (OutOfMemoryException ex)
            {
                throw new InvalidOperationException(MediaActionMessages.ImageInvalid(item.SourcePath), ex);
            }

            try
            {
                using (image)
                {
                    var rect = ImageToPdfGeometry.ToAbsoluteRect(item.ToRectangleF(), page);
                    var angle = ImageToPdfGeometry.NormalizeRotationAngle(item.GetRotationAngleDegrees());
                    var centerX = rect.X + (rect.Width / 2.0);
                    var centerY = rect.Y + (rect.Height / 2.0);
                    var state = graphics.Save();

                    try
                    {
                        graphics.TranslateTransform(centerX, centerY);
                        if (Math.Abs(angle) > 0.001)
                        {
                            graphics.RotateTransform(angle);
                        }

                        graphics.DrawImage(
                            image,
                            -rect.Width / 2.0,
                            -rect.Height / 2.0,
                            rect.Width,
                            rect.Height);
                    }
                    finally
                    {
                        graphics.Restore(state);
                    }
                }
            }
            finally
            {
                croppedBitmap?.Dispose();
                cropStream?.Dispose();
            }
        }

        document.Save(outputPath);
    }
}
