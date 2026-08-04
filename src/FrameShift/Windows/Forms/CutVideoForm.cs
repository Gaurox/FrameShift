using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class CutVideoForm : Form
{
    private const int StandardInlineButtonWidth = 28;
    private readonly string _inputPath;
    private readonly string _ffmpegPath;
    private readonly double _fps;
    private readonly int _totalFrames;
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly Panel _previewPanel;
    private readonly PictureBox _previewBox;
    private readonly TextBox _textStart;
    private readonly TextBox _textEnd;
    private readonly TextBox _textStartTime;
    private readonly TextBox _textEndTime;
    private readonly Label _labelSelection;
    private readonly Label _labelPreviewState;
    private readonly Label _labelHint;
    private readonly Button _buttonStartPrev;
    private readonly Button _buttonStartNext;
    private readonly Button _buttonEndPrev;
    private readonly Button _buttonEndNext;
    private readonly Panel _selectionPanel;
    private readonly System.Windows.Forms.Timer _previewTimer;
    private readonly SelectionState _selection;
    private readonly DragState _dragState;
    private readonly UiState _uiState;

    private Bitmap? _currentPreviewBitmap;
    private bool _busy;

    public CutVideoForm(
        string inputPath,
        string ffmpegPath,
        MediaProbeResult probe,
        FfmpegRunner ffmpegRunner)
    {
        _inputPath = inputPath;
        _ffmpegPath = ffmpegPath;
        _ffmpegRunner = ffmpegRunner;
        _fps = probe.VideoFrameRate ?? throw new InvalidOperationException(MediaActionMessages.VideoFrameRateUnavailable());
        _totalFrames = probe.EstimatedVideoFrameCount is long count && count > 0
            ? (int)Math.Min(int.MaxValue, count)
            : CutVideoMath.EstimateFrameCount(probe.Duration, _fps);

        if (_totalFrames <= 0)
        {
            throw new InvalidOperationException(MediaActionMessages.VideoFrameCountUnavailable());
        }

        _selection = new SelectionState(1, _totalFrames);
        _dragState = new DragState();
        _uiState = new UiState();

        SuspendLayout();

        FrameShiftWindowChrome.Apply(this, "FrameShift - Cut video");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        WindowState = FormWindowState.Normal;
        MinimumSize = new Size(980, 700);
        ClientSize = new Size(1040, 760);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        ControlHelper.SetDoubleBuffered(this);

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(FrameShiftUiMetrics.OuterPadding),
            ColumnCount = 1,
            RowCount = 8
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.HeaderHeight));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 196F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
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

        var selectionSection = CreateSectionPanel("Selection", out var selectionContentHost);
        selectionSection.Margin = Padding.Empty;
        selectionSection.Padding = FrameShiftUiMetrics.StandardSectionPadding;

        var selectionLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 3
        };
        selectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        selectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
        selectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        selectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var boundaryLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 3,
            RowCount = 1
        };
        boundaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        boundaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        boundaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        boundaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _textStart = CreateValueTextBox();
        _textStart.Leave += (_, _) => ApplyBoundaryFromText("start");
        _textStart.KeyDown += TextStartOnKeyDown;

        _buttonStartPrev = CreateInlineStepButton("<");
        _buttonStartPrev.Click += (_, _) => StepFrameBoundary("start", -1);

        _buttonStartNext = CreateInlineStepButton(">");
        _buttonStartNext.Click += (_, _) => StepFrameBoundary("start", 1);

        _textEnd = CreateValueTextBox();
        _textEnd.Leave += (_, _) => ApplyBoundaryFromText("end");
        _textEnd.KeyDown += TextEndOnKeyDown;

        _buttonEndPrev = CreateInlineStepButton("<");
        _buttonEndPrev.Click += (_, _) => StepFrameBoundary("end", -1);

        _buttonEndNext = CreateInlineStepButton(">");
        _buttonEndNext.Click += (_, _) => StepFrameBoundary("end", 1);

        _textStartTime = CreateValueTextBox();
        _textStartTime.Leave += (_, _) => ApplyBoundaryFromTimeText("start");
        _textStartTime.KeyDown += TextStartTimeOnKeyDown;

        _textEndTime = CreateValueTextBox();
        _textEndTime.Leave += (_, _) => ApplyBoundaryFromTimeText("end");
        _textEndTime.KeyDown += TextEndTimeOnKeyDown;

        var startGroup = CreateBoundaryGroup("Start", _textStart, _buttonStartPrev, _buttonStartNext, _textStartTime);
        var endGroup = CreateBoundaryGroup("End", _textEnd, _buttonEndPrev, _buttonEndNext, _textEndTime);
        boundaryLayout.Controls.Add(startGroup, 0, 0);
        boundaryLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 1, 0);
        boundaryLayout.Controls.Add(endGroup, 2, 0);

        _selectionPanel = CreateFramedPanel(FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, 8);
        _selectionPanel.Dock = DockStyle.Fill;
        _selectionPanel.Margin = Padding.Empty;
        ControlHelper.SetDoubleBuffered(_selectionPanel);
        _selectionPanel.Paint += SelectionPanelOnPaint;
        _selectionPanel.MouseDown += SelectionPanelOnMouseDown;
        _selectionPanel.MouseMove += SelectionPanelOnMouseMove;
        _selectionPanel.MouseUp += SelectionPanelOnMouseUp;

        selectionLayout.Controls.Add(boundaryLayout, 0, 0);
        selectionLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
        selectionLayout.Controls.Add(_selectionPanel, 0, 2);
        selectionContentHost.Controls.Add(selectionLayout);

        var hintCard = CreateInfoCardPanel();
        hintCard.Margin = new Padding(0, FrameShiftUiMetrics.OuterPadding, 0, 0);
        hintCard.Padding = new Padding(FrameShiftUiMetrics.OuterPadding, FrameShiftUiMetrics.LineGap, FrameShiftUiMetrics.OuterPadding, FrameShiftUiMetrics.LineGap);

        var hintLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 2
        };
        hintLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        hintLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
        hintLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));

        _labelSelection = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            AutoEllipsis = true,
            ForeColor = FrameShiftTheme.TextPrimary
        };

        _labelHint = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            AutoEllipsis = true,
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = "Drag a handle on the range bar, or edit the frame and time fields directly."
        };

        hintLayout.Controls.Add(_labelSelection, 0, 0);
        hintLayout.Controls.Add(_labelHint, 0, 1);
        hintCard.Controls.Add(hintLayout);

        var buttonOk = CreateActionButton("OK", primary: true);
        buttonOk.Click += (_, _) => ConfirmCut();

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
        rootLayout.Controls.Add(selectionSection, 0, 4);
        rootLayout.Controls.Add(hintCard, 0, 5);
        rootLayout.Controls.Add(footerPanel, 0, 7);

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
            _previewTimer.Stop();
            _previewTimer.Dispose();
            DisposePreviewBitmap();
        };

        InitializeWorkspace();
        ResumeLayout(true);
    }

    public CutVideoSettings? Selection { get; private set; }

    private Panel CreateHeaderPanel()
    {
        return FrameShiftUiFactory.CreateFillHeader(
            "FrameShift - Cut video",
            $"Source: {Path.GetFileName(_inputPath)}    FPS: {_fps:0.###}    Frames: {_totalFrames.ToString(CultureInfo.InvariantCulture)}",
            IconPaths.ContextMenuIco("cut-video-audio-icon.ico"),
            IconPaths.AppIcon,
            "✂",
            900);
    }

    private static Panel CreateBoundaryGroup(string title, TextBox frameTextBox, Button previousButton, Button nextButton, TextBox timeTextBox)
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
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

        var titleLabel = new Label
        {
            Text = $"{title} frame",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ForeColor = FrameShiftTheme.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var timeLabel = new Label
        {
            Text = $"{title} time",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ForeColor = FrameShiftTheme.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var frameHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        frameHost.Controls.Add(frameTextBox);
        frameHost.Controls.Add(previousButton);
        frameHost.Controls.Add(nextButton);
        frameHost.Resize += (_, _) => LayoutFrameEditor(frameHost, frameTextBox, previousButton, nextButton);
        LayoutFrameEditor(frameHost, frameTextBox, previousButton, nextButton);

        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(frameHost, 1, 0);
        layout.Controls.Add(timeLabel, 0, 1);
        layout.Controls.Add(timeTextBox, 1, 1);

        panel.Controls.Add(layout);
        return panel;
    }

    private static void LayoutFrameEditor(Control host, Control textBox, Control previousButton, Control nextButton)
    {
        if (host.ClientSize.Width <= 0 || host.ClientSize.Height <= 0)
        {
            return;
        }

        var rightButtonsWidth = (StandardInlineButtonWidth * 2) + FrameShiftUiMetrics.LineGap;
        var textWidth = Math.Max(0, host.ClientSize.Width - rightButtonsWidth - FrameShiftUiMetrics.LineGap);
        var verticalOffset = Math.Max(0, (host.ClientSize.Height - 26) / 2);

        textBox.SetBounds(0, verticalOffset, textWidth, 26);
        previousButton.SetBounds(textWidth + FrameShiftUiMetrics.LineGap, verticalOffset, StandardInlineButtonWidth, 26);
        nextButton.SetBounds(textWidth + FrameShiftUiMetrics.LineGap + StandardInlineButtonWidth + FrameShiftUiMetrics.LineGap, verticalOffset, StandardInlineButtonWidth, 26);
    }

    private static TextBox CreateValueTextBox()
    {
        var textBox = FrameShiftUiFactory.CreateValueTextBox();
        textBox.Margin = Padding.Empty;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        return textBox;
    }

    private static Button CreateInlineStepButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Margin = Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point),
            UseVisualStyleBackColor = false,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.AccentText
        };
        button.FlatAppearance.BorderColor = FrameShiftTheme.PrimaryBlue;
        button.FlatAppearance.MouseOverBackColor = FrameShiftTheme.AccentSoft;
        button.FlatAppearance.MouseDownBackColor = FrameShiftTheme.AccentSoftHover;
        return button;
    }

    private static Panel CreateSectionPanel(string title, out Panel contentHost)
    {
        return FrameShiftUiFactory.CreateFillSection(title, out contentHost);
    }

    private static Panel CreateInfoCardPanel()
    {
        return FrameShiftUiFactory.CreateFillInfoCard();
    }

    private static Button CreateActionButton(string text, bool primary)
    {
        return FrameShiftUiFactory.CreateActionButton(text, primary, primary ? FrameShiftUiMetrics.PrimaryButtonWidth : FrameShiftUiMetrics.SecondaryButtonWidth);
    }

    private static void UpdateFooterButtonLayout(Control footerPanel, Button cancelButton, Button okButton)
    {
        if (footerPanel.ClientSize.Width <= 0 || footerPanel.ClientSize.Height <= 0)
        {
            return;
        }

        FrameShiftUiLayout.LayoutFooterButtons(footerPanel, cancelButton, okButton, FrameShiftUiMetrics.LineGap);
    }

    private static Panel CreateFramedPanel(Color backgroundColor, Color borderColor, int radius)
    {
        return FrameShiftUiFactory.CreateFramedPanel(backgroundColor, borderColor, radius);
    }

    private void InitializeWorkspace()
    {
        RefreshSelectionUi(refreshPreview: false);
        LoadPreviewFrame(1);
    }

    private void LoadPreviewFrame(int frameNumber)
    {
        _busy = true;
        try
        {
            var seconds = Math.Max(0d, (frameNumber - 1) / _fps);
            var bitmap = PreviewFrameHelper.CaptureFrameAsync(
                _ffmpegPath,
                _ffmpegRunner,
                _inputPath,
                seconds,
                "Cut Video Preview",
                CancellationToken.None).GetAwaiter().GetResult();

            DisposePreviewBitmap();
            _currentPreviewBitmap = bitmap;
            _previewBox.Image = _currentPreviewBitmap;
            _labelPreviewState.Text = $"Previewing {Capitalize(_uiState.ActiveBoundary)} frame: {frameNumber} ({CutVideoMath.FormatPreciseTime(seconds)})";
        }
        finally
        {
            _busy = false;
        }
    }

    private void SchedulePreviewUpdate(int frameNumber)
    {
        _uiState.PendingPreviewFrame = frameNumber;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void RefreshSelectionUi(bool refreshPreview)
    {
        _uiState.UpdatingText = true;
        try
        {
            _textStart.Text = _selection.StartFrame.ToString(CultureInfo.InvariantCulture);
            _textEnd.Text = _selection.EndFrame.ToString(CultureInfo.InvariantCulture);
            _textStartTime.Text = CutVideoMath.FormatPreciseTime((_selection.StartFrame - 1) / _fps);
            _textEndTime.Text = CutVideoMath.FormatPreciseTime(_selection.EndFrame / _fps);
        }
        finally
        {
            _uiState.UpdatingText = false;
        }

        var startTimeSeconds = (_selection.StartFrame - 1) / _fps;
        var endTimeSeconds = _selection.EndFrame / _fps;
        var selectedFrames = _selection.EndFrame - _selection.StartFrame + 1;
        _labelSelection.Text = $"Selection: frame {_selection.StartFrame} to {_selection.EndFrame}  |  {CutVideoMath.FormatPreciseTime(startTimeSeconds)} -> {CutVideoMath.FormatPreciseTime(endTimeSeconds)}  |  {selectedFrames} frames";
        _selectionPanel.Invalidate();

        if (refreshPreview)
        {
            var frameToPreview = string.Equals(_uiState.ActiveBoundary, "end", StringComparison.Ordinal)
                ? _selection.EndFrame
                : _selection.StartFrame;
            SchedulePreviewUpdate(frameToPreview);
        }
    }

    private bool ApplyBoundaryFromText(string target)
    {
        if (_busy || _uiState.UpdatingText)
        {
            return true;
        }

        try
        {
            var rawValue = string.Equals(target, "start", StringComparison.Ordinal) ? _textStart.Text : _textEnd.Text;
            if (!int.TryParse(rawValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var frameValue))
            {
                throw new InvalidOperationException($"{Capitalize(target)} frame must be an integer.");
            }

            frameValue = Math.Clamp(frameValue, 1, _totalFrames);

            if (string.Equals(target, "start", StringComparison.Ordinal))
            {
                if (frameValue > _selection.EndFrame)
                {
                    frameValue = _selection.EndFrame;
                }

                _selection.StartFrame = frameValue;
            }
            else
            {
                if (frameValue < _selection.StartFrame)
                {
                    frameValue = _selection.StartFrame;
                }

                _selection.EndFrame = frameValue;
            }

            _uiState.ActiveBoundary = target;
            RefreshSelectionUi(true);
            return true;
        }
        catch (Exception ex)
        {
            ShowActionError(ex.Message);
            RefreshSelectionUi(false);
            return false;
        }
    }

    private bool ApplyBoundaryFromTimeText(string target)
    {
        if (_busy || _uiState.UpdatingText)
        {
            return true;
        }

        try
        {
            var rawValue = string.Equals(target, "start", StringComparison.Ordinal) ? _textStartTime.Text : _textEndTime.Text;
            if (!CutVideoSettings.TryParseTimeText(rawValue, out var seconds))
            {
                throw new InvalidOperationException(string.Equals(target, "start", StringComparison.Ordinal)
                    ? MediaActionMessages.CutVideoStartTimeInvalid()
                    : MediaActionMessages.CutVideoEndTimeInvalid());
            }

            var frameValue = string.Equals(target, "start", StringComparison.Ordinal)
                ? CutVideoMath.ConvertStartTimeToFrame(seconds, _fps)
                : CutVideoMath.ConvertEndTimeToFrame(seconds, _fps);

            if (string.Equals(target, "start", StringComparison.Ordinal))
            {
                if (frameValue > _selection.EndFrame)
                {
                    frameValue = _selection.EndFrame;
                }

                _selection.StartFrame = frameValue;
            }
            else
            {
                if (frameValue < _selection.StartFrame)
                {
                    frameValue = _selection.StartFrame;
                }

                _selection.EndFrame = frameValue;
            }

            _uiState.ActiveBoundary = target;
            RefreshSelectionUi(true);
            return true;
        }
        catch (Exception ex)
        {
            ShowActionError(ex.Message);
            RefreshSelectionUi(false);
            return false;
        }
    }

    private void StepFrameBoundary(string target, int delta)
    {
        if (_busy)
        {
            return;
        }

        if (string.Equals(target, "start", StringComparison.Ordinal))
        {
            var frameValue = _selection.StartFrame + delta;
            if (frameValue < 1)
            {
                frameValue = 1;
            }

            if (frameValue > _selection.EndFrame)
            {
                frameValue = _selection.EndFrame;
            }

            _selection.StartFrame = frameValue;
        }
        else
        {
            var frameValue = _selection.EndFrame + delta;
            if (frameValue < _selection.StartFrame)
            {
                frameValue = _selection.StartFrame;
            }

            if (frameValue > _totalFrames)
            {
                frameValue = _totalFrames;
            }

            _selection.EndFrame = frameValue;
        }

        _uiState.ActiveBoundary = target;
        RefreshSelectionUi(true);
    }

    private void ConfirmCut()
    {
        try
        {
            if (!ApplyBoundaryFromText("start") || !ApplyBoundaryFromText("end"))
            {
                return;
            }

            if (_selection.EndFrame < _selection.StartFrame)
            {
                throw new InvalidOperationException(MediaActionMessages.CutVideoEndInvalid());
            }

            StopPreview();
            Selection = new CutVideoSettings(_selection.StartFrame, _selection.EndFrame, _fps);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            ShowActionError(ex.Message);
        }
    }

    private void StopPreview()
    {
        if (_currentPreviewBitmap is not null)
        {
            _previewBox.Image = null;
            _currentPreviewBitmap.Dispose();
            _currentPreviewBitmap = null;
        }
    }

    private void DisposePreviewBitmap()
    {
        StopPreview();
    }

    private void ShowActionError(string message)
    {
        MessageBox.Show(this, message, "FrameShift", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void PreviewTimerOnTick(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        try
        {
            LoadPreviewFrame(_uiState.PendingPreviewFrame);
        }
        catch (Exception ex)
        {
            ShowActionError(ex.Message);
        }
    }

    private void SelectionPanelOnPaint(object? sender, PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var trackLeft = 18;
        var trackTop = 38;
        var trackWidth = Math.Max(1, _selectionPanel.ClientSize.Width - 36);
        var trackHeight = 6;
        var handleWidth = 10;
        var handleHeight = 28;

        var startX = ConvertFrameToTrackX(_selection.StartFrame);
        var endX = ConvertFrameToTrackX(_selection.EndFrame);
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
            graphics.DrawLine(tickPen, tickX, 18, tickX, 27);
        }

        graphics.FillRectangle(startBrush, startHandleX, startHandleY, handleWidth, handleHeight);
        graphics.FillRectangle(endBrush, endHandleX, endHandleY, handleWidth, handleHeight);
        graphics.DrawRectangle(handleBorderPen, startHandleX, startHandleY, handleWidth, handleHeight);
        graphics.DrawRectangle(handleBorderPen, endHandleX, endHandleY, handleWidth, handleHeight);

        graphics.DrawString("1", Font, labelBrush, 14, 2);
        var lastFrameText = _totalFrames.ToString(CultureInfo.InvariantCulture);
        var lastFrameSize = graphics.MeasureString(lastFrameText, Font);
        graphics.DrawString(lastFrameText, Font, labelBrush, _selectionPanel.ClientSize.Width - lastFrameSize.Width - 14, 2);
    }

    private void SelectionPanelOnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _busy)
        {
            return;
        }

        var startX = ConvertFrameToTrackX(_selection.StartFrame);
        var endX = ConvertFrameToTrackX(_selection.EndFrame);
        var distanceToStart = Math.Abs(e.X - startX);
        var distanceToEnd = Math.Abs(e.X - endX);

        _dragState.Active = true;
        _dragState.Target = distanceToStart <= distanceToEnd ? "start" : "end";
        _uiState.ActiveBoundary = _dragState.Target;
        ((Control)sender!).Capture = true;

        var frame = ConvertTrackXToFrame(e.X);
        if (_dragState.Target == "start")
        {
            if (frame > _selection.EndFrame)
            {
                frame = _selection.EndFrame;
            }

            _selection.StartFrame = frame;
        }
        else
        {
            if (frame < _selection.StartFrame)
            {
                frame = _selection.StartFrame;
            }

            _selection.EndFrame = frame;
        }

        RefreshSelectionUi(true);
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

        var frame = ConvertTrackXToFrame(e.X);
        if (_dragState.Target == "start")
        {
            if (frame > _selection.EndFrame)
            {
                frame = _selection.EndFrame;
            }

            _selection.StartFrame = frame;
        }
        else
        {
            if (frame < _selection.StartFrame)
            {
                frame = _selection.StartFrame;
            }

            _selection.EndFrame = frame;
        }

        RefreshSelectionUi(true);
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

    private void TextStartOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            ApplyBoundaryFromText("start");
            e.SuppressKeyPress = true;
        }
    }

    private void TextEndOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            ApplyBoundaryFromText("end");
            e.SuppressKeyPress = true;
        }
    }

    private void TextStartTimeOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            ApplyBoundaryFromTimeText("start");
            e.SuppressKeyPress = true;
        }
    }

    private void TextEndTimeOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            ApplyBoundaryFromTimeText("end");
            e.SuppressKeyPress = true;
        }
    }

    private int ConvertFrameToTrackX(int frame)
    {
        var trackLeft = 18;
        var trackWidth = _selectionPanel.ClientSize.Width - 36;
        if (_totalFrames <= 1)
        {
            return trackLeft;
        }

        var ratio = (frame - 1) / (double)(_totalFrames - 1);
        return (int)Math.Round(trackLeft + (ratio * trackWidth));
    }

    private int ConvertTrackXToFrame(int x)
    {
        var trackLeft = 18;
        var trackWidth = _selectionPanel.ClientSize.Width - 36;
        if (trackWidth <= 0 || _totalFrames <= 1)
        {
            return 1;
        }

        var clamped = Math.Min(trackLeft + trackWidth, Math.Max(trackLeft, x));
        var ratio = (clamped - trackLeft) / (double)trackWidth;
        var frame = 1 + (int)Math.Round(ratio * (_totalFrames - 1));
        return Math.Min(_totalFrames, Math.Max(1, frame));
    }

    private static string Capitalize(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return string.Empty;
        }

        return char.ToUpperInvariant(target[0]) + target[1..];
    }

    private sealed class SelectionState
    {
        public SelectionState(int startFrame, int endFrame)
        {
            StartFrame = startFrame;
            EndFrame = endFrame;
        }

        public int StartFrame { get; set; }

        public int EndFrame { get; set; }
    }

    private sealed class DragState
    {
        public bool Active { get; set; }

        public string Target { get; set; } = string.Empty;
    }

    private sealed class UiState
    {
        public string ActiveBoundary { get; set; } = "start";

        public int PendingPreviewFrame { get; set; } = 1;

        public bool UpdatingText { get; set; }
    }
}
