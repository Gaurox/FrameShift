using System.Text;
using System.Text.Json;
using FrameShift.Core.Actions;
using FrameShift.Core.Helpers;
using FrameShift.Core.Progress;

namespace FrameShift.Core.AI.Ocr;

internal sealed class ExtractTextAction : IFrameShiftAction, IDisposable
{
    public static bool Supports(string extension) => extension.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".pdf";
    public ActionDescriptor Descriptor { get; } = new("extract-text", "Extract Text", "Extracts text locally from images and PDFs using PP-OCRv6.");

    private OcrEngine? _engine;
    private string? _engineKey;
    private readonly string? _modelsRoot;
    internal ExtractTextAction(string? modelsRoot = null) => _modelsRoot = modelsRoot;
    public async Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken token)
    {
        if (!File.Exists(request.InputPath))
            return new(false, "The input file does not exist.");
        if (!Supports(Path.GetExtension(request.InputPath)))
            return new(false, "Supported inputs: PNG, JPG, JPEG, WebP, BMP and PDF.");
        using var item = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, item.Token);
        using var monitorStop = new CancellationTokenSource();
        var monitor = MonitorItemCancellationAsync(request.ProgressReporter, request.InputPath, item, monitorStop.Token);
        try
        {
            return await Task.Run(() => Extract(request, OcrSettings.Parse(request.Options), linked.Token), linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(false, "Canceled.", null, true, token.IsCancellationRequested ? CancellationScope.All : CancellationScope.CurrentItem);
        }
        catch (Exception ex)
        {
            request.Logger.Log($"ExtractText: failed. {ex}");
            return new(false, OnnxProviderHelper.GetCleanUserMessage(ex, "Extract Text"));
        }
        finally
        {
            monitorStop.Cancel();
            await monitor.ConfigureAwait(false);
        }
    }

    private ActionExecutionResult Extract(ActionRequest request, OcrSettings settings, CancellationToken token)
    {
        var pdf = Path.GetExtension(request.InputPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        var suffix = settings.Format == "pdf" ? "_searchable.pdf" : settings.Format == "json" ? "_ocr.json" : "_text.txt";
        var desired = Path.Combine(Path.GetDirectoryName(request.InputPath)!, Path.GetFileNameWithoutExtension(request.InputPath) + suffix);
        using var output = OutputOperation.ForFile(desired);
        using var writer = settings.Format == "txt" ? new StreamWriter(output.WorkingPath, false, new UTF8Encoding(false)) : null;
        using var jsonStream = settings.Format == "json" ? new FileStream(output.WorkingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None) : null;
        using var json = jsonStream is null ? null : new Utf8JsonWriter(jsonStream, new JsonWriterOptions { Indented = true });
        if (json is not null)
        {
            json.WriteStartObject();
            json.WriteNumber("schemaVersion", 1);
            json.WriteString("source", Path.GetFileName(request.InputPath));
            json.WriteString("model", settings.Model);
            json.WriteString("modelRevision", OcrModelCatalog.Revision);
            json.WriteString("coordinateSystem", "top-left of the displayed source page");
            json.WriteStartArray("pages");
        }

        var lineCount = 0;
        var lowConfidence = 0;
        var usedOcr = false;
        void WritePage(OcrPage page)
        {
            lineCount += page.Lines.Count;
            lowConfidence += page.Lines.Count(l => l.Confidence < .5f);
            if (writer is not null)
            {
                if (pdf && settings.PageMarkers)
                    writer.WriteLine($"[Page {page.PageNumber}]");
                writer.WriteLine(OcrReadingOrder.Text(page, settings.Paragraphs));
                writer.WriteLine();
            }

            if (json is not null)
                JsonSerializer.Serialize(json, page, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }

        void Report(int percent, string message) => request.ProgressReporter?.ReportProgress(Math.Clamp(percent, 0, 100) * 10, request.InputPath, "Extract Text", message);
        if (pdf)
        {
            using var document = new PdfOcrDocument(request.InputPath, token);
            var pages = settings.GetPages(document.PageCount);
            for (var i = 0; i < pages.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                Report(i * 95 / pages.Length, $"Reading page {pages[i]} of {document.PageCount}");
                var native = document.ReadText(pages[i], out var hasImages, token);
                var lines = settings.OcrOnly ? new List<OcrLine>() : native.Lines.ToList();
                if (settings.OcrOnly || native.Lines.Count == 0 || hasImages)
                {
                    Report(i * 95 / pages.Length, $"Rendering page {pages[i]} of {document.PageCount}");
                    using var image = document.Render(pages[i], settings.Dpi, token);
                    var engine = GetEngine(settings);
                    usedOcr = true;
                    var recognized = engine.Recognize(image, settings.Rotation, (percent, message) => Report((i * 95 + percent * 95 / 100) / pages.Length, $"Page {pages[i]}: {message}"), token);
                    var mapped = recognized.Select(l => l with { Polygon = l.Polygon.Select(p => new OcrPoint(p.X * native.Width / image.Width, p.Y * native.Height / image.Height)).ToArray() });
                    lines.AddRange(mapped.Where(line => settings.OcrOnly || !native.Lines.Any(existing => OcrGeometry.Overlaps(existing, line))));
                }

                var result = native with
                {
                    Lines = OcrReadingOrder.Sort(lines)
                };
                if (settings.Format == "pdf")
                    document.AddText(result, token);
                WritePage(result);
            }

            if (settings.Format == "pdf")
            {
                Report(96, "Writing searchable PDF...");
                document.Save(output.WorkingPath, token);
            }
        }
        else
        {
            using var image = OcrEngine.LoadImage(request.InputPath);
            var engine = GetEngine(settings);
            usedOcr = true;
            var lines = engine.Recognize(image, settings.Rotation, Report, token);
            var result = new OcrPage(1, image.Width, image.Height, "pixels", lines);
            WritePage(result);
            if (settings.Format == "pdf")
            {
                using var document = new PdfOcrDocument(null, token);
                document.AddImagePage(image);
                document.AddText(result, token);
                Report(96, "Writing searchable PDF...");
                document.Save(output.WorkingPath, token);
            }
        }

        if (json is not null)
        {
            json.WriteEndArray();
            json.WriteString("provider", usedOcr ? _engine!.Provider : "native PDF text");
            json.WriteEndObject();
            json.Flush();
        }

        writer?.Flush();
        writer?.Dispose();
        json?.Dispose();
        jsonStream?.Dispose();
        token.ThrowIfCancellationRequested();
        var published = output.Publish(token);
        request.Logger.Log($"ExtractText: saved '{published}', model={settings.Model}, format={settings.Format}, lines={lineCount}, lowConfidence={lowConfidence}.");
        Report(100, $"Saved: {Path.GetFileName(published)}");
        var message = lineCount == 0 ? "File created. No text was detected." : lowConfidence > 0 ? "Text extracted. Some low-confidence text may need review." : "Text extracted.";
        return new(true, message, published);
    }

    private OcrEngine GetEngine(OcrSettings settings)
    {
        var key = $"{settings.Model}:{settings.ForceCpu}";
        if (_engine is not null && _engineKey == key)
            return _engine;
        var model = OcrModelCatalog.Get(settings.Model);
        var directory = _modelsRoot is null ? model.DirectoryPath : Path.Combine(_modelsRoot, model.Folder);
        if (!OcrModelCatalog.IsReady(model, directory))
            throw new InvalidDataException("The OCR model is missing or damaged. Use Extract Text from FrameShift to download or repair it.");
        _engine?.Dispose();
        _engine = null;
        _engineKey = null;
        _engine = new OcrEngine(model, settings.ForceCpu, directory);
        _engineKey = key;
        return _engine;
    }

    internal static async Task MonitorItemCancellationAsync(IProgressReporter? reporter, string path, CancellationTokenSource item, CancellationToken stop)
    {
        if (reporter is null)
            return;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (reporter.IsQueueItemCancellationRequested(path))
                {
                    item.Cancel();
                    return;
                }

                await Task.Delay(100, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        _engine?.Dispose();
        _engine = null;
    }
}
