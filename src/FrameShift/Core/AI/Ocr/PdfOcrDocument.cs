using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using PDFiumCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FrameShift.Core.AI.Ocr;

/// <summary>Owns native PDF handles. PDFium calls are serialized, including metadata probes.</summary>
internal sealed class PdfOcrDocument : IDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _initialized;
    private FpdfDocumentT? _document;
    private bool _disposed;
    public int PageCount => fpdfview.FPDF_GetPageCount(_document!);

    public PdfOcrDocument(string? path, CancellationToken token = default)
    {
        Gate.Wait(token);
        try
        {
            if (!_initialized)
            {
                fpdfview.FPDF_InitLibrary();
                _initialized = true;
            }

            _document = path is null ? fpdf_edit.FPDF_CreateNewDocument() : fpdfview.FPDF_LoadDocument(path, null!);
            if (_document is null)
                throw new InvalidDataException(fpdfview.FPDF_GetLastError() == 4 ? "This PDF requires a password. Save an unlocked copy before extracting text." : "Cannot open the PDF. The file may be damaged or unsupported.");
        }
        catch
        {
            Gate.Release();
            throw;
        }
    }

    private FpdfPageT LoadPage(int number) => fpdfview.FPDF_LoadPage(_document!, number - 1) ?? throw new InvalidDataException($"Cannot open PDF page {number}.");
    public OcrPage ReadText(int number, out bool hasImages, CancellationToken token)
    {
        var page = LoadPage(number);
        try
        {
            var width = fpdfview.FPDF_GetPageWidth(page);
            var height = fpdfview.FPDF_GetPageHeight(page);
            hasImages = false;
            for (var i = 0; i < fpdf_edit.FPDFPageCountObjects(page); i++)
                if (ContainsImage(fpdf_edit.FPDFPageGetObject(page, i), 0))
                {
                    hasImages = true;
                    break;
                }

            var text = fpdf_text.FPDFTextLoadPage(page);
            if (text is null)
                return new(number, width, height, "points", []);
            try
            {
                var lines = new List<OcrLine>();
                var builder = new StringBuilder();
                var points = new List<OcrPoint>();
                void Flush()
                {
                    var value = builder.ToString().Trim();
                    if (value.Length > 0 && points.Count > 0)
                    {
                        var l = points.Min(p => p.X);
                        var r = points.Max(p => p.X);
                        var t = points.Min(p => p.Y);
                        var b = points.Max(p => p.Y);
                        lines.Add(new(value, null, [new(l, t), new(r, t), new(r, b), new(l, b)], "native"));
                    }

                    builder.Clear();
                    points.Clear();
                }

                var count = fpdf_text.FPDFTextCountChars(text);
                if (count > 2_000_000)
                    throw new InvalidDataException("This PDF page contains too many characters.");
                for (var i = 0; i < count; i++)
                {
                    if ((i & 1023) == 0)
                        token.ThrowIfCancellationRequested();
                    var code = fpdf_text.FPDFTextGetUnicode(text, i);
                    if (code is 10 or 13)
                    {
                        Flush();
                        continue;
                    }

                    if (code is >= 0xd800 and <= 0xdbff && i + 1 < count)
                    {
                        var low = fpdf_text.FPDFTextGetUnicode(text, i + 1);
                        if (low is >= 0xdc00 and <= 0xdfff)
                        {
                            code = (uint)char.ConvertToUtf32((char)code, (char)low);
                            i++;
                        }
                    }

                    if (code == 0 || code > 0x10ffff || code is >= 0xd800 and <= 0xdfff)
                        continue;
                    double l = 0, r = 0, t = 0, b = 0;
                    var boxed = fpdf_text.FPDFTextGetCharBox(text, i, ref l, ref r, ref b, ref t) != 0;
                    if (boxed && r > l && t > b)
                    {
                        points.Add(ToDisplay(page, new(l, t), width, height));
                        points.Add(ToDisplay(page, new(r, b), width, height));
                    }

                    builder.Append(char.ConvertFromUtf32((int)code));
                }

                Flush();
                return new(number, width, height, "points", OcrReadingOrder.Sort(lines));
            }
            finally
            {
                fpdf_text.FPDFTextClosePage(text);
            }
        }
        finally
        {
            fpdfview.FPDF_ClosePage(page);
        }
    }

    private static bool ContainsImage(FpdfPageobjectT obj, int depth)
    {
        var type = fpdf_edit.FPDFPageObjGetType(obj);
        if (type == 3)
            return true;
        if (type != 5 || depth >= 16)
            return false;
        for (var i = 0; i < fpdf_edit.FPDFFormObjCountObjects(obj); i++)
            if (ContainsImage(fpdf_edit.FPDFFormObjGetObject(obj, (ulong)i), depth + 1))
                return true;
        return false;
    }

    private static OcrPoint ToDisplay(FpdfPageT page, OcrPoint p, double width, double height)
    {
        int x = 0, y = 0;
        Require(fpdfview.FPDF_PageToDevice(page, 0, 0, (int)Math.Round(width * 1000), (int)Math.Round(height * 1000), 0, p.X, p.Y, ref x, ref y), "Cannot map PDF text coordinates.");
        return new(x / 1000d, y / 1000d);
    }

    private static OcrPoint ToPage(FpdfPageT page, OcrPoint p, double width, double height)
    {
        double x = 0, y = 0;
        Require(fpdfview.FPDF_DeviceToPage(page, 0, 0, (int)Math.Round(width * 1000), (int)Math.Round(height * 1000), 0, (int)Math.Round(p.X * 1000), (int)Math.Round(p.Y * 1000), ref x, ref y), "Cannot map OCR text coordinates.");
        return new(x, y);
    }

    public Image<Rgb24> Render(int number, int dpi, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var page = LoadPage(number);
        try
        {
            var w = (int)Math.Ceiling(fpdfview.FPDF_GetPageWidth(page) * dpi / 72);
            var h = (int)Math.Ceiling(fpdfview.FPDF_GetPageHeight(page) * dpi / 72);
            if (w <= 0 || h <= 0 || (long)w * h > 32_000_000)
                throw new InvalidDataException("This PDF page is too large to render. Use Standard quality or a smaller page.");
            var bitmap = fpdfview.FPDFBitmapCreateEx(w, h, 4, IntPtr.Zero, 0) ?? throw new OutOfMemoryException("Cannot allocate the PDF page image.");
            try
            {
                Require(fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, w, h, 0xffffffff), "Cannot initialize the PDF page image.");
                fpdfview.FPDF_RenderPageBitmap(bitmap, page, 0, 0, w, h, 0, 1);
                token.ThrowIfCancellationRequested();
                var stride = fpdfview.FPDFBitmapGetStride(bitmap);
                var bytes = new byte[stride * h];
                Marshal.Copy(fpdfview.FPDFBitmapGetBuffer(bitmap), bytes, 0, bytes.Length);
                var image = new Image<Rgb24>(w, h);
                image.ProcessPixelRows(rows =>
                {
                    for (var y = 0; y < h; y++)
                    {
                        var row = rows.GetRowSpan(y);
                        for (var x = 0; x < w; x++)
                        {
                            var index = y * stride + x * 4;
                            row[x] = new(bytes[index + 2], bytes[index + 1], bytes[index]);
                        }
                    }
                });
                return image;
            }
            finally
            {
                fpdfview.FPDFBitmapDestroy(bitmap);
            }
        }
        finally
        {
            fpdfview.FPDF_ClosePage(page);
        }
    }

    public void AddImagePage(Image<Rgb24> image)
    {
        var width = image.Width * 72d / 300;
        var height = image.Height * 72d / 300;
        var page = fpdf_edit.FPDFPageNew(_document!, PageCount, width, height) ?? throw new InvalidDataException("Cannot create the PDF page.");
        var bitmap = fpdfview.FPDFBitmapCreateEx(image.Width, image.Height, 4, IntPtr.Zero, 0) ?? throw new OutOfMemoryException();
        try
        {
            var stride = fpdfview.FPDFBitmapGetStride(bitmap);
            var bytes = new byte[stride * image.Height];
            image.ProcessPixelRows(rows =>
            {
                for (var y = 0; y < image.Height; y++)
                {
                    var row = rows.GetRowSpan(y);
                    for (var x = 0; x < image.Width; x++)
                    {
                        var i = y * stride + x * 4;
                        bytes[i] = row[x].B;
                        bytes[i + 1] = row[x].G;
                        bytes[i + 2] = row[x].R;
                        bytes[i + 3] = 255;
                    }
                }
            });
            Marshal.Copy(bytes, 0, fpdfview.FPDFBitmapGetBuffer(bitmap), bytes.Length);
            var obj = fpdf_edit.FPDFPageObjNewImageObj(_document!) ?? throw new InvalidDataException("Cannot create the PDF image.");
            try
            {
                Require(fpdf_edit.FPDFImageObjSetBitmap(page, 1, obj, bitmap), "Cannot embed the image in the PDF.");
                Require(fpdf_edit.FPDFImageObjSetMatrix(obj, width, 0, 0, height, 0, 0), "Cannot position the PDF image.");
                Require(fpdf_edit.FPDFPageInsertObject(page, obj), "Cannot insert the PDF image.");
                obj = null!;
                Require(fpdf_edit.FPDFPageGenerateContent(page), "Cannot write the PDF page.");
            }
            finally
            {
                if (obj is not null)
                    fpdf_edit.FPDFPageObjDestroy(obj);
            }
        }
        finally
        {
            fpdfview.FPDFBitmapDestroy(bitmap);
            fpdfview.FPDF_ClosePage(page);
        }
    }

    public unsafe void AddText(OcrPage result, CancellationToken token)
    {
        var lines = result.Lines.Where(l => l.Source == "ocr" && !string.IsNullOrWhiteSpace(l.Text)).ToArray();
        if (lines.Length == 0)
            return;
        token.ThrowIfCancellationRequested();
        var page = LoadPage(result.PageNumber);
        try
        {
            var runes = lines.SelectMany(l => l.Text.EnumerateRunes()).Distinct().ToArray();
            if (runes.Length > 65534)
                throw new InvalidDataException("Too many unique characters on this PDF page.");
            var codes = runes.Select((r, i) => (r.Value, Code: (uint)i + 1)).ToDictionary(v => v.Value, v => v.Code);
            var cmap = new StringBuilder("/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> def /CMapName /FrameShiftOCR def /CMapType 2 def 1 begincodespacerange <0000> <FFFF> endcodespacerange\n");
            foreach (var group in runes.Chunk(100))
            {
                cmap.Append(group.Length).Append(" beginbfchar\n");
                foreach (var rune in group)
                    cmap.Append('<').Append(codes[rune.Value].ToString("X4", CultureInfo.InvariantCulture)).Append("> <").Append(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(rune.ToString()))).Append(">\n");
                cmap.Append("endbfchar\n");
            }

            cmap.Append("endcmap CMapName currentdict /CMap defineresource pop end end");
            using var fontStream = typeof(PdfOcrDocument).Assembly.GetManifestResourceStream("FrameShift.Assets.Ocr.glyphless.ttf")!;
            using var memory = new MemoryStream();
            fontStream.CopyTo(memory);
            var fontData = memory.ToArray();
            var glyphMap = new byte[(runes.Length + 1) * 2];
            for (var i = 1; i <= runes.Length; i++)
                glyphMap[i * 2 + 1] = 1;
            FpdfFontT font;
            fixed (byte* f = fontData)
            fixed (byte* m = glyphMap)
                font = fpdf_edit.FPDFTextLoadCidType2Font(_document!, f, (uint)fontData.Length, cmap.ToString(), m, (uint)glyphMap.Length) ?? throw new InvalidDataException("Cannot create the searchable PDF font.");
            try
            {
                foreach (var line in lines)
                {
                    token.ThrowIfCancellationRequested();
                    var points = line.Polygon.Select(p => result.Units == "pixels" ? new OcrPoint(p.X * 72 / 300, p.Y * 72 / 300) : p).ToArray();
                    var width = fpdfview.FPDF_GetPageWidth(page);
                    var height = fpdfview.FPDF_GetPageHeight(page);
                    var p = points.Select(point => ToPage(page, point, width, height)).ToArray();
                    var chars = line.Text.EnumerateRunes().Select(r => codes[r.Value]).ToArray();
                    var obj = fpdf_edit.FPDFPageObjCreateTextObj(_document!, font, 1) ?? throw new InvalidDataException("Cannot create searchable text.");
                    try
                    {
                        Require(fpdf_edit.FPDFTextSetCharcodes(obj, ref chars[0], (ulong)chars.Length), "Cannot encode searchable text.");
                        Require(fpdf_edit.FPDFTextObjSetTextRenderMode(obj, FPDF_TEXT_RENDERMODE.FPDF_TEXTRENDERMODE_INVISIBLE), "Cannot make the OCR layer invisible.");
                        var dx = p[2].X - p[3].X;
                        var dy = p[2].Y - p[3].Y;
                        var vx = p[0].X - p[3].X;
                        var vy = p[0].Y - p[3].Y;
                        fpdf_edit.FPDFPageObjTransform(obj, dx / (chars.Length * .5), dy / (chars.Length * .5), vx, vy, p[3].X, p[3].Y);
                        Require(fpdf_edit.FPDFPageInsertObject(page, obj), "Cannot insert searchable text.");
                        obj = null!;
                    }
                    finally
                    {
                        if (obj is not null)
                            fpdf_edit.FPDFPageObjDestroy(obj);
                    }
                }

                Require(fpdf_edit.FPDFPageGenerateContent(page), "Cannot write the OCR text layer.");
            }
            finally
            {
                fpdf_edit.FPDFFontClose(font);
            }
        }
        finally
        {
            fpdfview.FPDF_ClosePage(page);
        }
    }

    public unsafe void Save(string path, CancellationToken token)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        Exception? callbackError = null;
        PDFiumCore.Delegates.Func_int___IntPtr___IntPtr_ulong callback = (_, data, length) =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (length > int.MaxValue)
                    throw new IOException("PDF output block is too large.");
                output.Write(new ReadOnlySpan<byte>(data.ToPointer(), (int)length));
                return 1;
            }
            catch (Exception ex)
            {
                callbackError = ex;
                return 0;
            }
        };
        using var writer = new FPDF_FILEWRITE_
        {
            Version = 1,
            WriteBlock = callback
        };
        var result = fpdf_save.FPDF_SaveAsCopy(_document!, writer, 2);
        GC.KeepAlive(callback);
        if (callbackError is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(callbackError).Throw();
        Require(result, "Cannot save the searchable PDF.");
        token.ThrowIfCancellationRequested();
    }

    private static void Require(int success, string message)
    {
        if (success == 0)
            throw new InvalidDataException(message);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            if (_document is not null)
                fpdfview.FPDF_CloseDocument(_document);
        }
        finally
        {
            _document = null;
            Gate.Release();
        }
    }
}
