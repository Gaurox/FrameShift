using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.Helpers;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class RotateFlipImageForm : Form
{
    private readonly string _inputPath;
    private readonly string _ffmpegPath;
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly Panel _previewPanel;
    private readonly Label _summaryLabel;
    private readonly Button _applyButton;
    private readonly Button _rotateCcwButton;
    private readonly Button _rotateCwButton;
    private readonly Button _flipHButton;
    private readonly Button _flipVButton;
    private Bitmap? _originalBitmap;
    private Bitmap? _displayBitmap;
    private RotateAngle _currentAngle = RotateAngle.None;
    private bool _flipH;
    private bool _flipV;
    private bool _closing;
    private CancellationTokenSource? _previewLoadCts;

    public RotateFlipImageForm(string inputPath, string ffmpegPath, FfmpegRunner ffmpegRunner)
    {
        _inputPath = inputPath;
        _ffmpegPath = ffmpegPath;
        _ffmpegRunner = ffmpegRunner;
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(900, 740), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, "FrameShift - Rotate / Flip Image");
        var header = FrameShiftUiFactory.CreateHeader("FrameShift - Rotate / Flip Image", $"Source: {Path.GetFileName(inputPath)}",
            IconPaths.ContextMenuIco("rotate-video-image-icon.ico"), IconPaths.AppIcon, "↻");
        _previewPanel = new Panel { Name = "preview", Dock = DockStyle.Fill, BackColor = Color.FromArgb(32, 32, 32) };
        ControlHelper.SetDoubleBuffered(_previewPanel);
        _previewPanel.Paint += PreviewPanelOnPaint;
        _previewPanel.Resize += (_, _) => _previewPanel.Invalidate();
        _rotateCcwButton = FrameShiftUiFactory.CreateMeasuredActionButton("↶ Rotate left", false);
        _rotateCwButton = FrameShiftUiFactory.CreateMeasuredActionButton("↷ Rotate right", false);
        _flipHButton = FrameShiftUiFactory.CreateMeasuredActionButton("↔ Flip horizontal", false);
        _flipVButton = FrameShiftUiFactory.CreateMeasuredActionButton("↕ Flip vertical", false);
        _rotateCcwButton.Click += (_, _) => ApplyRotate(false);
        _rotateCwButton.Click += (_, _) => ApplyRotate(true);
        _flipHButton.Click += (_, _) => ApplyFlipH();
        _flipVButton.Click += (_, _) => ApplyFlipV();
        var reset = FrameShiftUiFactory.CreateMeasuredActionButton("Reset", false);
        reset.Click += (_, _) => ResetTransforms();
        _summaryLabel = FrameShiftUiFactory.CreateWrappingLabel("Loading preview…");
        var transforms = FrameShiftUiFactory.CreateSection("Transform", FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateChoiceRow(_rotateCcwButton, _rotateCwButton, _flipHButton, _flipVButton, reset), _summaryLabel));
        var options = transforms;
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        cancel.DialogResult = DialogResult.Cancel;
        _applyButton = FrameShiftUiFactory.CreateMeasuredActionButton("Apply", true);
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
            FrameShiftUiFactory.CreateSection("Preview", _previewPanel, fill: true), options,
            FrameShiftDialogLayout.CreateActions(cancel, _applyButton)));
        Shown += async (_, _) => await InitializePreviewAsync();
        FormClosing += (_, _) => StopPreview();
        ResumeLayout(true);
    }

    public RotateFlipSettings? Selection { get; private set; }

    private async Task InitializePreviewAsync()
    {
        if (_closing || IsDisposed) return;
        using var request = new CancellationTokenSource();
        _previewLoadCts = request;
        try
        {
            var extension = Path.GetExtension(_inputPath).ToLowerInvariant();
            if (!ImageCropSupport.IsSupportedExtension(extension))
                throw new InvalidOperationException(MediaActionMessages.UnsupportedSourceFormat(extension, ImageCropSupport.GetSupportedExtensionsText()));
            var bitmap = extension == ".webp"
                ? await PreviewFrameHelper.CaptureFrameAsync(_ffmpegPath, _ffmpegRunner, _inputPath, 0,
                    "Rotate / Flip Image Preview", request.Token)
                : await Task.Run(() => ImageBitmapHelper.LoadBitmap(_inputPath), request.Token);
            if (_closing || IsDisposed || request.IsCancellationRequested) { bitmap.Dispose(); return; }
            _originalBitmap?.Dispose();
            _originalBitmap = bitmap;
            RefreshPreview();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closing && !IsDisposed)
            {
                _summaryLabel.Text = ImageCropSupport.GetFriendlyLoadError(ex);
                _applyButton.Enabled = false;
            }
        }
        finally { if (ReferenceEquals(_previewLoadCts, request)) _previewLoadCts = null; }
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
        _displayBitmap?.Dispose();
        _displayBitmap = null;

        var settings = new RotateFlipSettings(_currentAngle, _flipH, _flipV);

        if (_originalBitmap is not null)
        {
            _displayBitmap = BuildTransformedBitmap(_originalBitmap, settings);
        }

        _summaryLabel.Text = settings.BuildTransformSummary();
        _summaryLabel.ForeColor = settings.IsIdentity ? FrameShiftTheme.TextMuted : FrameShiftTheme.TextPrimary;
        _applyButton.Enabled = _originalBitmap is not null && !settings.IsIdentity;
        UpdateTransformButtons();
        _previewPanel.Invalidate();
    }

    private void UpdateTransformButtons()
    {
        // Rotation buttons perform a step; only mirrors have an on/off state.
        // Keep the standard palette and express the persistent state with a check mark.
        _flipHButton.Text = _flipH ? "↔ Flip horizontal ✓" : "↔ Flip horizontal";
        _flipVButton.Text = _flipV ? "↕ Flip vertical ✓" : "↕ Flip vertical";
        _flipHButton.AccessibleDescription = _flipH ? "Horizontal mirror enabled" : "Horizontal mirror disabled";
        _flipVButton.AccessibleDescription = _flipV ? "Vertical mirror enabled" : "Vertical mirror disabled";
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
        DisposeAll();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopPreview();
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
