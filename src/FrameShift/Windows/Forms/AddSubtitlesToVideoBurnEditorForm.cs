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
    private readonly EditorPreviewLifetime _previewLifetime = new();
    private bool _allowClose;
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

        FrameShiftWindowPolicy.Initialize(this, new Size(1240, 820), new Size(480, 340));
        FrameShiftWindowChrome.Apply(this, L.Get("AddSubtitlesToVideoBurnEditorForm_FrameShift_Burn_Subtitles_Into_Video_963601d2"), IconPaths.AddSubtitlesVideoAiIcon, IconPaths.AppIcon);
        ControlHelper.SetDoubleBuffered(this);
        _previewPanel = FrameShiftCropEditorUi.CreatePreviewPanel();
        _previewPanel.Paint += PreviewPanelOnPaint;
        ControlHelper.SetDoubleBuffered(_previewPanel);
        _previewImageBox = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
        _previewImageBox.AccessibleName = L.Get("AddSubtitlesToVideoBurnEditorForm_Subtitles_video_preview_3a57ab61");
        _previewImageBox.TabStop = false;
        _previewPanel.Controls.Add(_previewImageBox);
        _sourceKindLabel = FrameShiftUiFactory.CreateWrappingLabel("");
        _currentTimeLabel = FrameShiftUiFactory.CreateWrappingLabel("");
        _previewStatusLabel = FrameShiftUiFactory.CreateWrappingLabel("");
        _previewInfoLabel = FrameShiftUiFactory.CreateWrappingLabel("");
        _timelineBar = new SeekTrackBar { Minimum = 0, Maximum = 1000, TickFrequency = 100, SmallChange = 5, LargeChange = 50 };
        _timelineBar.ValueChanged += (_, _) => OnTimelineChanged();
        _animatedPreviewButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("AddSubtitlesToVideoBurnEditorForm_Preview_Motion_71be51d3"), false);
        _animatedPreviewButton.Click += async (_, _) => await ToggleAnimatedPreviewAsync();
        var temporal = FrameShiftUiFactory.CreateVerticalStack(_timelineBar, _currentTimeLabel,
            FrameShiftUiFactory.CreateChoiceRow(_animatedPreviewButton), _previewStatusLabel, _previewInfoLabel);
        var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 1, RowCount = 2 };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workspace.Controls.Add(_previewPanel, 0, 0);
        workspace.Controls.Add(temporal, 0, 1);
        _subtitlePathTextBox = new TextBox { ReadOnly = true };
        var browse = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("AddSubtitlesToVideoBurnEditorForm_Browse_28211d48"), false);
        browse.Click += (_, _) => BrowseSubtitleFile();
        var source = FrameShiftUiFactory.CreateSection(L.Get("AddSubtitlesToVideoBurnEditorForm_Subtitle_source_820711d5"), FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateFieldRow(L.Get("AddSubtitlesToVideoBurnEditorForm_File_9588151a"), _subtitlePathTextBox),
            FrameShiftUiFactory.CreateChoiceRow(browse), _sourceKindLabel));
        _presetCombo = CreateComboBox();
        PopulatePresetCombo(_presetCombo);
        _fontCombo = CreateComboBox();
        PopulateFontCombo(_fontCombo, _appearance.FontName);
        _positionCombo = CreateComboBox();
        PopulatePositionCombo(_positionCombo);
        _fontSizeUpDown = CreateIntEditor(12, 240, 1);
        _marginVerticalUpDown = CreateIntEditor(0, 600, 2);
        _outlineUpDown = CreateDecimalEditor(0, 12, 1, 0.1M);
        _shadowUpDown = CreateDecimalEditor(0, 12, 1, 0.1M);
        foreach (var combo in new[] { _presetCombo, _fontCombo, _positionCombo })
            combo.SelectedIndexChanged += (_, _) => OnAppearanceControlChanged();
        foreach (var number in new[] { _fontSizeUpDown, _marginVerticalUpDown, _outlineUpDown, _shadowUpDown })
            number.ValueChanged += (_, _) => OnAppearanceControlChanged();
        _styleDisabledLabel = FrameShiftUiFactory.CreateWrappingLabel(L.Get("AddSubtitlesToVideoBurnEditorForm_External_ASS_the_existing_file_style_99a07154"));
        var style = FrameShiftUiFactory.CreateSection(L.Get("AddSubtitlesToVideoBurnEditorForm_Style_d2b183b7"), FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateFieldRow(L.Get("AddSubtitlesToVideoBurnEditorForm_Preset_25ab893c"), _presetCombo),
            FrameShiftUiFactory.CreateFieldRow(L.Get("AddSubtitlesToVideoBurnEditorForm_Font_b77d0e29"), _fontCombo),
            FrameShiftUiFactory.CreateFieldRow(L.Get("AddSubtitlesToVideoBurnEditorForm_Size_0e196b48"), _fontSizeUpDown, logicalEditorWidth: 128),
            FrameShiftUiFactory.CreateFieldRow(L.Get("AddSubtitlesToVideoBurnEditorForm_Position_ea7dfd5d"), _positionCombo),
            FrameShiftUiFactory.CreateFieldRow(L.Get("AddSubtitlesToVideoBurnEditorForm_Vertical_margin_fa1d3277"), _marginVerticalUpDown, logicalEditorWidth: 128), _styleDisabledLabel));
        _primaryColorButton = CreateColorButton();
        _primaryColorButton.Click += (_, _) => PickColor(_primaryColorButton, L.Get("AddSubtitlesToVideoBurnEditorForm_Text_Color_a2d455a8"));
        _highlightColorButton = CreateColorButton();
        _highlightColorButton.Click += (_, _) => PickColor(_highlightColorButton, L.Get("AddSubtitlesToVideoBurnEditorForm_Highlight_Color_49ff8acb"));
        _outlineColorButton = CreateColorButton();
        _outlineColorButton.Click += (_, _) => PickColor(_outlineColorButton, L.Get("AddSubtitlesToVideoBurnEditorForm_Outline_Color_f097354a"));
        _shadowColorButton = CreateColorButton();
        _shadowColorButton.Click += (_, _) => PickColor(_shadowColorButton, L.Get("AddSubtitlesToVideoBurnEditorForm_Shadow_Color_deb1d12e"));
        var colors = FrameShiftUiFactory.CreateSection(L.Get("AddSubtitlesToVideoBurnEditorForm_Colors_effects_ca57d573"), FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateFieldRow(L.Get("AddSubtitlesToVideoBurnEditorForm_Text_b9b20f15"), _primaryColorButton),
            FrameShiftUiFactory.CreateFieldRow(L.Get("AddSubtitlesToVideoBurnEditorForm_Highlight_03d9a520"), _highlightColorButton),
            FrameShiftUiFactory.CreateFieldRow(L.Get("AddSubtitlesToVideoBurnEditorForm_Outline_color_493fdff2"), _outlineColorButton),
            FrameShiftUiFactory.CreateFieldRow(L.Get("AddSubtitlesToVideoBurnEditorForm_Shadow_color_2347d706"), _shadowColorButton),
            FrameShiftUiFactory.CreateFieldRow(L.Get("AddSubtitlesToVideoBurnEditorForm_Outline_bb43fc5f"), _outlineUpDown, logicalEditorWidth: 128),
            FrameShiftUiFactory.CreateFieldRow(L.Get("AddSubtitlesToVideoBurnEditorForm_Shadow_c444117d"), _shadowUpDown, logicalEditorWidth: 128)));
        _compatibilityWarningLabel = FrameShiftUiFactory.CreateWrappingLabel("");
        _compatibilityWarningLabel.ForeColor = Color.FromArgb(168, 72, 32);
        _compatibilityWarningLabel.Visible = false;
        var cancelButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("AddSubtitlesToVideoBurnEditorForm_Cancel_998b9c48"), false);
        cancelButton.DialogResult = DialogResult.Cancel;
        var applyButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("AddSubtitlesToVideoBurnEditorForm_Apply_3d57c12e"), true);
        applyButton.DialogResult = DialogResult.OK;
        applyButton.Click += (_, _) => { if (!ValidateCurrentSelection()) DialogResult = DialogResult.None; };
        var header = FrameShiftUiFactory.CreateHeader(L.Get("AddSubtitlesToVideoBurnEditorForm_FrameShift_Burn_Subtitles_Into_Video_963601d2"), L.Format("AddSubtitlesToVideoBurnEditorForm_Source_0_653ab450",Path.GetFileName(inputPath)),
            IconPaths.AddSubtitlesVideoAiIcon, IconPaths.FrameShiftAiIcon, "S");
        Controls.Add(FrameShiftEditorShellUi.Create(header, workspace, FrameShiftDialogLayout.CreateActions(cancelButton, applyButton),
            FrameShiftUiFactory.CreateVerticalStack(source, style, colors, _compatibilityWarningLabel), logicalRailWidth: 400));
        AcceptButton = applyButton;
        CancelButton = cancelButton;
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
        _currentTimeLabel.Text = L.Format("AddSubtitlesToVideoBurnEditorForm_Time_0_e0ccbce1",FormatTime(_pendingPreviewSeconds));
        _previewInfoLabel.Text = L.Format("AddSubtitlesToVideoBurnEditorForm_Display_0_207db77a",_probe.GetDisplayGeometrySummary());

        Shown += async (_, _) =>
        {
            SchedulePreviewRender();
            await Task.CompletedTask.ConfigureAwait(true);
        };

        FormClosing += CloseAfterPreviewAsync;
        ResumeLayout(true);
    }

    private async void CloseAfterPreviewAsync(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        var result = DialogResult;
        _previewDebounceTimer.Stop();
        Enabled = false;
        await _previewLifetime.CloseAsync();
        if (IsDisposed) return;
        // Restore the modal result after the first FormClosing has returned.
        BeginInvoke(new Action(() =>
        {
            if (IsDisposed) return;
            _allowClose = true;
            DialogResult = result;
            Close();
        }));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closing = true;
            _previewLifetime.Dispose();
            _previewDebounceTimer?.Dispose();
            DisposeAnimatedPreviewMedia();
            DisposePreviewBitmap();
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
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
            UpdateColorButton(_primaryColorButton, _appearance.PrimaryColor, L.Get("AddSubtitlesToVideoBurnEditorForm_Text_Color_a2d455a8"));
            UpdateColorButton(_highlightColorButton, _appearance.HighlightColor, L.Get("AddSubtitlesToVideoBurnEditorForm_Highlight_Color_49ff8acb"));
            UpdateColorButton(_outlineColorButton, _appearance.OutlineColor, L.Get("AddSubtitlesToVideoBurnEditorForm_Outline_Color_f097354a"));
            UpdateColorButton(_shadowColorButton, _appearance.ShadowColor, L.Get("AddSubtitlesToVideoBurnEditorForm_Shadow_Color_deb1d12e"));
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
            AddSubtitlesToVideoSubtitleSourceKind.Ass => L.Get("AddSubtitlesToVideoBurnEditorForm_Source_type_ASS_subtitle_file_style__a7117dc8"),
            AddSubtitlesToVideoSubtitleSourceKind.FrameShiftProject => L.Get("AddSubtitlesToVideoBurnEditorForm_Source_type_FrameShift_subtitle_proj_c82fdbed"),
            _ => L.Get("AddSubtitlesToVideoBurnEditorForm_Source_type_SRT_subtitle_file_f1be3582")
        };

        foreach (var control in _styleControls)
        {
            control.Enabled = _styleEditingEnabled;
        }

        _styleDisabledLabel.Visible = !_styleEditingEnabled;
        _previewStatusLabel.Text = _styleEditingEnabled
            ? L.Get("AddSubtitlesToVideoBurnEditorForm_Ready_to_render_preview_5b867106")
            : L.Get("AddSubtitlesToVideoBurnEditorForm_Preview_uses_the_external_ASS_style__0d0ba303");
        _previewInfoLabel.Text = L.Format("AddSubtitlesToVideoBurnEditorForm_Display_0_207db77a",_probe.GetDisplayGeometrySummary());
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

        _currentTimeLabel.Text = L.Format("AddSubtitlesToVideoBurnEditorForm_Time_0_e0ccbce1",FormatTime(_pendingPreviewSeconds));
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
        _previewStatusLabel.Text = L.Get("AddSubtitlesToVideoBurnEditorForm_Rendering_preview_c20ee915");
        _previewImageBox.Refresh();
        _previewPanel.Invalidate();
    }

    internal Task RenderPreviewAsync(double seconds) => _previewLifetime.RunAsync(token => RenderFrameCoreAsync(seconds, token));

    private async Task RenderFrameCoreAsync(double seconds, CancellationToken token)
    {
        AddSubtitlesToVideoPreparedSubtitleInput? preparedInput = null;
        try
        {
            preparedInput = await AddSubtitlesToVideoSubtitleSourceLoader
                .PrepareAssInputAsync(
                    _subtitlePath,
                    _probe,
                    AddSubtitlesToVideoBurnSettings.FromAppearance(_appearance),
                    token)
                .ConfigureAwait(true);

            var newBitmap = await PreviewFrameHelper.CaptureFrameAsync(
                _ffmpegPath,
                _ffmpegRunner,
                _inputPath,
                seconds,
                "Add Subtitles Burn Preview",
                AddSubtitlesToVideoAction.BuildAssVideoFilter(preparedInput.AssFilePath),
                token).ConfigureAwait(true);

            if (_closing || IsDisposed || token.IsCancellationRequested)
            {
                newBitmap.Dispose();
                return;
            }

            DisposePreviewBitmap();
            _previewBitmap = newBitmap;
            _previewImageBox.Image = _previewBitmap;
            _previewStatusLabel.Text = preparedInput.SourceKind == AddSubtitlesToVideoSubtitleSourceKind.Ass
                ? L.Get("AddSubtitlesToVideoBurnEditorForm_Preview_rendered_from_the_external_A_0ab0a204")
                : L.Format("AddSubtitlesToVideoBurnEditorForm_Preview_rendered_with_0_ac02704d",_appearance.AssPreset.GetDisplayName());
            _previewInfoLabel.Text = L.Format("AddSubtitlesToVideoBurnEditorForm_Frame_at_0_b4e03165",FormatTime(seconds));
            _previewPanel.Invalidate();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (_closing || IsDisposed || token.IsCancellationRequested)
            {
                return;
            }

            _previewStatusLabel.Text = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.PreviewGenerationFailed());
            DisposePreviewBitmap();
            _previewInfoLabel.Text = L.Format("AddSubtitlesToVideoBurnEditorForm_Display_0_207db77a",_probe.GetDisplayGeometrySummary());
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
            Title = L.Get("AddSubtitlesToVideoBurnEditorForm_Select_subtitle_file_12034b67"),
            Filter = L.Get("AddSubtitlesToVideoBurnEditorForm_Subtitle_files_srt_ass_frameshift_su_208c0351"),
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

        _previewDebounceTimer.Stop();
        DisposeAnimatedPreviewMedia();
        await _previewLifetime.RunAsync(RenderMotionCoreAsync);
    }

    private async Task RenderMotionCoreAsync(CancellationToken token)
    {
        _animatedPreviewBusy = true;
        _animatedPreviewButton.Enabled = false;
        string? clipPath = null;
        string? gifPath = null;
        var transferred = false;
        AddSubtitlesToVideoPreparedSubtitleInput? preparedInput = null;
        try
        {
            _previewStatusLabel.Text = L.Get("AddSubtitlesToVideoBurnEditorForm_Rendering_animated_burn_preview_e0dc8605");
            _previewInfoLabel.Text = L.Format("AddSubtitlesToVideoBurnEditorForm_Center_0_6f32da7b",FormatTime(_pendingPreviewSeconds));
            _previewPanel.Invalidate();

            preparedInput = await AddSubtitlesToVideoSubtitleSourceLoader
                .PrepareAssInputAsync(
                    _subtitlePath,
                    _probe,
                    AddSubtitlesToVideoBurnSettings.FromAppearance(_appearance),
                    token)
                .ConfigureAwait(true);

            var plan = AddSubtitlesToVideoBurnPlanner.BuildPlan(Path.GetExtension(_inputPath), _probe);
            var clipWindow = BuildAnimatedPreviewWindow(_probe.Duration?.TotalSeconds ?? 0d, _pendingPreviewSeconds);
            clipPath = Path.Combine(Path.GetTempPath(), $"frameshift_subtitles_preview_{Guid.NewGuid():N}{plan.TargetExtension}");
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
                token).ConfigureAwait(true);

            if (_closing || token.IsCancellationRequested || clipResult.Canceled)
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

            gifPath = await PreviewFrameHelper.CreateAnimatedGifAsync(
                _ffmpegPath,
                _ffmpegRunner,
                clipPath,
                "Burn Subtitles Animated Preview",
                token).ConfigureAwait(true);

            if (_closing || token.IsCancellationRequested)
            {
                ConversionActionHelper.DeleteIfExists(gifPath);
                ConversionActionHelper.DeleteIfExists(clipPath);
                return;
            }

            ApplyAnimatedPreview(gifPath, clipPath);
            transferred = true;
            _animatedPreviewActive = true;
            _animatedPreviewButton.Text = L.Get("AddSubtitlesToVideoBurnEditorForm_Stop_Motion_8800b2d0");
            _previewStatusLabel.Text = preparedInput.SourceKind == AddSubtitlesToVideoSubtitleSourceKind.Ass
                ? L.Get("AddSubtitlesToVideoBurnEditorForm_Animated_preview_uses_the_external_A_5054e879")
                : L.Format("AddSubtitlesToVideoBurnEditorForm_Animated_preview_loop_ready_with_0_374093c5",_appearance.AssPreset.GetDisplayName());
            _previewInfoLabel.Text = $"{FormatTime(clipWindow.StartSeconds)} -> {FormatTime(clipWindow.StartSeconds + clipWindow.DurationSeconds)}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (_closing || IsDisposed || token.IsCancellationRequested) return;
            _previewStatusLabel.Text = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.BurnSubtitlesToVideoFailed());
            _previewInfoLabel.Text = L.Format("AddSubtitlesToVideoBurnEditorForm_Display_0_207db77a",_probe.GetDisplayGeometrySummary());
            DisposeAnimatedPreviewMedia();
            _previewPanel.Invalidate();
        }
        finally
        {
            if (preparedInput is not null && preparedInput.DeleteAfterUse)
            {
                ConversionActionHelper.DeleteIfExists(preparedInput.AssFilePath);
            }

            if (!transferred)
            {
                ConversionActionHelper.DeleteIfExists(clipPath ?? string.Empty);
                ConversionActionHelper.DeleteIfExists(gifPath ?? string.Empty);
            }
            if (!_closing && !IsDisposed) { _animatedPreviewBusy = false; _animatedPreviewButton.Enabled = true; }
        }
    }

    private void StopAnimatedPreview(bool restoreFrame)
    {
        _previewLifetime.Cancel();
        _animatedPreviewActive = false;
        _animatedPreviewButton.Text = L.Get("AddSubtitlesToVideoBurnEditorForm_Preview_Motion_71be51d3");
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

        try { _animatedPreviewImage = Image.FromStream(stream); }
        catch { stream.Dispose(); throw; }
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
            warnings.Add(L.Get("AddSubtitlesToVideoBurnEditorForm_HDR_source_detected_libass_burn_in_r_f1f68925"));
        }

        if (_styleEditingEnabled && !string.IsNullOrWhiteSpace(_appearance.FontName) && !IsFontInstalled(_appearance.FontName))
        {
            warnings.Add(L.Get("AddSubtitlesToVideoBurnEditorForm_Selected_font_is_not_installed_on_th_e12ea7a0"));
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
            ? L.Get("AddSubtitlesToVideoBurnEditorForm_Preview_unavailable_667b1159")
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

    private static ComboBox CreateComboBox()
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
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
            AutoSize = true,
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary
        };
    }

    private static NumericUpDown CreateDecimalEditor(decimal minimum, decimal maximum, int decimalPlaces, decimal increment)
    {
        return new NumericUpDown
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 4, 0, 4),
            AutoSize = true,
            Minimum = minimum,
            Maximum = maximum,
            DecimalPlaces = decimalPlaces,
            Increment = increment,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary
        };
    }

    private Button CreateColorButton()
    {
        var button = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("AddSubtitlesToVideoBurnEditorForm_Choose_cd873176"), false);
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
