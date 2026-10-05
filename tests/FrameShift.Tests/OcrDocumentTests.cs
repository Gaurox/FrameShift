using System.Globalization;
using System.Text;
using System.Text.Json;
using FrameShift.Core.AI.Ocr;
using FrameShift.Core.Logging;
using FrameShift.Core.Progress;
using FrameShift.Windows.AI;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace FrameShift.Tests;

public sealed class OcrDocumentTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FrameShift OCR été " + Guid.NewGuid().ToString("N"));
    public OcrDocumentTests() => Directory.CreateDirectory(_directory);
    [Theory]
    [InlineData("txt")]
    [InlineData("json")]
    [InlineData("pdf")]
    public async Task NativePdfTextNeedsNoDownloadedModels(string format)
    {
        var input = Path.Combine(_directory, "native été.pdf");
        WriteNativePdf(input);
        var settings = new OcrSettings(Format: format);
        Assert.False(OcrPreparation.NeedsModel([input], settings, default));
        Assert.True(OcrPreparation.NeedsModel([input], settings with { OcrOnly = true }, default));
        using var action = new ExtractTextAction(Path.Combine(_directory, "missing models"));
        var result = await action.ExecuteAsync(new(input, new AppLogger(), null, settings.ToOptions()), default);
        Assert.True(result.Success, result.Message);
        if (format == "pdf")
        {
            using var doc = new PdfOcrDocument(result.OutputPath);
            Assert.Contains("Native FrameShift text", doc.ReadText(1, out _, default).Lines.Select(l => l.Text));
        }
        else
        {
            Assert.Contains("Native FrameShift text", File.ReadAllText(result.OutputPath!));
            if (format == "json")
            {
                using var json = JsonDocument.Parse(File.ReadAllText(result.OutputPath!));
                Assert.Equal("native PDF text", json.RootElement.GetProperty("provider").GetString());
                Assert.Equal("native", json.RootElement.GetProperty("pages")[0].GetProperty("lines")[0].GetProperty("source").GetString());
            }
        }
    }

    [OcrModelFact]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task ManualRotationMapsTextBackToTheOriginalImage(int rotation)
    {
        var input = PrintedImage();
        using (var image = OcrEngine.LoadImage(input))
        {
            image.Mutate(c => c.Rotate((RotateMode)(360 - rotation)));
            image.SaveAsPng(input);
        }

        using var action = new ExtractTextAction(ModelsRoot);
        var result = await action.ExecuteAsync(new(input, new AppLogger(), null, new OcrSettings("tiny", "pdf", Rotation: rotation, ForceCpu: true).ToOptions()), default);
        Assert.True(result.Success, result.Message);
        using var doc = new PdfOcrDocument(result.OutputPath);
        var page = doc.ReadText(1, out _, default);
        Assert.Contains("FrameShift", string.Join(" ", page.Lines.Select(l => l.Text)));
        Assert.All(page.Lines, line =>
        {
            Assert.InRange(line.Left, 0, page.Width);
            Assert.InRange(line.Right, 0, page.Width);
            Assert.InRange(line.Top, 0, page.Height);
            Assert.InRange(line.Bottom, 0, page.Height);
        });
    }

    [OcrModelFact]
    [InlineData(0)]
    [InlineData(90)]
    public async Task CroppedAndRotatedPdfKeepsItsAppearanceAndAlignedSearchLayer(int rotation)
    {
        var input = Path.Combine(_directory, "cropped été.pdf");
        using (var image = OcrEngine.LoadImage(PrintedImage()))
        using (var doc = new PdfOcrDocument(null))
        {
            doc.AddImagePage(image);
            doc.Save(input, default);
        }

        using (var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify))
        {
            var page = doc.Pages[0];
            page.CropBox = new PdfRectangle(new PdfSharp.Drawing.XRect(3, 3, page.Width.Point - 6, page.Height.Point - 6));
            page.Rotate = rotation;
            doc.Save(input);
        }

        byte[] originalPixels;
        using (var doc = new PdfOcrDocument(input))
        using (var image = doc.Render(1, 200, default))
        {
            originalPixels = new byte[image.Width * image.Height * 3];
            image.CopyPixelDataTo(originalPixels);
        }

        using var action = new ExtractTextAction(ModelsRoot);
        var settings = new OcrSettings("tiny", "pdf", Rotation: (360 - rotation) % 360, ForceCpu: true);
        var result = await action.ExecuteAsync(new(input, new AppLogger(), null, settings.ToOptions()), default);
        Assert.True(result.Success, result.Message);
        using var output = new PdfOcrDocument(result.OutputPath);
        var text = output.ReadText(1, out _, default);
        Assert.Contains("FrameShift", string.Join(" ", text.Lines.Select(l => l.Text)));
        Assert.All(text.Lines, line =>
        {
            Assert.InRange(line.Left, 0, text.Width);
            Assert.InRange(line.Top, 0, text.Height);
            Assert.InRange(line.Right, 0, text.Width);
            Assert.InRange(line.Bottom, 0, text.Height);
        });
        using var rendered = output.Render(1, 200, default);
        var pixels = new byte[rendered.Width * rendered.Height * 3];
        rendered.CopyPixelDataTo(pixels);
        Assert.Equal(originalPixels, pixels);
    }

    [OcrModelFact]
    [InlineData("json")]
    public async Task MixedPdfDoesNotDuplicateItsExistingText(string format)
    {
        var input = Path.Combine(_directory, "mixed été.pdf");
        using (var image = OcrEngine.LoadImage(PrintedImage()))
        using (var doc = new PdfOcrDocument(null))
        {
            doc.AddImagePage(image);
            doc.AddText(new(1, image.Width, image.Height, "pixels", [new("FrameShift OCR 2026", 1, [new(40, 40), new(1000, 40), new(1000, 120), new(40, 120)])]), default);
            doc.Save(input, default);
        }

        using var action = new ExtractTextAction(ModelsRoot);
        var result = await action.ExecuteAsync(new(input, new AppLogger(), null, new OcrSettings("tiny", format, ForceCpu: true).ToOptions()), default);
        Assert.True(result.Success, result.Message);
        using var json = JsonDocument.Parse(File.ReadAllText(result.OutputPath!));
        var lines = json.RootElement.GetProperty("pages")[0].GetProperty("lines").EnumerateArray().ToArray();
        Assert.Single(lines, l => l.GetProperty("text").GetString()!.Contains("FrameShift"));
        Assert.Contains(lines, l => l.GetProperty("source").GetString() == "ocr" && l.GetProperty("text").GetString()!.Contains("Paris"));
        var native = Path.Combine(_directory, "native.pdf");
        WriteNativePdf(native);
        result = await action.ExecuteAsync(new(native, new AppLogger(), null, new OcrSettings("tiny", "json", ForceCpu: true).ToOptions()), default);
        Assert.True(result.Success, result.Message);
        using var nativeJson = JsonDocument.Parse(File.ReadAllText(result.OutputPath!));
        Assert.Equal("native PDF text", nativeJson.RootElement.GetProperty("provider").GetString());
    }

    [OcrModelFact]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringInferenceOrBeforePdfPublicationCleansPartialOutput(bool whileWriting)
    {
        var input = PrintedImage();
        using var cancellation = new CancellationTokenSource();
        var reporter = new CancelReporter(message =>
        {
            if (message.Contains(whileWriting ? "Writing searchable PDF" : "Detecting text"))
                cancellation.Cancel();
        });
        using var action = new ExtractTextAction(ModelsRoot);
        var result = await action.ExecuteAsync(new(input, new AppLogger(), reporter, new OcrSettings("tiny", "pdf", ForceCpu: true).ToOptions()), cancellation.Token);
        Assert.True(result.Canceled, result.Message);
        Assert.False(File.Exists(Path.Combine(_directory, "sample été_searchable.pdf")));
        Assert.Empty(Directory.GetDirectories(_directory, ".frameshift-*"));
        // The same sessions remain usable for the next queue item after cancellation.
        var next = await action.ExecuteAsync(new(input, new AppLogger(), null, new OcrSettings("tiny", "txt", ForceCpu: true).ToOptions()), default);
        Assert.True(next.Success, next.Message);
    }

    private static string ModelsRoot => Environment.GetEnvironmentVariable("FRAMESHIFT_OCR_TEST_MODELS")!;

    private string PrintedImage()
    {
        var path = Path.Combine(_directory, "sample été.png");
        using var image = new System.Drawing.Bitmap(1200, 360);
        using var graphics = System.Drawing.Graphics.FromImage(image);
        using var font = new System.Drawing.Font("Arial", 42);
        graphics.Clear(System.Drawing.Color.White);
        graphics.DrawString("FrameShift OCR 2026", font, System.Drawing.Brushes.Black, 40, 40);
        graphics.DrawString("Été à Paris - façade", font, System.Drawing.Brushes.Black, 40, 150);
        image.Save(path);
        return path;
    }

    private static void WriteNativePdf(string path)
    {
        const string text = "BT /F1 20 Tf 30 120 Td (Native FrameShift text) Tj ET";
        string[] objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", $"<< /Length {text.Length} >>\nstream\n{text}\nendstream"];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
    }

    private sealed class CancelReporter(Action<string> onProgress) : IProgressReporter
    {
        public bool IsCancellationRequested => false;

        public bool IsQueueItemRemovalRequested(string inputPath) => false;
        public bool IsQueueItemCancellationRequested(string inputPath) => false;
        public void ReportQueue(IReadOnlyList<string> items)
        {
        }

        public void ReportProgress(int progressValue, string? currentFile, string? currentAction, string? message, string? etaText = null) => onProgress(message ?? "");
        public void ReportState(string state, string? message = null)
        {
        }

        public void ReportQueueItem(string currentFile, string state, string? message = null)
        {
        }
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
