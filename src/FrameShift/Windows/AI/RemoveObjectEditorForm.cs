using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FrameShift.Core.AI.RemoveObject;
using FrameShift.Core.Logging;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.AI;

public sealed class RemoveObjectEditorForm : Form
{
    private const float ZoomStep = 1.15f;
    private const float MinZoom = 0.05f;
    private const float MaxZoom = 16f;
    private const int DefaultBrushSize = 40;

    private readonly string _inputPath;
    private readonly AppLogger _logger;

    // Canvas state
    private Bitmap? _imageBitmap;
    private bool[,]? _maskData;
    private Bitmap? _maskOverlay;
    private float _zoom = 1f;
    private PointF _panOffset;
    private bool _isPanning;
    private Point _panStart;
    private PointF _panOffsetAtStart;
    private bool _isDrawing;
    private Point _lastDrawPoint;

    // Tool state
    private bool _isBrushMode = true;
    private int _brushSize = DefaultBrushSize;
    private Point? _cursorPos; // null when mouse is outside the canvas

    // Blank cursor (hide system cursor over canvas so we draw our own circle)
    private static readonly Cursor s_hiddenCursor = CreateHiddenCursor();
    private static Cursor CreateHiddenCursor()
    {
        using var bmp = new Bitmap(1, 1);
        return new Cursor(bmp.GetHicon());
    }

    // Inference
    private ObjectRemovalEngine? _engine;
    private string? _engineModelId;
    private readonly OnnxFormLifetime _lifetime = new();
    private Task? _closingTask;
    private bool _inferRunning;
    private bool _closingRequested;
    private bool _allowClose;
    private bool _resourcesCleaned;
    private bool _disposeRequested;
    private DialogResult? _requestedDialogResult;

    // UI controls
    private Panel _canvasPanel = null!;
    private Button _btnBrush = null!;
    private Button _btnEraser = null!;
    private TrackBar _brushSizeSlider = null!;
    private NumericUpDown _brushSizeNum = null!;
    private Button _btnReset = null!;
    private Button _btnFit = null!;
    private Label _zoomLabel = null!;
    private ComboBox _modelCombo = null!;
    private Button _btnCancel = null!;
    private Button _btnApply = null!;
    private ProgressBar _progressBar = null!;
    private Label _progressLabel = null!;
    private Panel _progressPanel = null!;

    public RemoveObjectEditorForm(string inputPath, AppLogger logger)
    {
        _inputPath = inputPath;
        _logger = logger;

        SuspendLayout();
        BuildUi();
        ResumeLayout(false);
    }

    private Task? _imageLoadTask;
    private int _canvasDpi = 96;

