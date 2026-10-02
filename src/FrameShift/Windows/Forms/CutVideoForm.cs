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

public sealed partial class CutVideoForm : Form
{
    private const int StandardInlineButtonWidth = 28;
    private readonly string _inputPath;
    private readonly double _fps;
    private readonly int _totalFrames;
    private readonly Panel _previewPanel;
    private readonly PictureBox _previewBox;
    private readonly TextBox _textStart;
    private readonly TextBox _textEnd;
    private readonly TextBox _textStartTime;
    private readonly TextBox _textEndTime;
    private readonly Label _labelSelection;
    private readonly Label _labelPreviewState;
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


    public CutVideoForm(string inputPath, string ffmpegPath, MediaProbeResult probe, FfmpegRunner ffmpegRunner)
        : this(inputPath, ffmpegPath, probe, ffmpegRunner, null) { }

    internal CutVideoForm(string inputPath, string ffmpegPath, MediaProbeResult probe, FfmpegRunner ffmpegRunner,
        Func<double, CancellationToken, Task<Bitmap>>? previewLoader)
    {
        _inputPath = inputPath;
        _capturePreview = previewLoader ?? ((seconds, token) => PreviewFrameHelper.CaptureFrameAsync(ffmpegPath, ffmpegRunner, inputPath, seconds, "Cut Video Preview", token));
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
        FrameShiftWindowPolicy.Initialize(this, new Size(1120, 760), new Size(480, 340));
        FrameShiftWindowChrome.Apply(this, "FrameShift - Cut video");
        ControlHelper.SetDoubleBuffered(this);
        _previewPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(32, 32, 32), Margin = Padding.Empty };
        _previewBox = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, TabStop = false };
        _previewPanel.Controls.Add(_previewBox);
        _labelPreviewState = FrameShiftUiFactory.CreateWrappingLabel("Preview: waiting for first frame...");
        _labelSelection = FrameShiftUiFactory.CreateWrappingLabel("");
        _textStart = new TextBox { Name = "startFrame" };
        _textEnd = new TextBox { Name = "endFrame" };
        _textStartTime = new TextBox { Name = "startTime" };
        _textEndTime = new TextBox { Name = "endTime" };
        _textStart.Leave += (_, _) => ApplyBoundaryFromText("start");
        _textEnd.Leave += (_, _) => ApplyBoundaryFromText("end");
        _textStartTime.Leave += (_, _) => ApplyBoundaryFromTimeText("start");
        _textEndTime.Leave += (_, _) => ApplyBoundaryFromTimeText("end");
        _textStart.KeyDown += TextStartOnKeyDown;
        _textEnd.KeyDown += TextEndOnKeyDown;
        _textStartTime.KeyDown += TextStartTimeOnKeyDown;
        _textEndTime.KeyDown += TextEndTimeOnKeyDown;
        _buttonStartPrev = CreateInlineStepButton("<");
        _buttonStartNext = CreateInlineStepButton(">");
        _buttonEndPrev = CreateInlineStepButton("<");
        _buttonEndNext = CreateInlineStepButton(">");
        _buttonStartPrev.Click += (_, _) => StepFrameBoundary("start", -1);
        _buttonStartNext.Click += (_, _) => StepFrameBoundary("start", 1);
        _buttonEndPrev.Click += (_, _) => StepFrameBoundary("end", -1);
        _buttonEndNext.Click += (_, _) => StepFrameBoundary("end", 1);
        _selectionPanel = FrameShiftUiFactory.CreateFramedPanel(FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, 8);
        _selectionPanel.Name = "frameRange";
        _selectionPanel.AccessibleName = "Frame range";
        _selectionPanel.AccessibleDescription = "Drag a handle, or edit the frame and time fields above.";
        _selectionPanel.Dock = DockStyle.Top;
        _selectionPanel.Height = 80;
        _selectionPanel.HandleCreated += (_, _) => _selectionPanel.Height = FrameShiftUiMetrics.ToPixels(_selectionPanel, 80);
        _selectionPanel.DpiChangedAfterParent += (_, _) =>
        {
            _selectionPanel.Height = FrameShiftUiMetrics.ToPixels(_selectionPanel, 80);
            _selectionPanel.Invalidate();
        };
        ControlHelper.SetDoubleBuffered(_selectionPanel);
        _selectionPanel.Paint += SelectionPanelOnPaint;
        _selectionPanel.MouseDown += SelectionPanelOnMouseDown;
        _selectionPanel.MouseMove += SelectionPanelOnMouseMove;
        _selectionPanel.MouseUp += SelectionPanelOnMouseUp;
        var options = FrameShiftUiFactory.CreateSection("Selection", FrameShiftUiFactory.CreateVerticalStack(
            CreateBoundaryRows(
                CreateBoundaryGroup("Start", _textStart, _buttonStartPrev, _buttonStartNext, _textStartTime),
                CreateBoundaryGroup("End", _textEnd, _buttonEndPrev, _buttonEndNext, _textEndTime)),
            _selectionPanel, _labelSelection, _labelPreviewState));
        var buttonOk = FrameShiftUiFactory.CreateMeasuredActionButton("OK", true);
        buttonOk.Click += (_, _) => ConfirmCut();
        var buttonCancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        buttonCancel.DialogResult = DialogResult.Cancel;
        var header = FrameShiftUiFactory.CreateHeader("FrameShift - Cut video",
            $"Source: {Path.GetFileName(_inputPath)}    FPS: {_fps:0.###}    Frames: {_totalFrames}",
            IconPaths.ContextMenuIco("cut-video-audio-icon.ico"), IconPaths.AppIcon, "✂");
        Controls.Add(FrameShiftEditorShellUi.CreateTimeline(header, _previewPanel, options,
            FrameShiftDialogLayout.CreateActions(buttonCancel, buttonOk)));
        AcceptButton = buttonOk;
        CancelButton = buttonCancel;
        _previewTimer = new System.Windows.Forms.Timer { Interval = 140 };
        _previewTimer.Tick += PreviewTimerOnTick;
        FormClosing += CloseAfterPreviewAsync;
        Shown += async (_, _) => await RequestPreviewAsync(1);
        RefreshSelectionUi(refreshPreview: false);
        ResumeLayout(true);
    }

    public CutVideoSettings? Selection { get; private set; }

    private static TableLayoutPanel CreateBoundaryRows(Control start, Control end)
    {
        var rows = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Margin = Padding.Empty, Size = Size.Empty };
        rows.Controls.Add(start);
        rows.Controls.Add(end);
        bool? stacked = null;
        var arranging = false;
        rows.Layout += (_, _) =>
        {
            if (arranging) return;
            arranging = true;
            try
            {
                var narrow = rows.Width < FrameShiftUiMetrics.ToPixels(rows, 640);
                if (stacked == narrow) return;
                stacked = narrow;
                rows.SuspendLayout();
                rows.ColumnStyles.Clear();
                rows.RowStyles.Clear();
                rows.ColumnCount = narrow ? 1 : 2;
                rows.RowCount = narrow ? 2 : 1;
                rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, narrow ? 100 : 50));
                rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                if (narrow) rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                else rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                rows.SetCellPosition(start, new TableLayoutPanelCellPosition(0, 0));
                rows.SetCellPosition(end, new TableLayoutPanelCellPosition(narrow ? 0 : 1, narrow ? 1 : 0));
                rows.ResumeLayout(true);
            }
            finally { arranging = false; }
        };
        return rows;
    }

    private static TableLayoutPanel CreateBoundaryGroup(string title, TextBox frame, Button previous, Button next, TextBox time)
    {
        var frameEditor = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        frameEditor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        frameEditor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        frameEditor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        frameEditor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        frame.Dock = DockStyle.Fill;
        frameEditor.Controls.Add(frame, 0, 0);
        frameEditor.Controls.Add(previous, 1, 0);
        frameEditor.Controls.Add(next, 2, 0);
        previous.AccessibleName = $"Previous {title.ToLowerInvariant()} frame";
        next.AccessibleName = $"Next {title.ToLowerInvariant()} frame";
        return FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateFieldRow($"{title} frame", frameEditor),
            FrameShiftUiFactory.CreateFieldRow($"{title} time", time));
    }

    private static Button CreateInlineStepButton(string text)
    {
        var button = new FrameShift.Windows.Controls.FrameShiftActionButton(StandardInlineButtonWidth)
        {
            Text = text, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false, BackColor = FrameShiftTheme.Surface, ForeColor = FrameShiftTheme.AccentText
        };
        button.FlatAppearance.BorderColor = FrameShiftTheme.PrimaryBlue;
        button.FlatAppearance.MouseOverBackColor = FrameShiftTheme.AccentSoft;
        button.FlatAppearance.MouseDownBackColor = FrameShiftTheme.AccentSoftHover;
        return button;
    }

    private void SchedulePreviewUpdate(int frameNumber)
    {
        if (_closing) return;
        _previewCancellation?.Cancel();
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
        if (_closing || _uiState.UpdatingText)
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
        if (_closing || _uiState.UpdatingText)
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
        if (_closing)
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

    private async void PreviewTimerOnTick(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        await RequestPreviewAsync(_uiState.PendingPreviewFrame);
    }
    private void SelectionPanelOnPaint(object? sender, PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var trackLeft = TrackPixels(18);
        var trackTop = TrackPixels(38);
        var trackWidth = Math.Max(1, _selectionPanel.ClientSize.Width - TrackPixels(36));
        var trackHeight = TrackPixels(6);
        var handleWidth = TrackPixels(10);
        var handleHeight = TrackPixels(28);

        var startX = ConvertFrameToTrackX(_selection.StartFrame);
        var endX = ConvertFrameToTrackX(_selection.EndFrame);
        if (endX < startX)
        {
            (startX, endX) = (endX, startX);
        }

        var selectionWidth = Math.Max(TrackPixels(2), endX - startX);
        var startHandleX = startX - (handleWidth / 2);
        var startHandleY = trackTop - TrackPixels(11);
        var endHandleX = endX - (handleWidth / 2);
        var endHandleY = trackTop - TrackPixels(11);

        using var trackBrush = new SolidBrush(FrameShiftTheme.SurfaceBorder);
        using var selectionBrush = new SolidBrush(FrameShiftTheme.PrimaryBlue);
        using var startBrush = new SolidBrush(string.Equals(_uiState.ActiveBoundary, "start", StringComparison.Ordinal)
            ? FrameShiftTheme.SecondaryBlue
            : FrameShiftTheme.PrimaryBlue);
        using var endBrush = new SolidBrush(string.Equals(_uiState.ActiveBoundary, "end", StringComparison.Ordinal)
            ? FrameShiftTheme.SecondaryBlue
            : FrameShiftTheme.PrimaryBlue);
        using var handleBorderPen = new Pen(FrameShiftTheme.SecondaryBlue, TrackPixels(1));
        using var tickPen = new Pen(FrameShiftTheme.TextMuted, TrackPixels(1));
        using var labelBrush = new SolidBrush(FrameShiftTheme.TextSecondary);

        graphics.FillRectangle(trackBrush, trackLeft, trackTop, trackWidth, trackHeight);
        graphics.FillRectangle(selectionBrush, startX, trackTop - TrackPixels(4), selectionWidth, TrackPixels(14));

        for (var index = 0; index <= 10; index++)
        {
            var tickX = trackLeft + (int)Math.Round((trackWidth * index) / 10.0);
            graphics.DrawLine(tickPen, tickX, TrackPixels(18), tickX, TrackPixels(27));
        }

        graphics.FillRectangle(startBrush, startHandleX, startHandleY, handleWidth, handleHeight);
        graphics.FillRectangle(endBrush, endHandleX, endHandleY, handleWidth, handleHeight);
        graphics.DrawRectangle(handleBorderPen, startHandleX, startHandleY, handleWidth, handleHeight);
        graphics.DrawRectangle(handleBorderPen, endHandleX, endHandleY, handleWidth, handleHeight);

        graphics.DrawString("1", Font, labelBrush, TrackPixels(14), TrackPixels(2));
        var lastFrameText = _totalFrames.ToString(CultureInfo.InvariantCulture);
        var lastFrameSize = graphics.MeasureString(lastFrameText, Font);
        graphics.DrawString(lastFrameText, Font, labelBrush, _selectionPanel.ClientSize.Width - lastFrameSize.Width - TrackPixels(14), TrackPixels(2));
    }

    private void SelectionPanelOnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _closing)
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
        if (_closing)
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

    private int TrackPixels(int logical) => FrameShiftUiMetrics.ToPixels(_selectionPanel, logical);

    private int ConvertFrameToTrackX(int frame)
    {
        var trackLeft = TrackPixels(18);
        var trackWidth = _selectionPanel.ClientSize.Width - TrackPixels(36);
        if (_totalFrames <= 1)
        {
            return trackLeft;
        }

        var ratio = (frame - 1) / (double)(_totalFrames - 1);
        return (int)Math.Round(trackLeft + (ratio * trackWidth));
    }

    private int ConvertTrackXToFrame(int x)
    {
        var trackLeft = TrackPixels(18);
        var trackWidth = _selectionPanel.ClientSize.Width - TrackPixels(36);
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
