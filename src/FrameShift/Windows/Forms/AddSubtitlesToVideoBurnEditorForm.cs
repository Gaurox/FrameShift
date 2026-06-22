using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.AI.CreateSubtitles;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

internal sealed class AddSubtitlesToVideoBurnEditorForm : Form
{
    private readonly string _inputPath;
    private readonly string _ffmpegPath;
    private readonly MediaProbeResult _probe;
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly Panel _previewPanel;
    private readonly PictureBox _previewImageBox;
    private readonly SeekTrackBar _timelineBar;
    private readonly Label _currentTimeLabel;
    private readonly Label _previewStatusLabel;
    private readonly Label _sourceKindLabel;
    private readonly Label _styleDisabledLabel;
    private readonly Label _compatibilityWarningLabel;
    private readonly Label _previewInfoLabel;
    private readonly TextBox _subtitlePathTextBox;
    private readonly ComboBox _presetCombo;
    private readonly ComboBox _fontCombo;
    private readonly ComboBox _positionCombo;
    private readonly NumericUpDown _fontSizeUpDown;
    private readonly NumericUpDown _outlineUpDown;
    private readonly NumericUpDown _shadowUpDown;
    private readonly NumericUpDown _marginVerticalUpDown;
    private readonly Button _primaryColorButton;
    private readonly Button _highlightColorButton;
    private readonly Button _outlineColorButton;
    private readonly Button _shadowColorButton;
    private readonly Button _animatedPreviewButton;
    private readonly Control[] _styleControls;
    private readonly System.Windows.Forms.Timer _previewDebounceTimer;
    private readonly ToolTip _toolTip = new();
    private readonly HashSet<string> _installedFontFamilies = new(StringComparer.OrdinalIgnoreCase);
    private Bitmap? _previewBitmap;
    private Image? _animatedPreviewImage;
    private MemoryStream? _animatedPreviewStream;
    private string? _animatedPreviewGifPath;
    private string? _animatedPreviewClipPath;
    private CancellationTokenSource? _previewRenderCts;
    private CancellationTokenSource? _animatedPreviewCts;
    private AddSubtitlesToVideoBurnAppearance _appearance;
    private string _subtitlePath;
    private AddSubtitlesToVideoSubtitleSourceKind _sourceKind;
    private bool _styleEditingEnabled;
    private bool _loadingControls;
    private bool _closing;
    private bool _animatedPreviewActive;
    private bool _animatedPreviewBusy;
    private double _pendingPreviewSeconds;

