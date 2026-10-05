using FrameShift.Core.AI;
using FrameShift.Core.AI.Ocr;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace FrameShift.Tests;

public sealed class OcrActionTests
{
    [OcrModelFact]
    [InlineData(false, "txt")]
    [InlineData(false, "json")]
    [InlineData(false, "pdf")]
    [InlineData(true, "txt")]
    [InlineData(true, "json")]
    [InlineData(true, "pdf")]
    public async Task ActualActionExportsImagesAndScannedPdfsWithoutOverwrite(bool pdf, string format)
    {
        var root = Environment.GetEnvironmentVariable("FRAMESHIFT_OCR_TEST_MODELS")!;
        var temp = Path.Combine(Path.GetTempPath(), "FrameShift OCR été " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var imagePath = Path.Combine(temp, "façade image.png");
            using (var bitmap = new System.Drawing.Bitmap(1200, 360))
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            using (var font = new System.Drawing.Font("Arial", 42))
            {
                graphics.Clear(System.Drawing.Color.White);
                graphics.DrawString("FrameShift OCR 2026", font, System.Drawing.Brushes.Black, 40, 40);
                graphics.DrawString("Été à Paris - façade", font, System.Drawing.Brushes.Black, 40, 150);
                bitmap.Save(imagePath);
            }

            var input = pdf ? Path.Combine(temp, "document été.pdf") : imagePath;
            if (pdf)
            {
                using var image = OcrEngine.LoadImage(imagePath);
                using var doc = new PdfOcrDocument(null);
                doc.AddImagePage(image);
                doc.AddImagePage(image);
                doc.Save(input, default);
            }

            var suffix = format == "pdf" ? "_searchable.pdf" : format == "json" ? "_ocr.json" : "_text.txt";
            var occupied = Path.Combine(temp, Path.GetFileNameWithoutExtension(input) + suffix);
            File.WriteAllText(occupied, "preserve");
            var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input));
            using var action = new ExtractTextAction(root);
            var settings = new OcrSettings("tiny", format, Pages: pdf ? "2" : "", ForceCpu: true);
            var result = await action.ExecuteAsync(new(input, new AppLogger(), null, settings.ToOptions()), default);
            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.OutputPath);
            Assert.EndsWith("_001" + Path.GetExtension(occupied), result.OutputPath);
            Assert.Equal("preserve", File.ReadAllText(occupied));
            Assert.Equal(before, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input)));
            if (format == "pdf")
            {
                using var doc = new PdfOcrDocument(result.OutputPath);
                Assert.Equal(pdf ? 2 : 1, doc.PageCount);
                var page = doc.ReadText(pdf ? 2 : 1, out _, default);
                Assert.Contains("FrameShift", string.Join(" ", page.Lines.Select(l => l.Text)));
                if (pdf)
                    Assert.Empty(doc.ReadText(1, out _, default).Lines);
                using var rendered = doc.Render(pdf ? 2 : 1, 200, default);
                if (pdf)
                {
                    // Release the output native document before opening a second PDFium document.
                    var pixels = new byte[rendered.Width * rendered.Height * 3];
                    rendered.CopyPixelDataTo(pixels);
                    doc.Dispose();
                    using var original = new PdfOcrDocument(input);
                    using var originalImage = original.Render(2, 200, default);
                    var originalPixels = new byte[originalImage.Width * originalImage.Height * 3];
                    originalImage.CopyPixelDataTo(originalPixels);
                    Assert.Equal(originalPixels, pixels);
                }
            }
            else
            {
                var text = File.ReadAllText(result.OutputPath!);
                Assert.Contains("FrameShift", text);
                if (format == "json")
                {
                    using var parsed = System.Text.Json.JsonDocument.Parse(text);
                    var pages = parsed.RootElement.GetProperty("pages");
                    Assert.Single(pages.EnumerateArray());
                    Assert.Equal(pdf ? 2 : 1, pages[0].GetProperty("pageNumber").GetInt32());
                }
                else if (pdf)
                {
                    Assert.Contains("[Page 2]", text);
                    Assert.DoesNotContain("[Page 1]", text);
                }
            }

            Assert.Empty(Directory.GetDirectories(temp, ".frameshift-*"));
            var cancelled = await action.ExecuteAsync(new(input, new AppLogger(), null, settings.ToOptions()), new CancellationToken(true));
            Assert.True(cancelled.Canceled);
            Assert.Empty(Directory.GetDirectories(temp, ".frameshift-*"));
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Theory]
    [InlineData("tiny")]
    [InlineData("small")]
    public void NestedCacheSafetyRejectsForeignFilesAndDirectories(string model)
    {
        var root = Path.Combine(Path.GetTempPath(), "frameshift cache " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var definition = OcrModelCatalog.Get(model);
            var folder = Path.Combine(root, definition.Folder);
            AiModelStorage.EnsureDirectory(root, folder);
            Directory.CreateDirectory(Path.Combine(folder, "det"));
            Directory.CreateDirectory(Path.Combine(folder, "rec"));
            File.WriteAllText(Path.Combine(folder, "rec/inference.onnx.tmp"), "partial");
            Assert.True(AiModelStorage.CanDeleteOwnedKnownModelDirectory(root, folder));
            var foreign = Path.Combine(folder, "rec/foreign.txt");
            File.WriteAllText(foreign, "preserve");
            Assert.False(AiModelStorage.CanDeleteOwnedKnownModelDirectory(root, folder));
            File.Delete(foreign);
            Directory.CreateDirectory(Path.Combine(folder, "rec/foreign"));
            Assert.False(AiModelStorage.CanDeleteOwnedKnownModelDirectory(root, folder));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
