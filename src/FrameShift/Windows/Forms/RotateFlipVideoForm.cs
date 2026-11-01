using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class RotateFlipVideoForm : Form
{
    private readonly string _inputPath;
    private readonly string _ffmpegPath;
    private readonly MediaProbeResult _probe;
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly Panel _previewPanel;
    private readonly SeekTrackBar _timelineBar;
    private readonly Label _summaryLabel;
    private readonly Button _applyButton;
    private readonly Button _rotateCcwButton;
    private readonly Button _rotateCwButton;
    private readonly Button _flipHButton;
    private readonly Button _flipVButton;
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private Bitmap? _originalBitmap;
    private Bitmap? _displayBitmap;
    private RotateAngle _currentAngle = RotateAngle.None;
    private bool _flipH;
    private bool _flipV;
    private double _pendingPreviewSeconds;
    private bool _closing;
    private CancellationTokenSource? _previewLoadCts;

    public RotateFlipVideoForm(
        string inputPath,
        string ffmpegPath,
        MediaProbeResult probe,
        FfmpegRunner ffmpegRunner)
    {
        _inputPath = inputPath;
        _ffmpegPath = ffmpegPath;
        _ffmpegRunner = ffmpegRunner;
        _probe = probe;
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(900, 740), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, L.Get("RotateFlipVideoForm_FrameShift_Rotate_Flip_Video_4e16afe6"));
        var header = FrameShiftUiFactory.CreateHeader(L.Get("RotateFlipVideoForm_FrameShift_Rotate_Flip_Video_4e16afe6"), L.Format("RotateFlipVideoForm_Source_0_653ab450",Path.GetFileName(inputPath)),
            IconPaths.ContextMenuIco("rotate-video-image-icon.ico"), IconPaths.AppIcon, "↻");
        _previewPanel = new Panel { Name = "preview", Dock = DockStyle.Fill, BackColor = Color.FromArgb(32, 32, 32) };
        ControlHelper.SetDoubleBuffered(_previewPanel);
        _previewPanel.Paint += PreviewPanelOnPaint;
        _previewPanel.Resize += (_, _) => _previewPanel.Invalidate();
        _rotateCcwButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("RotateFlipVideoForm_Rotate_left_9e20293b"), false);
        _rotateCwButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("RotateFlipVideoForm_Rotate_right_f316ebc1"), false);
        _flipHButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("RotateFlipVideoForm_Flip_horizontal_014031ed"), false);
        _flipVButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("RotateFlipVideoForm_Flip_vertical_e9b61839"), false);
        _rotateCcwButton.Click += (_, _) => ApplyRotate(false);
        _rotateCwButton.Click += (_, _) => ApplyRotate(true);
        _flipHButton.Click += (_, _) => ApplyFlipH();
        _flipVButton.Click += (_, _) => ApplyFlipV();
        var reset = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("RotateFlipVideoForm_Reset_24745fdf"), false);
        reset.Click += (_, _) => ResetTransforms();
        _summaryLabel = FrameShiftUiFactory.CreateWrappingLabel(L.Get("RotateFlipVideoForm_Loading_preview_58455073"));
        var transforms = FrameShiftUiFactory.CreateSection(L.Get("RotateFlipVideoForm_Transform_2e7cc3f0"), FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateChoiceRow(_rotateCcwButton, _rotateCwButton, _flipHButton, _flipVButton, reset), _summaryLabel));
        _timelineBar = new SeekTrackBar { Name = "timeline", Minimum = 0, Maximum = 1000,
            TickFrequency = 100, SmallChange = 5, LargeChange = 50, AutoSize = true };
        _timelineBar.ValueChanged += (_, _) => OnTimelineChanged();
        var timeline = FrameShiftUiFactory.CreateSection(L.Get("RotateFlipVideoForm_Preview_position_a7b0aca0"), FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateWrappingLabel(L.Get("RotateFlipVideoForm_Drag_the_slider_to_preview_another_m_d6534b0a")), _timelineBar));
        var options = FrameShiftUiFactory.CreateVerticalStack(timeline, transforms);
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("RotateFlipVideoForm_Cancel_998b9c48"), false);
        cancel.DialogResult = DialogResult.Cancel;
        _applyButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("RotateFlipVideoForm_Apply_3d57c12e"), true);
        _applyButton.Enabled = false;
        _applyButton.Click += (_, _) =>
        {
            Selection = new RotateFlipSettings(_currentAngle, _flipH, _flipV);
            DialogResult = DialogResult.OK;
            Close();
        };
        AcceptButton = _applyButton;
        CancelButton = cancel;
        Controls.Add(FrameShiftEditorShellUi.CreateTimeline(header,
            FrameShiftUiFactory.CreateSection(L.Get("RotateFlipVideoForm_Preview_68dd3b5d"), _previewPanel, fill: true), options,
            FrameShiftDialogLayout.CreateActions(cancel, _applyButton)));
        _debounceTimer = new System.Windows.Forms.Timer { Interval = 140 };
        _debounceTimer.Tick += async (_, _) =>
        {
            _debounceTimer.Stop();
            await LoadPreviewFrameAsync(_pendingPreviewSeconds);
        };
        Shown += async (_, _) => await LoadPreviewFrameAsync(0);
        FormClosing += (_, _) => StopPreview();
        ResumeLayout(true);
    }

    public RotateFlipSettings? Selection { get; private set; }

    private void OnTimelineChanged()
    {
        var durationSeconds = _probe.Duration?.TotalSeconds ?? 0;
        if (durationSeconds <= 0)
        {
            return;
        }

        _pendingPreviewSeconds = durationSeconds * (_timelineBar.Value / 1000.0);
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async Task LoadPreviewFrameAsync(double seconds)
    {
        if (_closing || IsDisposed)
        {
            return;
        }

        _previewLoadCts?.Cancel();
        using var localCts = new CancellationTokenSource();
        _previewLoadCts = localCts;

        try
        {
            var newBitmap = await PreviewFrameHelper.CaptureFrameAsync(
                _ffmpegPath,
                _ffmpegRunner,
                _inputPath,
                seconds,
                "Rotate / Flip Video Preview",
                localCts.Token).ConfigureAwait(true);

            if (_closing || IsDisposed || localCts.IsCancellationRequested || !ReferenceEquals(_previewLoadCts, localCts))
            {
                newBitmap.Dispose();
                return;
            }

            _originalBitmap?.Dispose();
            _originalBitmap = newBitmap;

            RefreshPreview();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (localCts.IsCancellationRequested || !ReferenceEquals(_previewLoadCts, localCts))
            {
                return;
            }

            if (!_closing && !IsDisposed)
            {
                var errorMessage = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed(L.Get("RotateFlipVideoForm_Rotate_Flip_Video_d0a33e8a")));
                _summaryLabel.Text = errorMessage;
                _applyButton.Enabled = false;
            }
        }
        finally { if (ReferenceEquals(_previewLoadCts, localCts)) _previewLoadCts = null; }
    }

    private void ApplyRotate(bool clockwise)
    {
        _currentAngle = clockwise
            ? _currentAngle switch
            {
                RotateAngle.None => RotateAngle.Cw90,
                RotateAngle.Cw90 => RotateAngle.Cw180,
                RotateAngle.Cw180 => RotateAngle.Cw270,
                _ => RotateAngle.None
            }
            : _currentAngle switch
            {
                RotateAngle.None => RotateAngle.Cw270,
                RotateAngle.Cw270 => RotateAngle.Cw180,
                RotateAngle.Cw180 => RotateAngle.Cw90,
                _ => RotateAngle.None
            };

        RefreshPreview();
    }

    private void ApplyFlipH()
    {
        _flipH = !_flipH;
        RefreshPreview();
    }

    private void ApplyFlipV()
    {
        _flipV = !_flipV;
        RefreshPreview();
    }

    private void ResetTransforms()
    {
        _currentAngle = RotateAngle.None;
        _flipH = false;
        _flipV = false;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        RefreshDisplayBitmap();

        var settings = new RotateFlipSettings(_currentAngle, _flipH, _flipV);
        _summaryLabel.Text = settings.BuildTransformSummary();
        _summaryLabel.ForeColor = settings.IsIdentity ? FrameShiftTheme.TextMuted : FrameShiftTheme.TextPrimary;
        _applyButton.Enabled = _originalBitmap is not null && !settings.IsIdentity;
        UpdateTransformButtons();
    }

    private void RefreshDisplayBitmap()
    {
        _displayBitmap?.Dispose();
        _displayBitmap = null;

        if (_originalBitmap is not null)
        {
            var settings = new RotateFlipSettings(_currentAngle, _flipH, _flipV);
            _displayBitmap = BuildTransformedBitmap(_originalBitmap, settings);
        }

        _previewPanel.Invalidate();
    }

    private void UpdateTransformButtons()
    {
        // Rotation buttons perform a step; only mirrors have an on/off state.
        // Keep the standard palette and express the persistent state with a check mark.
        _flipHButton.Text = _flipH ? L.Get("RotateFlipVideoForm_Flip_horizontal_8f4b97ca") : L.Get("RotateFlipVideoForm_Flip_horizontal_014031ed");
        _flipVButton.Text = _flipV ? L.Get("RotateFlipVideoForm_Flip_vertical_86e4301e") : L.Get("RotateFlipVideoForm_Flip_vertical_e9b61839");
        _flipHButton.AccessibleDescription = _flipH ? L.Get("RotateFlipVideoForm_Horizontal_mirror_enabled_df2d72b4") : L.Get("RotateFlipVideoForm_Horizontal_mirror_disabled_39108e4a");
        _flipVButton.AccessibleDescription = _flipV ? L.Get("RotateFlipVideoForm_Vertical_mirror_enabled_ca1417e6") : L.Get("RotateFlipVideoForm_Vertical_mirror_disabled_11bc54fb");
    }

    private static Bitmap BuildTransformedBitmap(Bitmap source, RotateFlipSettings settings)
    {
        var transformed = new Bitmap(source);

        if (settings.Angle == RotateAngle.Cw90)
        {
            transformed.RotateFlip(RotateFlipType.Rotate90FlipNone);
        }
        else if (settings.Angle == RotateAngle.Cw180)
        {
            transformed.RotateFlip(RotateFlipType.Rotate180FlipNone);
        }
        else if (settings.Angle == RotateAngle.Cw270)
        {
            transformed.RotateFlip(RotateFlipType.Rotate270FlipNone);
        }

        if (settings.FlipHorizontal)
        {
            transformed.RotateFlip(RotateFlipType.RotateNoneFlipX);
        }

        if (settings.FlipVertical)
        {
            transformed.RotateFlip(RotateFlipType.RotateNoneFlipY);
        }

        return transformed;
    }

    private void PreviewPanelOnPaint(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(_previewPanel.BackColor);

        if (_displayBitmap is null)
        {
            return;
        }

        var bounds = PreviewLayoutHelper.GetCenteredImageBounds(_previewPanel.ClientSize, _displayBitmap.Size);
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        e.Graphics.DrawImage(_displayBitmap, bounds);
    }

    private void StopPreview()
    {
        _closing = true;
        _previewLoadCts?.Cancel();
        _debounceTimer.Stop();
        DisposeAll();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopPreview();
            _debounceTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void DisposeAll()
    {
        _originalBitmap?.Dispose();
        _originalBitmap = null;
        _displayBitmap?.Dispose();
        _displayBitmap = null;
    }

}
