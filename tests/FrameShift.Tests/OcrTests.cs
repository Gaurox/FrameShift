using System.Drawing.Imaging;
using System.Text.Json;
using FrameShift.Core.AI.Ocr;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Xunit.Abstractions;

namespace FrameShift.Tests;

public sealed class OcrTests
{
    [Theory]
    [InlineData("1-3,5,2", 5, new[] { 1, 2, 3, 5 })]
    [InlineData("2", 4, new[] { 2 })]
    [InlineData("", 3, new[] { 1, 2, 3 })]
    public void PageRangesAreOrderedAndDeduplicated(string range, int count, int[] expected) => Assert.Equal(expected, new OcrSettings(Pages: range).GetPages(count));
    [Theory]
    [InlineData("0")]
    [InlineData("3-1")]
    [InlineData("1,")]
    [InlineData("6")]
    [InlineData("one")]
    public void InvalidPageRangesFail(string range) => Assert.Throws<ArgumentException>(() => new OcrSettings(Pages: range).GetPages(5));
    [Fact]
    public void PdfOcrOnlyIsRejected() => Assert.Throws<ArgumentException>(() => OcrSettings.Parse(new Dictionary<string, string> { ["ocr-format"] = "pdf", ["ocr-text-source"] = "ocr" }));
    [Fact]
    public void CtcKeepsRepeatedCharactersSeparatedByBlankAndUnicode()
    {
        float[] probabilities = [0, 1, 0, 0, 1, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1];
        var result = OcrEngine.Decode(probabilities, 5, 3, ["", "é", "😀"]);
        Assert.Equal("éé😀", result.Text);
        Assert.Equal(1, result.Confidence);
    }

    [Fact]
    public void MinimumRectangleKeepsRotatedGeometry()
    {
        var points = new[]
        {
            new OcrPoint(10, 0),
            new OcrPoint(20, 10),
            new OcrPoint(10, 20),
            new OcrPoint(0, 10),
            new OcrPoint(10, 10)
        };
        var box = OcrGeometry.MinimumRectangle(points);
        Assert.Equal(4, box.Length);
        Assert.InRange(OcrGeometry.Distance(box[0], box[1]), 14.13, 14.15);
    }