    private void BuildUi()
    {
        FrameShiftWindowPolicy.Initialize(this, new Size(1100, 720), new Size(480, 340));
        FrameShiftWindowChrome.Apply(this, "FrameShift - Remove Object", IconPaths.RemoveObjectAiIcon, IconPaths.FrameShiftAiIcon);
        ControlHelper.SetDoubleBuffered(this);
        _canvasPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 30), TabStop = true };
        _canvasPanel.AccessibleName = "Object removal mask canvas";
        _canvasPanel.AccessibleDescription = "Image and painted removal mask. Brush, eraser and zoom controls are available in the options panel.";
        ControlHelper.SetDoubleBuffered(_canvasPanel);
        _canvasPanel.Cursor = s_hiddenCursor;
        _canvasPanel.Paint += CanvasOnPaint;
        _canvasPanel.MouseDown += CanvasOnMouseDown;
        _canvasPanel.MouseMove += CanvasOnMouseMove;
        _canvasPanel.MouseUp += CanvasOnMouseUp;
        _canvasPanel.MouseWheel += CanvasOnMouseWheel;
        _canvasPanel.MouseEnter += (_, _) => _canvasPanel.Focus();
        _canvasPanel.MouseLeave += (_, _) => { _cursorPos = null; _canvasPanel.Invalidate(); };
        _canvasPanel.KeyDown += CanvasOnKeyDown;
        _canvasPanel.Resize += (_, _) => _canvasPanel.Invalidate();
        _canvasPanel.HandleCreated += (_, _) => _canvasDpi = _canvasPanel.DeviceDpi;
        _canvasPanel.DpiChangedAfterParent += (_, _) =>
        {
            var ratio = _canvasPanel.DeviceDpi / (float)_canvasDpi;
            _canvasDpi = _canvasPanel.DeviceDpi;
            _zoom *= ratio;
            _panOffset = new PointF(_panOffset.X * ratio, _panOffset.Y * ratio);
            UpdateZoomLabel();
            _canvasPanel.Invalidate();
        };
        _btnBrush = FrameShiftUiFactory.CreateMeasuredActionButton("Brush", false);
        _btnBrush.Click += (_, _) => SetTool(true);
        _btnEraser = FrameShiftUiFactory.CreateMeasuredActionButton("Eraser", false);
        _btnEraser.Click += (_, _) => SetTool(false);
        var mode = FrameShiftUiFactory.CreateSection("Mode", FrameShiftUiFactory.CreateChoiceRow(_btnBrush, _btnEraser));
        _brushSizeSlider = new TrackBar { Minimum = 1, Maximum = 200, Value = DefaultBrushSize, TickFrequency = 20, TickStyle = TickStyle.None };
        _brushSizeNum = new NumericUpDown { Minimum = 1, Maximum = 200, Value = DefaultBrushSize };
        _brushSizeSlider.ValueChanged += (_, _) => { _brushSize = _brushSizeSlider.Value; if (_brushSizeNum.Value != _brushSize) _brushSizeNum.Value = _brushSize; };
        _brushSizeNum.ValueChanged += (_, _) => { _brushSize = (int)_brushSizeNum.Value; if (_brushSizeSlider.Value != _brushSize) _brushSizeSlider.Value = _brushSize; };
        var size = FrameShiftUiFactory.CreateSection("Brush size", FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateFieldRow("Diameter", _brushSizeNum, "px", 96), _brushSizeSlider));
        _btnReset = FrameShiftUiFactory.CreateMeasuredActionButton("Reset mask", false);
        _btnReset.Click += (_, _) => ResetMask();
        _btnFit = FrameShiftUiFactory.CreateMeasuredActionButton("Fit", false);
        _btnFit.Click += (_, _) => FitToWindow();
        _zoomLabel = FrameShiftUiFactory.CreateWrappingLabel("Zoom 100%");
        var actions = FrameShiftUiFactory.CreateSection("View & mask", FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateChoiceRow(_btnReset, _btnFit), _zoomLabel));
        _modelCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var def in ObjectRemovalModelCatalog.GetAll()) _modelCombo.Items.Add(new ModelComboItem(def));
        _modelCombo.SelectedIndex = 0;
        var model = FrameShiftUiFactory.CreateSection("Model", FrameShiftUiFactory.CreateFieldRow("Model", _modelCombo));
        _progressBar = new ProgressBar { Style = ProgressBarStyle.Continuous };
        _progressLabel = FrameShiftUiFactory.CreateWrappingLabel("Loading image...");
        _progressPanel = FrameShiftUiFactory.CreateSection("Activity", FrameShiftUiFactory.CreateVerticalStack(_progressBar, _progressLabel));
        _btnCancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        _btnCancel.Click += (_, _) => CancelOrClose();
        _btnApply = FrameShiftUiFactory.CreateMeasuredActionButton("Apply", true);
        _btnApply.Enabled = false;
        _btnApply.Click += async (_, _) => await StartInferenceAsync();
        var header = FrameShiftUiFactory.CreateHeader("FrameShift - Remove Object", $"Source: {Path.GetFileName(_inputPath)}",
            IconPaths.RemoveObjectAiIcon, IconPaths.FrameShiftAiIcon, "✂");
        Controls.Add(FrameShiftEditorShellUi.Create(header, _canvasPanel, FrameShiftDialogLayout.CreateActions(_btnCancel, _btnApply),
            FrameShiftUiFactory.CreateVerticalStack(mode, size, actions, model, _progressPanel),
            FrameShiftUiFactory.CreateStatusMessage("Paint over the object to remove. PNG output stays next to the source. [ ] adjusts brush size.")));
        AcceptButton = _btnApply;
        CancelButton = _btnCancel;
        SetTool(true);
        Shown += async (_, _) => await StartImageAsync();
        FormClosing += OnFormClosing;
        FormClosed += (_, _) => _lifetime.Dispose();
    }

    internal Task StartImageAsync() => _imageLoadTask ??= _lifetime.RunAsync(async token =>
    {
        Bitmap? bitmap = null;
        try
        {
            var loaded = await Task.Run(() =>
            {
                var image = ImageBitmapHelper.LoadBitmap(_inputPath);
                try { return (Image: image, Mask: new bool[image.Width, image.Height]); }
                catch { image.Dispose(); throw; }
            }, token);
            bitmap = loaded.Image;
            token.ThrowIfCancellationRequested();
            if (!CanUpdateUi) return;
            _imageBitmap = bitmap;
            bitmap = null;
            _maskData = loaded.Mask;
            FitToWindow();
            _progressPanel.Visible = false;
            _btnApply.Enabled = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.Log($"RemoveObjectEditorForm: failed to load image. {ex}");
            if (CanUpdateUi) _progressLabel.Text = $"Image unavailable: {ex.Message}";
        }
        finally { bitmap?.Dispose(); }
    });

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposeRequested)
        {
            _disposeRequested = true;
            _closingRequested = true;
            _ = DisposeResourcesAsync();
        }
        base.Dispose(disposing);
    }

    private async Task DisposeResourcesAsync()
    {
        await _lifetime.BeginClosingAsync(CleanupResourcesAsync, ex => _logger.Log($"Remove Object cleanup: {ex}"));
        _lifetime.Dispose();
    }
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_closingTask is not null)
        {
            return;
        }

        _closingRequested = true;
        _requestedDialogResult ??= DialogResult == DialogResult.None ? DialogResult.Cancel : DialogResult;
        SetClosingUiState();
        _closingTask = CompleteCloseAsync();
    }

    // ─── Canvas painting ───────────────────────────────────────────────────────

    private void CanvasOnPaint(object? sender, PaintEventArgs e)
    {
        if (_imageBitmap is null) return;

        var g = e.Graphics;
        g.Clear(Color.FromArgb(30, 30, 30));
        g.InterpolationMode = _zoom < 1f ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        var destRect = GetImageDestRect();
        g.DrawImage(_imageBitmap, destRect);

        if (_maskOverlay != null)
        {
            using var ia = new System.Drawing.Imaging.ImageAttributes();
            var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.45f };
            ia.SetColorMatrix(cm);
            g.DrawImage(_maskOverlay, Rectangle.Round(destRect),
                0, 0, _maskOverlay.Width, _maskOverlay.Height,
                GraphicsUnit.Pixel, ia);
        }

        // Draw brush cursor circle — on top of everything
        if (_cursorPos.HasValue && !_isPanning && _zoom > 0)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float screenRadius = _brushSize * _zoom / 2f;
            float cx = _cursorPos.Value.X;
            float cy = _cursorPos.Value.Y;
            // Black outline for visibility on bright areas
            using var outlinePen = new Pen(Color.FromArgb(160, 0, 0, 0), 2.5f * _canvasPanel.DeviceDpi / 96f);
            g.DrawEllipse(outlinePen, cx - screenRadius, cy - screenRadius, screenRadius * 2, screenRadius * 2);
            // White inner circle
            using var circlePen = new Pen(Color.FromArgb(220, 255, 255, 255), 1.2f * _canvasPanel.DeviceDpi / 96f);
            g.DrawEllipse(circlePen, cx - screenRadius, cy - screenRadius, screenRadius * 2, screenRadius * 2);
            // Tiny crosshair dot at center
            using var dotPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1f);
            g.DrawLine(dotPen, cx - 3, cy, cx + 3, cy);
            g.DrawLine(dotPen, cx, cy - 3, cx, cy + 3);
        }
    }

    private RectangleF GetImageDestRect()
    {
        if (_imageBitmap is null) return RectangleF.Empty;
        float w = _imageBitmap.Width * _zoom;
        float h = _imageBitmap.Height * _zoom;
        return new RectangleF(_panOffset.X, _panOffset.Y, w, h);
    }

    // ─── Mouse: drawing ────────────────────────────────────────────────────────

    private void CanvasOnMouseDown(object? sender, MouseEventArgs e)
    {
        _canvasPanel.Focus();

        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && ModifierKeys == Keys.Space))
        {
            _isPanning = true;
            _panStart = e.Location;
            _panOffsetAtStart = _panOffset;
            _canvasPanel.Cursor = Cursors.SizeAll;
            return;
        }

        if (e.Button == MouseButtons.Left && !_inferRunning)
        {
            _isDrawing = true;
            _lastDrawPoint = e.Location;
            PaintMask(e.Location, _isBrushMode);
        }

        if (e.Button == MouseButtons.Right && !_inferRunning)
        {
            _isDrawing = true;
            _lastDrawPoint = e.Location;
            PaintMask(e.Location, false);
        }
    }

    private void CanvasOnMouseMove(object? sender, MouseEventArgs e)
    {
        _cursorPos = e.Location;

        if (_isPanning)
        {
            _panOffset = new PointF(
                _panOffsetAtStart.X + e.X - _panStart.X,
                _panOffsetAtStart.Y + e.Y - _panStart.Y);
            _canvasPanel.Invalidate();
            return;
        }

        if (_isDrawing)
        {
            var erase = e.Button == MouseButtons.Right || !_isBrushMode;
            DrawLine(_lastDrawPoint, e.Location, erase);
            _lastDrawPoint = e.Location;
        }

        _canvasPanel.Invalidate();
    }

    private void CanvasOnMouseUp(object? sender, MouseEventArgs e)
    {
        _isDrawing = false;
        _isPanning = false;
        _canvasPanel.Cursor = s_hiddenCursor;
    }

    private void CanvasOnMouseWheel(object? sender, MouseEventArgs e)
    {
        float factor = e.Delta > 0 ? ZoomStep : 1f / ZoomStep;
        float newZoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);

        // Zoom around cursor position
        float mx = e.X, my = e.Y;
        _panOffset = new PointF(
            mx - (mx - _panOffset.X) * (newZoom / _zoom),
            my - (my - _panOffset.Y) * (newZoom / _zoom));
        _zoom = newZoom;

        UpdateZoomLabel();
        _canvasPanel.Invalidate();
    }

    private void CanvasOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.OemOpenBrackets)
        {
            _brushSize = Math.Max(1, _brushSize - 5);
            _brushSizeSlider.Value = _brushSize;
            _brushSizeNum.Value = _brushSize;
            _canvasPanel.Invalidate();
        }
        else if (e.KeyCode == Keys.OemCloseBrackets)
        {
            _brushSize = Math.Min(200, _brushSize + 5);
            _brushSizeSlider.Value = _brushSize;
            _brushSizeNum.Value = _brushSize;
            _canvasPanel.Invalidate();
        }
    }

    // ─── Mask painting ─────────────────────────────────────────────────────────

    private void PaintMask(Point screenPt, bool paint)
    {
        if (_maskData is null || _imageBitmap is null) return;
        var imgPt = ScreenToImage(screenPt);
        PaintCircle((int)imgPt.X, (int)imgPt.Y, paint);
        RebuildMaskOverlay();
        _canvasPanel.Invalidate();
    }

    private void DrawLine(Point from, Point to, bool erase)
    {
        if (_maskData is null || _imageBitmap is null) return;

        var imgFrom = ScreenToImage(from);
        var imgTo = ScreenToImage(to);
        int dx = (int)imgTo.X - (int)imgFrom.X;
        int dy = (int)imgTo.Y - (int)imgFrom.Y;
        int steps = Math.Max(1, Math.Max(Math.Abs(dx), Math.Abs(dy)));

        for (int i = 0; i <= steps; i++)
        {
            int px = (int)imgFrom.X + dx * i / steps;
            int py = (int)imgFrom.Y + dy * i / steps;
            PaintCircle(px, py, !erase);
        }

        RebuildMaskOverlay();
        _canvasPanel.Invalidate();
    }

    private void PaintCircle(int cx, int cy, bool paint)
    {
        if (_maskData is null || _imageBitmap is null) return;

        int r = (int)Math.Ceiling(_brushSize / 2f);
        int r2 = r * r;
        int w = _imageBitmap.Width, h = _imageBitmap.Height;

        for (int y = cy - r; y <= cy + r; y++)
        {
            if (y < 0 || y >= h) continue;
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || x >= w) continue;
                int dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy <= r2)
                    _maskData[x, y] = paint;
            }
        }
    }

    private void RebuildMaskOverlay()
    {
        if (_maskData is null || _imageBitmap is null) return;

        int w = _imageBitmap.Width;
        int h = _imageBitmap.Height;

        _maskOverlay?.Dispose();
        _maskOverlay = new Bitmap(w, h, PixelFormat.Format32bppArgb);

        var bmpData = _maskOverlay.LockBits(
            new Rectangle(0, 0, w, h),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        int stride = bmpData.Stride;
        var buf = new byte[stride * h]; // zero = transparent

        for (int y = 0; y < h; y++)
        {
            int row = y * stride;
            for (int x = 0; x < w; x++)
            {
                if (_maskData[x, y])
                {
                    int i = row + x * 4;
                    buf[i]     = 30;  // B
                    buf[i + 1] = 30;  // G
                    buf[i + 2] = 220; // R
                    buf[i + 3] = 255; // A
                }
            }
        }

        Marshal.Copy(buf, 0, bmpData.Scan0, buf.Length);
        _maskOverlay.UnlockBits(bmpData);
    }

    // ─── Coordinate transforms ─────────────────────────────────────────────────

    private PointF ScreenToImage(Point screenPt)
    {
        if (_imageBitmap is null || _zoom <= 0) return PointF.Empty;
        float x = (screenPt.X - _panOffset.X) / _zoom;
        float y = (screenPt.Y - _panOffset.Y) / _zoom;
        return new PointF(
            Math.Clamp(x, 0, _imageBitmap.Width - 1),
            Math.Clamp(y, 0, _imageBitmap.Height - 1));
    }

    // ─── Tool / zoom helpers ───────────────────────────────────────────────────

    private void SetTool(bool brush)
    {
        _isBrushMode = brush;
        _btnBrush.BackColor = brush ? FrameShiftTheme.PrimaryButtonBackground : FrameShiftTheme.Surface;
        _btnBrush.ForeColor = brush ? Color.White : FrameShiftTheme.TextPrimary;
        _btnEraser.BackColor = brush ? FrameShiftTheme.Surface : FrameShiftTheme.PrimaryButtonBackground;
        _btnEraser.ForeColor = brush ? FrameShiftTheme.TextPrimary : Color.White;
        _btnBrush.FlatAppearance.MouseOverBackColor = brush ? FrameShiftTheme.PrimaryButtonHover : FrameShiftTheme.AccentSoft;
        _btnEraser.FlatAppearance.MouseOverBackColor = brush ? FrameShiftTheme.AccentSoft : FrameShiftTheme.PrimaryButtonHover;
        _btnBrush.FlatAppearance.MouseDownBackColor = brush ? FrameShiftTheme.PrimaryButtonPressed : FrameShiftTheme.AccentSoftHover;
        _btnEraser.FlatAppearance.MouseDownBackColor = brush ? FrameShiftTheme.AccentSoftHover : FrameShiftTheme.PrimaryButtonPressed;
        _btnBrush.Invalidate();
        _btnEraser.Invalidate();
    }

    private void ResetMask()
    {
        if (_maskData is null || _imageBitmap is null) return;
        Array.Clear(_maskData, 0, _maskData.Length);
        _maskOverlay?.Dispose();
        _maskOverlay = null;
        _canvasPanel.Invalidate();
    }

    private void FitToWindow()
    {
        if (_imageBitmap is null) return;
        float panelW = _canvasPanel.ClientSize.Width;
        float panelH = _canvasPanel.ClientSize.Height;
        if (panelW <= 0 || panelH <= 0) return;
        float scale = Math.Min(panelW / _imageBitmap.Width, panelH / _imageBitmap.Height);
        _zoom = scale;
        _panOffset = new PointF(
            (panelW - _imageBitmap.Width * scale) / 2f,
            (panelH - _imageBitmap.Height * scale) / 2f);
        UpdateZoomLabel();
        _canvasPanel.Invalidate();
    }

    private void UpdateZoomLabel()
    {
        _zoomLabel.Text = $"Zoom {(int)(_zoom * 100)}%";
    }

    private void CancelOrClose()
    {
        RequestClose(DialogResult.Cancel);
    }

    private void RequestClose(DialogResult requestedDialogResult)
    {
        _requestedDialogResult = requestedDialogResult;
        Close();
    }

    // ─── Inference ─────────────────────────────────────────────────────────────

    private bool CanUpdateUi => !_closingRequested && !IsDisposed && !Disposing;

    private async Task StartInferenceAsync()
    {
        if (_closingRequested)
        {
            return;
        }

        try
        {
            await _lifetime.RunAsync(RunInferenceCoreAsync).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsClosing || _lifetime.Token.IsCancellationRequested)
        {
        }
    }

    private async Task RunInferenceCoreAsync(CancellationToken cancellationToken)
    {
        if (_maskData is null || _imageBitmap is null || _closingRequested)
        {
            return;
        }

        if (!HasAnyMask())
        {
            MessageBox.Show("Nothing to remove — paint over the object first.",
                "FrameShift", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selected = _modelCombo.SelectedItem as ModelComboItem;
        if (selected is null) return;
        var def = selected.Definition;

        // Preflight: download model if needed
        if (!ModelLocator.ModelExists(def))
        {
            var ready = await DownloadModelAsync(def);
            if (!ready || cancellationToken.IsCancellationRequested || _closingRequested)
            {
                return;
            }
        }

        SetInferenceUiState(running: true);

        if (_engine == null || _engineModelId != def.Id)
        {
            _engine?.Dispose();
            _engine = new ObjectRemovalEngine(def);
            _engineModelId = def.Id;
        }

        var maskSnapshot = (bool[,])_maskData.Clone();

        try
        {
            var progress = new Progress<InpaintProgress>(p =>
            {
                if (CanUpdateUi)
                {
                    _progressBar.Value = Math.Clamp(p.Percent, 0, 100);
                    _progressLabel.Text = p.Status;
                }
            });

            await _engine.InpaintAsync(_inputPath, maskSnapshot, progress, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            if (CanUpdateUi)
            {
                RequestClose(DialogResult.OK);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _closingRequested)
        {
            _logger.Log("RemoveObjectEditorForm: inference canceled.");
        }
        catch (Exception ex)
        {
            _logger.Log($"RemoveObjectEditorForm: inference failed. {ex}");
            if (CanUpdateUi)
            {
                MessageBox.Show($"Inference failed: {ex.Message}", "FrameShift",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (CanUpdateUi)
            {
                SetInferenceUiState(running: false);
            }
        }
    }

    private async Task CompleteCloseAsync()
    {
        await _lifetime.BeginClosingAsync(
            CleanupResourcesAsync,
            ex => _logger.Log($"RemoveObjectEditorForm: close cleanup failed. {ex}")).ConfigureAwait(true);

        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        // Even completed cleanup must leave the original FormClosing first.
        BeginInvoke(new Action(() =>
        {
            if (IsDisposed || Disposing) return;
            _allowClose = true;
            DialogResult = _requestedDialogResult ?? DialogResult.Cancel;
            Close();
        }));
    }

    private Task CleanupResourcesAsync()
    {
        if (_resourcesCleaned)
        {
            return Task.CompletedTask;
        }

        _resourcesCleaned = true;
        _engine?.Dispose();
        _engine = null;
        _imageBitmap?.Dispose();
        _imageBitmap = null;
        _maskOverlay?.Dispose();
        _maskOverlay = null;
        return Task.CompletedTask;
    }

    private void SetClosingUiState()
    {
        SetInferenceUiState(running: true);
        _btnCancel.Enabled = false;
        _btnBrush.Enabled = false;
        _btnEraser.Enabled = false;
        _btnFit.Enabled = false;
        _brushSizeSlider.Enabled = false;
        _brushSizeNum.Enabled = false;
        _canvasPanel.Enabled = false;
        _progressLabel.Text = "Closing: waiting for AI processing...";
    }

    private Task<bool> DownloadModelAsync(ObjectRemovalModelDefinition def)
    {
        using var dlForm = new DownloadModelForm(
            "FrameShift AI - Remove Object",
            "Download the AI model to enable object removal",
            IconPaths.RemoveObjectAiIcon,
            def.DisplayName,
            def.License,
            def.ExpectedSizeBytes,
            async (progress, ct) =>
            {
                ModelLocator.EnsureDirectoryExists(def);
                await ModelDownloader.DownloadAsync(def,
                    ModelLocator.GetModelPath(def),
                    progress, ct).ConfigureAwait(false);
            });

        var dr = dlForm.ShowDialog(this);
        bool ready = dr == DialogResult.OK && ModelLocator.ModelExists(def);
        _logger.Log($"RemoveObjectEditorForm: model download result. dialogResult={dr}, ready={ready}, model={def.Id}");
        return Task.FromResult(ready);
    }

    private void SetInferenceUiState(bool running)
    {
        _inferRunning = running;
        _btnApply.Enabled = !running;
        _btnReset.Enabled = !running;
        _modelCombo.Enabled = !running;
        _progressPanel.Visible = running;
        if (!running)
        {
            _progressBar.Value = 0;
            _progressLabel.Text = string.Empty;
        }
    }

    private bool HasAnyMask()
    {
        if (_maskData is null) return false;
        foreach (var v in _maskData)
            if (v) return true;
        return false;
    }

    private static void OpenFolderAndSelect(string filePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (dir != null)
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
        catch { }
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    // ─── Nested type ───────────────────────────────────────────────────────────

    private sealed class ModelComboItem
    {
        public ObjectRemovalModelDefinition Definition { get; }

        public ModelComboItem(ObjectRemovalModelDefinition def) => Definition = def;

        public override string ToString() => Definition.DisplayName;
    }
}
