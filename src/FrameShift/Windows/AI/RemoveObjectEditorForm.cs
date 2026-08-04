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
    private CancellationTokenSource? _inferCts;
    private bool _inferRunning;

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

    private void BuildUi()
    {
        FrameShiftWindowChrome.Apply(this, "FrameShift - Remove Object",
            IconPaths.RemoveObjectAiIcon, IconPaths.FrameShiftAiIcon);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        ClientSize = new Size(1100, 720);
        MinimumSize = new Size(900, 620);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = FrameShiftTheme.PageBackground;
        ControlHelper.SetDoubleBuffered(this);

        // Root layout — 7 rows: header / gap / content / gap / info-bar / gap / buttons
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(FrameShiftUiMetrics.OuterPadding),
            ColumnCount = 1,
            RowCount = 7
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.HeaderHeight)); // 0
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding)); // 1
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));                              // 2 content
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.LineGap));      // 3
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));                              // 4 info bar
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.LineGap));      // 5
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));                              // 6 buttons

        // Header
        var fileInfo = new FileInfo(_inputPath);
        using var bmpCheck = LoadImageSafe(_inputPath);
        var dims = bmpCheck != null ? $"{bmpCheck.Width}×{bmpCheck.Height}" : "—";
        var ext = fileInfo.Extension.TrimStart('.').ToUpperInvariant();
        var header = FrameShiftUiFactory.CreateFillHeader(
            "FrameShift — Remove Object",
            $"{fileInfo.Name} · {dims} · {ext}",
            IconPaths.RemoveObjectAiIcon,
            IconPaths.FrameShiftAiIcon,
            "✂",
            subtitleWidth: 500);
        root.Controls.Add(header, 0, 0);

        // Content: canvas + tools rail
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = 1
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FrameShiftUiMetrics.EditorRailWidth));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.Controls.Add(content, 0, 2);

        // Info bar (row 4)
        root.Controls.Add(BuildInfoBar(), 0, 4);

        // Canvas panel (left)
        _canvasPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, FrameShiftUiMetrics.OuterPadding, 0),
            BackColor = Color.FromArgb(30, 30, 30),
            TabStop = true
        };
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
        content.Controls.Add(_canvasPanel, 0, 0);

        // Tools rail (right)
        var rail = BuildToolsRail();
        content.Controls.Add(rail, 1, 0);

        // Buttons footer (row 6)
        var footer = BuildFooter();
        root.Controls.Add(footer, 0, 6);

        Controls.Add(root);

        Load += OnLoad;
        Shown += (_, _) => FitToWindow();
        FormClosing += OnFormClosing;
    }

    private Panel BuildToolsRail()
    {
        // Use a plain Panel — Dock=Top on each group fills the full rail width automatically.
        var rail = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, AutoScroll = true };

        // Groups are added top-to-bottom; CreateSidebarGroup already sets Dock=Top.
        rail.Controls.Add(FrameShiftEditorShellUi.CreateSidebarGroup("Mode", BuildModeContent()));
        rail.Controls.Add(FrameShiftEditorShellUi.CreateSidebarGroup("Brush Size", BuildSizeContent()));
        rail.Controls.Add(FrameShiftEditorShellUi.CreateSidebarGroup("Actions", BuildActionsContent()));
        rail.Controls.Add(FrameShiftEditorShellUi.CreateSidebarGroup("Model", BuildModelContent()));

        _progressPanel = BuildProgressPanel();
        _progressPanel.Dock = DockStyle.Top;
        _progressPanel.Visible = false;
        rail.Controls.Add(_progressPanel);

        return rail;
    }

    private Panel BuildModeContent()
    {
        var panel = new Panel { Height = 34, AutoSize = false };

        _btnBrush = new Button
        {
            Text = "Brush",
            Size = new Size(110, 30),
            Location = new Point(0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = FrameShiftTheme.SecondaryBlue,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9F)
        };
        _btnBrush.FlatAppearance.BorderSize = 0;
        _btnBrush.Click += (_, _) => SetTool(brush: true);

        _btnEraser = new Button
        {
            Text = "Eraser",
            Size = new Size(110, 30),
            Location = new Point(118, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = FrameShiftTheme.SurfaceBorder,
            ForeColor = FrameShiftTheme.TextPrimary,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9F)
        };
        _btnEraser.FlatAppearance.BorderSize = 0;
        _btnEraser.Click += (_, _) => SetTool(brush: false);

        panel.Controls.Add(_btnBrush);
        panel.Controls.Add(_btnEraser);
        return panel;
    }

    private Panel BuildSizeContent()
    {
        var panel = new Panel { Height = 46, AutoSize = false };

        _brushSizeSlider = new TrackBar
        {
            Minimum = 1,
            Maximum = 200,
            Value = DefaultBrushSize,
            TickFrequency = 20,
            TickStyle = TickStyle.None,
            Location = new Point(0, 0),
            Size = new Size(160, 26),
            BackColor = FrameShiftTheme.PageBackground
        };
        _brushSizeSlider.ValueChanged += (_, _) =>
        {
            _brushSize = _brushSizeSlider.Value;
            if (_brushSizeNum.Value != _brushSize)
                _brushSizeNum.Value = _brushSize;
        };

        _brushSizeNum = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 200,
            Value = DefaultBrushSize,
            Location = new Point(166, 2),
            Size = new Size(60, 24),
            Font = new Font("Segoe UI", 9F),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary
        };
        _brushSizeNum.ValueChanged += (_, _) =>
        {
            _brushSize = (int)_brushSizeNum.Value;
            if (_brushSizeSlider.Value != _brushSize)
                _brushSizeSlider.Value = _brushSize;
        };

        panel.Controls.Add(_brushSizeSlider);
        panel.Controls.Add(_brushSizeNum);
        return panel;
    }

    private Panel BuildActionsContent()
    {
        var panel = new Panel { Height = 80, AutoSize = false };

        _btnReset = new Button
        {
            Text = "Reset Mask",
            Size = new Size(230, 30),
            Location = new Point(0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9F)
        };
        _btnReset.FlatAppearance.BorderColor = FrameShiftTheme.SurfaceBorder;
        _btnReset.Click += (_, _) => ResetMask();

        _btnFit = new Button
        {
            Text = "Fit",
            Size = new Size(110, 30),
            Location = new Point(0, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9F)
        };
        _btnFit.FlatAppearance.BorderColor = FrameShiftTheme.SurfaceBorder;
        _btnFit.Click += (_, _) => FitToWindow();

        _zoomLabel = new Label
        {
            Text = "Zoom 100%",
            Location = new Point(120, 44),
            Size = new Size(110, 20),
            Font = new Font("Segoe UI", 9F),
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };

        panel.Controls.Add(_btnReset);
        panel.Controls.Add(_btnFit);
        panel.Controls.Add(_zoomLabel);
        return panel;
    }

    private Panel BuildModelContent()
    {
        var panel = new Panel { Height = 30, AutoSize = false };

        _modelCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(0, 0),
            Size = new Size(230, 28),
            Font = new Font("Segoe UI", 9F),
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary
        };
        foreach (var def in ObjectRemovalModelCatalog.GetAll())
            _modelCombo.Items.Add(new ModelComboItem(def));
        _modelCombo.SelectedIndex = 0;

        panel.Controls.Add(_modelCombo);
        return panel;
    }

    private Panel BuildProgressPanel()
    {
        var panel = new Panel
        {
            Width = 230,
            Height = 54,
            Margin = new Padding(0, 6, 0, 0)
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(0, 0),
            Size = new Size(230, 18),
            Style = ProgressBarStyle.Continuous
        };

        _progressLabel = new Label
        {
            Location = new Point(0, 24),
            Size = new Size(230, 18),
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = string.Empty
        };

        panel.Controls.Add(_progressBar);
        panel.Controls.Add(_progressLabel);
        return panel;
    }

    private static Panel BuildInfoBar()
    {
        var card = FrameShiftUiFactory.CreateFillInfoCard();
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(FrameShiftUiMetrics.LineGap, 0, FrameShiftUiMetrics.LineGap, 0);
        card.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "ⓘ  Paint over the object to remove. Output saved next to the source (PNG). [ ] keys adjust brush size.",
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        });
        return card;
    }

    private Panel BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };

        _btnCancel = new Button
        {
            Text = "Cancel",
            Size = new Size(FrameShiftUiMetrics.SecondaryButtonWidth, FrameShiftUiMetrics.FooterButtonHeight),
            FlatStyle = FlatStyle.Flat,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9F)
        };
        _btnCancel.FlatAppearance.BorderColor = FrameShiftTheme.SurfaceBorder;
        _btnCancel.Click += (_, _) => CancelOrClose();

        _btnApply = new Button
        {
            Text = "Apply ▶",
            Size = new Size(FrameShiftUiMetrics.PrimaryButtonWidth, FrameShiftUiMetrics.FooterButtonHeight),
            FlatStyle = FlatStyle.Flat,
            BackColor = FrameShiftTheme.SecondaryBlue,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };
        _btnApply.FlatAppearance.BorderSize = 0;
        _btnApply.Click += (_, _) => _ = RunInferenceAsync();

        footer.Resize += (_, _) => LayoutFooter(footer);
        Shown += (_, _) => LayoutFooter(footer);

        footer.Controls.Add(_btnCancel);
        footer.Controls.Add(_btnApply);
        return footer;
    }

    private void LayoutFooter(Panel footer)
    {
        int btnY = (footer.Height - FrameShiftUiMetrics.FooterButtonHeight) / 2;
        _btnApply.Location = new Point(footer.Width - FrameShiftUiMetrics.FooterRightPadding - _btnApply.Width, btnY);
        _btnCancel.Location = new Point(_btnApply.Left - FrameShiftUiMetrics.FooterButtonGap - _btnCancel.Width, btnY);
    }

    // ─── Load ──────────────────────────────────────────────────────────────────

    private void OnLoad(object? sender, EventArgs e)
    {
        try
        {
            _imageBitmap = new Bitmap(_inputPath);
            _maskData = new bool[_imageBitmap.Width, _imageBitmap.Height];
            // FitToWindow is called from Shown, after layout is finalized
        }
        catch (Exception ex)
        {
            _logger.Log($"RemoveObjectEditorForm: failed to load image. {ex}");
            MessageBox.Show($"Failed to load image: {ex.Message}", "FrameShift",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _inferCts?.Cancel();
        _engine?.Dispose();
        _imageBitmap?.Dispose();
        _maskOverlay?.Dispose();
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
            using var outlinePen = new Pen(Color.FromArgb(160, 0, 0, 0), 2.5f);
            g.DrawEllipse(outlinePen, cx - screenRadius, cy - screenRadius, screenRadius * 2, screenRadius * 2);
            // White inner circle
            using var circlePen = new Pen(Color.FromArgb(220, 255, 255, 255), 1.2f);
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
        _btnBrush.BackColor = brush ? FrameShiftTheme.SecondaryBlue : FrameShiftTheme.SurfaceBorder;
        _btnBrush.ForeColor = brush ? Color.White : FrameShiftTheme.TextPrimary;
        _btnEraser.BackColor = brush ? FrameShiftTheme.SurfaceBorder : FrameShiftTheme.SecondaryBlue;
        _btnEraser.ForeColor = brush ? FrameShiftTheme.TextPrimary : Color.White;
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
        _inferCts?.Cancel();
        DialogResult = DialogResult.Cancel;
        Close();
    }

    // ─── Inference ─────────────────────────────────────────────────────────────

    private async Task RunInferenceAsync()
    {
        if (_maskData is null || _imageBitmap is null) return;

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
            if (!ready) return;
        }

        SetInferenceUiState(running: true);
        _inferCts?.Dispose();
        _inferCts = new CancellationTokenSource();
        var ct = _inferCts.Token;

        if (_engine == null || _engineModelId != def.Id)
        {
            _engine?.Dispose();
            _engine = new ObjectRemovalEngine(def);
            _engineModelId = def.Id;
        }

        var maskSnapshot = (bool[,])_maskData.Clone();
        string? outputPath = null;

        try
        {
            var progress = new Progress<InpaintProgress>(p =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    Invoke(() =>
                    {
                        _progressBar.Value = Math.Clamp(p.Percent, 0, 100);
                        _progressLabel.Text = p.Status;
                    });
                }
                catch (ObjectDisposedException) { }
            });

            outputPath = await _engine.InpaintAsync(_inputPath, maskSnapshot, progress, ct);

            if (!IsDisposed)
                Invoke(() => Close());
        }
        catch (OperationCanceledException)
        {
            _logger.Log("RemoveObjectEditorForm: inference canceled.");
            if (!IsDisposed)
                Invoke(() => SetInferenceUiState(running: false));
        }
        catch (Exception ex)
        {
            _logger.Log($"RemoveObjectEditorForm: inference failed. {ex}");
            if (!IsDisposed)
            {
                Invoke(() =>
                {
                    SetInferenceUiState(running: false);
                    MessageBox.Show($"Inference failed: {ex.Message}", "FrameShift",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            }
        }
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

    private static Bitmap? LoadImageSafe(string path)
    {
        try { return new Bitmap(path); }
        catch { return null; }
    }

    // ─── Nested type ───────────────────────────────────────────────────────────

    private sealed class ModelComboItem
    {
        public ObjectRemovalModelDefinition Definition { get; }

        public ModelComboItem(ObjectRemovalModelDefinition def) => Definition = def;

        public override string ToString() => Definition.DisplayName;
    }
}
