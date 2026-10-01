using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.Helpers;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class ConvertToIconForm : Form
{
    private const int TilePreviewCanvasSize = 74;
    private const int TilePreviewSizeCap = 68;
    private readonly Image _sourceImage;
    private readonly List<CheckBox> _sizeChecks = [];
    private readonly List<PreviewTile> _previewTiles = [];
    private RadioButton _radioFit = null!;
    private RadioButton _radioFill = null!;
    private RadioButton _radioTransparent = null!;
    private RadioButton _radioWhite = null!;
    private RadioButton _radioBlack = null!;

    public ConvertToIconForm(string sourcePath, string previewImagePath)
    {
        try
        {
            _sourceImage = Image.FromFile(previewImagePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(ImageCropSupport.GetFriendlyLoadError(ex));
        }

        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(900, 600), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, "FrameShift - Convert to Icon");
        var header = FrameShiftUiFactory.CreateHeader("FrameShift - Convert to Icon", $"Source: {Path.GetFileName(sourcePath)}",
            IconPaths.ContextMenuIco("convert-icon-image-icon.ico"), IconPaths.AppIcon, "ICO");
        _radioFit = new RadioButton { Text = "Fit", Checked = true, AutoSize = true };
        _radioFill = new RadioButton { Text = "Fill", AutoSize = true };
        _radioTransparent = new RadioButton { Text = "Transparent", Checked = true, AutoSize = true };
        _radioWhite = new RadioButton { Text = "White", AutoSize = true };
        _radioBlack = new RadioButton { Text = "Black", AutoSize = true };
        var sizes = FrameShiftUiFactory.CreateVerticalStack();
        foreach (var size in ConvertToIconSettings.GetSupportedSizes())
        {
            var check = new CheckBox { Text = $"{size} x {size}", Name = $"size{size}", Checked = true, AutoSize = true };
            check.CheckedChanged += (_, _) => { if (!_updatingSizes) UpdatePreviewTiles(); };
            _sizeChecks.Add(check);
            sizes.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sizes.Controls.Add(check, 0, sizes.RowCount++);
            check.Dock = DockStyle.Top;
        }
        var all = FrameShiftUiFactory.CreateMeasuredActionButton("Select all", false, 80);
        var clear = FrameShiftUiFactory.CreateMeasuredActionButton("Clear all", false, 80);
        all.Click += (_, _) => SetAllSizes(true);
        clear.Click += (_, _) => SetAllSizes(false);
        var sizesSection = FrameShiftUiFactory.CreateSection("Sizes",
            FrameShiftUiFactory.CreateVerticalStack(sizes, FrameShiftUiFactory.CreateChoiceRow(all, clear)));
        var settings = FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateSection("Fit mode", FrameShiftUiFactory.CreateVerticalStack(_radioFit, _radioFill)),
            FrameShiftUiFactory.CreateSection("Background", FrameShiftUiFactory.CreateVerticalStack(_radioTransparent, _radioWhite, _radioBlack)));
        var tiles = FrameShiftUiFactory.CreateChoiceRow();
        foreach (var size in ConvertToIconSettings.GetSupportedSizes())
        {
            var tile = CreatePreviewTile(size, _previewTiles.Count);
            _previewTiles.Add(tile);
            tiles.Controls.Add(tile.Panel);
        }
        foreach (var radio in new[] { _radioFit, _radioFill, _radioTransparent, _radioWhite, _radioBlack })
            radio.CheckedChanged += (_, _) => { if (radio.Checked) UpdatePreviewTiles(); };
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        cancel.DialogResult = DialogResult.Cancel;
        var convert = FrameShiftUiFactory.CreateMeasuredActionButton("Convert", true);
        convert.Click += (_, _) => ConfirmSelection();
        AcceptButton = convert;
        CancelButton = cancel;
        var content = new IconContentLayout(sizesSection, settings,
            FrameShiftUiFactory.CreateSection("Preview — click a size to include or skip it", tiles));
        var layout = FrameShiftDialogLayout.Create(header, content,
            FrameShiftDialogLayout.CreateActions(cancel, convert),
            FrameShiftUiFactory.CreateStatusMessage("The ICO file is created next to the original image."));
        Controls.Add(layout);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, layout);
        UpdatePreviewTiles();
        ResumeLayout(true);
    }

    public ConvertToIconSettings? Selection { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var tile in _previewTiles)
            {
                tile.PictureBox.Image?.Dispose();
                tile.PictureBox.Image = null;
            }

            _sourceImage.Dispose();
        }

        base.Dispose(disposing);
    }

    private bool _updatingSizes;

    private void SetAllSizes(bool value)
    {
        _updatingSizes = true;
        try { foreach (var check in _sizeChecks) check.Checked = value; }
        finally { _updatingSizes = false; }
        UpdatePreviewTiles();
    }

    private PreviewTile CreatePreviewTile(int size, int index)
    {
        var picture = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Anchor = AnchorStyles.None,
            AccessibleName = $"Preview {size} pixels", TabStop = false };
        var sizeLabel = FrameShiftUiFactory.CreateWrappingLabel($"{size} × {size}");
        sizeLabel.TextAlign = ContentAlignment.MiddleCenter;
        var tile = FrameShiftUiFactory.CreateVerticalStack(picture, sizeLabel);
        tile.Dock = DockStyle.None;
        picture.Dock = DockStyle.None;
        tile.BackColor = FrameShiftTheme.Surface;
        tile.Cursor = Cursors.Hand;
        void Metrics()
        {
            var width = FrameShiftUiMetrics.ToPixels(tile, 108);
            tile.MinimumSize = new Size(width, 0);
            picture.Size = new Size(FrameShiftUiMetrics.ToPixels(tile, TilePreviewCanvasSize), FrameShiftUiMetrics.ToPixels(tile, TilePreviewCanvasSize));
            tile.Padding = new Padding(FrameShiftUiMetrics.ToPixels(tile, 8));
            picture.Margin = new Padding(0, 0, 0, FrameShiftUiMetrics.ToPixels(tile, FrameShiftUiMetrics.LineGap));
            sizeLabel.Margin = Padding.Empty;
        }
        tile.HandleCreated += (_, _) => Metrics();
        tile.DpiChangedAfterParent += (_, _) => Metrics();
        Metrics();
        void Toggle(object? sender, EventArgs e) => _sizeChecks[index].Checked = !_sizeChecks[index].Checked;
        tile.Click += Toggle;
        picture.Click += Toggle;
        sizeLabel.Click += Toggle;
        FrameShiftUiPainter.AttachRoundedBorder(tile, FrameShiftTheme.SurfaceBorder, FrameShiftUiMetrics.PanelCornerRadius);
        return new PreviewTile(size, tile, picture, sizeLabel);
    }

    private void UpdatePreviewTiles()
    {
        var fitMode = _radioFill.Checked ? "fill" : "fit";
        var background = GetSelectedBackground();

        for (var index = 0; index < _previewTiles.Count; index++)
        {
            var tile = _previewTiles[index];
            var isChecked = _sizeChecks[index].Checked;

            var previousImage = tile.PictureBox.Image;
            tile.PictureBox.Image = CreatePreviewBitmap(_sourceImage, tile.Size, fitMode, background, isChecked);
            previousImage?.Dispose();

            tile.Panel.BackColor = isChecked ? FrameShiftTheme.Surface : FrameShiftTheme.PageBackground;
            tile.SizeLabel.ForeColor = isChecked ? FrameShiftTheme.TextPrimary : FrameShiftTheme.TextMuted;
            tile.SizeLabel.Text = isChecked ? $"✓ {tile.Size} × {tile.Size}" : $"{tile.Size} × {tile.Size}";
        }
    }

    private void ConfirmSelection()
    {
        var selectedSizes = _sizeChecks
            .Where(static check => check.Checked)
            .Select(static check => int.Parse(check.Text.Split('x')[0].Trim(), System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(static size => size)
            .ToArray();

        if (selectedSizes.Length == 0)
        {
            MessageBox.Show(this, MediaActionMessages.ConvertToIconNoSizesSelected(), "FrameShift", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Selection = new ConvertToIconSettings(
            selectedSizes,
            _radioFill.Checked ? "fill" : "fit",
            GetSelectedBackground());

        DialogResult = DialogResult.OK;
        Close();
    }

    private string GetSelectedBackground()
    {
        if (_radioWhite.Checked)
        {
            return "white";
        }

        if (_radioBlack.Checked)
        {
            return "black";
        }

        return "transparent";
    }

    private static Bitmap CreatePreviewBitmap(Image sourceImage, int iconSize, string fitMode, string background, bool enabled)
    {
        var bitmap = new Bitmap(TilePreviewCanvasSize, TilePreviewCanvasSize, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        using var cellBrushLight = new SolidBrush(Color.FromArgb(242, 242, 242));
        using var cellBrushDark = new SolidBrush(Color.FromArgb(223, 223, 223));
        for (var y = 0; y < TilePreviewCanvasSize; y += 8)
        {
            for (var x = 0; x < TilePreviewCanvasSize; x += 8)
            {
                var brush = (((x + y) / 8) % 2 == 0) ? cellBrushLight : cellBrushDark;
                graphics.FillRectangle(brush, x, y, 8, 8);
            }
        }

        var previewSize = Math.Min(iconSize, TilePreviewSizeCap);
        var previewRect = new Rectangle(
            (TilePreviewCanvasSize - previewSize) / 2,
            (TilePreviewCanvasSize - previewSize) / 2,
            previewSize,
            previewSize);

        if (string.Equals(background, "white", StringComparison.OrdinalIgnoreCase))
        {
            graphics.FillRectangle(Brushes.White, previewRect);
        }
        else if (string.Equals(background, "black", StringComparison.OrdinalIgnoreCase))
        {
            graphics.FillRectangle(Brushes.Black, previewRect);
        }

        var sourceRatio = sourceImage.Width / (double)sourceImage.Height;
        Rectangle sourceRect;
        Rectangle destinationRect;

        if (string.Equals(fitMode, "fill", StringComparison.OrdinalIgnoreCase))
        {
            if (sourceRatio > 1d)
            {
                var cropWidth = (int)Math.Round(sourceImage.Height * 1d);
                var cropX = (sourceImage.Width - cropWidth) / 2;
                sourceRect = new Rectangle(cropX, 0, cropWidth, sourceImage.Height);
            }
            else
            {
                var cropHeight = (int)Math.Round(sourceImage.Width / 1d);
                var cropY = (sourceImage.Height - cropHeight) / 2;
                sourceRect = new Rectangle(0, cropY, sourceImage.Width, cropHeight);
            }

            destinationRect = previewRect;
        }
        else
        {
            int drawWidth;
            int drawHeight;
            if (sourceRatio > 1d)
            {
                drawWidth = previewSize;
                drawHeight = (int)Math.Round(previewSize / sourceRatio);
            }
            else
            {
                drawHeight = previewSize;
                drawWidth = (int)Math.Round(previewSize * sourceRatio);
            }

            sourceRect = new Rectangle(0, 0, sourceImage.Width, sourceImage.Height);
            destinationRect = new Rectangle(
                previewRect.X + ((previewRect.Width - drawWidth) / 2),
                previewRect.Y + ((previewRect.Height - drawHeight) / 2),
                drawWidth,
                drawHeight);
        }

        if (enabled)
        {
            graphics.DrawImage(sourceImage, destinationRect, sourceRect, GraphicsUnit.Pixel);
        }
        else
        {
            using var attributes = new ImageAttributes();
            var matrix = new ColorMatrix
            {
                Matrix00 = 0.30f,
                Matrix01 = 0.30f,
                Matrix02 = 0.30f,
                Matrix10 = 0.59f,
                Matrix11 = 0.59f,
                Matrix12 = 0.59f,
                Matrix20 = 0.11f,
                Matrix21 = 0.11f,
                Matrix22 = 0.11f,
                Matrix33 = 0.35f,
                Matrix44 = 1f
            };
            attributes.SetColorMatrix(matrix);
            graphics.DrawImage(sourceImage, destinationRect, sourceRect.X, sourceRect.Y, sourceRect.Width, sourceRect.Height, GraphicsUnit.Pixel, attributes);
        }

        return bitmap;
    }

    // The familiar sizes / settings / previews arrangement, with measured rows and a narrow fallback.
    private sealed class IconContentLayout : TableLayoutPanel
    {
        private readonly Control _sizes;
        private readonly Control _settings;
        private readonly Control _previews;
        private bool _arranging;
        private int _mode = -1;

        public IconContentLayout(Control sizes, Control settings, Control previews)
        {
            _sizes = sizes;
            _settings = settings;
            _previews = previews;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Top;
            Margin = Padding.Empty;
            Controls.AddRange([sizes, settings, previews]);
        }

        private (int Mode, int SizesWidth, int SettingsWidth, int Gap) MeasureColumns(int width)
        {
            var gap = FrameShiftUiMetrics.ToPixels(this, FrameShiftUiMetrics.OuterPadding);
            var sizesWidth = Math.Max(FrameShiftUiMetrics.ToPixels(this, 200), _sizes.GetPreferredSize(Size.Empty).Width);
            var settingsWidth = Math.Max(FrameShiftUiMetrics.ToPixels(this, 170), _settings.GetPreferredSize(Size.Empty).Width);
            var mode = width >= sizesWidth + settingsWidth + FrameShiftUiMetrics.ToPixels(this, 360) + 2 * gap ? 3
                : width >= sizesWidth + settingsWidth + gap ? 2 : 1;
            return (mode, sizesWidth, settingsWidth, gap);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            if (_sizes is null) return base.GetPreferredSize(proposedSize);
            var width = Math.Max(1, proposedSize.Width > 1 ? proposedSize.Width : ClientSize.Width);
            var (mode, sizesWidth, settingsWidth, gap) = MeasureColumns(width);
            int Height(Control control, int available) => control.GetPreferredSize(new Size(Math.Max(1, available), 0)).Height;
            var height = mode == 3 ? Math.Max(Math.Max(Height(_sizes, sizesWidth), Height(_settings, settingsWidth)), Height(_previews, width - sizesWidth - settingsWidth - 2 * gap))
                : mode == 2 ? Math.Max(Height(_sizes, sizesWidth), Height(_settings, width - sizesWidth - gap)) + gap + Height(_previews, width)
                : Height(_sizes, width) + gap + Height(_settings, width) + gap + Height(_previews, width);
            return new Size(width, height);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (_sizes is null || _arranging) { base.OnLayout(e); return; }
            _arranging = true;
            try
            {
                var (mode, sizesWidth, settingsWidth, gap) = MeasureColumns(ClientSize.Width);
                if (_mode != mode)
                {
                    _mode = mode;
                    ColumnStyles.Clear();
                    RowStyles.Clear();
                    ColumnCount = mode;
                    RowCount = mode == 3 ? 1 : mode == 2 ? 2 : 3;
                    for (var column = 0; column < ColumnCount; column++)
                        ColumnStyles.Add(new ColumnStyle(column == ColumnCount - 1 ? SizeType.Percent : SizeType.Absolute, column == ColumnCount - 1 ? 100 : 0));
                    for (var row = 0; row < RowCount; row++) RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    SetColumnSpan(_previews, 1);
                    SetCellPosition(_sizes, new TableLayoutPanelCellPosition(0, 0));
                    SetCellPosition(_settings, new TableLayoutPanelCellPosition(mode == 1 ? 0 : 1, mode == 1 ? 1 : 0));
                    SetCellPosition(_previews, new TableLayoutPanelCellPosition(mode == 3 ? 2 : 0, mode == 1 ? 2 : mode == 2 ? 1 : 0));
                    if (mode == 2) SetColumnSpan(_previews, 2);
                }
                if (mode > 1) ColumnStyles[0].Width = sizesWidth + gap;
                if (mode == 3) ColumnStyles[1].Width = settingsWidth + gap;
                _sizes.Margin = mode > 1 ? new Padding(0, 0, gap, mode == 2 ? gap : 0) : new Padding(0, 0, 0, gap);
                _settings.Margin = mode == 3 ? new Padding(0, 0, gap, 0) : new Padding(0, 0, 0, gap);
                _previews.Margin = Padding.Empty;
                _sizes.Dock = _settings.Dock = _previews.Dock = DockStyle.Top;
                base.OnLayout(e);
            }
            finally { _arranging = false; }
        }
    }

    private sealed record PreviewTile(int Size, Panel Panel, PictureBox PictureBox, Label SizeLabel);
}
