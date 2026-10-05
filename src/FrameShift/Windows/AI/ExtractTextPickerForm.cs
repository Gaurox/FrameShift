using FrameShift.Core.AI.Ocr;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.AI;

internal sealed class ExtractTextPickerForm : Form
{
    private readonly ComboBox _model = Choice("Small — Higher accuracy", "Tiny — Faster processing");
    private readonly ComboBox _format = Choice("Plain text (.txt)", "Searchable PDF (.pdf)", "Structured data (.json)");
    private readonly ComboBox _layout = Choice("Keep line breaks", "Group into paragraphs");
    private readonly ComboBox _source = Choice("Automatic", "OCR only");
    private readonly ComboBox _quality = Choice("Standard (200 DPI)", "High (300 DPI)");
    private readonly ComboBox _rotation = Choice("As stored", "90° clockwise", "180°", "270° clockwise");
    private readonly ComboBox _device = Choice("Automatic (GPU preferred)", "CPU");
    private readonly CheckBox _selectedPages = new()
    {
        Text = "Selected pages",
        AutoSize = true
    };
    private readonly CheckBox _markers = new()
    {
        Text = "Include page markers",
        Checked = true,
        AutoSize = true
    };
    private readonly TextBox _pages = new()
    {
        PlaceholderText = "For example: 1-3,5",
        Enabled = false
    };
    private readonly TextBox _status;
    private readonly Label _modelStatus;
    private readonly Control _pdfSection, _advanced, _layoutRow, _sourceRow, _qualityRow;
    private readonly Button _extract, _cancel;
    private readonly TableLayoutPanel _root;
    private readonly Control _options;
    private readonly Func<IReadOnlyList<string>> _inputs;
    private readonly System.Windows.Forms.Timer _arrivals = new()
    {
        Interval = 250
    };
    private string _sourceSignature = "";
    private readonly CancellationTokenSource _stop = new();
    private Task? _preparation;
    private bool _closingRequested, _loaded;
    public OcrSettings? Selection { get; private set; }

