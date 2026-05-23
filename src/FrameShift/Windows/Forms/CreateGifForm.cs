using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Logging;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class CreateGifForm : Form
{
    private const int StandardPreviewButtonWidth = 140;
    private readonly string _inputPath;
    private readonly string _ffmpegPath;
    private readonly MediaProbeResult _probe;
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly Panel _previewPanel;
    private readonly PictureBox _previewBox;
    private readonly Panel _selectionPanel;
    private readonly Label _labelPreviewState;
    private readonly Label _labelSelectionSummary;
    private readonly TextBox _textStartFrame;
    private readonly TextBox _textEndFrame;
    private readonly TextBox _textStart;
    private readonly TextBox _textEnd;
    private readonly TextBox _textDuration;
    private readonly ComboBox _comboResolution;
    private readonly ComboBox _comboFps;
    private readonly ComboBox _comboQuality;
    private readonly Button _buttonPreview;
    private readonly Label _labelPreviewStatus;
    private readonly System.Windows.Forms.Timer _previewTimer;
    private readonly SelectionState _selection;
    private readonly DragState _dragState;
    private readonly UiState _uiState;

    private Bitmap? _currentPreviewBitmap;
    private string? _gifPreviewPath;
    private MemoryStream? _gifPreviewStream;
    private Image? _gifPreviewImage;
    private CancellationTokenSource? _previewRenderCts;
    private bool _busy;
    private bool _closing;

    public CreateGifForm(
        string inputPath,
        string ffmpegPath,
        MediaProbeResult probe,
        FfmpegRunner ffmpegRunner)
    {
        AppLogger.LogStatic("CreateGifForm: constructor entered.");
        _inputPath = inputPath;
        _ffmpegPath = ffmpegPath;
        _probe = probe;
        _ffmpegRunner = ffmpegRunner;

        if (_probe.Duration is null || _probe.Duration.Value.TotalSeconds <= 0)
        {
            throw new InvalidOperationException(MediaActionMessages.DurationUnavailable());
        }

        _selection = new SelectionState(0d, _probe.Duration.Value.TotalSeconds);
        _dragState = new DragState();
        _uiState = new UiState();

        SuspendLayout();

        FrameShiftWindowChrome.Apply(this, "FrameShift - Create GIF");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        MinimumSize = new Size(980, 800);
        ClientSize = new Size(1040, 820);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        ControlHelper.SetDoubleBuffered(this);

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(FrameShiftUiMetrics.OuterPadding),
            ColumnCount = 1,
            RowCount = 7
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.HeaderHeight));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 356F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.FooterButtonHeight));

        var headerPanel = CreateHeaderPanel();

        _previewPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(32, 32, 32),
            Margin = Padding.Empty
        };
        ControlHelper.SetDoubleBuffered(_previewPanel);

        _previewBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        _previewPanel.Controls.Add(_previewBox);

        var previewStateCard = CreateInfoCardPanel();
        previewStateCard.Margin = Padding.Empty;
        previewStateCard.Padding = new Padding(10, 4, 10, 4);

        _labelPreviewState = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = FrameShiftTheme.TextSecondary,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        previewStateCard.Controls.Add(_labelPreviewState);

        var previewSection = CreateSectionPanel("Preview", out var previewContentHost);
        previewSection.Margin = Padding.Empty;
        previewSection.Padding = FrameShiftUiMetrics.StandardSectionPadding;

        var previewSectionLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 3
        };
        previewSectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        previewSectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        previewSectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.LineGap));
        previewSectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        previewSectionLayout.Controls.Add(_previewPanel, 0, 0);
        previewSectionLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
        previewSectionLayout.Controls.Add(previewStateCard, 0, 2);
        previewContentHost.Controls.Add(previewSectionLayout);

        var settingsSection = CreateSectionPanel("GIF settings", out var settingsContentHost);
        settingsSection.Margin = Padding.Empty;
        settingsSection.Padding = FrameShiftUiMetrics.StandardSectionPadding;

        var settingsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 9
        };
        settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74F));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.LineGap));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.LineGap));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.LineGap));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.LineGap));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

        _selectionPanel = CreateFramedPanel(FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, 8);
        _selectionPanel.Dock = DockStyle.Fill;
        _selectionPanel.Margin = Padding.Empty;
        ControlHelper.SetDoubleBuffered(_selectionPanel);
        _selectionPanel.Paint += SelectionPanelOnPaint;
        _selectionPanel.MouseDown += SelectionPanelOnMouseDown;
        _selectionPanel.MouseMove += SelectionPanelOnMouseMove;
        _selectionPanel.MouseUp += SelectionPanelOnMouseUp;

        var summaryCard = CreateInfoCardPanel();
        summaryCard.Margin = Padding.Empty;
        summaryCard.Padding = new Padding(FrameShiftUiMetrics.OuterPadding, FrameShiftUiMetrics.LineGap, FrameShiftUiMetrics.OuterPadding, FrameShiftUiMetrics.LineGap);

        var summaryLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 2
        };
        summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));

        _labelSelectionSummary = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            AutoEllipsis = true,
            ForeColor = FrameShiftTheme.TextPrimary
        };

        var labelHint = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            AutoEllipsis = true,
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = "Drag the left or right handle to choose the GIF range. The preview shows the active boundary frame."
        };
        summaryLayout.Controls.Add(_labelSelectionSummary, 0, 0);
        summaryLayout.Controls.Add(labelHint, 0, 1);
        summaryCard.Controls.Add(summaryLayout);

        _textStartFrame = CreateReadOnlyTextBox();
        _textEndFrame = CreateReadOnlyTextBox();
        _textStart = CreateReadOnlyTextBox();
        _textEnd = CreateReadOnlyTextBox();
        _textDuration = CreateReadOnlyTextBox();

        var boundaryGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 3,
            RowCount = 2
        };
        boundaryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        boundaryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        boundaryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        boundaryGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        boundaryGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        boundaryGrid.Controls.Add(CreateFieldGroup("Frame start", _textStartFrame), 0, 0);
        boundaryGrid.Controls.Add(CreateFieldGroup("Frame end", _textEndFrame), 1, 0);
        boundaryGrid.Controls.Add(CreateTextOnlyGroup("Frame basis", "Frames use the selected GIF FPS."), 2, 0);
        boundaryGrid.Controls.Add(CreateFieldGroup("Start", _textStart), 0, 1);
        boundaryGrid.Controls.Add(CreateFieldGroup("End", _textEnd), 1, 1);
        boundaryGrid.Controls.Add(CreateFieldGroup("Duration", _textDuration), 2, 1);

        _comboResolution = CreatePresetComboBox(CreateGifSettings.GetResolutionItems(), CreateGifSettings.DefaultResolutionKey);
        _comboFps = CreatePresetComboBox(CreateGifSettings.GetFpsItems(), CreateGifSettings.DefaultFps.ToString(CultureInfo.InvariantCulture));
        _comboQuality = CreatePresetComboBox(CreateGifSettings.GetQualityItems(), CreateGifSettings.DefaultQualityKey);

        _comboResolution.SelectedIndexChanged += (_, _) => StopGifPreview(restoreFrame: true);
        _comboFps.SelectedIndexChanged += (_, _) =>
        {
            StopGifPreview(restoreFrame: true);
            RefreshSelectionUi(refreshPreview: true);
        };
        _comboQuality.SelectedIndexChanged += (_, _) => StopGifPreview(restoreFrame: true);

        var presetGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 6,
            RowCount = 1
        };
        presetGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
        presetGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        presetGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
        presetGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        presetGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));
        presetGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        presetGrid.Controls.Add(CreateFieldLabel("Resolution"), 0, 0);
        presetGrid.Controls.Add(_comboResolution, 1, 0);
        presetGrid.Controls.Add(CreateFieldLabel("FPS"), 2, 0);
        presetGrid.Controls.Add(_comboFps, 3, 0);
        presetGrid.Controls.Add(CreateFieldLabel("Quality"), 4, 0);
        presetGrid.Controls.Add(_comboQuality, 5, 0);

        var previewRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = 1
        };
        previewRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, StandardPreviewButtonWidth));
        previewRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        _buttonPreview = CreateActionButton("Preview GIF", primary: false, width: StandardPreviewButtonWidth);
        _buttonPreview.Click += async (_, _) => await TogglePreviewAsync().ConfigureAwait(true);

        _labelPreviewStatus = new Label
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(FrameShiftUiMetrics.LineGap, 0, 0, 0),
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        previewRow.Controls.Add(_buttonPreview, 0, 0);
        previewRow.Controls.Add(_labelPreviewStatus, 1, 0);

        settingsLayout.Controls.Add(_selectionPanel, 0, 0);
        settingsLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
        settingsLayout.Controls.Add(summaryCard, 0, 2);
        settingsLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 3);
        settingsLayout.Controls.Add(boundaryGrid, 0, 4);
        settingsLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 5);
        settingsLayout.Controls.Add(presetGrid, 0, 6);
        settingsLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 7);
        settingsLayout.Controls.Add(previewRow, 0, 8);
        settingsContentHost.Controls.Add(settingsLayout);

        var buttonOk = CreateActionButton("Create GIF", primary: true);
        buttonOk.Click += (_, _) => ConfirmSelection();

        var buttonCancel = CreateActionButton("Cancel", primary: false);
        buttonCancel.DialogResult = DialogResult.Cancel;

        var footerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        footerPanel.Controls.Add(buttonCancel);
        footerPanel.Controls.Add(buttonOk);
        footerPanel.Resize += (_, _) => UpdateFooterButtonLayout(footerPanel, buttonCancel, buttonOk);
        Shown += (_, _) => UpdateFooterButtonLayout(footerPanel, buttonCancel, buttonOk);

        rootLayout.Controls.Add(headerPanel, 0, 0);
        rootLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
        rootLayout.Controls.Add(previewSection, 0, 2);
        rootLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 3);
        rootLayout.Controls.Add(settingsSection, 0, 4);
        rootLayout.Controls.Add(footerPanel, 0, 6);

        Controls.Add(rootLayout);

        AcceptButton = buttonOk;
        CancelButton = buttonCancel;

        _previewTimer = new System.Windows.Forms.Timer
        {
            Interval = 140
        };
        _previewTimer.Tick += PreviewTimerOnTick;

        FormClosing += (_, _) =>
        {
            AppLogger.LogStatic("CreateGifForm: FormClosing entered.");
            _closing = true;
            _previewTimer.Stop();
            _previewTimer.Dispose();
            _previewRenderCts?.Cancel();
            _previewRenderCts?.Dispose();
            _previewRenderCts = null;
            DisposeGifPreview();
            DisposePreviewBitmap();
        };

        AppLogger.LogStatic("CreateGifForm: before InitializeWorkspace.");
        InitializeWorkspace();
        AppLogger.LogStatic("CreateGifForm: after InitializeWorkspace.");
        ResumeLayout(true);
        AppLogger.LogStatic("CreateGifForm: constructor exiting.");
    }

    public CreateGifSettings? Selection { get; private set; }

    private Panel CreateHeaderPanel()
    {
        var sourceExtension = Path.GetExtension(_inputPath).TrimStart('.').ToUpperInvariant();
        var resolutionText = _probe.VideoWidth > 0 && _probe.VideoHeight > 0
            ? $"{_probe.VideoWidth} x {_probe.VideoHeight}"
            : "unknown";
        var durationText = CutAudioSettings.FormatDisplayTime(_probe.Duration?.TotalSeconds ?? 0d);
        return FrameShiftUiFactory.CreateFillHeader(
            "FrameShift - Create GIF",
            $"Source: {sourceExtension}    Duration: {durationText}    Video: {resolutionText}",
            IconPaths.ContextMenuIco("create-gif-video-icon.ico"),
            IconPaths.AppIcon,
            "GIF",
            780);
    }

    private static Panel CreateSectionPanel(string title, out Panel contentHost)
    {
        return FrameShiftUiFactory.CreateFillSection(title, out contentHost);
    }

    private static Panel CreateInfoCardPanel()
    {
        return FrameShiftUiFactory.CreateFillInfoCard();
    }

    private static Panel CreateFramedPanel(Color backgroundColor, Color borderColor, int radius)
    {
        return FrameShiftUiFactory.CreateFramedPanel(backgroundColor, borderColor, radius);
    }

    private static Button CreateActionButton(string text, bool primary, int? width = null)
    {
        return FrameShiftUiFactory.CreateActionButton(
            text,
            primary,
            width ?? (primary ? FrameShiftUiMetrics.PrimaryButtonWidth : FrameShiftUiMetrics.SecondaryButtonWidth));
    }

    private static void UpdateFooterButtonLayout(Control footerPanel, Button cancelButton, Button okButton)
    {
        if (footerPanel.ClientSize.Width <= 0 || footerPanel.ClientSize.Height <= 0)
        {
            return;
        }

        FrameShiftUiLayout.LayoutFooterButtons(footerPanel, cancelButton, okButton, FrameShiftUiMetrics.LineGap);
    }

    private static Label CreateFieldLabel(string text)
    {
        return FrameShiftUiFactory.CreateFieldLabel(text);
    }

    private static TextBox CreateReadOnlyTextBox()
    {
        return new TextBox
        {
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ReadOnly = true,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary,
            TabStop = false
        };
    }

    private static Panel CreateFieldGroup(string labelText, TextBox textBox)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var label = CreateFieldLabel(labelText);
        var inputHost = FrameShiftUiFactory.CreateTextInputHost(textBox);
        inputHost.Height = 30;

        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(inputHost, 1, 0);
        panel.Controls.Add(layout);
        return panel;
    }

    private static Panel CreateTextOnlyGroup(string labelText, string valueText)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var label = CreateFieldLabel(labelText);
        var value = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = valueText
        };

        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(value, 1, 0);
        panel.Controls.Add(layout);
        return panel;
    }

    private static ComboBox CreatePresetComboBox(System.Collections.Generic.IReadOnlyList<CreateGifPresetItem> items, string selectedKey)
    {
        var comboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true
        };

        foreach (var item in items)
        {
            comboBox.Items.Add(item);
            if (string.Equals(item.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedIndex = comboBox.Items.Count - 1;
            }
        }

        if (comboBox.SelectedIndex < 0 && comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }

        comboBox.DisplayMember = nameof(CreateGifPresetItem.Label);
        return comboBox;
    }

    private void InitializeWorkspace()
    {
        AppLogger.LogStatic("CreateGifForm: InitializeWorkspace entered.");
        RefreshSelectionUi(refreshPreview: false);
        LoadInitialPreviewFrame(0d);
        AppLogger.LogStatic("CreateGifForm: InitializeWorkspace exiting.");
    }

    private void LoadInitialPreviewFrame(double timeSeconds)
    {
        AppLogger.LogStatic($"CreateGifForm: LoadInitialPreviewFrame entered at {timeSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s.");
        _busy = true;
        try
        {
            var safeMax = Math.Max(0d, (_probe.Duration?.TotalSeconds ?? 0d) - 0.001d);
            var safeTime = Math.Max(0d, Math.Min(safeMax, timeSeconds));
            var bitmap = PreviewFrameHelper.CaptureFrameAsync(
                _ffmpegPath,
                _ffmpegRunner,
                _inputPath,
                safeTime,
                "Create GIF Preview",
                CancellationToken.None).GetAwaiter().GetResult();

            DisposePreviewBitmap();
            _currentPreviewBitmap = bitmap;
            _previewBox.Image = _currentPreviewBitmap;
            _labelPreviewState.Text = $"Previewing {Capitalize(_uiState.ActiveBoundary)} boundary: {CutAudioSettings.FormatDisplayTime(safeTime)}";
            AppLogger.LogStatic("CreateGifForm: LoadInitialPreviewFrame preview applied.");
        }
        finally
        {
            _busy = false;
            AppLogger.LogStatic("CreateGifForm: LoadInitialPreviewFrame exiting.");
        }
    }

    private async Task TogglePreviewAsync()
    {
        if (_busy)
        {
            return;
        }

        if (_uiState.GifPreviewRunning)
        {
            StopGifPreview(restoreFrame: true);
            return;
        }

        await StartGifPreviewAsync().ConfigureAwait(true);
    }

    private async Task StartGifPreviewAsync()
    {
        StopGifPreview(restoreFrame: false);
        _busy = true;
        _buttonPreview.Enabled = false;
        _labelPreviewStatus.Text = "Rendering GIF preview...";
        _previewRenderCts?.Cancel();
        _previewRenderCts?.Dispose();
        _previewRenderCts = new CancellationTokenSource();

        try
        {
            var settings = BuildSelection();
            var previewDuration = Math.Min(6d, Math.Max(0.5d, settings.DurationSeconds));
            var previewSettings = settings with { DurationSeconds = previewDuration };
            var previewPath = Path.Combine(Path.GetTempPath(), $"frameshift_create_gif_preview_{Guid.NewGuid():N}.gif");
            var arguments = CreateGifAction.BuildArguments(_inputPath, previewPath, previewSettings);

            var runResult = await _ffmpegRunner.RunAsync(
                _ffmpegPath,
                arguments,
                TimeSpan.FromSeconds(previewDuration),
                Math.Max(1L, (long)Math.Ceiling(previewDuration * settings.Fps)),
                null,
                _inputPath,
                "Create GIF Preview",
                "CPU",
                _previewRenderCts.Token).ConfigureAwait(true);

            if (_closing || _previewRenderCts.IsCancellationRequested || runResult.Canceled)
            {
                if (File.Exists(previewPath))
                {
                    File.Delete(previewPath);
                }

                return;
            }

            if (runResult.ExitCode != 0 || !File.Exists(previewPath))
            {
                if (File.Exists(previewPath))
                {
                    File.Delete(previewPath);
                }

                throw new InvalidOperationException(ConversionActionHelper.GetFriendlyFfmpegError(runResult.StandardError, MediaActionMessages.CreateGifPreviewFailed()));
            }

            SetGifPreviewImage(previewPath);
            _uiState.GifPreviewRunning = true;
            _buttonPreview.Text = "Stop preview";
            _labelPreviewStatus.Text = $"Looping GIF preview, {previewDuration.ToString("0.#", CultureInfo.InvariantCulture)}s at {settings.Fps} fps";
            _labelPreviewState.Text = $"GIF preview active: {previewDuration.ToString("0.#", CultureInfo.InvariantCulture)}s loop";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StopGifPreview(restoreFrame: true);
            MessageBox.Show(this, ex.Message, "FrameShift", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            _buttonPreview.Enabled = true;
        }
    }

    private async Task LoadPreviewFrameAsync(double timeSeconds)
    {
        AppLogger.LogStatic($"CreateGifForm: LoadPreviewFrameAsync entered at {timeSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s.");
        _busy = true;
        try
        {
            var safeMax = Math.Max(0d, (_probe.Duration?.TotalSeconds ?? 0d) - 0.001d);
            var safeTime = Math.Max(0d, Math.Min(safeMax, timeSeconds));
            var bitmap = await PreviewFrameHelper.CaptureFrameAsync(
                _ffmpegPath,
                _ffmpegRunner,
                _inputPath,
                safeTime,
                "Create GIF Preview",
                CancellationToken.None).ConfigureAwait(true);

            DisposePreviewBitmap();
            _currentPreviewBitmap = bitmap;
            _previewBox.Image = _currentPreviewBitmap;
            _labelPreviewState.Text = $"Previewing {Capitalize(_uiState.ActiveBoundary)} boundary: {CutAudioSettings.FormatDisplayTime(safeTime)}";
            AppLogger.LogStatic("CreateGifForm: LoadPreviewFrameAsync preview applied.");
        }
        finally
        {
            _busy = false;
            AppLogger.LogStatic("CreateGifForm: LoadPreviewFrameAsync exiting.");
        }
    }

    private void SetGifPreviewImage(string previewPath)
    {
        DisposeGifPreview();
        DisposePreviewBitmap();

        var bytes = File.ReadAllBytes(previewPath);
        var stream = new MemoryStream();
        stream.Write(bytes, 0, bytes.Length);
        stream.Position = 0;

        _gifPreviewImage = Image.FromStream(stream);
        _gifPreviewStream = stream;
        _gifPreviewPath = previewPath;
        _previewBox.Image = _gifPreviewImage;
    }

    private void StopGifPreview(bool restoreFrame)
    {
        _previewRenderCts?.Cancel();
        _uiState.GifPreviewRunning = false;
        _buttonPreview.Text = "Preview GIF";
        _labelPreviewStatus.Text = string.Empty;
        DisposeGifPreview();

        if (!restoreFrame || _closing)
        {
            return;
        }

        var previewTime = string.Equals(_uiState.ActiveBoundary, "end", StringComparison.Ordinal)
            ? _selection.EndSeconds
            : _selection.StartSeconds;
        SchedulePreviewUpdate(previewTime);
    }

    private void DisposeGifPreview()
    {
        if (_previewBox.Image == _gifPreviewImage)
        {
            _previewBox.Image = null;
        }

        if (_gifPreviewImage is not null)
        {
            _gifPreviewImage.Dispose();
            _gifPreviewImage = null;
        }

        if (_gifPreviewStream is not null)
        {
            _gifPreviewStream.Dispose();
            _gifPreviewStream = null;
        }

        if (!string.IsNullOrWhiteSpace(_gifPreviewPath) && File.Exists(_gifPreviewPath))
        {
            try
            {
                File.Delete(_gifPreviewPath);
            }
            catch
            {
            }
        }

        _gifPreviewPath = null;
    }

    private void DisposePreviewBitmap()
    {
        if (_currentPreviewBitmap is not null)
        {
            if (_previewBox.Image == _currentPreviewBitmap)
            {
                _previewBox.Image = null;
            }

            _currentPreviewBitmap.Dispose();
            _currentPreviewBitmap = null;
        }
    }

    private void SchedulePreviewUpdate(double timeSeconds)
    {
        _uiState.PendingPreviewTime = timeSeconds;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void RefreshSelectionUi(bool refreshPreview)
    {
        var gifFps = GetSelectedFps();
        var startSeconds = Math.Max(0d, _selection.StartSeconds);
        var endSeconds = Math.Min(_probe.Duration?.TotalSeconds ?? 0d, _selection.EndSeconds);
        if (endSeconds < startSeconds)
        {
            endSeconds = startSeconds;
        }

        var durationSeconds = Math.Max(0.001d, endSeconds - startSeconds);
        var startFrame = 1 + (int)Math.Floor(startSeconds * gifFps);
        var endFrame = (int)Math.Ceiling(endSeconds * gifFps);
        if (endFrame < startFrame)
        {
            endFrame = startFrame;
        }

        _textStartFrame.Text = startFrame.ToString(CultureInfo.InvariantCulture);
        _textEndFrame.Text = endFrame.ToString(CultureInfo.InvariantCulture);
        _textStart.Text = CutAudioSettings.FormatDisplayTime(startSeconds);
        _textEnd.Text = CutAudioSettings.FormatDisplayTime(endSeconds);
        _textDuration.Text = CutAudioSettings.FormatDisplayTime(durationSeconds);
        _labelSelectionSummary.Text = $"Selection: frame {startFrame} -> {endFrame}  |  {CutAudioSettings.FormatDisplayTime(startSeconds)} -> {CutAudioSettings.FormatDisplayTime(endSeconds)}  |  Duration: {CutAudioSettings.FormatDisplayTime(durationSeconds)}";
        _selectionPanel.Invalidate();

        if (refreshPreview)
        {
            var previewTime = string.Equals(_uiState.ActiveBoundary, "end", StringComparison.Ordinal)
                ? endSeconds
                : startSeconds;
            SchedulePreviewUpdate(previewTime);
        }
    }

    private void ConfirmSelection()
    {
        try
        {
            StopGifPreview(restoreFrame: false);
            Selection = BuildSelection();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "FrameShift", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private CreateGifSettings BuildSelection()
    {
        var settings = new CreateGifSettings(
            _selection.StartSeconds,
            Math.Max(0.001d, _selection.EndSeconds - _selection.StartSeconds),
            GetSelectedKey(_comboResolution),
            GetSelectedFps(),
            GetSelectedKey(_comboQuality));

        if (!CreateGifSettings.TryFromOptions(settings.ToOptions(), _probe.Duration?.TotalSeconds ?? 0d, out var validatedSettings, out var errorMessage) ||
            validatedSettings is null)
        {
            throw new InvalidOperationException(errorMessage ?? MediaActionMessages.CreateGifSettingsInvalid());
        }

        return validatedSettings;
    }

    private int GetSelectedFps()
    {
        return int.Parse(GetSelectedKey(_comboFps), CultureInfo.InvariantCulture);
    }

    private static string GetSelectedKey(ComboBox comboBox)
    {
        return comboBox.SelectedItem is CreateGifPresetItem item
            ? item.Key
            : string.Empty;
    }

    private void PreviewTimerOnTick(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        _ = LoadPreviewFrameAsync(_uiState.PendingPreviewTime).ContinueWith(
            task =>
            {
                if (task.Exception is null || IsDisposed)
                {
                    return;
                }

                BeginInvoke(new Action(() =>
                {
                    if (!_closing)
                    {
                        MessageBox.Show(this, task.Exception.GetBaseException().Message, "FrameShift", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }));
            },
            TaskScheduler.Default);
    }

    private void SelectionPanelOnPaint(object? sender, PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var trackLeft = 18;
        var trackTop = 36;
        var trackWidth = Math.Max(1, _selectionPanel.ClientSize.Width - 36);
        var trackHeight = 6;
        var handleWidth = 10;
        var handleHeight = 28;

        var startX = ConvertSecondsToTrackX(_selection.StartSeconds);
        var endX = ConvertSecondsToTrackX(_selection.EndSeconds);
        if (endX < startX)
        {
            (startX, endX) = (endX, startX);
        }

        var selectionWidth = Math.Max(2, endX - startX);
        var startHandleX = startX - (handleWidth / 2);
        var startHandleY = trackTop - 11;
        var endHandleX = endX - (handleWidth / 2);
        var endHandleY = trackTop - 11;

        using var trackBrush = new SolidBrush(FrameShiftTheme.SurfaceBorder);
        using var selectionBrush = new SolidBrush(FrameShiftTheme.PrimaryBlue);
        using var startBrush = new SolidBrush(string.Equals(_uiState.ActiveBoundary, "start", StringComparison.Ordinal)
            ? FrameShiftTheme.SecondaryBlue
            : FrameShiftTheme.PrimaryBlue);
        using var endBrush = new SolidBrush(string.Equals(_uiState.ActiveBoundary, "end", StringComparison.Ordinal)
            ? FrameShiftTheme.SecondaryBlue
            : FrameShiftTheme.PrimaryBlue);
        using var handleBorderPen = new Pen(FrameShiftTheme.SecondaryBlue);
        using var tickPen = new Pen(FrameShiftTheme.TextMuted);
        using var labelBrush = new SolidBrush(FrameShiftTheme.TextSecondary);

        graphics.FillRectangle(trackBrush, trackLeft, trackTop, trackWidth, trackHeight);
        graphics.FillRectangle(selectionBrush, startX, trackTop - 4, selectionWidth, 14);

        for (var index = 0; index <= 10; index++)
        {
            var tickX = trackLeft + (int)Math.Round((trackWidth * index) / 10.0);
            graphics.DrawLine(tickPen, tickX, 18, tickX, 26);
        }

        graphics.FillRectangle(startBrush, startHandleX, startHandleY, handleWidth, handleHeight);
        graphics.FillRectangle(endBrush, endHandleX, endHandleY, handleWidth, handleHeight);
        graphics.DrawRectangle(handleBorderPen, startHandleX, startHandleY, handleWidth, handleHeight);
        graphics.DrawRectangle(handleBorderPen, endHandleX, endHandleY, handleWidth, handleHeight);

        graphics.DrawString("0", Font, labelBrush, 14, 2);
        var lastText = CutAudioSettings.FormatDisplayTime(_probe.Duration?.TotalSeconds ?? 0d);
        var lastSize = graphics.MeasureString(lastText, Font);
        graphics.DrawString(lastText, Font, labelBrush, _selectionPanel.ClientSize.Width - lastSize.Width - 14, 2);
    }

    private void SelectionPanelOnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _busy)
        {
            return;
        }

        StopGifPreview(restoreFrame: false);

        var startX = ConvertSecondsToTrackX(_selection.StartSeconds);
        var endX = ConvertSecondsToTrackX(_selection.EndSeconds);
        var distanceToStart = Math.Abs(e.X - startX);
        var distanceToEnd = Math.Abs(e.X - endX);

        _dragState.Active = true;
        _dragState.Target = distanceToStart <= distanceToEnd ? "start" : "end";
        _uiState.ActiveBoundary = _dragState.Target;
        ((Control)sender!).Capture = true;

        ApplyDragPosition(e.X);
    }

    private void SelectionPanelOnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (!_dragState.Active)
        {
            _selectionPanel.Cursor = Cursors.SizeWE;
            return;
        }

        ApplyDragPosition(e.X);
    }

    private void SelectionPanelOnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragState.Active = false;
        _dragState.Target = string.Empty;
        ((Control)sender!).Capture = false;
    }

    private void ApplyDragPosition(int x)
    {
        var seconds = ConvertTrackXToSeconds(x);
        if (string.Equals(_dragState.Target, "start", StringComparison.Ordinal))
        {
            var maxStart = Math.Max(0d, _selection.EndSeconds - 0.001d);
            _selection.StartSeconds = Math.Min(maxStart, seconds);
        }
        else
        {
            var minEnd = Math.Min(_probe.Duration?.TotalSeconds ?? 0d, _selection.StartSeconds + 0.001d);
            _selection.EndSeconds = Math.Max(minEnd, seconds);
        }

        RefreshSelectionUi(refreshPreview: true);
    }

    private int ConvertSecondsToTrackX(double seconds)
    {
        var trackLeft = 18;
        var trackWidth = _selectionPanel.ClientSize.Width - 36;
        var durationSeconds = _probe.Duration?.TotalSeconds ?? 0d;
        if (durationSeconds <= 0 || trackWidth <= 0)
        {
            return trackLeft;
        }

        var ratio = seconds / durationSeconds;
        ratio = Math.Max(0d, Math.Min(1d, ratio));
        return (int)Math.Round(trackLeft + (ratio * trackWidth));
    }

    private double ConvertTrackXToSeconds(int x)
    {
        var trackLeft = 18;
        var trackWidth = _selectionPanel.ClientSize.Width - 36;
        var durationSeconds = _probe.Duration?.TotalSeconds ?? 0d;
        if (trackWidth <= 0 || durationSeconds <= 0)
        {
            return 0d;
        }

        var clamped = Math.Min(trackLeft + trackWidth, Math.Max(trackLeft, x));
        var ratio = (clamped - trackLeft) / (double)trackWidth;
        return ratio * durationSeconds;
    }

    private static string Capitalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private sealed class SelectionState
    {
        public SelectionState(double startSeconds, double endSeconds)
        {
            StartSeconds = startSeconds;
            EndSeconds = endSeconds;
        }

        public double StartSeconds { get; set; }

        public double EndSeconds { get; set; }
    }

    private sealed class DragState
    {
        public bool Active { get; set; }

        public string Target { get; set; } = string.Empty;
    }

    private sealed class UiState
    {
        public string ActiveBoundary { get; set; } = "start";

        public double PendingPreviewTime { get; set; }

        public bool GifPreviewRunning { get; set; }
    }
}
