using FrameShift.Core.AI.Ocr;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.AI;

internal sealed class ExtractTextPickerForm : Form
{
    private readonly ComboBox _model = Choice(L.Get("ExtractTextPickerForm_Small_Higher_accuracy_46116de3"), L.Get("ExtractTextPickerForm_Tiny_Faster_processing_0dc802fc"));
    private readonly ComboBox _format = Choice(L.Get("ExtractTextPickerForm_Plain_text_txt_3673330d"), L.Get("ExtractTextPickerForm_Searchable_PDF_pdf_0ba4dfef"), L.Get("ExtractTextPickerForm_Structured_data_json_c331573d"));
    private readonly ComboBox _layout = Choice(L.Get("ExtractTextPickerForm_Keep_line_breaks_b4a76425"), L.Get("ExtractTextPickerForm_Group_into_paragraphs_3ddcd441"));
    private readonly ComboBox _source = Choice(L.Get("ExtractTextPickerForm_Automatic_70aefbbf"), L.Get("ExtractTextPickerForm_OCR_only_d174d03d"));
    private readonly ComboBox _quality = Choice(L.Get("ExtractTextPickerForm_Standard_200_DPI_95e191aa"), L.Get("ExtractTextPickerForm_High_300_DPI_578f02f2"));
    private readonly ComboBox _rotation = Choice(L.Get("ExtractTextPickerForm_As_stored_0f8dee9f"), L.Get("ExtractTextPickerForm_90_clockwise_2a5703ed"), "180°", L.Get("ExtractTextPickerForm_270_clockwise_e43b3296"));
    private readonly ComboBox _device = Choice(L.Get("ExtractTextPickerForm_Automatic_GPU_preferred_462f0be4"), "CPU");
    private readonly CheckBox _selectedPages = new()
    {
        Text = L.Get("ExtractTextPickerForm_Selected_pages_c5c21ea3"),
        AutoSize = true
    };
    private readonly CheckBox _markers = new()
    {
        Text = L.Get("ExtractTextPickerForm_Include_page_markers_85020a1d"),
        Checked = true,
        AutoSize = true
    };
    private readonly TextBox _pages = new()
    {
        PlaceholderText = L.Get("ExtractTextPickerForm_For_example_1_3_5_a52e0347"),
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
        var title = L.Get("ExtractTextPickerForm_FrameShift_Extract_Text_d831480c");
        FrameShiftWindowChrome.Apply(this, title, IconPaths.FrameShiftAiIcon, IconPaths.AppIcon);
        var paths = inputs();
        var source = paths.Count == 1 ? Path.GetFileName(paths[0]) : L.Format("ExtractTextPickerForm_0_selected_files_dd068a4b",paths.Count);
        var header = FrameShiftUiFactory.CreateHeader(title, L.Format("ExtractTextPickerForm_Source_0_653ab450",source), IconPaths.FrameShiftAiIcon, IconPaths.AppIcon, "AI");
        _arrivals.Tick += (_, _) =>
        {
            var pending = inputs();
            var signature = string.Join("\n", pending);
            if (signature == _sourceSignature)
                return;
            _sourceSignature = signature;
            header.SubtitleLabel.Text = pending.Count == 1 ? L.Format("ExtractTextPickerForm_Source_0_653ab450",Path.GetFileName(pending[0])) : L.Format("ExtractTextPickerForm_Source_0_selected_files_0357c442",pending.Count);
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
        var basic = FrameShiftUiFactory.CreateSection(L.Get("ExtractTextPickerForm_Options_59a2f992"), FrameShiftUiFactory.CreateVerticalStack(FrameShiftUiFactory.CreateFieldRow(L.Get("ExtractTextPickerForm_Model_a53a5cc6"), _model), _modelStatus, FrameShiftUiFactory.CreateFieldRow(L.Get("ExtractTextPickerForm_Output_3cc71f65"), _format)));
        _pdfSection = FrameShiftUiFactory.CreateSection(L.Get("ExtractTextPickerForm_PDF_pages_05a9e8ba"), FrameShiftUiFactory.CreateVerticalStack(FrameShiftUiFactory.CreateWrappingLabel(L.Get("ExtractTextPickerForm_All_pages_are_processed_unless_you_s_da3f9ad0")), _selectedPages, FrameShiftUiFactory.CreateFieldRow(L.Get("ExtractTextPickerForm_Pages_fac29fc4"), _pages)));
        _layoutRow = FrameShiftUiFactory.CreateFieldRow(L.Get("ExtractTextPickerForm_Text_layout_c08483b4"), _layout);
        _sourceRow = FrameShiftUiFactory.CreateFieldRow(L.Get("ExtractTextPickerForm_PDF_text_source_689fd625"), _source);
        _qualityRow = FrameShiftUiFactory.CreateFieldRow(L.Get("ExtractTextPickerForm_PDF_rendering_quality_4c583a02"), _quality);
        _advanced = FrameShiftUiFactory.CreateSection(L.Get("ExtractTextPickerForm_Advanced_options_38dfce8b"), FrameShiftUiFactory.CreateVerticalStack(_layoutRow, _markers, _sourceRow, _qualityRow, FrameShiftUiFactory.CreateFieldRow(L.Get("ExtractTextPickerForm_Recognition_rotation_05d62dd4"), _rotation), FrameShiftUiFactory.CreateFieldRow(L.Get("ExtractTextPickerForm_Processing_8bb154ac"), _device)));
        _advanced.Visible = false;
        var expand = new CheckBox
        {
            Text = L.Get("ExtractTextPickerForm_Advanced_options_38dfce8b"),
            AutoSize = true
        };
        expand.CheckedChanged += (_, _) =>
        {
            _advanced.Visible = expand.Checked;
            Fit();
        };
        _status = FrameShiftUiFactory.CreateStatusMessage(L.Get("ExtractTextPickerForm_Processed_locally_Saves_a_new_file_n_bc86867d"));
        _cancel = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("ExtractTextPickerForm_Cancel_998b9c48"), false);
        _cancel.DialogResult = DialogResult.Cancel;
        _extract = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("ExtractTextPickerForm_Extract_Text_9b5993ad"), true);
        _extract.AccessibleName = L.Get("ExtractTextPickerForm_Extract_Text_9b5993ad");
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
        _modelStatus.Text = OcrModelCatalog.IsPresent(model) ? L.Get("ExtractTextPickerForm_Downloaded_integrity_is_checked_befo_0c65a202") : L.Format("ExtractTextPickerForm_Download_required_for_OCR_approximat_64d6c675",model.SizeBytes / 1_000_000d);
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
            _status.Text = L.Get("ExtractTextPickerForm_Checking_inputs_PDF_pages_and_model__461a9c68");
            var model = OcrModelCatalog.Get(settings.Model);
            var ready = await Task.Run(() => OcrModelCatalog.IsReady(model, token: _stop.Token), _stop.Token);
            if (!ready)
                ready = !await Task.Run(() => OcrPreparation.NeedsModel(paths, settings, _stop.Token, paths.Length > 1), _stop.Token);
            _stop.Token.ThrowIfCancellationRequested();
            if (!ready)
            {
                using var download = new DownloadModelForm(L.Get("ExtractTextPickerForm_FrameShift_AI_Extract_Text_d681b9ce"), L.Format("ExtractTextPickerForm_Download_0_for_local_OCR_e0a80ce0",model.DisplayName), IconPaths.FrameShiftAiIcon, model.DisplayName, "Apache-2.0", model.SizeBytes, (progress, ct) => OcrModelDownloader.DownloadAsync(model, progress, ct));
                if (download.ShowDialog(this) != DialogResult.OK)
                {
                    _status.Text = L.Get("ExtractTextPickerForm_Download_canceled_Choose_a_downloade_07ecb48f");
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
            _status.Text = L.Get("ExtractTextPickerForm_Canceled_437433cc");
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
            _status.Text = L.Get("ExtractTextPickerForm_Canceling_88e58e14");
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
                    throw new FileNotFoundException(L.Format("ExtractTextPickerForm_Input_not_found_0_83fdd16c",Path.GetFileName(path)));
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
