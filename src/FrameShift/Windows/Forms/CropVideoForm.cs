using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class CropVideoForm : Form
{
    private const int MinSourceCropDimension = 146;
    private const float PreviewZoomStep = 1.12f;
    private const float MinimumPreviewZoom = 1f;
    private const float MaximumPreviewZoom = 12f;
    private readonly string _inputPath;
    private readonly string _ffmpegPath;
    private readonly MediaProbeResult _probe;
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly Panel _previewPanel;
    private readonly SeekTrackBar _timelineBar;
    private readonly Label _frameLabel;
    private readonly Label _sizeLabel;
    private readonly Label _sourceLabel;
    private readonly RadioButton[] _ratioButtons;
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private readonly System.Windows.Forms.Timer _resizeRefreshTimer;
    private Bitmap? _previewBitmap;
    private RectangleF _imageBounds = new(0, 0, 1, 1);
    private RectangleF _cropRect = new(0, 0, 1, 1);
    private RectangleF _dragRect = new(0, 0, 1, 1);
    private PointF _dragStart;
    private PointF _panStartCenter;
    private string _dragMode = string.Empty;
    private double _pendingPreviewSeconds;
    private VideoCropSettings? _selection;
    private bool _closing;
    private bool _isLiveResize;
    private float _previewZoom = 1f;
    private float _fitImageScale = 1f;
    private PointF _previewSourceCenter = new(0.5f, 0.5f);
    private CancellationTokenSource? _previewLoadCts;

    public CropVideoForm(
        string inputPath,
        string ffmpegPath,
        MediaProbeResult probe,
        FfmpegRunner ffmpegRunner)
    {
        _inputPath = inputPath;
        _ffmpegPath = ffmpegPath;
        _probe = probe;
        _ffmpegRunner = ffmpegRunner;

        SuspendLayout();

        FrameShiftWindowChrome.Apply(this, "FrameShift - Crop video");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        WindowState = FormWindowState.Normal;
        ClientSize = new Size(1120, 720);
        MinimumSize = new Size(1020, 690);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = FrameShiftTheme.PageBackground;
        ControlHelper.SetDoubleBuffered(this);

        _resizeRefreshTimer = new System.Windows.Forms.Timer { Interval = 35 };
        _resizeRefreshTimer.Tick += (_, _) =>
        {
            _resizeRefreshTimer.Stop();
            RefreshPreviewLayout();
        };

        var rootLayout = FrameShiftCropEditorUi.CreateRootLayout();

        var headerPanel = CreateHeaderPanel();

        var contentLayout = FrameShiftCropEditorUi.CreateContentLayout(out var leftLayout, out var rightLayout);

        _previewPanel = FrameShiftCropEditorUi.CreatePreviewPanel();
        ControlHelper.SetDoubleBuffered(_previewPanel);
        _previewPanel.Paint += PreviewPanelOnPaint;
        _previewPanel.MouseDown += PreviewPanelOnMouseDown;
        _previewPanel.MouseMove += PreviewPanelOnMouseMove;
        _previewPanel.MouseUp += PreviewPanelOnMouseUp;
        _previewPanel.MouseWheel += PreviewPanelOnMouseWheel;
        _previewPanel.MouseEnter += (_, _) => _previewPanel.Focus();
        _previewPanel.Resize += (_, _) => SchedulePreviewLayoutRefresh();
        _previewPanel.TabStop = true;

        _timelineBar = new SeekTrackBar
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            Minimum = 0,
            Maximum = 1000,
            TickFrequency = 100,
            SmallChange = 5,
            LargeChange = 50,
            Margin = Padding.Empty
        };
        _timelineBar.ValueChanged += (_, _) => OnTimelineChanged();

        var timelineHintCard = FrameShiftUiFactory.CreateFillInfoCardWithMargin(topMargin: FrameShiftUiMetrics.BlockGap);
        timelineHintCard.Margin = Padding.Empty;
        timelineHintCard.Padding = new Padding(10, 4, 10, 4);

        var timelineHint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Drag the slider to preview another moment of the video.",
            ForeColor = FrameShiftTheme.TextSecondary,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft
        };
        timelineHintCard.Controls.Add(timelineHint);
        var previewSection = FrameShiftUiFactory.CreateFillSection("Preview", out var previewContentHost);
        previewSection.Margin = Padding.Empty;
        previewSection.Padding = FrameShiftUiMetrics.StandardSectionPadding;
        var previewSectionLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        previewSectionLayout.RowCount = 5;
        previewSectionLayout.ColumnCount = 1;
        previewSectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        previewSectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        previewSectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.BlockGap));
        previewSectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        previewSectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.LineGap));
        previewSectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        previewContentHost.Controls.Add(previewSectionLayout);

        var ratioOptions = new[] { "Free", "Square", "16:9", "9:16", "4:3", "3:4", "3:2", "2:3" };
        var ratioSection = FrameShiftCropEditorUi.CreateRatioSection(
            ratioOptions,
            ApplyRatioToCurrentCrop,
            out _ratioButtons,
            out var ratioSectionHeight);
        rightLayout.RowStyles[0] = new RowStyle(SizeType.Absolute, ratioSectionHeight);

        var mediaCard = FrameShiftCropEditorUi.CreateMediaInfoCard(3);
        var mediaLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 3
        };
        mediaLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mediaLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.InfoLineHeight));
        mediaLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.InfoLineHeight));
        mediaLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.InfoLineHeight));

        _sourceLabel = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            AutoEllipsis = true,
            Text = $"Source: {_probe.VideoWidth} x {_probe.VideoHeight} px",
            ForeColor = FrameShiftTheme.TextPrimary
        };

        _frameLabel = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            AutoEllipsis = true,
            Text = $"Frame: {FormatTimeForDisplay(0)}",
            ForeColor = FrameShiftTheme.TextSecondary
        };

        _sizeLabel = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            AutoEllipsis = true,
            Text = "Crop: ---",
            ForeColor = FrameShiftTheme.TextSecondary
        };
        mediaLayout.Controls.Add(_sourceLabel, 0, 0);
        mediaLayout.Controls.Add(_frameLabel, 0, 1);
        mediaLayout.Controls.Add(_sizeLabel, 0, 2);
        mediaCard.Controls.Add(mediaLayout);
        rightLayout.RowStyles[1] = new RowStyle(SizeType.Absolute, FrameShiftUiFactory.GetInfoCardHeight(3) + mediaCard.Margin.Vertical);

        var autoCropButton = CreateActionButton("Auto crop", primary: false);
        autoCropButton.Click += (_, _) => ApplyAutoCrop();

        var fitButton = CreateActionButton("Fit", primary: false);
        fitButton.Click += (_, _) => FitPreviewToView();

        var resetButton = CreateActionButton("Reset", primary: false);
        resetButton.Click += (_, _) => ResetCropRect();
        var toolsSection = FrameShiftCropEditorUi.CreateToolsSection(out var toolsButtonHost, autoCropButton, fitButton, resetButton);

        var okButton = CreateActionButton("OK", primary: true);
        okButton.Size = new Size(FrameShiftUiMetrics.PrimaryButtonWidth, FrameShiftUiMetrics.FooterButtonHeight);
        okButton.Click += (_, _) =>
        {
            var sourceCrop = GetSourceCropRect();
            _selection = sourceCrop;
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancelButton = CreateActionButton("Cancel", primary: false);
        cancelButton.Size = new Size(FrameShiftUiMetrics.SecondaryButtonWidth, FrameShiftUiMetrics.FooterButtonHeight);
        cancelButton.Margin = Padding.Empty;
        cancelButton.DialogResult = DialogResult.Cancel;

        var footerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        footerPanel.Controls.Add(cancelButton);
        footerPanel.Controls.Add(okButton);

        previewSectionLayout.Controls.Add(_previewPanel, 0, 0);
        previewSectionLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
        previewSectionLayout.Controls.Add(timelineHintCard, 0, 2);
        previewSectionLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 3);
        previewSectionLayout.Controls.Add(_timelineBar, 0, 4);

        leftLayout.Controls.Add(previewSection, 0, 0);

        rightLayout.Controls.Add(ratioSection, 0, 0);
        rightLayout.Controls.Add(mediaCard, 0, 1);
        rightLayout.Controls.Add(toolsSection, 0, 2);
        rightLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 3);

        contentLayout.Controls.Add(leftLayout, 0, 0);
        contentLayout.Controls.Add(rightLayout, 1, 0);

        headerPanel.Margin = Padding.Empty;
        contentLayout.Margin = Padding.Empty;
        footerPanel.Margin = Padding.Empty;

        rootLayout.Controls.Add(headerPanel, 0, 0);
        rootLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
        rootLayout.Controls.Add(contentLayout, 0, 2);
        rootLayout.Controls.Add(footerPanel, 0, 3);

        Controls.Add(rootLayout);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        _debounceTimer = new System.Windows.Forms.Timer { Interval = 140 };
        _debounceTimer.Tick += async (_, _) =>
        {
            _debounceTimer.Stop();
            await LoadPreviewFrameAsync(_pendingPreviewSeconds, preserveCrop: true).ConfigureAwait(true);
        };

        Shown += async (_, _) =>
        {
            _timelineBar.Value = 0;
            _pendingPreviewSeconds = 0.0;
            await LoadPreviewFrameAsync(0.0, preserveCrop: false).ConfigureAwait(true);
        };

        ResizeBegin += (_, _) => _isLiveResize = true;
        ResizeEnd += (_, _) =>
        {
            _isLiveResize = false;
            RefreshPreviewLayout();
        };

        FormClosing += (_, _) =>
        {
            _closing = true;
            _debounceTimer.Stop();
            _resizeRefreshTimer.Stop();
            _previewLoadCts?.Cancel();
            _previewLoadCts?.Dispose();
            _previewLoadCts = null;
            DisposePreviewBitmaps();
        };

        FrameShiftCropEditorUi.WireFooterLayout(this, footerPanel, cancelButton, okButton);
        FrameShiftCropEditorUi.WireToolLayout(this, toolsButtonHost, autoCropButton, fitButton, resetButton);

        ResumeLayout(true);
    }

    private Panel CreateHeaderPanel()
    {
        return FrameShiftUiFactory.CreateFillHeader(
            "FrameShift - Crop video",
            $"Source: {_probe.VideoWidth} x {_probe.VideoHeight} px    Duration: {FormatTimeForDisplay(_probe.Duration?.TotalSeconds ?? 0)}",
            IconPaths.ContextMenuIco("crop-video-image-icon.ico"),
            IconPaths.AppIcon,
            "◧",
            460);
    }

    private static Button CreateActionButton(string text, bool primary)
    {
        return FrameShiftUiFactory.CreateActionButton(text, primary, primary ? FrameShiftUiMetrics.PrimaryButtonWidth : FrameShiftUiMetrics.SecondaryButtonWidth);
    }
    private static Panel CreateFramedPanel(Color backgroundColor, Color borderColor, int radius)
    {
        return FrameShiftUiFactory.CreateFramedPanel(backgroundColor, borderColor, radius);
    }

    public VideoCropSettings? Selection => _selection;

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

    private async Task LoadPreviewFrameAsync(double seconds, bool preserveCrop)
    {
        if (_closing || IsDisposed)
        {
            return;
        }

        VideoCropSettings? oldSourceCrop = preserveCrop && _cropRect.Width > 1 && _cropRect.Height > 1 && _imageBounds.Width > 1 && _imageBounds.Height > 1
            ? GetSourceCropRect()
            : null;

        _previewLoadCts?.Cancel();
        _previewLoadCts?.Dispose();
        var localPreviewLoadCts = new CancellationTokenSource();
        _previewLoadCts = localPreviewLoadCts;

        try
        {
            var newBitmap = await PreviewFrameHelper.CaptureFrameAsync(
                _ffmpegPath,
                _ffmpegRunner,
                _inputPath,
                seconds,
                "Crop Video Preview",
                localPreviewLoadCts.Token).ConfigureAwait(true);
            if (_closing ||
                IsDisposed ||
                localPreviewLoadCts.IsCancellationRequested ||
                !ReferenceEquals(_previewLoadCts, localPreviewLoadCts))
            {
                newBitmap.Dispose();
                return;
            }

            DisposePreviewBitmaps();
            _previewBitmap = newBitmap;
            if (Math.Abs(_previewSourceCenter.X - 0.5f) < 0.0001f &&
                Math.Abs(_previewSourceCenter.Y - 0.5f) < 0.0001f)
            {
                _previewSourceCenter = new PointF(_probe.VideoWidth / 2.0f, _probe.VideoHeight / 2.0f);
            }
            UpdateImageBounds();

            if (oldSourceCrop is not null)
            {
                SetCropRectFromSource(oldSourceCrop);
            }
            else
            {
                ResetCropRect();
            }

            UpdateFrameLabel(seconds);
            UpdateSizeLabel();
            _previewPanel.Invalidate();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (localPreviewLoadCts.IsCancellationRequested || !ReferenceEquals(_previewLoadCts, localPreviewLoadCts))
            {
                return;
            }

            if (!_closing && !IsDisposed)
            {
                var errorMessage = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed("Crop Video"));
                MessageBox.Show(this, errorMessage, "FrameShift", MessageBoxButtons.OK, MessageBoxIcon.Error);
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }
    }

    private void UpdateImageBounds()
    {
        if (_previewBitmap is null || _previewPanel.ClientSize.Width <= 1 || _previewPanel.ClientSize.Height <= 1)
        {
            return;
        }

        var fitBounds = PreviewLayoutHelper.GetCenteredImageBounds(_previewPanel.ClientSize, _previewBitmap.Size);
        _fitImageScale = fitBounds.Width / Math.Max(1, _previewBitmap.Width);
        if (_fitImageScale <= 0.0001f)
        {
            _fitImageScale = 1f;
        }

        _previewZoom = Math.Clamp(_previewZoom, MinimumPreviewZoom, MaximumPreviewZoom);
        var displayScale = _fitImageScale * _previewZoom;
        var displayWidth = (float)(_previewBitmap.Width * displayScale);
        var displayHeight = (float)(_previewBitmap.Height * displayScale);
        var panelCenterX = _previewPanel.ClientSize.Width / 2.0f;
        var panelCenterY = _previewPanel.ClientSize.Height / 2.0f;
        var imageX = panelCenterX - (_previewSourceCenter.X * displayScale);
        var imageY = panelCenterY - (_previewSourceCenter.Y * displayScale);
        _imageBounds = new RectangleF(imageX, imageY, displayWidth, displayHeight);
    }

    private void ResetCropRect()
    {
        var ratio = GetRatioValue(GetSelectedRatioMode());
        var margin = 0.08;
        var width = _imageBounds.Width * (1.0 - (margin * 2.0));
        var height = _imageBounds.Height * (1.0 - (margin * 2.0));

        if (ratio is not null)
        {
            if (width / height > ratio.Value)
            {
                width = height * ratio.Value;
            }
            else
            {
                height = width / ratio.Value;
            }
        }

        var x = _imageBounds.X + ((_imageBounds.Width - width) / 2.0);
        var y = _imageBounds.Y + ((_imageBounds.Height - height) / 2.0);
        _cropRect = new RectangleF((float)x, (float)y, (float)width, (float)height);
        UpdateSizeLabel();
        _previewPanel.Invalidate();
    }

    private void FitPreviewToView()
    {
        var sourceCrop = GetSourceCropRect();
        _previewZoom = 1f;
        _previewSourceCenter = new PointF(_probe.VideoWidth / 2.0f, _probe.VideoHeight / 2.0f);
        UpdateImageBounds();
        SetCropRectFromSource(sourceCrop);
        _previewPanel.Invalidate();
    }

    private void CenterCropRect()
    {
        _cropRect.X = _imageBounds.X + ((_imageBounds.Width - _cropRect.Width) / 2.0f);
        _cropRect.Y = _imageBounds.Y + ((_imageBounds.Height - _cropRect.Height) / 2.0f);
        _cropRect = ClampCropRect(_cropRect, _imageBounds, GetRatioValue(GetSelectedRatioMode()), GetMinimumPreviewCropSize());
        _previewPanel.Invalidate();
    }

    private void ApplyAutoCrop()
    {
        if (_previewBitmap is null)
        {
            return;
        }

        try
        {
            if (!ImageAutoCropDetector.TryDetectCropBounds(_previewBitmap, out var detectedBounds))
            {
                MessageBox.Show(this, "Auto crop could not detect clear borders on this frame.", "FrameShift", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SelectRatioMode("Free");
            var normalizedCrop = NormalizeCropRect(detectedBounds.X, detectedBounds.Y, detectedBounds.Width, detectedBounds.Height, _probe.VideoWidth, _probe.VideoHeight);
            var previousRect = _cropRect;
            SetCropRectFromSource(normalizedCrop);
            UpdateSizeLabel();
            InvalidateCropArea(previousRect, _cropRect);
        }
        catch (Exception ex)
        {
            var message = ConversionActionHelper.GetFriendlyExceptionMessage(ex, "Auto crop failed.");
            MessageBox.Show(this, message, "FrameShift", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private VideoCropSettings GetSourceCropRect()
    {
        if (_previewBitmap is null || _imageBounds.Width <= 1 || _imageBounds.Height <= 1 || _probe.VideoWidth <= 0 || _probe.VideoHeight <= 0)
        {
            return new VideoCropSettings(0, 0, Math.Max(1, _probe.VideoWidth), Math.Max(1, _probe.VideoHeight));
        }

        var scaleX = _probe.VideoWidth / (double)_imageBounds.Width;
        var scaleY = _probe.VideoHeight / (double)_imageBounds.Height;
        var x = (int)Math.Round((_cropRect.X - _imageBounds.X) * scaleX);
        var y = (int)Math.Round((_cropRect.Y - _imageBounds.Y) * scaleY);
        var width = (int)Math.Round(_cropRect.Width * scaleX);
        var height = (int)Math.Round(_cropRect.Height * scaleY);
        return NormalizeCropRect(x, y, width, height, _probe.VideoWidth, _probe.VideoHeight);
    }

    private void SetCropRectFromSource(VideoCropSettings sourceCrop)
    {
        if (_previewBitmap is null || _probe.VideoWidth <= 0 || _probe.VideoHeight <= 0)
        {
            return;
        }

        var scaleX = _imageBounds.Width / (double)_probe.VideoWidth;
        var scaleY = _imageBounds.Height / (double)_probe.VideoHeight;
        var x = _imageBounds.X + (sourceCrop.X * scaleX);
        var y = _imageBounds.Y + (sourceCrop.Y * scaleY);
        var width = sourceCrop.Width * scaleX;
        var height = sourceCrop.Height * scaleY;
        _cropRect = new RectangleF((float)x, (float)y, (float)width, (float)height);
        _cropRect = ClampCropRect(_cropRect, _imageBounds, GetRatioValue(GetSelectedRatioMode()), GetMinimumPreviewCropSize());
    }

    private void UpdateSizeLabel()
    {
        var sourceCrop = GetSourceCropRect();
        _sizeLabel.Text = $"Crop: {sourceCrop.Width} x {sourceCrop.Height} px";
    }

    private void UpdateFrameLabel(double seconds)
    {
        _frameLabel.Text = $"Frame: {FormatTimeForDisplay(seconds)}";
    }

    private void DisposePreviewBitmaps()
    {
        _previewBitmap?.Dispose();
        _previewBitmap = null;
    }

    private void PreviewPanelOnPaint(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(_previewPanel.BackColor);

        if (_previewBitmap is not null)
        {
            e.Graphics.InterpolationMode = _isLiveResize
                ? System.Drawing.Drawing2D.InterpolationMode.Bilinear
                : System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = _isLiveResize
                ? System.Drawing.Drawing2D.PixelOffsetMode.Default
                : System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            e.Graphics.SmoothingMode = _isLiveResize
                ? System.Drawing.Drawing2D.SmoothingMode.Default
                : System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            e.Graphics.DrawImage(_previewBitmap, _imageBounds);
        }

        using var overlayBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
        using var outer = new Region(_imageBounds);
        outer.Exclude(_cropRect);
        e.Graphics.FillRegion(overlayBrush, outer);

        using var pen = new Pen(Color.White, 2);
        using var accentPen = new Pen(Color.FromArgb(32, 145, 255), 1);
        using var handleBrush = new SolidBrush(Color.White);
        e.Graphics.DrawRectangle(pen, (int)_cropRect.X, (int)_cropRect.Y, (int)_cropRect.Width, (int)_cropRect.Height);

        var thirdW = _cropRect.Width / 3.0f;
        var thirdH = _cropRect.Height / 3.0f;
        e.Graphics.DrawLine(accentPen, _cropRect.X + thirdW, _cropRect.Top, _cropRect.X + thirdW, _cropRect.Bottom);
        e.Graphics.DrawLine(accentPen, _cropRect.X + (thirdW * 2.0f), _cropRect.Top, _cropRect.X + (thirdW * 2.0f), _cropRect.Bottom);
        e.Graphics.DrawLine(accentPen, _cropRect.Left, _cropRect.Y + thirdH, _cropRect.Right, _cropRect.Y + thirdH);
        e.Graphics.DrawLine(accentPen, _cropRect.Left, _cropRect.Y + (thirdH * 2.0f), _cropRect.Right, _cropRect.Y + (thirdH * 2.0f));

        foreach (var handle in GetHandles())
        {
            e.Graphics.FillRectangle(handleBrush, handle.Point.X - 4, handle.Point.Y - 4, 8, 8);
        }
    }

    private void PreviewPanelOnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var point = new PointF(e.X, e.Y);
        _dragMode = GetHitMode(point);
        if (string.IsNullOrWhiteSpace(_dragMode))
        {
            if (_previewZoom > MinimumPreviewZoom && _imageBounds.Contains(point))
            {
                _dragMode = "panview";
                _dragStart = point;
                _panStartCenter = _previewSourceCenter;
                _previewPanel.Cursor = Cursors.Hand;
            }
            return;
        }

        _dragStart = point;
        _dragRect = _cropRect;
    }

    private void PreviewPanelOnMouseMove(object? sender, MouseEventArgs e)
    {
        var point = new PointF(e.X, e.Y);
        if (string.IsNullOrWhiteSpace(_dragMode))
        {
            var hitMode = GetHitMode(point);
            _previewPanel.Cursor = string.IsNullOrWhiteSpace(hitMode) && _previewZoom > MinimumPreviewZoom && _imageBounds.Contains(point)
                ? Cursors.Hand
                : GetCursorForHitMode(hitMode);
            return;
        }

        if (_dragMode == "panview")
        {
            PanPreview(point);
            return;
        }

        var ratio = GetRatioValue(GetSelectedRatioMode());
        var minPreviewSize = GetMinimumPreviewCropSize();
        var nextRect = _dragMode == "move"
            ? MoveRect(_dragRect, point.X - _dragStart.X, point.Y - _dragStart.Y, _imageBounds)
            : ResizeFromDrag(_dragRect, point, _dragMode, ratio, _imageBounds, minPreviewSize);

        var oldRect = _cropRect;
        _cropRect = nextRect;
        UpdateSizeLabel();
        InvalidateCropArea(oldRect, _cropRect);
    }

    private void PreviewPanelOnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragMode = string.Empty;
        var point = new PointF(e.X, e.Y);
        var hitMode = GetHitMode(point);
        _previewPanel.Cursor = string.IsNullOrWhiteSpace(hitMode) && _previewZoom > MinimumPreviewZoom && _imageBounds.Contains(point)
            ? Cursors.Hand
            : GetCursorForHitMode(hitMode);
    }

    private void PreviewPanelOnMouseWheel(object? sender, MouseEventArgs e)
    {
        if (_previewBitmap is null || e.Delta == 0)
        {
            return;
        }

        var zoomFactor = (float)Math.Pow(PreviewZoomStep, e.Delta / 120.0);
        ZoomPreviewAtPoint(_previewZoom * zoomFactor, e.Location);
    }

    private void ApplyRatioToCurrentCrop()
    {
        var oldRect = _cropRect;
        _cropRect = ClampCropRect(_cropRect, _imageBounds, GetRatioValue(GetSelectedRatioMode()), GetMinimumPreviewCropSize());
        CenterCropRect();
        UpdateSizeLabel();
        InvalidateCropArea(oldRect, _cropRect);
    }

    private void InvalidateCropArea(RectangleF oldRect, RectangleF newRect)
    {
        var oldNorm = NormalizeRect(oldRect);
        var newNorm = NormalizeRect(newRect);
        var union = RectangleF.Union(oldNorm, newNorm);
        union.Inflate(18.0f, 18.0f);

        var left = Math.Max(0, (int)Math.Floor(union.X));
        var top = Math.Max(0, (int)Math.Floor(union.Y));
        var right = Math.Min(_previewPanel.ClientSize.Width, (int)Math.Ceiling(union.Right));
        var bottom = Math.Min(_previewPanel.ClientSize.Height, (int)Math.Ceiling(union.Bottom));
        if (right <= left || bottom <= top)
        {
            _previewPanel.Invalidate();
            return;
        }

        _previewPanel.Invalidate(new Rectangle(left, top, right - left, bottom - top));
    }

    private string GetHitMode(PointF point)
    {
        const float handle = 9.0f;
        foreach (var handleHit in GetHandles())
        {
            if (Math.Abs(point.X - handleHit.Point.X) <= handle && Math.Abs(point.Y - handleHit.Point.Y) <= handle)
            {
                return handleHit.Mode;
            }
        }

        if (_cropRect.Contains(point))
        {
            return "move";
        }

        return string.Empty;
    }

    private Cursor GetCursorForHitMode(string hitMode)
    {
        return hitMode switch
        {
            "panview" => Cursors.Hand,
            "move" => Cursors.SizeAll,
            "tl" or "br" => Cursors.SizeNWSE,
            "tr" or "bl" => Cursors.SizeNESW,
            "lm" or "rm" => Cursors.SizeWE,
            "tm" or "bm" => Cursors.SizeNS,
            _ => Cursors.Default
        };
    }

    private IReadOnlyList<(string Mode, PointF Point)> GetHandles()
    {
        return
        [
            ("tl", new PointF(_cropRect.Left, _cropRect.Top)),
            ("tm", new PointF(_cropRect.Left + (_cropRect.Width / 2.0f), _cropRect.Top)),
            ("tr", new PointF(_cropRect.Right, _cropRect.Top)),
            ("rm", new PointF(_cropRect.Right, _cropRect.Top + (_cropRect.Height / 2.0f))),
            ("br", new PointF(_cropRect.Right, _cropRect.Bottom)),
            ("bm", new PointF(_cropRect.Left + (_cropRect.Width / 2.0f), _cropRect.Bottom)),
            ("bl", new PointF(_cropRect.Left, _cropRect.Bottom)),
            ("lm", new PointF(_cropRect.Left, _cropRect.Top + (_cropRect.Height / 2.0f)))
        ];
    }

    private static RectangleF MoveRect(RectangleF rect, float dx, float dy, RectangleF bounds)
    {
        rect.X += dx;
        rect.Y += dy;
        rect = NormalizeRect(rect);

        if (rect.Width > bounds.Width)
        {
            rect.Width = bounds.Width;
        }

        if (rect.Height > bounds.Height)
        {
            rect.Height = bounds.Height;
        }

        if (rect.X < bounds.X)
        {
            rect.X = bounds.X;
        }

        if (rect.Y < bounds.Y)
        {
            rect.Y = bounds.Y;
        }

        if (rect.Right > bounds.Right)
        {
            rect.X = bounds.Right - rect.Width;
        }

        if (rect.Bottom > bounds.Bottom)
        {
            rect.Y = bounds.Bottom - rect.Height;
        }

        return rect;
    }

    private static RectangleF ResizeFromDrag(
        RectangleF rect,
        PointF point,
        string mode,
        double? ratio,
        RectangleF bounds,
        SizeF minSize)
    {
        return mode switch
        {
            "tl" => ResizeCorner(rect, point, bounds, ratio, minSize, fixedRight: rect.Right, fixedBottom: rect.Bottom),
            "tr" => ResizeCorner(rect, point, bounds, ratio, minSize, fixedLeft: rect.Left, fixedBottom: rect.Bottom),
            "bl" => ResizeCorner(rect, point, bounds, ratio, minSize, fixedRight: rect.Right, fixedTop: rect.Top),
            "br" => ResizeCorner(rect, point, bounds, ratio, minSize, fixedLeft: rect.Left, fixedTop: rect.Top),
            "tm" => ratio is not null
                ? ResizeVerticalEdgeWithRatio(rect, point, bounds, ratio.Value, minSize, anchorTop: false)
                : new RectangleF(rect.Left, Clamp(point.Y, bounds.Top, rect.Bottom - minSize.Height), rect.Width, Math.Max(minSize.Height, rect.Bottom - Clamp(point.Y, bounds.Top, rect.Bottom - minSize.Height))),
            "bm" => ratio is not null
                ? ResizeVerticalEdgeWithRatio(rect, point, bounds, ratio.Value, minSize, anchorTop: true)
                : new RectangleF(rect.Left, rect.Top, rect.Width, Math.Max(minSize.Height, Clamp(point.Y, rect.Top + minSize.Height, bounds.Bottom) - rect.Top)),
            "lm" => ratio is not null
                ? ResizeHorizontalEdgeWithRatio(rect, point, bounds, ratio.Value, minSize, anchorLeft: false)
                : new RectangleF(Clamp(point.X, bounds.Left, rect.Right - minSize.Width), rect.Top, Math.Max(minSize.Width, rect.Right - Clamp(point.X, bounds.Left, rect.Right - minSize.Width)), rect.Height),
            "rm" => ratio is not null
                ? ResizeHorizontalEdgeWithRatio(rect, point, bounds, ratio.Value, minSize, anchorLeft: true)
                : new RectangleF(rect.Left, rect.Top, Math.Max(minSize.Width, Clamp(point.X, rect.Left + minSize.Width, bounds.Right) - rect.Left), rect.Height),
            _ => rect
        };
    }

    private static RectangleF ResizeHorizontalEdgeWithRatio(
        RectangleF rect,
        PointF point,
        RectangleF bounds,
        double ratio,
        SizeF minSize,
        bool anchorLeft)
    {
        var fixedX = anchorLeft ? rect.Left : rect.Right;
        var movingX = anchorLeft
            ? Clamp(point.X, rect.Left + minSize.Width, bounds.Right)
            : Clamp(point.X, bounds.Left, rect.Right - minSize.Width);

        var width = Math.Max(minSize.Width, Math.Abs(movingX - fixedX));
        var height = (float)(width / ratio);
        if (height < minSize.Height)
        {
            height = minSize.Height;
            width = (float)(height * ratio);
        }

        var centerY = rect.Top + (rect.Height / 2.0f);
        var top = centerY - (height / 2.0f);
        if (top < bounds.Top)
        {
            top = bounds.Top;
        }

        var bottom = top + height;
        if (bottom > bounds.Bottom)
        {
            bottom = bounds.Bottom;
            top = bottom - height;
        }

        if (anchorLeft)
        {
            return new RectangleF(fixedX, top, width, bottom - top);
        }

        return new RectangleF(fixedX - width, top, width, bottom - top);
    }

    private static RectangleF ResizeVerticalEdgeWithRatio(
        RectangleF rect,
        PointF point,
        RectangleF bounds,
        double ratio,
        SizeF minSize,
        bool anchorTop)
    {
        var fixedY = anchorTop ? rect.Top : rect.Bottom;
        var movingY = anchorTop
            ? Clamp(point.Y, rect.Top + minSize.Height, bounds.Bottom)
            : Clamp(point.Y, bounds.Top, rect.Bottom - minSize.Height);

        var height = Math.Max(minSize.Height, Math.Abs(movingY - fixedY));
        var width = (float)(height * ratio);
        if (width < minSize.Width)
        {
            width = minSize.Width;
            height = (float)(width / ratio);
        }

        var centerX = rect.Left + (rect.Width / 2.0f);
        var left = centerX - (width / 2.0f);
        if (left < bounds.Left)
        {
            left = bounds.Left;
        }

        var right = left + width;
        if (right > bounds.Right)
        {
            right = bounds.Right;
            left = right - width;
        }

        if (anchorTop)
        {
            return new RectangleF(left, fixedY, right - left, height);
        }

        return new RectangleF(left, fixedY - height, right - left, height);
    }

    private static RectangleF ResizeCorner(
        RectangleF rect,
        PointF point,
        RectangleF bounds,
        double? ratio,
        SizeF minSize,
        float? fixedLeft = null,
        float? fixedRight = null,
        float? fixedTop = null,
        float? fixedBottom = null)
    {
        var left = fixedLeft ?? point.X;
        var right = fixedRight ?? point.X;
        var top = fixedTop ?? point.Y;
        var bottom = fixedBottom ?? point.Y;

        if (fixedLeft.HasValue)
        {
            right = Clamp(point.X, fixedLeft.Value + minSize.Width, bounds.Right);
        }
        if (fixedRight.HasValue)
        {
            left = Clamp(point.X, bounds.Left, fixedRight.Value - minSize.Width);
        }
        if (fixedTop.HasValue)
        {
            bottom = Clamp(point.Y, fixedTop.Value + minSize.Height, bounds.Bottom);
        }
        if (fixedBottom.HasValue)
        {
            top = Clamp(point.Y, bounds.Top, fixedBottom.Value - minSize.Height);
        }

        var width = Math.Abs(right - left);
        var height = Math.Abs(bottom - top);

        if (ratio is not null && width > 1 && height > 1)
        {
            if (width / height > ratio.Value)
            {
                width = (float)(height * ratio.Value);
            }
            else
            {
                height = (float)(width / ratio.Value);
            }

            if (fixedLeft.HasValue)
            {
                right = fixedLeft.Value + width;
            }
            else if (fixedRight.HasValue)
            {
                left = fixedRight.Value - width;
            }

            if (fixedTop.HasValue)
            {
                bottom = fixedTop.Value + height;
            }
            else if (fixedBottom.HasValue)
            {
                top = fixedBottom.Value - height;
            }
        }

        if (ratio is not null)
        {
            var minScale = Math.Max(minSize.Width / Math.Max(1.0f, width), minSize.Height / Math.Max(1.0f, height));
            if (minScale > 1.0f)
            {
                width *= minScale;
                height *= minScale;

                if (fixedLeft.HasValue)
                {
                    right = fixedLeft.Value + width;
                }
                else if (fixedRight.HasValue)
                {
                    left = fixedRight.Value - width;
                }

                if (fixedTop.HasValue)
                {
                    bottom = fixedTop.Value + height;
                }
                else if (fixedBottom.HasValue)
                {
                    top = fixedBottom.Value - height;
                }
            }
        }

        return new RectangleF(
            Math.Min(left, right),
            Math.Min(top, bottom),
            Math.Max(minSize.Width, Math.Abs(right - left)),
            Math.Max(minSize.Height, Math.Abs(bottom - top)));
    }

    private static RectangleF ClampCropRect(RectangleF rect, RectangleF bounds, double? ratio, SizeF minSize)
    {
        rect = NormalizeRect(rect);
        if (ratio is not null)
        {
            if (rect.Width / rect.Height > ratio.Value)
            {
                rect.Width = (float)(rect.Height * ratio.Value);
            }
            else
            {
                rect.Height = (float)(rect.Width / ratio.Value);
            }

            var minScale = Math.Max(minSize.Width / Math.Max(1.0f, rect.Width), minSize.Height / Math.Max(1.0f, rect.Height));
            if (minScale > 1.0f)
            {
                rect.Width *= minScale;
                rect.Height *= minScale;
            }

            var maxScale = Math.Min(bounds.Width / Math.Max(1.0f, rect.Width), bounds.Height / Math.Max(1.0f, rect.Height));
            if (maxScale < 1.0f)
            {
                rect.Width *= maxScale;
                rect.Height *= maxScale;
            }
        }
        else
        {
            if (rect.Width < minSize.Width)
            {
                rect.Width = minSize.Width;
            }

            if (rect.Height < minSize.Height)
            {
                rect.Height = minSize.Height;
            }

            if (rect.Width > bounds.Width)
            {
                rect.Width = bounds.Width;
            }

            if (rect.Height > bounds.Height)
            {
                rect.Height = bounds.Height;
            }
        }

        if (rect.X < bounds.X)
        {
            rect.X = bounds.X;
        }

        if (rect.Y < bounds.Y)
        {
            rect.Y = bounds.Y;
        }

        if (rect.Right > bounds.Right)
        {
            rect.X = bounds.Right - rect.Width;
        }

        if (rect.Bottom > bounds.Bottom)
        {
            rect.Y = bounds.Bottom - rect.Height;
        }

        return rect;
    }

    private static RectangleF NormalizeRect(RectangleF rect)
    {
        var x = Math.Min(rect.Left, rect.Right);
        var y = Math.Min(rect.Top, rect.Bottom);
        var width = Math.Abs(rect.Width);
        var height = Math.Abs(rect.Height);
        return new RectangleF(x, y, width, height);
    }

    private static double? GetRatioValue(string? mode)
    {
        return mode switch
        {
            "Square" => 1.0,
            "16:9" => 16.0 / 9.0,
            "9:16" => 9.0 / 16.0,
            "4:3" => 4.0 / 3.0,
            "3:4" => 3.0 / 4.0,
            "3:2" => 3.0 / 2.0,
            "2:3" => 2.0 / 3.0,
            _ => null
        };
    }

    private string GetSelectedRatioMode()
    {
        return _ratioButtons.FirstOrDefault(button => button.Checked)?.Tag as string ?? "Free";
    }

    private void SelectRatioMode(string mode)
    {
        var targetButton = _ratioButtons.FirstOrDefault(button => string.Equals(button.Tag as string, mode, StringComparison.OrdinalIgnoreCase));
        if (targetButton is not null && !targetButton.Checked)
        {
            targetButton.Checked = true;
        }
    }

    private static string FormatTimeForDisplay(double seconds)
    {
        if (seconds < 0)
        {
            seconds = 0;
        }

        var hours = (int)Math.Floor(seconds / 3600);
        var remaining = seconds - (hours * 3600);
        var minutes = (int)Math.Floor(remaining / 60);
        var secs = remaining - (minutes * 60);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:00.000}", hours, minutes, secs);
    }

    private static VideoCropSettings NormalizeCropRect(int x, int y, int width, int height, int sourceWidth, int sourceHeight)
    {
        var minWidth = sourceWidth >= MinSourceCropDimension ? MinSourceCropDimension : 1;
        var minHeight = sourceHeight >= MinSourceCropDimension ? MinSourceCropDimension : 1;

        if (x < 0)
        {
            x = 0;
        }

        if (y < 0)
        {
            y = 0;
        }

        if (width < minWidth)
        {
            width = minWidth;
        }

        if (height < minHeight)
        {
            height = minHeight;
        }

        if (x + width > sourceWidth)
        {
            width = sourceWidth - x;
        }

        if (y + height > sourceHeight)
        {
            height = sourceHeight - y;
        }

        if (x > 0 && x % 2 != 0 && width > minWidth)
        {
            x++;
            width--;
        }

        if (y > 0 && y % 2 != 0 && height > minHeight)
        {
            y++;
            height--;
        }

        if (width > 1 && width % 2 != 0)
        {
            if (width > minWidth)
            {
                width--;
            }
            else if (x + width < sourceWidth)
            {
                width++;
            }
        }

        if (height > 1 && height % 2 != 0)
        {
            if (height > minHeight)
            {
                height--;
            }
            else if (y + height < sourceHeight)
            {
                height++;
            }
        }

        if (x > 0 && x % 2 != 0)
        {
            x--;
        }

        if (y > 0 && y % 2 != 0)
        {
            y--;
        }

        if (x + width > sourceWidth)
        {
            width = sourceWidth - x;
        }

        if (y + height > sourceHeight)
        {
            height = sourceHeight - y;
        }

        if (width < minWidth)
        {
            width = minWidth;
        }

        if (height < minHeight)
        {
            height = minHeight;
        }

        return new VideoCropSettings(x, y, width, height);
    }

    private SizeF GetMinimumPreviewCropSize()
    {
        if (_probe.VideoWidth <= 0 || _probe.VideoHeight <= 0 || _imageBounds.Width <= 0 || _imageBounds.Height <= 0)
        {
            return new SizeF(MinSourceCropDimension, MinSourceCropDimension);
        }

        var sourceMin = GetMinimumSourceCropSize(GetSelectedRatioMode());
        var scaleX = _imageBounds.Width / _probe.VideoWidth;
        var scaleY = _imageBounds.Height / _probe.VideoHeight;
        return new SizeF(
            Math.Max(2.0f, (float)(sourceMin.Width * scaleX)),
            Math.Max(2.0f, (float)(sourceMin.Height * scaleY)));
    }

    private static SizeF GetMinimumSourceCropSize(string? mode)
    {
        var ratio = GetRatioValue(mode);
        if (ratio is null)
        {
            return new SizeF(MinSourceCropDimension, MinSourceCropDimension);
        }

        if (ratio.Value >= 1.0)
        {
            return new SizeF((float)(MinSourceCropDimension * ratio.Value), MinSourceCropDimension);
        }

        return new SizeF(MinSourceCropDimension, (float)(MinSourceCropDimension / ratio.Value));
    }

    private void RefreshPreviewLayout()
    {
        if (_previewBitmap is null || _closing || IsDisposed)
        {
            return;
        }

        if (_previewPanel.ClientSize.Width <= 1 || _previewPanel.ClientSize.Height <= 1)
        {
            return;
        }

        var sourceCrop = GetSourceCropRect();
        UpdateImageBounds();
        SetCropRectFromSource(sourceCrop);
        UpdateSizeLabel();
        _previewPanel.Invalidate();
    }

    private void SchedulePreviewLayoutRefresh()
    {
        if (_closing || IsDisposed)
        {
            return;
        }

        _resizeRefreshTimer.Stop();
        _resizeRefreshTimer.Start();
    }

    private void ZoomPreviewAtPoint(float zoom, Point clientAnchor)
    {
        if (_previewBitmap is null)
        {
            return;
        }

        var nextZoom = Math.Clamp(zoom, MinimumPreviewZoom, MaximumPreviewZoom);
        if (Math.Abs(nextZoom - _previewZoom) < 0.0001f)
        {
            return;
        }

        var sourceCrop = GetSourceCropRect();
        var anchorSourcePoint = GetSourcePointForPreviewPoint(clientAnchor);
        _previewZoom = nextZoom;

        var displayScale = _fitImageScale * _previewZoom;
        var panelCenterX = _previewPanel.ClientSize.Width / 2.0f;
        var panelCenterY = _previewPanel.ClientSize.Height / 2.0f;
        _previewSourceCenter = new PointF(
            (float)(anchorSourcePoint.X - ((clientAnchor.X - panelCenterX) / displayScale)),
            (float)(anchorSourcePoint.Y - ((clientAnchor.Y - panelCenterY) / displayScale)));
        ClampPreviewSourceCenter();
        UpdateImageBounds();
        SetCropRectFromSource(sourceCrop);
        UpdateSizeLabel();
        _previewPanel.Invalidate();
    }

    private PointF GetSourcePointForPreviewPoint(Point clientPoint)
    {
        if (_previewBitmap is null || _imageBounds.Width <= 0.001f || _imageBounds.Height <= 0.001f)
        {
            return new PointF(_probe.VideoWidth / 2.0f, _probe.VideoHeight / 2.0f);
        }

        var scaleX = _probe.VideoWidth / _imageBounds.Width;
        var scaleY = _probe.VideoHeight / _imageBounds.Height;
        var sourceX = (float)Math.Clamp((clientPoint.X - _imageBounds.X) * scaleX, 0, _probe.VideoWidth);
        var sourceY = (float)Math.Clamp((clientPoint.Y - _imageBounds.Y) * scaleY, 0, _probe.VideoHeight);
        return new PointF(sourceX, sourceY);
    }

    private void ClampPreviewSourceCenter()
    {
        if (_previewBitmap is null)
        {
            return;
        }

        var displayScale = _fitImageScale * _previewZoom;
        if (displayScale <= 0.0001f)
        {
            _previewSourceCenter = new PointF(_probe.VideoWidth / 2.0f, _probe.VideoHeight / 2.0f);
            return;
        }

        var halfVisibleWidth = (_previewPanel.ClientSize.Width / 2.0f) / displayScale;
        var halfVisibleHeight = (_previewPanel.ClientSize.Height / 2.0f) / displayScale;

        if (_probe.VideoWidth <= halfVisibleWidth * 2.0f)
        {
            _previewSourceCenter.X = _probe.VideoWidth / 2.0f;
        }
        else
        {
            _previewSourceCenter.X = Clamp(_previewSourceCenter.X, (float)halfVisibleWidth, _probe.VideoWidth - (float)halfVisibleWidth);
        }

        if (_probe.VideoHeight <= halfVisibleHeight * 2.0f)
        {
            _previewSourceCenter.Y = _probe.VideoHeight / 2.0f;
        }
        else
        {
            _previewSourceCenter.Y = Clamp(_previewSourceCenter.Y, (float)halfVisibleHeight, _probe.VideoHeight - (float)halfVisibleHeight);
        }
    }

    private void PanPreview(PointF point)
    {
        if (_previewBitmap is null)
        {
            return;
        }

        var displayScale = _fitImageScale * _previewZoom;
        if (displayScale <= 0.0001f)
        {
            return;
        }

        var dx = point.X - _dragStart.X;
        var dy = point.Y - _dragStart.Y;
        var sourceCrop = GetSourceCropRect();
        _previewSourceCenter = new PointF(
            _panStartCenter.X - (dx / displayScale),
            _panStartCenter.Y - (dy / displayScale));
        ClampPreviewSourceCenter();
        UpdateImageBounds();
        SetCropRectFromSource(sourceCrop);
        UpdateSizeLabel();
        _previewPanel.Invalidate();
    }

    private static float Clamp(float value, float min, float max)
    {
        if (min > max)
        {
            return min;
        }

        return Math.Max(min, Math.Min(max, value));
    }

}
