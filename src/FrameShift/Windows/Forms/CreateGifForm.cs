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
    private readonly EditorPreviewLifetime _previewLifetime = new();
    private readonly Func<double, CancellationToken, Task<Bitmap>> _capturePreview;
    private bool _allowClose;
    private bool _busy;
    private bool _closing;

    public CreateGifForm(
        string inputPath,
        string ffmpegPath,
        MediaProbeResult probe,
        FfmpegRunner ffmpegRunner) : this(inputPath, ffmpegPath, probe, ffmpegRunner, null) { }

    internal CreateGifForm(string inputPath, string ffmpegPath, MediaProbeResult probe, FfmpegRunner ffmpegRunner,
        Func<double, CancellationToken, Task<Bitmap>>? previewLoader)
    {
        AppLogger.LogStatic("CreateGifForm: constructor entered.");
        _inputPath = inputPath;
        _ffmpegPath = ffmpegPath;
        _probe = probe;
        _ffmpegRunner = ffmpegRunner;
        _capturePreview = previewLoader ?? ((seconds, token) => PreviewFrameHelper.CaptureFrameAsync(
            ffmpegPath, ffmpegRunner, inputPath, seconds, "Create GIF Preview", token));

        if (_probe.Duration is null || _probe.Duration.Value.TotalSeconds <= 0)
        {
            throw new InvalidOperationException(MediaActionMessages.DurationUnavailable());
        }

        _selection = new SelectionState(0d, _probe.Duration.Value.TotalSeconds);
        _dragState = new DragState();
        _uiState = new UiState();

        SuspendLayout();

        FrameShiftWindowPolicy.Initialize(this, new Size(1040, 740), new Size(480, 340));
        FrameShiftWindowChrome.Apply(this, "FrameShift - Create GIF");
        ControlHelper.SetDoubleBuffered(this);
        _previewPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(32, 32, 32) };
        _previewPanel.AccessibleName = "GIF frame preview";
        _previewBox = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
        _previewPanel.Controls.Add(_previewBox);
        _labelPreviewState = FrameShiftUiFactory.CreateWrappingLabel("Preview: waiting for first frame...");
        _labelSelectionSummary = FrameShiftUiFactory.CreateWrappingLabel("");
        _labelPreviewStatus = FrameShiftUiFactory.CreateWrappingLabel("");
        _selectionPanel = FrameShiftUiFactory.CreateFramedPanel(FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, 8);
        _selectionPanel.Dock = DockStyle.Top;
        _selectionPanel.Height = 74;
        void RangeMetrics() { _selectionPanel.Height = FrameShiftUiMetrics.ToPixels(_selectionPanel, 74); _selectionPanel.Invalidate(); }
        _selectionPanel.HandleCreated += (_, _) => RangeMetrics();
        _selectionPanel.DpiChangedAfterParent += (_, _) => RangeMetrics();
        _selectionPanel.AccessibleName = "GIF time range";
        ControlHelper.SetDoubleBuffered(_selectionPanel);
        _selectionPanel.Paint += SelectionPanelOnPaint;
        _selectionPanel.MouseDown += SelectionPanelOnMouseDown;
        _selectionPanel.MouseMove += SelectionPanelOnMouseMove;
        _selectionPanel.MouseUp += SelectionPanelOnMouseUp;
        _textStartFrame = new TextBox { ReadOnly = true };
        _textEndFrame = new TextBox { ReadOnly = true };
        _textStart = new TextBox { ReadOnly = true };
        _textEnd = new TextBox { ReadOnly = true };
        _textDuration = new TextBox { ReadOnly = true };
        _comboResolution = CreatePresetComboBox(CreateGifSettings.GetResolutionItems(), CreateGifSettings.DefaultResolutionKey);
        _comboFps = CreatePresetComboBox(CreateGifSettings.GetFpsItems(), CreateGifSettings.DefaultFps.ToString(CultureInfo.InvariantCulture));
        _comboQuality = CreatePresetComboBox(CreateGifSettings.GetQualityItems(), CreateGifSettings.DefaultQualityKey);
        _comboResolution.SelectedIndexChanged += (_, _) => StopGifPreview(true);
        _comboFps.SelectedIndexChanged += (_, _) => { StopGifPreview(true); RefreshSelectionUi(true); };
        _comboQuality.SelectedIndexChanged += (_, _) => StopGifPreview(true);
        _buttonPreview = FrameShiftUiFactory.CreateMeasuredActionButton("Preview GIF", false);
        _buttonPreview.Click += async (_, _) => await TogglePreviewAsync();
        var fields = FrameShiftUiFactory.CreateChoiceRow(
            FrameShiftUiFactory.CreateVerticalStack(
                FrameShiftUiFactory.CreateFieldRow("Start frame", _textStartFrame, logicalEditorWidth: 120),
                FrameShiftUiFactory.CreateFieldRow("Start time", _textStart, logicalEditorWidth: 120)),
            FrameShiftUiFactory.CreateVerticalStack(
                FrameShiftUiFactory.CreateFieldRow("End frame", _textEndFrame, logicalEditorWidth: 120),
                FrameShiftUiFactory.CreateFieldRow("End time", _textEnd, logicalEditorWidth: 120)),
            FrameShiftUiFactory.CreateFieldRow("Duration", _textDuration, logicalEditorWidth: 120));
        var presets = FrameShiftUiFactory.CreateChoiceRow(
            FrameShiftUiFactory.CreateFieldRow("Resolution", _comboResolution, logicalEditorWidth: 160),
            FrameShiftUiFactory.CreateFieldRow("FPS", _comboFps, logicalEditorWidth: 96),
            FrameShiftUiFactory.CreateFieldRow("Quality", _comboQuality, logicalEditorWidth: 160));
        var options = FrameShiftUiFactory.CreateSection("GIF settings", FrameShiftUiFactory.CreateVerticalStack(
            _selectionPanel, fields, presets, _labelSelectionSummary,
            FrameShiftUiFactory.CreateChoiceRow(_buttonPreview), _labelPreviewStatus, _labelPreviewState));
        var ok = FrameShiftUiFactory.CreateMeasuredActionButton("Create GIF", true);
        ok.Click += (_, _) => ConfirmSelection();
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        cancel.DialogResult = DialogResult.Cancel;
        var header = FrameShiftUiFactory.CreateHeader("FrameShift - Create GIF",
            $"Source: {Path.GetFileName(inputPath)}    Video: {probe.VideoWidth} × {probe.VideoHeight}    Duration: {CutAudioSettings.FormatDisplayTime(_probe.Duration.Value.TotalSeconds)}",
            IconPaths.ContextMenuIco("create-gif-video-icon.ico"), IconPaths.AppIcon, "GIF");
        Controls.Add(FrameShiftEditorShellUi.CreateTimeline(header, _previewPanel, options,
            FrameShiftDialogLayout.CreateActions(cancel, ok)));
        AcceptButton = ok;
        CancelButton = cancel;
        _previewTimer = new System.Windows.Forms.Timer { Interval = 140 };
        _previewTimer.Tick += PreviewTimerOnTick;
        FormClosing += CloseAfterPreviewAsync;
        Shown += async (_, _) => await RequestFrameAsync(0d);
        RefreshSelectionUi(false);
        ResumeLayout(true);
    }

    public CreateGifSettings? Selection { get; private set; }
    private static ComboBox CreatePresetComboBox(System.Collections.Generic.IReadOnlyList<CreateGifPresetItem> items, string selectedKey)
    {
        var comboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary
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

    internal Task RequestFrameAsync(double seconds) => _previewLifetime.RunAsync(async token =>
    {
        Bitmap? bitmap = null;
        try
        {
            var safeTime = Math.Clamp(seconds, 0d, Math.Max(0d, _probe.Duration!.Value.TotalSeconds - 0.001d));
            bitmap = await _capturePreview(safeTime, token);
            token.ThrowIfCancellationRequested();
            if (_closing || IsDisposed) return;
            DisposePreviewBitmap();
            _currentPreviewBitmap = bitmap;
            _previewBox.Image = bitmap;
            bitmap = null;
            _labelPreviewState.Text = $"Previewing {Capitalize(_uiState.ActiveBoundary)} boundary: {CutAudioSettings.FormatDisplayTime(safeTime)}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { if (!_closing && !IsDisposed) _labelPreviewState.Text = $"Preview unavailable: {ex.Message}"; }
        finally { bitmap?.Dispose(); }
    });

    private async void CloseAfterPreviewAsync(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        var result = DialogResult;
        _previewTimer.Stop();
        Enabled = false;
        await _previewLifetime.CloseAsync();
        if (IsDisposed) return;
        // A completed preview can resume inline inside FormClosing. Let WinForms
        // finish cancelling that close before restoring the modal result.
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
            _previewTimer?.Dispose();
            DisposeGifPreview();
            DisposePreviewBitmap();
        }
        base.Dispose(disposing);
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

    private Task StartGifPreviewAsync()
    {
        StopGifPreview(false);
        return _previewLifetime.RunAsync(RenderGifPreviewAsync);
    }

    private async Task RenderGifPreviewAsync(CancellationToken token)
    {
        _busy = true;
        _buttonPreview.Enabled = false;
        _labelPreviewStatus.Text = "Rendering GIF preview...";
        var previewPath = Path.Combine(Path.GetTempPath(), $"frameshift_create_gif_preview_{Guid.NewGuid():N}.gif");
        var transferred = false;
        try
        {
            var settings = BuildSelection();
            var duration = Math.Min(6d, Math.Max(0.5d, settings.DurationSeconds));
            var result = await _ffmpegRunner.RunAsync(_ffmpegPath,
                CreateGifAction.BuildArguments(_inputPath, previewPath, settings with { DurationSeconds = duration }),
                TimeSpan.FromSeconds(duration), Math.Max(1L, (long)Math.Ceiling(duration * settings.Fps)),
                null, _inputPath, "Create GIF Preview", "CPU", token);
            token.ThrowIfCancellationRequested();
            if (_closing || IsDisposed || result.Canceled) return;
            if (result.ExitCode != 0 || !File.Exists(previewPath))
                throw new InvalidOperationException(ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, MediaActionMessages.CreateGifPreviewFailed()));
            SetGifPreviewImage(previewPath);
            transferred = true;
            _uiState.GifPreviewRunning = true;
            _buttonPreview.Text = "Stop preview";
            _labelPreviewStatus.Text = $"Looping GIF preview, {duration:0.#}s at {settings.Fps} fps";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { if (!_closing && !IsDisposed) _labelPreviewStatus.Text = $"Preview unavailable: {ex.Message}"; }
        finally
        {
            if (!transferred) ConversionActionHelper.DeleteIfExists(previewPath);
            if (!_closing && !IsDisposed) { _busy = false; _buttonPreview.Enabled = true; }
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

        try { _gifPreviewImage = Image.FromStream(stream); }
        catch { stream.Dispose(); throw; }
        _gifPreviewStream = stream;
        _gifPreviewPath = previewPath;
        _previewBox.Image = _gifPreviewImage;
    }

    private void StopGifPreview(bool restoreFrame)
    {
        _previewLifetime.Cancel();
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
        if (_closing) return;
        _previewLifetime.Cancel();
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

    private async void PreviewTimerOnTick(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        await RequestFrameAsync(_uiState.PendingPreviewTime);
    }
    private void SelectionPanelOnPaint(object? sender, PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var trackLeft = FrameShiftUiMetrics.ToPixels(_selectionPanel, 18);
        var trackTop = FrameShiftUiMetrics.ToPixels(_selectionPanel, 36);
        var trackWidth = Math.Max(1, _selectionPanel.ClientSize.Width - 2 * FrameShiftUiMetrics.ToPixels(_selectionPanel, 18));
        var trackHeight = FrameShiftUiMetrics.ToPixels(_selectionPanel, 6);
        var handleWidth = FrameShiftUiMetrics.ToPixels(_selectionPanel, 10);
        var handleHeight = FrameShiftUiMetrics.ToPixels(_selectionPanel, 28);

        var startX = ConvertSecondsToTrackX(_selection.StartSeconds);
        var endX = ConvertSecondsToTrackX(_selection.EndSeconds);
        if (endX < startX)
        {
            (startX, endX) = (endX, startX);
        }

        var selectionWidth = Math.Max(2, endX - startX);
        var startHandleX = startX - (handleWidth / 2);
        var startHandleY = trackTop - FrameShiftUiMetrics.ToPixels(_selectionPanel, 11);
        var endHandleX = endX - (handleWidth / 2);
        var endHandleY = trackTop - FrameShiftUiMetrics.ToPixels(_selectionPanel, 11);

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
        graphics.FillRectangle(selectionBrush, startX, trackTop - FrameShiftUiMetrics.ToPixels(_selectionPanel, 4), selectionWidth, FrameShiftUiMetrics.ToPixels(_selectionPanel, 14));

        for (var index = 0; index <= 10; index++)
        {
            var tickX = trackLeft + (int)Math.Round((trackWidth * index) / 10.0);
            graphics.DrawLine(tickPen, tickX, FrameShiftUiMetrics.ToPixels(_selectionPanel, 18), tickX, FrameShiftUiMetrics.ToPixels(_selectionPanel, 26));
        }

        graphics.FillRectangle(startBrush, startHandleX, startHandleY, handleWidth, handleHeight);
        graphics.FillRectangle(endBrush, endHandleX, endHandleY, handleWidth, handleHeight);
        graphics.DrawRectangle(handleBorderPen, startHandleX, startHandleY, handleWidth, handleHeight);
        graphics.DrawRectangle(handleBorderPen, endHandleX, endHandleY, handleWidth, handleHeight);

        graphics.DrawString("0", Font, labelBrush, FrameShiftUiMetrics.ToPixels(_selectionPanel, 14), FrameShiftUiMetrics.ToPixels(_selectionPanel, 2));
        var lastText = CutAudioSettings.FormatDisplayTime(_probe.Duration?.TotalSeconds ?? 0d);
        var lastSize = graphics.MeasureString(lastText, Font);
        graphics.DrawString(lastText, Font, labelBrush, _selectionPanel.ClientSize.Width - lastSize.Width - FrameShiftUiMetrics.ToPixels(_selectionPanel, 14), FrameShiftUiMetrics.ToPixels(_selectionPanel, 2));
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
        var trackLeft = FrameShiftUiMetrics.ToPixels(_selectionPanel, 18);
        var trackWidth = _selectionPanel.ClientSize.Width - 2 * FrameShiftUiMetrics.ToPixels(_selectionPanel, 18);
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
        var trackLeft = FrameShiftUiMetrics.ToPixels(_selectionPanel, 18);
        var trackWidth = _selectionPanel.ClientSize.Width - 2 * FrameShiftUiMetrics.ToPixels(_selectionPanel, 18);
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
