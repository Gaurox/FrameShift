using System.Globalization;
using FrameShift.Core.Actions;

namespace FrameShift.Core.AI.Ocr;

internal sealed record OcrSettings(string Model = "small", string Format = "txt", string Pages = "", bool Paragraphs = false, bool PageMarkers = true, bool OcrOnly = false, int Dpi = 200, int Rotation = 0, bool ForceCpu = false)
{
    internal static readonly string[] OptionNames = ["ocr-model", "ocr-format", "ocr-pages", "ocr-layout", "ocr-page-markers", "ocr-text-source", "ocr-dpi", "ocr-rotation", "ocr-device"];
    public static OcrSettings Parse(IReadOnlyDictionary<string, string>? options)
    {
        string Get(string key, string fallback) => options?.TryGetValue(key, out var value) == true ? value.Trim().ToLowerInvariant() : fallback;
        string Choice(string key, string fallback, params string[] allowed)
        {
            var value = Get(key, fallback);
            if (!allowed.Contains(value))
                throw new ArgumentException(L.Format("OcrSettings_Invalid_0_Expected_1_e2ffe983",key,string.Join(", ", allowed)));
            return value;
        }

        var result = new OcrSettings(Choice("ocr-model", "small", "small", "tiny"), Choice("ocr-format", "txt", "txt", "pdf", "json"), Get("ocr-pages", ""), Choice("ocr-layout", "lines", "lines", "paragraphs") == "paragraphs", Choice("ocr-page-markers", "true", "true", "false") == "true", Choice("ocr-text-source", "auto", "auto", "ocr") == "ocr", int.Parse(Choice("ocr-dpi", "200", "200", "300"), CultureInfo.InvariantCulture), int.Parse(Choice("ocr-rotation", "0", "0", "90", "180", "270"), CultureInfo.InvariantCulture), Choice("ocr-device", "auto", "auto", "cpu") == "cpu");
        if (result.Format == "pdf" && result.OcrOnly)
            throw new ArgumentException(L.Get("OcrSettings_OCR_only_is_available_for_TXT_and_JS_f2991b47"));
        // Validate syntax without silently clipping ranges to a particular document.
        if (result.Pages.Length > 0)
            result.GetPages(int.MaxValue);
        return result;
    }

    public Dictionary<string, string> ToOptions() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["ocr-model"] = Model,
        ["ocr-format"] = Format,
        ["ocr-pages"] = Pages,
        ["ocr-layout"] = Paragraphs ? "paragraphs" : "lines",
        ["ocr-page-markers"] = PageMarkers ? "true" : "false",
        ["ocr-text-source"] = OcrOnly ? "ocr" : "auto",
        ["ocr-dpi"] = Dpi.ToString(CultureInfo.InvariantCulture),
        ["ocr-rotation"] = Rotation.ToString(CultureInfo.InvariantCulture),
        ["ocr-device"] = ForceCpu ? "cpu" : "auto"
    };
    public int[] GetPages(int pageCount)
    {
        if (pageCount < 1)
            throw new InvalidDataException(L.Get("OcrSettings_The_PDF_contains_no_pages_9f46aba9"));
        if (string.IsNullOrWhiteSpace(Pages))
            return Enumerable.Range(1, pageCount).ToArray();
        if (Pages.Length > 1024)
            throw new ArgumentException(L.Get("OcrSettings_The_page_selection_is_too_long_56bf7a7a"));
        var selected = new SortedSet<int>();
        foreach (var token in Pages.Split(','))
        {
            var ends = token.Trim().Split('-');
            if (ends.Length is < 1 or > 2 || !int.TryParse(ends[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var first))
                throw new ArgumentException(L.Get("OcrSettings_Invalid_page_selection_Use_numbers_a_6e6a4f34"));
            var last = first;
            if (ends.Length == 2 && !int.TryParse(ends[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out last))
                throw new ArgumentException(L.Get("OcrSettings_Invalid_page_range_Use_a_range_such__cd9b8471"));
            if (first < 1 || last < first || last > pageCount)
                throw new ArgumentException(L.Format("OcrSettings_Page_range_0_is_outside_this_documen_3b4622b1",token.Trim(),pageCount));
            if ((long)last - first + selected.Count > 100_000)
                throw new ArgumentException(L.Get("OcrSettings_Select_at_most_100_000_pages_per_ope_2d262954"));
            for (var page = first;; page++)
            {
                selected.Add(page);
                if (page == last)
                    break;
            }
        }

        return selected.ToArray();
    }
}

internal readonly record struct OcrPoint(double X, double Y);
internal sealed record OcrLine(string Text, float? Confidence, OcrPoint[] Polygon, string Source = "ocr")
{
    public double Left => Polygon.Min(p => p.X);
    public double Top => Polygon.Min(p => p.Y);
    public double Right => Polygon.Max(p => p.X);
    public double Bottom => Polygon.Max(p => p.Y);
}

internal sealed record OcrPage(int PageNumber, double Width, double Height, string Units, IReadOnlyList<OcrLine> Lines);