    public ExtractTextPickerForm(Func<IReadOnlyList<string>> inputs, OcrSettings initial)
    {
        _inputs = inputs;
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(600, 430), new Size(380, 280));
        const string title = "FrameShift - Extract Text";
        FrameShiftWindowChrome.Apply(this, title, IconPaths.FrameShiftAiIcon, IconPaths.AppIcon);
        var paths = inputs();
        var source = paths.Count == 1 ? Path.GetFileName(paths[0]) : $"{paths.Count} selected files";
        var header = FrameShiftUiFactory.CreateHeader(title, $"Source: {source}", IconPaths.FrameShiftAiIcon, IconPaths.AppIcon, "AI");
        _arrivals.Tick += (_, _) =>
        {
            var pending = inputs();
            var signature = string.Join("\n", pending);
            if (signature == _sourceSignature)
                return;
            _sourceSignature = signature;
            header.SubtitleLabel.Text = pending.Count == 1 ? $"Source: {Path.GetFileName(pending[0])}" : $"Source: {pending.Count} selected files";
            UpdateVisibility();
        };
        _model.SelectedIndex = initial.Model == "tiny" ? 1 : 0;
        _format.SelectedIndex = initial.Format == "pdf" ? 1 : initial.Format == "json" ? 2 : 0;
        _layout.SelectedIndex = initial.Paragraphs ? 1 : 0;
        _quality.SelectedIndex = initial.Dpi == 300 ? 1 : 0;
        _source.SelectedIndex = initial.OcrOnly ? 1 : 0;
        _rotation.SelectedIndex = initial.Rotation / 90;
        _device.SelectedIndex = initial.ForceCpu ? 1 : 0;
        _pages.Text = initial.Pages;
        _selectedPages.Checked = initial.Pages.Length > 0;
        _pages.Enabled = _selectedPages.Checked;
        _modelStatus = FrameShiftUiFactory.CreateWrappingLabel("");
        var basic = FrameShiftUiFactory.CreateSection("Options", FrameShiftUiFactory.CreateVerticalStack(FrameShiftUiFactory.CreateFieldRow("&Model", _model), _modelStatus, FrameShiftUiFactory.CreateFieldRow("&Output", _format)));
        _pdfSection = FrameShiftUiFactory.CreateSection("PDF pages", FrameShiftUiFactory.CreateVerticalStack(FrameShiftUiFactory.CreateWrappingLabel("All pages are processed unless you select a range. A range applies to each PDF."), _selectedPages, FrameShiftUiFactory.CreateFieldRow("&Pages", _pages)));
        _layoutRow = FrameShiftUiFactory.CreateFieldRow("Text &layout", _layout);
        _sourceRow = FrameShiftUiFactory.CreateFieldRow("PDF text &source", _source);
        _qualityRow = FrameShiftUiFactory.CreateFieldRow("PDF rendering &quality", _quality);
        _advanced = FrameShiftUiFactory.CreateSection("Advanced options", FrameShiftUiFactory.CreateVerticalStack(_layoutRow, _markers, _sourceRow, _qualityRow, FrameShiftUiFactory.CreateFieldRow("Recognition &rotation", _rotation), FrameShiftUiFactory.CreateFieldRow("&Processing", _device)));
        _advanced.Visible = false;
        var expand = new CheckBox
        {
            Text = "Advanced options",
            AutoSize = true
        };
        expand.CheckedChanged += (_, _) =>
        {
            _advanced.Visible = expand.Checked;
            Fit();
        };
        _status = FrameShiftUiFactory.CreateStatusMessage("Processed locally. Saves a new file next to each source.");
        _cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        _cancel.DialogResult = DialogResult.Cancel;
        _extract = FrameShiftUiFactory.CreateMeasuredActionButton("Extract Text", true);
        _extract.AccessibleName = "Extract Text";
        _extract.Click += (_, _) =>
        {
            if (_preparation is null || _preparation.IsCompleted)
                _preparation = PrepareAsync();
        };
        _options = FrameShiftUiFactory.CreateVerticalStack(basic, _pdfSection, expand, _advanced);
        _root = FrameShiftDialogLayout.Create(header, _options, FrameShiftDialogLayout.CreateActions(_cancel, _extract), _status);
        Controls.Add(_root);
        AcceptButton = _extract;
        CancelButton = _cancel;
        _selectedPages.CheckedChanged += (_, _) => _pages.Enabled = _selectedPages.Checked;
        _format.SelectedIndexChanged += (_, _) => UpdateVisibility();
        _model.SelectedIndexChanged += (_, _) => UpdateModelStatus();
        Load += (_, _) =>
        {
            _loaded = true;
            UpdateModelStatus();
            UpdateVisibility();
            _arrivals.Start();
        };
        Shown += (_, _) => Fit();
        FormClosing += OnClosing;
        ResumeLayout(true);
    }

    private static ComboBox Choice(params string[] values)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill
        };
        combo.Items.AddRange(values);
        combo.SelectedIndex = 0;
        return combo;
    }

    private void UpdateVisibility()
    {
        var pdf = _inputs().Any(p => Path.GetExtension(p).Equals(".pdf", StringComparison.OrdinalIgnoreCase));
        _pdfSection.Visible = pdf;
        _layoutRow.Visible = _format.SelectedIndex == 0;
        _markers.Visible = pdf && _format.SelectedIndex == 0;
        _sourceRow.Visible = pdf;
        _source.Enabled = _format.SelectedIndex != 1;
        if (!_source.Enabled)
            _source.SelectedIndex = 0;
        _qualityRow.Visible = pdf;
        Fit();
    }

    private void UpdateModelStatus()
    {
        var model = OcrModelCatalog.Get(_model.SelectedIndex == 1 ? "tiny" : "small");
        _modelStatus.Text = OcrModelCatalog.IsPresent(model) ? "Downloaded — integrity is checked before processing." : FormattableString.Invariant($"Download required for OCR: approximately {model.SizeBytes / 1_000_000d:0.0} MB · Apache-2.0");
    }

    private void Fit()
    {
        if (!_loaded || WindowState != FormWindowState.Normal)
            return;
        var area = Screen.FromControl(this).WorkingArea;
        FrameShiftDialogLayout.FitInitialHeight(this, _root, area.Size);
        Bounds = FrameShiftWindowPolicy.FitBounds(Bounds, area);
    }

    private async Task PrepareAsync()
    {
        _extract.Enabled = false;
        _options.Enabled = false;
        try
        {
            var settings = new OcrSettings(_model.SelectedIndex == 1 ? "tiny" : "small", _format.SelectedIndex == 1 ? "pdf" : _format.SelectedIndex == 2 ? "json" : "txt", _selectedPages.Checked ? _pages.Text.Trim() : "", _layout.SelectedIndex == 1, _markers.Checked, _source.SelectedIndex == 1, _quality.SelectedIndex == 1 ? 300 : 200, _rotation.SelectedIndex * 90, _device.SelectedIndex == 1);
            settings = OcrSettings.Parse(settings.ToOptions());
            var paths = _inputs().ToArray();
            _status.Text = "Checking inputs, PDF pages and model files...";
            var model = OcrModelCatalog.Get(settings.Model);
            var ready = await Task.Run(() => OcrModelCatalog.IsReady(model, token: _stop.Token), _stop.Token);
            if (!ready)
                ready = !await Task.Run(() => OcrPreparation.NeedsModel(paths, settings, _stop.Token, paths.Length > 1), _stop.Token);
            _stop.Token.ThrowIfCancellationRequested();
            if (!ready)
            {
                using var download = new DownloadModelForm("FrameShift AI - Extract Text", $"Download {model.DisplayName} for local OCR", IconPaths.FrameShiftAiIcon, model.DisplayName, "Apache-2.0", model.SizeBytes, (progress, ct) => OcrModelDownloader.DownloadAsync(model, progress, ct));
                if (download.ShowDialog(this) != DialogResult.OK)
                {
                    _status.Text = "Download canceled. Choose a downloaded model or try again.";
                    return;
                }
            }

            var preferences = FrameShiftUiSettings.Load();
            preferences.OcrModel = settings.Model;
            preferences.OcrFormat = settings.Format;
            preferences.OcrParagraphs = settings.Paragraphs;
            preferences.OcrDpi = settings.Dpi;
            preferences.Save();
            Selection = settings;
            // Finish the async preparation before the posted close reaches FormClosing.
            BeginInvoke((Action)(() =>
            {
                DialogResult = DialogResult.OK;
                Close();
            }));
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Canceled.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            if (!_closingRequested && !IsDisposed)
            {
                _extract.Enabled = true;
                _options.Enabled = true;
            }
        }
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_preparation is { IsCompleted: false })
        {
            e.Cancel = true;
            if (_closingRequested)
                return;
            _closingRequested = true;
            _stop.Cancel();
            _cancel.Enabled = false;
            _status.Text = "Canceling...";
            await _preparation;
            BeginInvoke((Action)(() =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _arrivals.Dispose();
            _stop.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal static class OcrPreparation
{
    public static bool NeedsModel(IReadOnlyList<string> paths, OcrSettings settings, CancellationToken token, bool tolerateInvalidFiles = false)
    {
        var needed = false;
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!ExtractTextAction.Supports(Path.GetExtension(path)))
                    continue;
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Input not found: {Path.GetFileName(path)}");
                if (!Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    needed = true;
                    continue;
                }

                using var pdf = new PdfOcrDocument(path, token);
                var pages = settings.GetPages(pdf.PageCount);
                if (needed)
                    continue;
                foreach (var number in pages)
                {
                    var text = pdf.ReadText(number, out var images, token);
                    if (settings.OcrOnly || images || text.Lines.Count == 0)
                    {
                        needed = true;
                        break;
                    }
                }
            }
            catch (Exception ex) when (tolerateInvalidFiles && ex is not OperationCanceledException)
            {
                Core.Logging.AppLogger.LogStatic($"OCR preflight: deferred file error for '{path}': {ex.Message}");
            }
        }

        return needed;
    }
}
