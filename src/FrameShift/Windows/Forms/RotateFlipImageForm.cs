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
    private Bitmap? _iconRotateCcw;
    private Bitmap? _iconRotateCw;
    private Bitmap? _iconFlipH;
    private Bitmap? _iconFlipV;
    private RotateAngle _currentAngle = RotateAngle.None;
    private bool _flipH;
    private bool _flipV;
    private bool _closing;

    public RotateFlipImageForm(string inputPath, string ffmpegPath, FfmpegRunner ffmpegRunner)
    {
        _inputPath = inputPath;
        _ffmpegPath = ffmpegPath;
        _ffmpegRunner = ffmpegRunner;

        FrameShiftWindowChrome.Apply(this, "FrameShift - Rotate / Flip Image");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 560);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        Controls.Add(CreateHeader(inputPath));

        // Preview section (536px wide = 560 - 2×12)
        var previewSection = FrameShiftUiFactory.CreateFixedSection(new Point(12, 82), new Size(536, 252), "Preview");
        Controls.Add(previewSection);

        _previewPanel = new Panel
        {
            Location = new Point(12, 28),
            Size = new Size(512, 212),
            BackColor = Color.FromArgb(32, 32, 32)
        };
        ControlHelper.SetDoubleBuffered(_previewPanel);
        _previewPanel.Paint += PreviewPanelOnPaint;
        previewSection.Controls.Add(_previewPanel);

        // Transform section
        var transformSection = FrameShiftUiFactory.CreateFixedSection(new Point(12, 346), new Size(536, 104), "Transform");
        Controls.Add(transformSection);

        _iconRotateCcw = TryLoadButtonIcon(IconPaths.RotateFlipIco("rotate-left-icon.ico"));
        _iconRotateCw = TryLoadButtonIcon(IconPaths.RotateFlipIco("rotate-right-icon.ico"));
        _iconFlipH = TryLoadButtonIcon(IconPaths.RotateFlipIco("flip-horizontal-icon.ico"));
        _iconFlipV = TryLoadButtonIcon(IconPaths.RotateFlipIco("flip-vertical-icon.ico"));

        // 4 × 122px + 3 × 8px gap = 512px = section inner width
        const int btnW = 122;
        const int btnH = 36;
        const int btnGap = 8;
        const int btnStartX = 12;
        const int btnY = 30;

        _rotateCcwButton = CreateTransformButton("Rotate CCW", _iconRotateCcw, new Point(btnStartX, btnY), new Size(btnW, btnH));
        _rotateCwButton = CreateTransformButton("Rotate CW", _iconRotateCw, new Point(btnStartX + btnW + btnGap, btnY), new Size(btnW, btnH));
        _flipHButton = CreateTransformButton("Flip H", _iconFlipH, new Point(btnStartX + (btnW + btnGap) * 2, btnY), new Size(btnW, btnH));
        _flipVButton = CreateTransformButton("Flip V", _iconFlipV, new Point(btnStartX + (btnW + btnGap) * 3, btnY), new Size(btnW, btnH));

        _rotateCcwButton.Click += (_, _) => ApplyRotate(clockwise: false);
        _rotateCwButton.Click += (_, _) => ApplyRotate(clockwise: true);
        _flipHButton.Click += (_, _) => ApplyFlipH();
        _flipVButton.Click += (_, _) => ApplyFlipV();

        transformSection.Controls.Add(_rotateCcwButton);
        transformSection.Controls.Add(_rotateCwButton);
        transformSection.Controls.Add(_flipHButton);
        transformSection.Controls.Add(_flipVButton);

        _summaryLabel = new Label
        {
            AutoSize = false,
            Location = new Point(12, 76),
            Size = new Size(512, 18),
            ForeColor = FrameShiftTheme.TextMuted,
            Text = "No transforms applied"
        };
        transformSection.Controls.Add(_summaryLabel);

        // Info card
        var infoCard = FrameShiftUiFactory.CreateFixedInfoCard(new Point(12, 462), new Size(536, 44));
        Controls.Add(infoCard);
        infoCard.Controls.Add(new Label
        {
            Location = new Point(16, 13),
            Size = new Size(504, 18),
            Text = "The transformed image is created next to the original file.",
            ForeColor = FrameShiftTheme.TextSecondary
        });

        // Footer buttons — right-aligned in 560px form
        // Apply: x = 560 - 12 - 140 = 408
        // Cancel: x = 408 - 10 - 110 = 288
        // Reset: x = 288 - 10 - 100 = 178
        const int footerY = 518;

        var resetButton = FrameShiftUiFactory.CreateFixedActionButton("Reset", new Point(178, footerY), new Size(100, 34), primary: false);
        resetButton.Click += (_, _) => ResetTransforms();
        Controls.Add(resetButton);

        var cancelButton = FrameShiftUiFactory.CreateFixedActionButton("Cancel", new Point(288, footerY), new Size(110, 34), primary: false);
        cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);

        _applyButton = FrameShiftUiFactory.CreateFixedActionButton("Apply", new Point(408, footerY), new Size(140, 34), primary: true);
        _applyButton.Enabled = false;
        _applyButton.Click += (_, _) =>
        {
            Selection = new RotateFlipSettings(_currentAngle, _flipH, _flipV);
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(_applyButton);

        AcceptButton = _applyButton;
        CancelButton = cancelButton;

        Shown += (_, _) => InitializePreview();
        FormClosing += (_, _) =>
        {
            _closing = true;
            DisposeAll();
        };
    }

    public RotateFlipSettings? Selection { get; private set; }

    private void InitializePreview()
    {
        try
        {
            var sourceExtension = Path.GetExtension(_inputPath).ToLowerInvariant();
            if (!ImageCropSupport.IsSupportedExtension(sourceExtension))
            {
                throw new InvalidOperationException(MediaActionMessages.UnsupportedSourceFormat(sourceExtension, ImageCropSupport.GetSupportedExtensionsText()));
            }

            if (sourceExtension == ".webp")
            {
                _originalBitmap = PreviewFrameHelper.CaptureFrameAsync(
                    _ffmpegPath,
                    _ffmpegRunner,
                    _inputPath,
                    0d,
                    "Rotate / Flip Image Preview",
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            else
            {
                _originalBitmap = ImageBitmapHelper.LoadBitmap(_inputPath);
            }

            _displayBitmap = new Bitmap(_originalBitmap);
            _previewPanel.Invalidate();
        }
        catch (Exception ex)
        {
            if (!_closing && !IsDisposed)
            {
                var errorMessage = ImageCropSupport.GetFriendlyLoadError(ex);
                MessageBox.Show(this, errorMessage, "FrameShift", MessageBoxButtons.OK, MessageBoxIcon.Error);
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }
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
        _applyButton.Enabled = !settings.IsIdentity;
        UpdateTransformButtonHighlights();
        _previewPanel.Invalidate();
    }

    private void UpdateTransformButtonHighlights()
    {
        UpdateButtonActiveState(_rotateCwButton, _currentAngle != RotateAngle.None);
        UpdateButtonActiveState(_rotateCcwButton, _currentAngle != RotateAngle.None);
        UpdateButtonActiveState(_flipHButton, _flipH);
        UpdateButtonActiveState(_flipVButton, _flipV);
    }

    private static void UpdateButtonActiveState(Button button, bool active)
    {
        button.BackColor = active ? FrameShiftTheme.AccentSoft : FrameShiftTheme.Surface;
        button.ForeColor = active ? FrameShiftTheme.AccentText : FrameShiftTheme.TextPrimary;
        button.FlatAppearance.BorderColor = active ? FrameShiftTheme.SecondaryBlue : FrameShiftTheme.PrimaryBlue;
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

    private void DisposeAll()
    {
        _originalBitmap?.Dispose();
        _originalBitmap = null;
        _displayBitmap?.Dispose();
        _displayBitmap = null;
        _iconRotateCcw?.Dispose();
        _iconRotateCcw = null;
        _iconRotateCw?.Dispose();
        _iconRotateCw = null;
        _iconFlipH?.Dispose();
        _iconFlipH = null;
        _iconFlipV?.Dispose();
        _iconFlipV = null;
    }

    private static Control CreateHeader(string inputPath)
    {
        return FrameShiftUiFactory.CreateFixedHeader(
            "FrameShift - Rotate / Flip Image",
            $"Source: {Path.GetFileName(inputPath)}",
            IconPaths.ContextMenuIco("rotate-video-image-icon.ico"),
            IconPaths.AppIcon,
            "↻");
    }

    private static Button CreateTransformButton(string text, Bitmap? icon, Point location, Size size)
    {
        var button = new Button
        {
            Text = text,
            Location = location,
            Size = size,
            FlatStyle = FlatStyle.Flat,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Padding = new Padding(4, 0, 6, 0)
        };
        button.FlatAppearance.BorderColor = FrameShiftTheme.PrimaryBlue;
        button.FlatAppearance.BorderSize = 1;

        if (icon is not null)
        {
            button.Image = icon;
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextAlign = ContentAlignment.MiddleRight;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
        }

        return button;
    }

    private static Bitmap? TryLoadButtonIcon(string icoPath)
    {
        if (!File.Exists(icoPath))
        {
            return null;
        }

        try
        {
            using var icon = new Icon(icoPath, 20, 20);
            return icon.ToBitmap();
        }
        catch
        {
            return null;
        }
    }
}
