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
    private const int TileGap = 4;
    private const int SizesColumnWidth = 250;
    private const int MiddleColumnWidth = 184;
    private const int SectionTitleRowHeight = 22;
    private const int SizesCardHeight = 366;
    private const int FitModePanelHeight = 106;
    private const int BackgroundPanelHeight = 138;
    private const int InfoCardHeight = 34;
    private const int PreviewTileWidth = 108;
    private const int PreviewTileRowHeight = 126;
    private const int PreviewGridWidth = (PreviewTileWidth * 3) + (TileGap * 2);
    private const int MainContentHeight = SectionTitleRowHeight + 4 + SizesCardHeight + FrameShiftUiMetrics.OuterPadding + InfoCardHeight;
    private const int FooterRowHeight = 34;

    private readonly string _sourcePath;
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
        _sourcePath = sourcePath;

        try
        {
            _sourceImage = Image.FromFile(previewImagePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(ImageCropSupport.GetFriendlyLoadError(ex));
        }

        FrameShiftWindowChrome.Apply(this, "FrameShift - Convert to Icon");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(820, 578);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(FrameShiftUiMetrics.OuterPadding),
            ColumnCount = 1,
            RowCount = 5
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.HeaderHeight));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, MainContentHeight));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FooterRowHeight));

        var headerPanel = FrameShiftUiFactory.CreateFillHeader(
            "FrameShift - Convert to Icon",
            $"Source: {Path.GetFileName(sourcePath)}",
            IconPaths.ContextMenuIco("convert-icon-image-icon.ico"),
            IconPaths.AppIcon,
            "ICO",
            860);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 3,
            RowCount = 1
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SizesColumnWidth + MiddleColumnWidth + 8F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var leftCluster = CreateLeftCluster();
        var previewColumn = CreatePreviewColumn();

        mainLayout.Controls.Add(leftCluster, 0, 0);
        mainLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 1, 0);
        mainLayout.Controls.Add(previewColumn, 2, 0);

        var buttonCancel = FrameShiftUiFactory.CreateActionButton("Cancel", primary: false, FrameShiftUiMetrics.SecondaryButtonWidth);
        buttonCancel.DialogResult = DialogResult.Cancel;

        var buttonOk = FrameShiftUiFactory.CreateActionButton("Convert", primary: true, FrameShiftUiMetrics.PrimaryButtonWidth);
        buttonOk.Click += (_, _) => ConfirmSelection();

        var footerPanel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        footerPanel.Controls.Add(buttonCancel);
        footerPanel.Controls.Add(buttonOk);
        footerPanel.Resize += (_, _) => FrameShiftUiLayout.LayoutFooterButtons(footerPanel, buttonCancel, buttonOk, FrameShiftUiMetrics.LineGap);
        Shown += (_, _) => FrameShiftUiLayout.LayoutFooterButtons(footerPanel, buttonCancel, buttonOk, FrameShiftUiMetrics.LineGap);

        rootLayout.Controls.Add(headerPanel, 0, 0);
        rootLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
        rootLayout.Controls.Add(mainLayout, 0, 2);
        rootLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 3);
        rootLayout.Controls.Add(footerPanel, 0, 4);

        Controls.Add(rootLayout);

        AcceptButton = buttonOk;
        CancelButton = buttonCancel;

        UpdatePreviewTiles();
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

    private TableLayoutPanel CreateLeftCluster()
    {
        var cluster = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 3,
            RowCount = 5
        };
        cluster.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SizesColumnWidth));
        cluster.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8F));
        cluster.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, MiddleColumnWidth));
        cluster.RowStyles.Add(new RowStyle(SizeType.Absolute, SectionTitleRowHeight));
        cluster.RowStyles.Add(new RowStyle(SizeType.Absolute, 4F));
        cluster.RowStyles.Add(new RowStyle(SizeType.Absolute, SizesCardHeight));
        cluster.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        cluster.RowStyles.Add(new RowStyle(SizeType.Absolute, InfoCardHeight));

        var sizesTitle = CreateSectionHeading("Sizes");
        var sizesCard = CreateSizesCard();
        var settingsColumn = CreateSettingsColumn();
        var infoCard = CreateInfoCard();

        cluster.Controls.Add(sizesTitle, 0, 0);
        cluster.Controls.Add(sizesCard, 0, 2);
        cluster.Controls.Add(settingsColumn, 2, 0);
        cluster.SetRowSpan(settingsColumn, 3);
        cluster.Controls.Add(infoCard, 0, 4);
        cluster.SetColumnSpan(infoCard, 3);

        return cluster;
    }

    private Panel CreateSizesCard()
    {
        var card = FrameShiftUiFactory.CreateFramedPanel(FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.PanelCornerRadius);
        card.Dock = DockStyle.Fill;
        card.Margin = Padding.Empty;
        card.Padding = new Padding(12, 10, 12, 10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 288F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));

        var checkboxesLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 9
        };
        checkboxesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < 9; i++)
        {
            checkboxesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        }

        var supportedSizes = ConvertToIconSettings.GetSupportedSizes();
        for (var index = 0; index < supportedSizes.Count; index++)
        {
            var size = supportedSizes[index];
            var check = new CheckBox
            {
                Text = $"{size} x {size}",
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Checked = true,
                ForeColor = FrameShiftTheme.TextPrimary,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
                AutoSize = false,
                Padding = new Padding(1, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            check.CheckedChanged += (_, _) => UpdatePreviewTiles();
            _sizeChecks.Add(check);
            checkboxesLayout.Controls.Add(check, 0, index);
        }

        var buttonsRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 3,
            RowCount = 1
        };
        buttonsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        buttonsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8F));
        buttonsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        buttonsRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var btnSelectAll = FrameShiftUiFactory.CreateActionButton("Select all", primary: false, 0);
        btnSelectAll.Dock = DockStyle.Fill;
        btnSelectAll.Margin = Padding.Empty;
        btnSelectAll.Click += (_, _) =>
        {
            foreach (var check in _sizeChecks)
            {
                check.Checked = true;
            }
        };

        var btnClearAll = FrameShiftUiFactory.CreateActionButton("Clear all", primary: false, 0);
        btnClearAll.Dock = DockStyle.Fill;
        btnClearAll.Margin = Padding.Empty;
        btnClearAll.Click += (_, _) =>
        {
            foreach (var check in _sizeChecks)
            {
                check.Checked = false;
            }
        };

        buttonsRow.Controls.Add(btnSelectAll, 0, 0);
        buttonsRow.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 1, 0);
        buttonsRow.Controls.Add(btnClearAll, 2, 0);

        layout.Controls.Add(checkboxesLayout, 0, 0);
        layout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
        layout.Controls.Add(buttonsRow, 0, 2);
        card.Controls.Add(layout);

        return card;
    }

    private TableLayoutPanel CreateSettingsColumn()
    {
        var settingsColumn = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 6
        };
        settingsColumn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        settingsColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, SectionTitleRowHeight));
        settingsColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 4F));
        settingsColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, FitModePanelHeight));
        settingsColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, SectionTitleRowHeight));
        settingsColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 4F));
        settingsColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, BackgroundPanelHeight));

        var fitTitle = CreateSectionHeading("Fit mode");
        var fitCard = FrameShiftUiFactory.CreateFramedPanel(FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.PanelCornerRadius);
        fitCard.Dock = DockStyle.Fill;
        fitCard.Margin = Padding.Empty;
        fitCard.Padding = new Padding(12, 8, 12, 8);

        var fitLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        _radioFit = CreateCompactRadioButton("Fit", true);
        _radioFill = CreateCompactRadioButton("Fill", false);
        fitLayout.Controls.Add(_radioFit);
        fitLayout.Controls.Add(_radioFill);
        fitCard.Controls.Add(fitLayout);

        var backgroundTitle = CreateSectionHeading("Background");
        var backgroundCard = FrameShiftUiFactory.CreateFramedPanel(FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.PanelCornerRadius);
        backgroundCard.Dock = DockStyle.Fill;
        backgroundCard.Margin = Padding.Empty;
        backgroundCard.Padding = new Padding(12, 8, 12, 8);

        var backgroundLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        _radioTransparent = CreateCompactRadioButton("Transparent", true);
        _radioWhite = CreateCompactRadioButton("White", false);
        _radioBlack = CreateCompactRadioButton("Black", false);
        backgroundLayout.Controls.Add(_radioTransparent);
        backgroundLayout.Controls.Add(_radioWhite);
        backgroundLayout.Controls.Add(_radioBlack);
        backgroundCard.Controls.Add(backgroundLayout);

        _radioFit.CheckedChanged += (_, _) => UpdatePreviewTiles();
        _radioFill.CheckedChanged += (_, _) => UpdatePreviewTiles();
        _radioTransparent.CheckedChanged += (_, _) => UpdatePreviewTiles();
        _radioWhite.CheckedChanged += (_, _) => UpdatePreviewTiles();
        _radioBlack.CheckedChanged += (_, _) => UpdatePreviewTiles();

        settingsColumn.Controls.Add(fitTitle, 0, 0);
        settingsColumn.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
        settingsColumn.Controls.Add(fitCard, 0, 2);
        settingsColumn.Controls.Add(backgroundTitle, 0, 3);
        settingsColumn.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 4);
        settingsColumn.Controls.Add(backgroundCard, 0, 5);

        return settingsColumn;
    }

    private Panel CreateInfoCard()
    {
        var infoCard = FrameShiftUiFactory.CreateFillInfoCard();
        infoCard.Dock = DockStyle.Top;
        infoCard.Margin = Padding.Empty;
        infoCard.Height = InfoCardHeight;
        infoCard.Padding = new Padding(10, 6, 10, 6);

        var infoLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = 1
        };
        infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22F));
        infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        infoLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var infoIcon = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = "i",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = FrameShiftTheme.SecondaryBlue,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point)
        };

        var infoLabel = new Label
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 0, 0, 0),
            ForeColor = FrameShiftTheme.SecondaryBlue,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Text = "The ICO file will be created next to the original image.",
            TextAlign = ContentAlignment.MiddleLeft
        };

        infoLayout.Controls.Add(infoIcon, 0, 0);
        infoLayout.Controls.Add(infoLabel, 1, 0);
        infoCard.Controls.Add(infoLayout);

        return infoCard;
    }

    private TableLayoutPanel CreatePreviewColumn()
    {
        var previewColumn = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 4
        };
        previewColumn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        previewColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, SectionTitleRowHeight));
        previewColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 4F));
        previewColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, (PreviewTileRowHeight * 3) + (TileGap * 2)));
        previewColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var gridHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        var grid = new TableLayoutPanel
        {
            Margin = Padding.Empty,
            ColumnCount = 5,
            RowCount = 5,
            Size = new Size(PreviewGridWidth, (PreviewTileRowHeight * 3) + (TileGap * 2)),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, PreviewTileWidth));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TileGap));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, PreviewTileWidth));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TileGap));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, PreviewTileWidth));

        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, PreviewTileRowHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, TileGap));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, PreviewTileRowHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, TileGap));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, PreviewTileRowHeight));

        var supportedSizes = ConvertToIconSettings.GetSupportedSizes();
        for (var index = 0; index < supportedSizes.Count; index++)
        {
            var tile = CreatePreviewTile(supportedSizes[index], index);
            _previewTiles.Add(tile);
            grid.Controls.Add(tile.Panel, (index % 3) * 2, (index / 3) * 2);
        }

        gridHost.Controls.Add(grid);
        gridHost.Resize += (_, _) => grid.Location = new Point(0, 0);

        previewColumn.Controls.Add(CreateSectionHeading("Preview"), 0, 0);
        previewColumn.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
        previewColumn.Controls.Add(gridHost, 0, 2);
        previewColumn.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 3);

        return previewColumn;
    }

    private PreviewTile CreatePreviewTile(int size, int index)
    {
        var tilePanel = FrameShiftUiFactory.CreateFramedPanel(FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.PanelCornerRadius);
        tilePanel.Dock = DockStyle.Fill;
        tilePanel.Margin = Padding.Empty;
        tilePanel.Padding = new Padding(4, 4, 4, 6);
        tilePanel.Cursor = Cursors.Hand;

        var tileLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 3
        };
        tileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        tileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
        tileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));

        var pictureBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            SizeMode = PictureBoxSizeMode.CenterImage,
            BackColor = Color.Transparent
        };

        var labelSize = new Label
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = FrameShiftTheme.TextPrimary,
            Text = $"{size} x {size}",
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Regular, GraphicsUnit.Point)
        };

        var labelState = new Label
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 2),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = FrameShiftTheme.SecondaryBlue,
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Regular, GraphicsUnit.Point)
        };

        tileLayout.Controls.Add(pictureBox, 0, 0);
        tileLayout.Controls.Add(labelSize, 0, 1);
        tileLayout.Controls.Add(labelState, 0, 2);
        tilePanel.Controls.Add(tileLayout);

        void ToggleTile(object? _, EventArgs __)
        {
            var checkBox = _sizeChecks.First(check => check.Text == $"{size} x {size}");
            checkBox.Checked = !checkBox.Checked;
        }

        tilePanel.Click += ToggleTile;
        pictureBox.Click += ToggleTile;
        labelSize.Click += ToggleTile;
        labelState.Click += ToggleTile;

        return new PreviewTile(size, tilePanel, pictureBox, labelSize, labelState);
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

            tile.Panel.BackColor = isChecked ? FrameShiftTheme.Surface : Color.FromArgb(247, 248, 251);
            tile.SizeLabel.ForeColor = isChecked ? FrameShiftTheme.TextPrimary : FrameShiftTheme.TextMuted;
            tile.StateLabel.ForeColor = isChecked ? FrameShiftTheme.SecondaryBlue : FrameShiftTheme.TextMuted;
            tile.StateLabel.Text = isChecked ? "Included" : "Skipped";
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

    private static Label CreateSectionHeading(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = text,
            ForeColor = FrameShiftTheme.SecondaryBlue,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
    }

    private static RadioButton CreateCompactRadioButton(string text, bool isChecked)
    {
        return new RadioButton
        {
            Text = text,
            Checked = isChecked,
            ForeColor = FrameShiftTheme.TextPrimary,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            AutoSize = false,
            Height = 30,
            Width = 148,
            Margin = new Padding(0),
            Padding = new Padding(0, 2, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private sealed record PreviewTile(int Size, Panel Panel, PictureBox PictureBox, Label SizeLabel, Label StateLabel);
}