    [Fact]
    public void ColumnsAreReadIndependently()
    {
        OcrLine Line(string value, double x, double y) => new(value, 1, [new(x, y), new(x + 50, y), new(x + 50, y + 10), new(x, y + 10)]);
        Assert.Equal(new[] { "L1", "L2", "R1", "R2" }, OcrReadingOrder.Sort([Line("R2", 200, 30), Line("L2", 0, 30), Line("R1", 200, 0), Line("L1", 0, 0)]).Select(l => l.Text));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(.36)] // The same rendered bounds expressed as PDF points.
    [InlineData(1.5)]
    public void RealBrochureKeepsFullWidthSectionsAndReadsWholeColumns(double scale)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "OcrBrochureLayout.json")));
        var lines = fixture.RootElement.GetProperty("Lines").EnumerateArray().Select(item =>
        {
            var left = item.GetProperty("Left").GetDouble() * scale;
            var right = item.GetProperty("Right").GetDouble() * scale;
            var top = item.GetProperty("Top").GetDouble() * scale;
            var bottom = item.GetProperty("Bottom").GetDouble() * scale;
            return new OcrLine(item.GetProperty("Id").GetInt32().ToString(), 1,
                [new(left, top), new(right, top), new(right, bottom), new(left, bottom)]);
        }).Reverse().ToArray();
        var expected = fixture.RootElement.GetProperty("ExpectedOrder").EnumerateArray().Select(id => id.GetInt32().ToString()).ToArray();

        Assert.Equal(expected, OcrReadingOrder.Sort(lines).Select(line => line.Text));
    }

    [Fact]
    public void NarrowColumnsSurroundedByFullWidthTextStayTogether()
    {
        OcrLine Line(string text, double left, double right, double top) => new(text, 1,
            [new(left, top), new(right, top), new(right, top + 10), new(left, top + 10)]);
        var lines = new[]
        {
            Line("Title", 0, 200, 0), Line("Introduction", 0, 200, 18),
            Line("L1", 0, 95, 36), Line("R1", 104, 200, 36),
            Line("L2", 0, 95, 50), Line("R2", 104, 200, 50),
            Line("L3", 0, 95, 64), Line("R3", 104, 200, 64),
            Line("Footer", 0, 200, 82)
        };

        Assert.Equal(new[] { "Title", "Introduction", "L1", "L2", "L3", "R1", "R2", "R3", "Footer" },
            OcrReadingOrder.Sort(lines.Reverse().ToArray()).Select(line => line.Text));
    }

    [Fact]
    public void IndentedSingleColumnParagraphsKeepTheirLineOrder()
    {
        OcrLine Line(string text, double left, double right, double top) => new(text, 1,
            [new(left, top), new(right, top), new(right, top + 10), new(left, top + 10)]);
        var lines = new[]
        {
            Line("First paragraph", 10, 200, 0), Line("First continuation", 0, 175, 14),
            Line("Second paragraph", 10, 200, 37), Line("Second continuation", 0, 160, 51)
        };

        Assert.Equal(lines.Select(line => line.Text), OcrReadingOrder.Sort(lines.Reverse().ToArray()).Select(line => line.Text));
    }

    [Fact]
    public void SearchablePdfRetainsUnicodeAndAppearance()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FrameShift-OCR-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "été searchable.pdf");
            using (var image = new Image<Rgb24>(900, 300, new Rgb24(255, 255, 255)))
            using (var doc = new PdfOcrDocument(null))
            {
                doc.AddImagePage(image);
                doc.AddText(new(1, 900, 300, "pixels", [new("Été à Paris, façade œuf 😀", .98f, [new(50, 50), new(800, 50), new(800, 110), new(50, 110)])]), default);
                doc.Save(path, default);
            }

            using (var doc = new PdfOcrDocument(path))
            {
                var text = doc.ReadText(1, out var hasImages, default);
                Assert.True(hasImages);
                Assert.Contains("Été à Paris, façade œuf 😀", string.Join(" ", text.Lines.Select(l => l.Text)));
                using var render = doc.Render(1, 200, default);
                Assert.Equal(new Rgb24(255, 255, 255), render[render.Width / 2, render.Height / 2]);
                Assert.Single(text.Lines);
                Assert.InRange(text.Lines[0].Left, 11, 13);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void VeryTallPagesHaveBoundedReadingOrderAndRetainEveryLine()
    {
        var lines = Enumerable.Range(0, 5000).Reverse().Select(i => new OcrLine(i.ToString(), 1,
            [new(0, i * 50), new(100, i * 50), new(100, i * 50 + 10), new(0, i * 50 + 10)])).ToArray();
        var ordered = OcrReadingOrder.Sort(lines);
        Assert.Equal(5000, ordered.Count);
        Assert.Equal(Enumerable.Range(0, 5000).Select(i => i.ToString()), ordered.Select(l => l.Text));
    }
}

public sealed class OcrModelFactAttribute : TheoryAttribute
{
    public OcrModelFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FRAMESHIFT_OCR_TEST_MODELS")))
            Skip = "Set FRAMESHIFT_OCR_TEST_MODELS to run real-model qualification.";
    }
}

public sealed class OcrModelTests(ITestOutputHelper output)
{
    [OcrModelFact]
    [InlineData("tiny", true)]
    [InlineData("small", true)]
    [InlineData("tiny", false)]
    [InlineData("small", false)]
    public void ReadsPrintedTextUsingActualModels(string variant, bool cpu)
    {
        var root = Environment.GetEnvironmentVariable("FRAMESHIFT_OCR_TEST_MODELS")!;
        var directory = Path.Combine(root, $"pp-ocrv6-{variant}-onnx");
        var temp = Path.Combine(Path.GetTempPath(), "FrameShift OCR été " + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var bitmap = new System.Drawing.Bitmap(1200, 360))
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            using (var font = new System.Drawing.Font("Arial", 42))
            {
                graphics.Clear(System.Drawing.Color.White);
                graphics.DrawString("FrameShift OCR 2026", font, System.Drawing.Brushes.Black, 40, 40);
                graphics.DrawString("Été à Paris - façade", font, System.Drawing.Brushes.Black, 40, 150);
                bitmap.Save(temp, ImageFormat.Png);
            }

            Assert.True(OcrModelCatalog.IsReady(OcrModelCatalog.Get(variant), directory));
            var timer = System.Diagnostics.Stopwatch.StartNew();
            using var engine = new OcrEngine(OcrModelCatalog.Get(variant), cpu, directory);
            using var image = OcrEngine.LoadImage(temp);
            var lines = engine.Recognize(image, 0, null, default);
            var text = string.Join("\n", lines.Select(l => l.Text));
            output.WriteLine($"model={variant}, requestedCpu={cpu}, provider={engine.Provider}, elapsed={timer.Elapsed}, text={text}");
            Assert.Contains("FrameShift", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("2026", text);
            Assert.Contains("Paris", text);
            if (cpu)
                Assert.Equal("CPU", engine.Provider);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
