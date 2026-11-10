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
        _hasInjectedCapture = previewLoader is not null;
        _capturePreview = previewLoader ?? ((seconds, token) => PreviewFrameHelper.CaptureFrameAsync(ffmpegPath, ffmpegRunner, inputPath, seconds, L.Get("Loop_CutPreview"), token));
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
        FrameShiftWindowPolicy.Initialize(this, new Size(1120, CutClientHeight), new Size(480, 340));
        FrameShiftWindowChrome.Apply(this, L.Get("CutVideoForm_FrameShift_Cut_video_654b1c4b"));
        ControlHelper.SetDoubleBuffered(this);
        _previewPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(32, 32, 32), Margin = Padding.Empty };
        _previewBox = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, TabStop = false };
        _previewPanel.Controls.Add(_previewBox);
        var longestTime = CutVideoMath.FormatPreciseTime(probe.Duration?.TotalSeconds ?? _totalFrames / _fps);
        _labelPreviewState = new CutMessageLabel(
            L.Format("CutVideoForm_Preview_Previewing_0_frame_1_2_439822df", Capitalize("start"), _totalFrames, longestTime)
            + L.Format("Loop_NativePreview", Capitalize("end"), longestTime) + L.Get("Loop_LastIncluded"))
        { Text = L.Get("CutVideoForm_Preview_waiting_for_first_frame_b7c6d15a") };
        _labelSelection = new CutMessageLabel(
            L.Format("CutVideoForm_Selection_frame_0_to_1_2_3_4_frames_64942ba4", _totalFrames, _totalFrames, longestTime, longestTime, _totalFrames)
            + L.Get("Loop_ApproximateNumbers") + L.Get("Loop_IndexHint"));
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
        _selectionPanel.AccessibleName = L.Get("CutVideoForm_Frame_range_8c4e44f6");
        _selectionPanel.AccessibleDescription = L.Get("CutVideoForm_Drag_a_handle_or_edit_the_frame_and__2a5f25bf");
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
        var loopControls = CreateLoopControls(ffmpegPath, probe, ffmpegRunner);
        var options = FrameShiftUiFactory.CreateSection(L.Get("CutVideoForm_Selection_dfd52658"), FrameShiftUiFactory.CreateVerticalStack(
            CreateBoundaryRows(
                CreateBoundaryGroup(L.Get("CutVideoForm_Start_19a9997b"), _textStart, _buttonStartPrev, _buttonStartNext, _textStartTime),
                CreateBoundaryGroup(L.Get("CutVideoForm_End_a1c0e75a"), _textEnd, _buttonEndPrev, _buttonEndNext, _textEndTime)),
            _selectionPanel, _labelSelection, _labelPreviewState, loopControls));
        _cutSelectionControls = _selectionPanel;
        var buttonOk = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("Loop_ExportSelection"), true);
        _cutOk = buttonOk;
        buttonOk.Name = "exportSelection";
        buttonOk.Click += (_, _) => ConfirmSelection();
        var buttonCancel = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("CutVideoForm_Cancel_998b9c48"), false);
        buttonCancel.DialogResult = DialogResult.Cancel;
        var header = FrameShiftUiFactory.CreateHeader(L.Get("CutVideoForm_FrameShift_Cut_video_654b1c4b"),
            L.Format("CutVideoForm_Source_0_FPS_1_0_Frames_2_5338f9af",Path.GetFileName(_inputPath),_fps,_totalFrames),
            IconPaths.ContextMenuIco("cut-video-audio-icon.ico"), IconPaths.AppIcon, "✂");
        _cutActions = FrameShiftDialogLayout.CreateActions(buttonCancel, _exportAllLoops, buttonOk);
        _cutRoot = FrameShiftEditorShellUi.CreateTimeline(header, _previewPanel, options, _cutActions);
        _cutWorkspace = (FrameShift.Windows.Controls.FrameShiftTimelineWorkspace)_cutRoot.GetControlFromPosition(0, 1)!;
        _cutWorkspace.MinimumPreviewHeight = 180;
        BufferCutControls(_cutRoot);
        Controls.Add(_cutRoot);
        Load += (_, _) => FitCutWindowToContent();
        DpiChanged += (_, _) => BeginInvoke((Action)(() => FitCutWindowToContent()));
        AcceptButton = buttonOk;
        CancelButton = buttonCancel;
        UpdateLoopCommands();
        _previewTimer = new System.Windows.Forms.Timer { Interval = 140 };
        _previewTimer.Tick += PreviewTimerOnTick;
        InitializeNativeTiming();
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
        previous.AccessibleName = L.Format("CutVideoForm_Previous_0_frame_e6710963",title.ToLowerInvariant());
        next.AccessibleName = L.Format("CutVideoForm_Next_0_frame_6234baab",title.ToLowerInvariant());
        return FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateFieldRow(L.Format("CutVideoForm_0_frame_729889bd",title), frameEditor),
            FrameShiftUiFactory.CreateFieldRow(L.Format("CutVideoForm_0_time_8fe4affc",title), time));
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
        InvalidateNativeFrameEdits();
        if (_loopCandidate is not null && _candidateFrames != (_selection.StartFrame, _selection.EndFrame))
            MarkCustomLoopSelection();
        if (refreshPreview && _loopCandidate is null && _loopContext != LoopContext.Cut)
            _loopCustomSelection = true;
        if (refreshPreview) StopLoopPlayback();
        _uiState.UpdatingText = true;
        try
        {
            _textStart.Text = _selection.StartFrame.ToString(CultureInfo.InvariantCulture);
            _textEnd.Text = _selection.EndFrame.ToString(CultureInfo.InvariantCulture);
            _textStartTime.Text = CutVideoMath.FormatPreciseTime(_nativeStartTime ?? _loopCandidate?.StartSeconds ?? (_selection.StartFrame - 1) / _fps);
            _textEndTime.Text = CutVideoMath.FormatPreciseTime(_nativeEndTime ?? _loopCandidate?.EndSeconds ?? _selection.EndFrame / _fps);
        }
        finally
        {
            _uiState.UpdatingText = false;
        }

        var startTimeSeconds = _nativeStartTime ?? _loopCandidate?.StartSeconds ?? (_selection.StartFrame - 1) / _fps;
        var endTimeSeconds = _nativeEndTime ?? _loopCandidate?.EndSeconds ?? _selection.EndFrame / _fps;
        var selectedFrames = _selection.EndFrame - _selection.StartFrame + 1;
        var selectionText = L.Format("CutVideoForm_Selection_frame_0_to_1_2_3_4_frames_64942ba4",_selection.StartFrame,_selection.EndFrame,CutVideoMath.FormatPreciseTime(startTimeSeconds),CutVideoMath.FormatPreciseTime(endTimeSeconds),selectedFrames);
        if (_loopCandidate is not null) selectionText += NativeTimingEnabled && _nativeStartNumberKnown && _nativeEndNumberKnown
            ? L.Get("Loop_NativeNumbers") : L.Get("Loop_ApproximateNumbers");
        else if (_loopContext != LoopContext.Cut) selectionText += L.Get("Loop_CustomSelection");
        if (NativeTimingEnabled && (!_nativeStartNumberKnown || !_nativeEndNumberKnown))
            selectionText += L.Get("Loop_IndexHint");
        _labelSelection.Text = selectionText;
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
                throw new InvalidOperationException(L.Format("CutVideoForm_0_frame_must_be_an_integer_15224a16",Capitalize(target)));
            }

            frameValue = Math.Clamp(frameValue, 1, _totalFrames);
            if (frameValue == (target == "start" ? _selection.StartFrame : _selection.EndFrame))
            {
                _uiState.ActiveBoundary = target;
                return true;
            }

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
            var nativeTime = target == "start" ? _nativeStartTime : _nativeEndTime;
            if (NativeTimingEnabled && nativeTime is { } value && rawValue == CutVideoMath.FormatPreciseTime(value)) return true;
            if (_loopCandidate is { } currentCandidate && rawValue == CutVideoMath.FormatPreciseTime(
                target == "start" ? currentCandidate.StartSeconds : currentCandidate.EndSeconds)) return true;
            if (!CutVideoSettings.TryParseTimeText(rawValue, out var seconds))
            {
                throw new InvalidOperationException(string.Equals(target, "start", StringComparison.Ordinal)
                    ? MediaActionMessages.CutVideoStartTimeInvalid()
                    : MediaActionMessages.CutVideoEndTimeInvalid());
            }

            var frameValue = string.Equals(target, "start", StringComparison.Ordinal)
                ? CutVideoMath.ConvertStartTimeToFrame(seconds, _fps)
                : CutVideoMath.ConvertEndTimeToFrame(seconds, _fps);

            if (!NativeTimingEnabled && _loopCandidate is { } candidate && rawValue != CutVideoMath.FormatPreciseTime(
                target == "start" ? candidate.StartSeconds : candidate.EndSeconds)) MarkCustomLoopSelection();

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

            if (NativeTimingEnabled) ApplyNativeTimeEdit(target, seconds);

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
        if (NativeTimingEnabled) { _ = StepNativeBoundaryAsync(target, delta); return; }

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
        if (_loopBusy != LoopBusy.Idle || _nativeBusy || _closing || !CheckSourceStillValid()) return;
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

        return target switch { "start" => L.Get("CutVideo_Start"), "end" => L.Get("CutVideo_End"), _ => char.ToUpperInvariant(target[0]) + target[1..] };
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