    public AddSubtitlesToVideoBurnEditorForm(
        string inputPath,
        string ffmpegPath,
        MediaProbeResult probe,
        FfmpegRunner ffmpegRunner,
        AddSubtitlesToVideoSettings initialSettings)
    {
        _inputPath = inputPath;
        _ffmpegPath = ffmpegPath;
        _probe = probe;
        _ffmpegRunner = ffmpegRunner;
        _subtitlePath = initialSettings.SubtitleFilePath;
        _sourceKind = AddSubtitlesToVideoSubtitleSourceLoader.DetectSourceKind(_subtitlePath);
        _styleEditingEnabled = _sourceKind != AddSubtitlesToVideoSubtitleSourceKind.Ass;
        _appearance = (initialSettings.BurnSettings ?? AddSubtitlesToVideoBurnSettings.Default).ResolveAppearanceForVideo(probe);

        SuspendLayout();

        FrameShiftWindowChrome.Apply(this, "FrameShift - Burn Subtitles Into Video", IconPaths.AddSubtitlesVideoAiIcon, IconPaths.AppIcon);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = FrameShiftTheme.PageBackground;
        ClientSize = new Size(1240, 800);
        MinimumSize = new Size(1120, 760);
        ControlHelper.SetDoubleBuffered(this);

        var rootLayout = FrameShiftCropEditorUi.CreateRootLayout();
        var header = FrameShiftUiFactory.CreateFillHeader(
            "FrameShift - Burn Subtitles Into Video",
            $"Source: {Path.GetFileName(inputPath)}",
            IconPaths.AddSubtitlesVideoAiIcon,
            IconPaths.FrameShiftAiIcon,
            "S",
            460);

        var contentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = 1
        };
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FrameShiftUiMetrics.WideEditorRailWidth));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var leftLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, FrameShiftUiMetrics.OuterPadding, 0),
            ColumnCount = 1,
            RowCount = 1
        };
        leftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var rightHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            AutoScroll = true
        };
        var rightRailLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnCount = 1,
            RowCount = 4
        };
        rightRailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rightRailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightRailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightRailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightRailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightHost.Controls.Add(rightRailLayout);

        var previewSection = FrameShiftUiFactory.CreateFillSection("Preview", out var previewContentHost);
        previewSection.Margin = Padding.Empty;
        previewSection.Padding = FrameShiftUiMetrics.StandardSectionPadding;

        var previewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 5
        };
        previewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.BlockGap));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
        previewContentHost.Controls.Add(previewLayout);

        var previewInfoPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = 2
        };
        previewInfoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        previewInfoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        previewInfoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        previewInfoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        previewLayout.Controls.Add(previewInfoPanel, 0, 0);

        _sourceKindLabel = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ForeColor = FrameShiftTheme.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        previewInfoPanel.Controls.Add(_sourceKindLabel, 0, 0);

        _currentTimeLabel = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleRight
        };
        previewInfoPanel.Controls.Add(_currentTimeLabel, 1, 0);

        _previewStatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        previewInfoPanel.SetColumnSpan(_previewStatusLabel, 2);
        previewInfoPanel.Controls.Add(_previewStatusLabel, 0, 1);

        _previewPanel = FrameShiftCropEditorUi.CreatePreviewPanel();
        _previewPanel.Paint += PreviewPanelOnPaint;
        ControlHelper.SetDoubleBuffered(_previewPanel);
        _previewImageBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        _previewPanel.Controls.Add(_previewImageBox);
        previewLayout.Controls.Add(_previewPanel, 0, 1);

        previewLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 2);

        var timelinePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 3
        };
        timelinePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        timelinePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        timelinePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        timelinePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        previewLayout.Controls.Add(timelinePanel, 0, 4);

        _timelineBar = new SeekTrackBar
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Minimum = 0,
            Maximum = 1000,
            TickFrequency = 100,
            SmallChange = 5,
            LargeChange = 50
        };
        _timelineBar.ValueChanged += (_, _) => OnTimelineChanged();
        timelinePanel.Controls.Add(_timelineBar, 0, 0);

        timelinePanel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = "Drag the slider to preview a different frame, then render a short animated loop around that position.",
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);

        var animatedPreviewRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = 1
        };
        animatedPreviewRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148F));
        animatedPreviewRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        timelinePanel.Controls.Add(animatedPreviewRow, 0, 2);

        _animatedPreviewButton = FrameShiftUiFactory.CreateActionButton("Preview Motion", primary: false, width: 148);
        _animatedPreviewButton.Dock = DockStyle.Fill;
        _animatedPreviewButton.Margin = Padding.Empty;
        _animatedPreviewButton.Click += async (_, _) => await ToggleAnimatedPreviewAsync().ConfigureAwait(true);
        animatedPreviewRow.Controls.Add(_animatedPreviewButton, 0, 0);

        _previewInfoLabel = new Label
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(FrameShiftUiMetrics.LineGap, 0, 0, 0),
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        animatedPreviewRow.Controls.Add(_previewInfoLabel, 1, 0);

        leftLayout.Controls.Add(previewSection, 0, 0);
        contentLayout.Controls.Add(leftLayout, 0, 0);
        contentLayout.Controls.Add(rightHost, 1, 0);

        var fileSection = CreateStackedSection("Subtitle Source", 164, out var fileContentHost);
        rightRailLayout.Controls.Add(fileSection, 0, 3);

        var fileLayout = CreatePropertyGrid(3);
        fileContentHost.Controls.Add(fileLayout);

        fileLayout.Controls.Add(FrameShiftUiFactory.CreateFieldLabel("File"), 0, 0);
        _subtitlePathTextBox = FrameShiftUiFactory.CreateValueTextBox(readOnly: true);
        fileLayout.Controls.Add(FrameShiftUiFactory.CreateTextInputHost(_subtitlePathTextBox), 1, 0);

        var browseButton = FrameShiftUiFactory.CreateActionButton("Browse...", primary: false, width: 96);
        browseButton.Dock = DockStyle.Right;
        browseButton.Click += (_, _) => BrowseSubtitleFile();
        var browseHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        browseHost.Controls.Add(browseButton);
        fileLayout.Controls.Add(browseHost, 1, 1);

        var fileHintLabel = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = "ASS input keeps its existing style. SRT and FrameShift projects use the shared temporary ASS generator.",
            AutoEllipsis = false
        };
        fileLayout.Controls.Add(fileHintLabel, 0, 2);
        fileLayout.SetColumnSpan(fileHintLabel, 2);

        var styleSection = CreateStackedSection("Style", 274, out var styleContentHost);
        rightRailLayout.Controls.Add(styleSection, 0, 2);

        var styleLayout = CreatePropertyGrid(6);
        styleContentHost.Controls.Add(styleLayout);

        _presetCombo = CreateComboBox();
        PopulatePresetCombo(_presetCombo);
        _presetCombo.SelectedIndexChanged += (_, _) => OnAppearanceControlChanged();
        AddPropertyRow(styleLayout, 0, "Preset", _presetCombo);

        _fontCombo = CreateComboBox();
        PopulateFontCombo(_fontCombo, _appearance.FontName);
        _fontCombo.SelectedIndexChanged += (_, _) => OnAppearanceControlChanged();
        AddPropertyRow(styleLayout, 1, "Font", _fontCombo);

        _fontSizeUpDown = CreateIntEditor(12, 240, 1);
        _fontSizeUpDown.ValueChanged += (_, _) => OnAppearanceControlChanged();
        AddPropertyRow(styleLayout, 2, "Size", _fontSizeUpDown);

        _positionCombo = CreateComboBox();
        PopulatePositionCombo(_positionCombo);
        _positionCombo.SelectedIndexChanged += (_, _) => OnAppearanceControlChanged();
        AddPropertyRow(styleLayout, 3, "Position", _positionCombo);

        _marginVerticalUpDown = CreateIntEditor(0, 600, 2);
        _marginVerticalUpDown.ValueChanged += (_, _) => OnAppearanceControlChanged();
        AddPropertyRow(styleLayout, 4, "Vertical Margin", _marginVerticalUpDown);

        _styleDisabledLabel = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ForeColor = FrameShiftTheme.TextMuted,
            Text = "External ASS detected: preset and visual controls are disabled because the file style is preserved as-is."
        };
        styleLayout.Controls.Add(_styleDisabledLabel, 0, 5);
        styleLayout.SetColumnSpan(_styleDisabledLabel, 2);

        var colorsSection = CreateStackedSection("Colors & Effects", 274, out var colorsContentHost);
        rightRailLayout.Controls.Add(colorsSection, 0, 1);

        var colorsLayout = CreatePropertyGrid(6);
        colorsContentHost.Controls.Add(colorsLayout);

        _primaryColorButton = CreateColorButton();
        _primaryColorButton.Click += (_, _) => PickColor(_primaryColorButton, "Text Color");
        AddPropertyRow(colorsLayout, 0, "Text Color", _primaryColorButton);

        _highlightColorButton = CreateColorButton();
        _highlightColorButton.Click += (_, _) => PickColor(_highlightColorButton, "Highlight Color");
        AddPropertyRow(colorsLayout, 1, "Highlight", _highlightColorButton);

        _outlineColorButton = CreateColorButton();
        _outlineColorButton.Click += (_, _) => PickColor(_outlineColorButton, "Outline Color");
        AddPropertyRow(colorsLayout, 2, "Outline Color", _outlineColorButton);

        _shadowColorButton = CreateColorButton();
        _shadowColorButton.Click += (_, _) => PickColor(_shadowColorButton, "Shadow Color");
        AddPropertyRow(colorsLayout, 3, "Shadow Color", _shadowColorButton);

        _outlineUpDown = CreateDecimalEditor(0, 12, 1, 0.1M);
        _outlineUpDown.ValueChanged += (_, _) => OnAppearanceControlChanged();
        AddPropertyRow(colorsLayout, 4, "Outline", _outlineUpDown);

        _shadowUpDown = CreateDecimalEditor(0, 12, 1, 0.1M);
        _shadowUpDown.ValueChanged += (_, _) => OnAppearanceControlChanged();
        AddPropertyRow(colorsLayout, 5, "Shadow", _shadowUpDown);

        var infoCard = FrameShiftUiFactory.CreateFillInfoCardWithMargin(topMargin: FrameShiftUiMetrics.OuterPadding);
        infoCard.Dock = DockStyle.Fill;
        infoCard.Height = 110;
        rightRailLayout.Controls.Add(infoCard, 0, 0);
        var infoLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 2
        };
        infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        infoLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        infoLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        infoCard.Controls.Add(infoLayout);

        infoLayout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = FrameShiftTheme.TextSecondary,
            Padding = FrameShiftUiMetrics.StandardInfoCardPadding,
            Text = "FrameShift regenerates the preview with FFmpeg/libass after a short debounce, uses the display geometry from the video metadata, and renders a short burn preview clip for animated checks."
        }, 0, 0);

        _compatibilityWarningLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(168, 72, 32),
            Padding = new Padding(
                FrameShiftUiMetrics.StandardInfoCardPadding.Left,
                0,
                FrameShiftUiMetrics.StandardInfoCardPadding.Right,
                FrameShiftUiMetrics.StandardInfoCardPadding.Bottom),
            Visible = false
        };
        infoLayout.Controls.Add(_compatibilityWarningLabel, 0, 1);

        var footerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        var cancelButton = FrameShiftUiFactory.CreateActionButton("Cancel", primary: false, width: FrameShiftUiMetrics.SecondaryButtonWidth);
        cancelButton.DialogResult = DialogResult.Cancel;
        footerPanel.Controls.Add(cancelButton);

        var applyButton = FrameShiftUiFactory.CreateActionButton("Apply", primary: true, width: FrameShiftUiMetrics.PrimaryButtonWidth);
        applyButton.DialogResult = DialogResult.OK;
        applyButton.Click += (_, _) =>
        {
            if (!ValidateCurrentSelection())
            {
                DialogResult = DialogResult.None;
            }
        };
        footerPanel.Controls.Add(applyButton);

        rootLayout.Controls.Add(header, 0, 0);
        rootLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
        rootLayout.Controls.Add(contentLayout, 0, 2);
        rootLayout.Controls.Add(footerPanel, 0, 3);
        Controls.Add(rootLayout);

        AcceptButton = applyButton;
        CancelButton = cancelButton;
        FrameShiftCropEditorUi.WireFooterLayout(this, footerPanel, cancelButton, applyButton);

        _styleControls =
        [
            _presetCombo,
            _fontCombo,
            _fontSizeUpDown,
            _positionCombo,
            _marginVerticalUpDown,
            _primaryColorButton,
            _highlightColorButton,
            _outlineColorButton,
            _shadowColorButton,
            _outlineUpDown,
            _shadowUpDown
        ];

        _previewDebounceTimer = new System.Windows.Forms.Timer { Interval = 180 };
        _previewDebounceTimer.Tick += async (_, _) =>
        {
            _previewDebounceTimer.Stop();
            await RenderPreviewAsync(_pendingPreviewSeconds).ConfigureAwait(true);
        };

        LoadAppearanceIntoControls();
        UpdateSourceKindState();
        _currentTimeLabel.Text = $"Time: {FormatTime(_pendingPreviewSeconds)}";
        _previewInfoLabel.Text = $"Display: {_probe.GetDisplayGeometrySummary()}";

        Shown += async (_, _) =>
        {
            SchedulePreviewRender();
            await Task.CompletedTask.ConfigureAwait(true);
        };

        FormClosing += (_, _) =>
        {
            _closing = true;
            _previewDebounceTimer.Stop();
            _previewRenderCts?.Cancel();
            _previewRenderCts?.Dispose();
            _previewRenderCts = null;
            _animatedPreviewCts?.Cancel();
            _animatedPreviewCts?.Dispose();
            _animatedPreviewCts = null;
            DisposeAnimatedPreviewMedia();
            DisposePreviewBitmap();
            _toolTip.Dispose();
        };

        ResumeLayout(true);
    }

    public AddSubtitlesToVideoSettings SelectedSettings =>
        new(_subtitlePath, AddSubtitlesToVideoMode.BurnIntoVideo, AddSubtitlesToVideoBurnSettings.FromAppearance(_appearance));

    internal bool IsStyleEditingEnabled => _styleEditingEnabled;

    internal string CurrentSubtitlePath => _subtitlePath;

    internal string CompatibilityWarningText => _compatibilityWarningLabel.Text;

    private void LoadAppearanceIntoControls()
    {
        _loadingControls = true;
        try
        {
            SelectComboValue(_presetCombo, _appearance.AssPreset.ToOptionValue());
            SelectComboValue(_fontCombo, _appearance.FontName);
            _fontSizeUpDown.Value = ClampDecimal(_appearance.FontSize, _fontSizeUpDown.Minimum, _fontSizeUpDown.Maximum);
            SelectComboValue(_positionCombo, _appearance.VerticalAlignment.ToOptionValue());
            _marginVerticalUpDown.Value = ClampDecimal(_appearance.MarginVertical, _marginVerticalUpDown.Minimum, _marginVerticalUpDown.Maximum);
            _outlineUpDown.Value = ClampDecimal((decimal)_appearance.OutlineThickness, _outlineUpDown.Minimum, _outlineUpDown.Maximum);
            _shadowUpDown.Value = ClampDecimal((decimal)_appearance.ShadowDepth, _shadowUpDown.Minimum, _shadowUpDown.Maximum);
            UpdateColorButton(_primaryColorButton, _appearance.PrimaryColor, "Text Color");
            UpdateColorButton(_highlightColorButton, _appearance.HighlightColor, "Highlight Color");
            UpdateColorButton(_outlineColorButton, _appearance.OutlineColor, "Outline Color");
            UpdateColorButton(_shadowColorButton, _appearance.ShadowColor, "Shadow Color");
            _subtitlePathTextBox.Text = _subtitlePath;
        }
        finally
        {
            _loadingControls = false;
        }
    }

    private void UpdateSourceKindState()
    {
        _sourceKind = AddSubtitlesToVideoSubtitleSourceLoader.DetectSourceKind(_subtitlePath);
        _styleEditingEnabled = _sourceKind != AddSubtitlesToVideoSubtitleSourceKind.Ass;
        _sourceKindLabel.Text = _sourceKind switch
        {
            AddSubtitlesToVideoSubtitleSourceKind.Ass => "Source type: ASS subtitle file (style passthrough)",
            AddSubtitlesToVideoSubtitleSourceKind.FrameShiftProject => "Source type: FrameShift subtitle project",
            _ => "Source type: SRT subtitle file"
        };

        foreach (var control in _styleControls)
        {
            control.Enabled = _styleEditingEnabled;
        }

        _styleDisabledLabel.Visible = !_styleEditingEnabled;
        _previewStatusLabel.Text = _styleEditingEnabled
            ? "Ready to render preview."
            : "Preview uses the external ASS style without modification.";
        _previewInfoLabel.Text = $"Display: {_probe.GetDisplayGeometrySummary()}";
        UpdateCompatibilityWarnings();
    }

    private void OnTimelineChanged()
    {
        var durationSeconds = _probe.Duration?.TotalSeconds ?? 0;
        if (durationSeconds <= 0)
        {
            _pendingPreviewSeconds = 0;
        }
        else
        {
            _pendingPreviewSeconds = durationSeconds * (_timelineBar.Value / 1000d);
        }

        _currentTimeLabel.Text = $"Time: {FormatTime(_pendingPreviewSeconds)}";
        SchedulePreviewRender();
    }

    private void OnAppearanceControlChanged()
    {
        if (_loadingControls)
        {
            return;
        }

        _appearance = BuildAppearanceFromControls();
        UpdateCompatibilityWarnings();
        if (_styleEditingEnabled)
        {
            SchedulePreviewRender();
        }
    }

    private void SchedulePreviewRender()
    {
        if (_closing || IsDisposed)
        {
            return;
        }

        StopAnimatedPreview(restoreFrame: false);
        _previewDebounceTimer.Stop();
        _previewDebounceTimer.Start();
        _previewStatusLabel.Text = "Rendering preview...";
        _previewImageBox.Refresh();
        _previewPanel.Invalidate();
    }

    private async Task RenderPreviewAsync(double seconds)
    {
        if (_closing || IsDisposed)
        {
            return;
        }

        _previewRenderCts?.Cancel();
        _previewRenderCts?.Dispose();
        var localCts = new CancellationTokenSource();
        _previewRenderCts = localCts;

        AddSubtitlesToVideoPreparedSubtitleInput? preparedInput = null;
        try
        {
            preparedInput = await AddSubtitlesToVideoSubtitleSourceLoader
                .PrepareAssInputAsync(
                    _subtitlePath,
                    _probe,
                    AddSubtitlesToVideoBurnSettings.FromAppearance(_appearance),
                    localCts.Token)
                .ConfigureAwait(true);

            var newBitmap = await PreviewFrameHelper.CaptureFrameAsync(
                _ffmpegPath,
                _ffmpegRunner,
                _inputPath,
                seconds,
                "Add Subtitles Burn Preview",
                AddSubtitlesToVideoAction.BuildAssVideoFilter(preparedInput.AssFilePath),
                localCts.Token).ConfigureAwait(true);

            if (_closing || IsDisposed || localCts.IsCancellationRequested || !ReferenceEquals(_previewRenderCts, localCts))
            {
                newBitmap.Dispose();
                return;
            }

            DisposePreviewBitmap();
            _previewBitmap = newBitmap;
            _previewImageBox.Image = _previewBitmap;
            _previewStatusLabel.Text = preparedInput.SourceKind == AddSubtitlesToVideoSubtitleSourceKind.Ass
                ? "Preview rendered from the external ASS style."
                : $"Preview rendered with {_appearance.AssPreset.GetDisplayName()}.";
            _previewInfoLabel.Text = $"Frame at {FormatTime(seconds)}";
            _previewPanel.Invalidate();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (localCts.IsCancellationRequested || !ReferenceEquals(_previewRenderCts, localCts))
            {
                return;
            }

            _previewStatusLabel.Text = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.PreviewGenerationFailed());
            DisposePreviewBitmap();
            _previewInfoLabel.Text = $"Display: {_probe.GetDisplayGeometrySummary()}";
            _previewPanel.Invalidate();
        }
        finally
        {
            if (preparedInput is not null && preparedInput.DeleteAfterUse)
            {
                ConversionActionHelper.DeleteIfExists(preparedInput.AssFilePath);
            }
        }
    }

    private void BrowseSubtitleFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select subtitle file",
            Filter = "Subtitle files (*.srt;*.ass;*.frameshift-subtitles.json)|*.srt;*.ass;*.frameshift-subtitles.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        try
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_subtitlePath);
        }
        catch
        {
        }

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        if (!AddSubtitlesToVideoSettings.IsSupportedSubtitleFilePath(dialog.FileName, AddSubtitlesToVideoMode.BurnIntoVideo))
        {
            MessageBox.Show(this, MediaActionMessages.AddSubtitlesToVideoBurnSubtitleFormatInvalid(), "FrameShift", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _subtitlePath = dialog.FileName;
        _subtitlePathTextBox.Text = _subtitlePath;
        UpdateSourceKindState();
        SchedulePreviewRender();
    }

    private AddSubtitlesToVideoBurnAppearance BuildAppearanceFromControls()
    {
        var preset = GetComboValue(_presetCombo, CreateSubtitlesAssPresets.Default.ToOptionValue());
        CreateSubtitlesAssPresets.TryParse(preset, out var parsedPreset);
        var position = GetComboValue(_positionCombo, AddSubtitlesToVideoVerticalAlignments.Default.ToOptionValue());
        AddSubtitlesToVideoVerticalAlignments.TryParse(position, out var parsedPosition);

        return new AddSubtitlesToVideoBurnAppearance(
            parsedPreset,
            GetComboValue(_fontCombo, _appearance.FontName),
            Decimal.ToInt32(_fontSizeUpDown.Value),
            _primaryColorButton.Tag as string ?? AddSubtitlesToVideoBurnAppearance.DefaultPrimaryColor,
            _highlightColorButton.Tag as string ?? AddSubtitlesToVideoBurnAppearance.DefaultHighlightColor,
            _outlineColorButton.Tag as string ?? AddSubtitlesToVideoBurnAppearance.DefaultOutlineColor,
            _shadowColorButton.Tag as string ?? AddSubtitlesToVideoBurnAppearance.DefaultShadowColor,
            Decimal.ToDouble(_outlineUpDown.Value),
            Decimal.ToDouble(_shadowUpDown.Value),
            parsedPosition,
            Decimal.ToInt32(_marginVerticalUpDown.Value));
    }

    private void PickColor(Button button, string title)
    {
        using var dialog = new ColorDialog
        {
            FullOpen = true,
            Color = AddSubtitlesToVideoBurnAppearance.ParseColor(button.Tag as string, Color.White)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var html = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        UpdateColorButton(button, html, title);
        OnAppearanceControlChanged();
    }

    private bool ValidateCurrentSelection()
    {
        if (string.IsNullOrWhiteSpace(_subtitlePath) || !File.Exists(_subtitlePath))
        {
            MessageBox.Show(this, MediaActionMessages.InputFileNotFound(_subtitlePath), "FrameShift", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        return true;
    }

    private async Task ToggleAnimatedPreviewAsync()
    {
        if (_animatedPreviewBusy)
        {
            return;
        }

        if (_animatedPreviewActive)
        {
            StopAnimatedPreview(restoreFrame: true);
            return;
        }

        if (!ValidateCurrentSelection())
        {
            return;
        }

        _animatedPreviewBusy = true;
        _animatedPreviewButton.Enabled = false;
        _previewDebounceTimer.Stop();
        _previewRenderCts?.Cancel();
        _previewRenderCts?.Dispose();
        _previewRenderCts = null;
        DisposeAnimatedPreviewMedia();

        _animatedPreviewCts?.Cancel();
        _animatedPreviewCts?.Dispose();
        var localCts = new CancellationTokenSource();
        _animatedPreviewCts = localCts;

        AddSubtitlesToVideoPreparedSubtitleInput? preparedInput = null;
        try
        {
            _previewStatusLabel.Text = "Rendering animated burn preview...";
            _previewInfoLabel.Text = $"Center: {FormatTime(_pendingPreviewSeconds)}";
            _previewPanel.Invalidate();

            preparedInput = await AddSubtitlesToVideoSubtitleSourceLoader
                .PrepareAssInputAsync(
                    _subtitlePath,
                    _probe,
                    AddSubtitlesToVideoBurnSettings.FromAppearance(_appearance),
                    localCts.Token)
                .ConfigureAwait(true);

            var plan = AddSubtitlesToVideoBurnPlanner.BuildPlan(Path.GetExtension(_inputPath), _probe);
            var clipWindow = BuildAnimatedPreviewWindow(_probe.Duration?.TotalSeconds ?? 0d, _pendingPreviewSeconds);
            var clipPath = Path.Combine(Path.GetTempPath(), $"frameshift_subtitles_preview_{Guid.NewGuid():N}{plan.TargetExtension}");
            var clipArguments = AddSubtitlesToVideoAction.BuildBurnArguments(
                _inputPath,
                preparedInput.AssFilePath,
                clipPath,
                plan,
                _probe.HasAudio,
                clipWindow);

            var estimatedFrameCount = _probe.VideoFrameRate is > 0d
                ? (long?)Math.Max(1L, (long)Math.Ceiling(_probe.VideoFrameRate.Value * clipWindow.DurationSeconds))
                : null;
            var clipResult = await _ffmpegRunner.RunAsync(
                _ffmpegPath,
                clipArguments,
                TimeSpan.FromSeconds(clipWindow.DurationSeconds),
                estimatedFrameCount,
                null,
                clipPath,
                "Burn Subtitles Animated Preview",
                "CPU",
                localCts.Token).ConfigureAwait(true);

            if (_closing || localCts.IsCancellationRequested || clipResult.Canceled)
            {
                ConversionActionHelper.DeleteIfExists(clipPath);
                return;
            }

            if (clipResult.ExitCode != 0 || !File.Exists(clipPath))
            {
                ConversionActionHelper.DeleteIfExists(clipPath);
                throw new InvalidOperationException(ConversionActionHelper.GetFriendlyFfmpegError(
                    clipResult.StandardError,
                    MediaActionMessages.BurnSubtitlesToVideoFailed()));
            }

            var gifPath = await PreviewFrameHelper.CreateAnimatedGifAsync(
                _ffmpegPath,
                _ffmpegRunner,
                clipPath,
                "Burn Subtitles Animated Preview",
                localCts.Token).ConfigureAwait(true);

            if (_closing || localCts.IsCancellationRequested || !ReferenceEquals(_animatedPreviewCts, localCts))
            {
                ConversionActionHelper.DeleteIfExists(gifPath);
                ConversionActionHelper.DeleteIfExists(clipPath);
                return;
            }

            ApplyAnimatedPreview(gifPath, clipPath);
            _animatedPreviewActive = true;
            _animatedPreviewButton.Text = "Stop Motion";
            _previewStatusLabel.Text = preparedInput.SourceKind == AddSubtitlesToVideoSubtitleSourceKind.Ass
                ? "Animated preview uses the external ASS style."
                : $"Animated preview loop ready with {_appearance.AssPreset.GetDisplayName()}.";
            _previewInfoLabel.Text = $"{FormatTime(clipWindow.StartSeconds)} -> {FormatTime(clipWindow.StartSeconds + clipWindow.DurationSeconds)}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _previewStatusLabel.Text = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.BurnSubtitlesToVideoFailed());
            _previewInfoLabel.Text = $"Display: {_probe.GetDisplayGeometrySummary()}";
            DisposeAnimatedPreviewMedia();
            _previewPanel.Invalidate();
        }
        finally
        {
            if (preparedInput is not null && preparedInput.DeleteAfterUse)
            {
                ConversionActionHelper.DeleteIfExists(preparedInput.AssFilePath);
            }

            _animatedPreviewBusy = false;
            _animatedPreviewButton.Enabled = true;
        }
    }

    private void StopAnimatedPreview(bool restoreFrame)
    {
        _animatedPreviewCts?.Cancel();
        _animatedPreviewCts?.Dispose();
        _animatedPreviewCts = null;
        _animatedPreviewActive = false;
        _animatedPreviewButton.Text = "Preview Motion";
        DisposeAnimatedPreviewMedia();

        if (!restoreFrame || _closing || IsDisposed)
        {
            return;
        }

        SchedulePreviewRender();
    }

    private void ApplyAnimatedPreview(string gifPath, string clipPath)
    {
        DisposeAnimatedPreviewMedia();
        DisposePreviewBitmap();

        var bytes = File.ReadAllBytes(gifPath);
        var stream = new MemoryStream();
        stream.Write(bytes, 0, bytes.Length);
        stream.Position = 0;

        _animatedPreviewImage = Image.FromStream(stream);
        _animatedPreviewStream = stream;
        _animatedPreviewGifPath = gifPath;
        _animatedPreviewClipPath = clipPath;
        _previewImageBox.Image = _animatedPreviewImage;
    }

    private void DisposeAnimatedPreviewMedia()
    {
        if (_previewImageBox.Image == _animatedPreviewImage)
        {
            _previewImageBox.Image = null;
        }

        if (_animatedPreviewImage is not null)
        {
            _animatedPreviewImage.Dispose();
            _animatedPreviewImage = null;
        }

        if (_animatedPreviewStream is not null)
        {
            _animatedPreviewStream.Dispose();
            _animatedPreviewStream = null;
        }

        if (!string.IsNullOrWhiteSpace(_animatedPreviewGifPath))
        {
            ConversionActionHelper.DeleteIfExists(_animatedPreviewGifPath);
            _animatedPreviewGifPath = null;
        }

        if (!string.IsNullOrWhiteSpace(_animatedPreviewClipPath))
        {
            ConversionActionHelper.DeleteIfExists(_animatedPreviewClipPath);
            _animatedPreviewClipPath = null;
        }
    }

    private void UpdateCompatibilityWarnings()
    {
        var warnings = new List<string>();
        if (_probe.IsHdrLikely)
        {
            warnings.Add("HDR source detected: libass burn-in re-encodes the video and may change colorimetry.");
        }

        if (_styleEditingEnabled && !string.IsNullOrWhiteSpace(_appearance.FontName) && !IsFontInstalled(_appearance.FontName))
        {
            warnings.Add("Selected font is not installed on this PC. libass will substitute a fallback font.");
        }

        _compatibilityWarningLabel.Text = string.Join("  ", warnings);
        _compatibilityWarningLabel.Visible = warnings.Count > 0;
    }

    private bool IsFontInstalled(string fontName)
    {
        return _installedFontFamilies.Contains(fontName.Trim());
    }

    private static AddSubtitlesToVideoBurnClipWindow BuildAnimatedPreviewWindow(double totalDurationSeconds, double centerSeconds)
    {
        const double previewDurationSeconds = 2.4d;

        if (totalDurationSeconds <= 0)
        {
            return new AddSubtitlesToVideoBurnClipWindow(0d, previewDurationSeconds);
        }

        var duration = Math.Min(previewDurationSeconds, Math.Max(0.2d, totalDurationSeconds));
        var start = Math.Max(0d, centerSeconds - (duration / 2d));
        var maxStart = Math.Max(0d, totalDurationSeconds - duration);
        if (start > maxStart)
        {
            start = maxStart;
        }

        return new AddSubtitlesToVideoBurnClipWindow(start, duration);
    }

    private void PreviewPanelOnPaint(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(Color.FromArgb(32, 32, 32));
        if (_previewImageBox.Image is not null)
        {
            return;
        }

        var message = string.IsNullOrWhiteSpace(_previewStatusLabel.Text)
            ? "Preview unavailable."
            : _previewStatusLabel.Text;
        TextRenderer.DrawText(
            e.Graphics,
            message,
            Font,
            _previewPanel.ClientRectangle,
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    private void DisposePreviewBitmap()
    {
        if (_previewImageBox.Image == _previewBitmap)
        {
            _previewImageBox.Image = null;
        }

        _previewBitmap?.Dispose();
        _previewBitmap = null;
    }

    private static Panel CreateStackedSection(string title, int height, out Panel contentHost)
    {
        var section = FrameShiftUiFactory.CreateFillSection(title, out contentHost);
        section.Dock = DockStyle.Fill;
        section.Height = height;
        section.Margin = new Padding(0, 0, 0, FrameShiftUiMetrics.OuterPadding);
        section.Padding = FrameShiftUiMetrics.StandardSectionPadding;
        return section;
    }

    private static TableLayoutPanel CreatePropertyGrid(int rows)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = rows
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        for (var index = 0; index < rows; index++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        }

        return layout;
    }

    private static void AddPropertyRow(TableLayoutPanel layout, int row, string labelText, Control control)
    {
        var label = FrameShiftUiFactory.CreateFieldLabel(labelText);
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private static ComboBox CreateComboBox()
    {
        var combo = FrameShiftUiFactory.CreateFixedComboBox(Point.Empty, new Size(120, 24));
        combo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        combo.Margin = new Padding(0, 4, 0, 4);
        return combo;
    }

    private static NumericUpDown CreateIntEditor(int minimum, int maximum, int increment)
    {
        return new NumericUpDown
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 4, 0, 4),
            AutoSize = false,
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            Height = 26
        };
    }

    private static NumericUpDown CreateDecimalEditor(decimal minimum, decimal maximum, int decimalPlaces, decimal increment)
    {
        return new NumericUpDown
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 4, 0, 4),
            AutoSize = false,
            Minimum = minimum,
            Maximum = maximum,
            DecimalPlaces = decimalPlaces,
            Increment = increment,
            Height = 26
        };
    }

    private Button CreateColorButton()
    {
        var button = FrameShiftUiFactory.CreateActionButton("Choose...", primary: false, width: 116);
        button.Dock = DockStyle.Fill;
        button.Margin = Padding.Empty;
        return button;
    }

    private void UpdateColorButton(Button button, string colorText, string title)
    {
        button.Tag = colorText;
        var color = AddSubtitlesToVideoBurnAppearance.ParseColor(colorText, Color.White);
        button.BackColor = color;
        button.ForeColor = GetContrastColor(color);
        button.Text = colorText;
        _toolTip.SetToolTip(button, title);
    }

    private static Color GetContrastColor(Color color)
    {
        var brightness = ((color.R * 299) + (color.G * 587) + (color.B * 114)) / 1000d;
        return brightness >= 140 ? Color.Black : Color.White;
    }

    private static void PopulatePresetCombo(ComboBox combo)
    {
        foreach (var preset in CreateSubtitlesAssPresets.GetAll())
        {
            combo.Items.Add(new ComboItem(preset.GetDisplayName(), preset.ToOptionValue()));
        }
    }

    private static void PopulatePositionCombo(ComboBox combo)
    {
        foreach (var alignment in new[]
                 {
                     AddSubtitlesToVideoVerticalAlignment.Bottom,
                     AddSubtitlesToVideoVerticalAlignment.Middle,
                     AddSubtitlesToVideoVerticalAlignment.Top
                 })
        {
            combo.Items.Add(new ComboItem(alignment.GetDisplayName(), alignment.ToOptionValue()));
        }
    }

    private void PopulateFontCombo(ComboBox combo, string initialFont)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(initialFont))
        {
            combo.Items.Add(new ComboItem(initialFont, initialFont));
            seen.Add(initialFont);
        }

        using var fontCollection = new InstalledFontCollection();
        foreach (var familyName in fontCollection.Families
                     .Select(static family => family.Name)
                     .OrderBy(static name => name, StringComparer.CurrentCultureIgnoreCase))
        {
            _installedFontFamilies.Add(familyName);
            if (!seen.Add(familyName))
            {
                continue;
            }

            combo.Items.Add(new ComboItem(familyName, familyName));
        }
    }

    private static void SelectComboValue(ComboBox combo, string value)
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is ComboItem item &&
                string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = index;
                return;
            }
        }

        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private static string GetComboValue(ComboBox combo, string fallback)
    {
        return (combo.SelectedItem as ComboItem)?.Value ?? fallback;
    }

    private static decimal ClampDecimal(decimal value, decimal minimum, decimal maximum)
    {
        if (value < minimum)
        {
            return minimum;
        }

        if (value > maximum)
        {
            return maximum;
        }

        return value;
    }

    private static string FormatTime(double seconds)
    {
        if (seconds < 0)
        {
            seconds = 0;
        }

        var time = TimeSpan.FromSeconds(seconds);
        if (time.TotalHours >= 1)
        {
            return time.ToString(@"hh\:mm\:ss\.fff");
        }

        return time.ToString(@"mm\:ss\.fff");
    }

    private sealed record ComboItem(string Text, string Value)
    {
        public override string ToString() => Text;
    }
}
