using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.Helpers;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class ImageToPdfForm : Form
{
    private const int MaximumImageDimension = 16384;
    private const long MaximumImagePixels = 64_000_000;
    private const int MaximumHistoryEntries = 60;
    private const float SnapTolerancePreview = 12f;
    private const float MinimumPreviewScale = 0.20f;
    private const float MaximumPreviewScale = 5.00f;
    private const float PreviewZoomStep = 1.12f;
    private readonly List<ImageCanvasItem> _items = [];
    private readonly Dictionary<string, Bitmap> _bitmapCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<EditorHistoryState> _undoHistory = [];
    private readonly Stack<EditorHistoryState> _redoHistory = [];
    private readonly List<ClipboardImageItem> _clipboardItems = [];
    private readonly List<string> _temporarySourcePaths = [];
    private readonly List<int> _selectedIndices = [];
    private readonly string _ffmpegPath;
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly Panel _previewPanel;
    private readonly Button _buttonAddImage;
    private readonly Button _buttonDeleteImage;
    private readonly Button _buttonMoveBackward;
    private readonly Button _buttonMoveForward;
    private readonly Button _buttonSendToBack;
    private readonly Button _buttonBringToFront;
    private readonly Button _buttonFitPage;
    private readonly Button _buttonCenter;
    private readonly Button _buttonCrop;
    private readonly Button _buttonClearSelection;
    private readonly Button _buttonPrint;
    private readonly Button _buttonExport;
    private readonly Button _buttonCancel;
    private readonly Button _buttonZoomFit;
    private readonly Button _buttonZoomIn;
    private readonly Button _buttonZoomOut;
    private readonly ToolTip _toolTip;
    private readonly ComboBox _pageFormatComboBox;
    private readonly NumericUpDown _customPageWidthUpDown;
    private readonly NumericUpDown _customPageHeightUpDown;
    private readonly NumericUpDown _heightUpDown;
    private readonly CheckBox _lockRatioCheckBox;
    private readonly CheckBox _snapImagesCheckBox;
    private readonly CheckBox _rulersCheckBox;
    private readonly CheckBox _inchesCheckBox;
    private readonly Label _selectionLabel;
    private int _selectedIndex = -1;
    private ImageToPdfGeometry.PageDefinition _pageDefinition;
    private float _previewScale;
    private ImageInteractionMode _interactionMode;
    private ResizeHandle? _activeResizeHandle;
    private int _activeResizeSourceIndex = -1;
    private int _activeRotateSourceIndex = -1;
    private CropHandle? _activeCropHandle;
    private bool _cropModeEnabled;
    private bool _isUpdatingSizeFields;
    private bool _isUpdatingPageControls;
    private int _clipboardPasteCount;
    private Point _interactionStartPoint;
    private Point _interactionStartScrollOffset;
    private RectangleF _interactionStartRect;
    private RectangleF _interactionStartPreviewRect;
    private RectangleF _interactionStartCropFullPreviewRect;
    private RectangleF _interactionStartCropFullPageRect;
    private RectangleF _interactionStartSelectionBounds;
    private List<SelectionInteractionItemState> _interactionStartSelectionItems = [];
    private ImageToPdfCropSettings _interactionStartCrop = ImageToPdfCropSettings.CreateDefault();
    private EditorHistoryState? _interactionStartHistoryState;
    private double _interactionStartAngle;
    private double? _snapGuideX;
    private double? _snapGuideY;
    private double? _snapCenterGuideX;
    private double? _snapCenterGuideY;
    private bool _panViewMoved;
    private bool _isRestoringHistory;
    private bool _isDragCopyMode;
    private bool _copyDragActivated;
    private int _copyDragHitIndex = -1;

    public ImageToPdfForm(string inputPath, string ffmpegPath, FfmpegRunner ffmpegRunner)
        : this([inputPath], ffmpegPath, ffmpegRunner)
    {
    }

    public ImageToPdfForm(IReadOnlyList<string> inputPaths, string ffmpegPath, FfmpegRunner ffmpegRunner)
    {
        if (inputPaths is null || inputPaths.Count == 0)
        {
            throw new ArgumentException("At least one input image is required.", nameof(inputPaths));
        }

        _ffmpegPath = ffmpegPath;
        _ffmpegRunner = ffmpegRunner;
        _pageDefinition = ImageToPdfGeometry.GetPageDefinition(ImageToPdfSettings.DefaultPageFormat);

        FrameShiftWindowChrome.Apply(this, "FrameShift - Image to PDF");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        MinimumSize = new Size(1080, 760);
        ClientSize = new Size(1280, 860);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        KeyPreview = true;
        _toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 150,
            ReshowDelay = 100,
            AutoPopDelay = 6000
        };

        var rootLayout = FrameShiftEditorShellUi.CreateRootLayout();

        var headerPanel = CreateHeaderPanel(inputPaths);
        var contentLayout = FrameShiftEditorShellUi.CreateTwoPaneContentLayout(
            FrameShiftUiMetrics.WideEditorRailWidth,
            out var leftHost,
            out var rightHost);

        _previewPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = FrameShiftTheme.AccentSoft,
            TabStop = true
        };
        ControlHelper.SetDoubleBuffered(_previewPanel);
        _previewPanel.Paint += PreviewPanelOnPaint;
        _previewPanel.MouseDown += PreviewPanelOnMouseDown;
        _previewPanel.MouseMove += PreviewPanelOnMouseMove;
        _previewPanel.MouseUp += PreviewPanelOnMouseUp;
        _previewPanel.MouseWheel += PreviewPanelOnMouseWheel;
        _previewPanel.AllowDrop = true;
        _previewPanel.DragEnter += PreviewPanelOnDragEnter;
        _previewPanel.DragOver += PreviewPanelOnDragOver;
        _previewPanel.DragDrop += PreviewPanelOnDragDrop;
        _previewPanel.AutoScroll = true;
        _previewPanel.SizeChanged += (_, _) =>
        {
            UpdatePreviewCanvasLayout();
            _previewPanel.Invalidate();
        };
        _previewPanel.MouseEnter += (_, _) =>
        {
            if (!_previewPanel.ContainsFocus)
            {
                _previewPanel.Focus();
            }
        };
        _previewPanel.MouseLeave += (_, _) =>
        {
            if (_interactionMode == ImageInteractionMode.None)
            {
                _previewPanel.Cursor = Cursors.Default;
            }
        };

        var previewSection = CreatePreviewSection(out var previewContentHost);
        previewSection.Margin = Padding.Empty;
        previewSection.Padding = FrameShiftUiMetrics.StandardSectionPadding;
        previewContentHost.Controls.Add(_previewPanel);

        var sidePanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            AutoScroll = false,
            BackColor = FrameShiftTheme.PageBackground
        };

        var sideContent = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 6,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        sideContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        sideContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sideContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sideContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sideContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sideContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sideContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _buttonAddImage = CreateToolbarButton("Add image");
        _buttonDeleteImage = CreateToolbarButton("Remove selected");
        _buttonFitPage = CreateToolbarButton("Fit page");
        _buttonCenter = CreateToolbarButton("Center");
        _buttonCrop = CreateToolbarButton("Crop");
        _buttonPrint = CreateToolbarButton("Print");
        _buttonExport = CreateToolbarButton("Export");
        _buttonMoveBackward = CreateToolbarButton("Back 1");
        _buttonMoveForward = CreateToolbarButton("Front 1");
        _buttonSendToBack = CreateToolbarButton("To back");
        _buttonBringToFront = CreateToolbarButton("To front");
        _buttonClearSelection = CreateToolbarButton("Clear");
        _buttonZoomFit = CreateToolbarButton("Fit view");
        _buttonZoomIn = CreateToolbarButton("Zoom +");
        _buttonZoomOut = CreateToolbarButton("Zoom -");
        _buttonCancel = CreateToolbarButton("Close");

        ConfigureTileButton(_buttonAddImage, "add-image-icon.ico", "Add one or more images to the current PDF page.");
        ConfigureTileButton(_buttonDeleteImage, "remove-icon.ico", "Remove the selected image from the page.");
        ConfigureTileButton(_buttonClearSelection, "clear-icon.ico", "Remove all images from the page.");
        ConfigureTileButton(_buttonSendToBack, "send-to-back-icon.ico", "Send image to back.", compactBadge: true);
        ConfigureTileButton(_buttonMoveBackward, "send-backward-icon.ico", "Move one layer backward.", compactBadge: true);
        ConfigureTileButton(_buttonMoveForward, "bring-forward-icon.ico", "Move one layer forward.", compactBadge: true);
        ConfigureTileButton(_buttonBringToFront, "bring-to-front-icon.ico", "Bring image to front.", compactBadge: true);
        ApplyOrderTileStyle(_buttonSendToBack);
        ApplyOrderTileStyle(_buttonMoveBackward);
        ApplyOrderTileStyle(_buttonMoveForward);
        ApplyOrderTileStyle(_buttonBringToFront);
        ConfigureTileButton(_buttonFitPage, "fit-to-page-icon.ico", "Fit the active image inside the current page.");
        ConfigureTileButton(_buttonCenter, "center-icon.ico", "Center the active image on the page.");
        ConfigureTileButton(_buttonCrop, "crop-icon.ico", "Toggle crop mode for the active image.");
        ConfigureTileButton(_buttonZoomFit, "fit-view-icon.ico", "Fit preview to the current window.");
        ConfigureTileButton(_buttonZoomOut, "zoom-out-icon.ico", "Zoom out.");
        ConfigureTileButton(_buttonZoomIn, "zoom-in-icon.ico", "Zoom in.");
        ConfigureTileButton(_buttonExport, "export-icon.ico", "Export the current layout as a single-page PDF.", wideBadge: true);
        ConfigureTileButton(_buttonPrint, "print-icon.ico", "Print the current single-page layout.");
        ConfigureTileButton(_buttonCancel, "cancel-icon.ico", "Close the editor without exporting.");
        _buttonCrop.Enabled = false;

        _selectionLabel = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = FrameShiftTheme.TextPrimary,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point)
        };

        var libraryLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Margin = Padding.Empty,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        libraryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        libraryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        libraryLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        libraryLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        libraryLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        libraryLayout.Controls.Add(_selectionLabel, 0, 0);
        libraryLayout.SetColumnSpan(_selectionLabel, 2);
        libraryLayout.Controls.Add(_buttonAddImage, 0, 1);
        libraryLayout.SetColumnSpan(_buttonAddImage, 2);
        libraryLayout.Controls.Add(_buttonDeleteImage, 0, 2);
        libraryLayout.Controls.Add(_buttonClearSelection, 1, 2);
        var libraryCard = FrameShiftEditorShellUi.CreateSidebarGroup("Library", libraryLayout);

        var arrangeLayout = CreateFixedHeightTileGrid(4, 64);
        arrangeLayout.Controls.Add(_buttonSendToBack, 0, 0);
        arrangeLayout.Controls.Add(_buttonMoveBackward, 1, 0);
        arrangeLayout.Controls.Add(_buttonMoveForward, 2, 0);
        arrangeLayout.Controls.Add(_buttonBringToFront, 3, 0);
        var arrangeCard = FrameShiftEditorShellUi.CreateSidebarGroup("Order", arrangeLayout);

        var pageLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Margin = Padding.Empty,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        pageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
        pageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        pageLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pageLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pageLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pageLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _pageFormatComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat
        };
        _customPageWidthUpDown = CreateCentimeterNumericUpDown();
        _customPageHeightUpDown = CreateCentimeterNumericUpDown();

        _pageFormatComboBox.Items.AddRange([
            new PageFormatOption("A4 portrait", "A4Portrait"),
            new PageFormatOption("A4 landscape", "A4Landscape"),
            new PageFormatOption("A3 portrait", "A3Portrait"),
            new PageFormatOption("A3 landscape", "A3Landscape"),
            new PageFormatOption("Custom", "Custom")
        ]);
        _pageFormatComboBox.DisplayMember = nameof(PageFormatOption.Display);
        _pageFormatComboBox.ValueMember = nameof(PageFormatOption.Value);
        _pageFormatComboBox.SelectedIndexChanged += (_, _) => ApplyPageFormatSelection();
        _customPageWidthUpDown.ValueChanged += (_, _) => ApplyCustomPageSizeFromFields();
        _customPageHeightUpDown.ValueChanged += (_, _) => ApplyCustomPageSizeFromFields();

        pageLayout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Format", TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        pageLayout.Controls.Add(_pageFormatComboBox, 1, 0);
        pageLayout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Width cm", TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        pageLayout.Controls.Add(_customPageWidthUpDown, 1, 1);
        pageLayout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Height cm", TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        pageLayout.Controls.Add(_customPageHeightUpDown, 1, 2);
        var pageCard = FrameShiftEditorShellUi.CreateSidebarGroup("Page", pageLayout);

        var activeImageLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        activeImageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        activeImageLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        activeImageLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var activeImageButtonsLayout = CreateTileGrid(3, 1);
        activeImageButtonsLayout.AutoSize = true;
        activeImageButtonsLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        activeImageButtonsLayout.Controls.Add(_buttonFitPage, 0, 0);
        activeImageButtonsLayout.Controls.Add(_buttonCenter, 1, 0);
        activeImageButtonsLayout.Controls.Add(_buttonCrop, 2, 0);

        var activeImageOptionsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        activeImageOptionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        activeImageOptionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        activeImageOptionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        activeImageOptionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        activeImageOptionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _heightUpDown = CreatePercentageNumericUpDown();
        _heightUpDown.Visible = false;
        _lockRatioCheckBox = new CheckBox
        {
            Dock = DockStyle.Fill,
            Text = "Ratio",
            Checked = true
        };
        _snapImagesCheckBox = new CheckBox
        {
            Dock = DockStyle.Fill,
            Text = "Snap",
            Checked = true
        };
        _rulersCheckBox = new CheckBox
        {
            Dock = DockStyle.Fill,
            Text = "Rulers",
            Checked = true
        };
        _inchesCheckBox = new CheckBox
        {
            Dock = DockStyle.Fill,
            Text = "Inches",
            Enabled = false,
            TabStop = false
        };

        _heightUpDown.Leave += (_, _) => ApplyResizeFromFields(heightChanged: true);
        _heightUpDown.KeyDown += ResizeFieldOnKeyDown;
        _snapImagesCheckBox.CheckedChanged += (_, _) =>
        {
            ClearSnapGuides();
            _previewPanel.Invalidate();
        };
        _rulersCheckBox.CheckedChanged += (_, _) => _previewPanel.Invalidate();

        activeImageOptionsLayout.Controls.Add(_lockRatioCheckBox, 0, 0);
        activeImageOptionsLayout.Controls.Add(_snapImagesCheckBox, 1, 0);
        activeImageOptionsLayout.Controls.Add(_rulersCheckBox, 2, 0);
        activeImageOptionsLayout.Controls.Add(_inchesCheckBox, 3, 0);

        activeImageLayout.Controls.Add(activeImageButtonsLayout, 0, 0);
        activeImageLayout.Controls.Add(activeImageOptionsLayout, 0, 1);
        var activeImageCard = FrameShiftEditorShellUi.CreateSidebarGroup("Active image", activeImageLayout);

        var finalActionsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Margin = Padding.Empty,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        finalActionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        finalActionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var outputViewLayout = CreateTileGrid(3, 1);
        outputViewLayout.AutoSize = true;
        outputViewLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        outputViewLayout.Controls.Add(_buttonZoomFit, 0, 0);
        outputViewLayout.Controls.Add(_buttonZoomOut, 1, 0);
        outputViewLayout.Controls.Add(_buttonZoomIn, 2, 0);

        var outputActionsLayout = CreateTileGrid(2, 1);
        outputActionsLayout.AutoSize = true;
        outputActionsLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        outputActionsLayout.Controls.Add(_buttonExport, 0, 0);
        outputActionsLayout.Controls.Add(_buttonPrint, 1, 0);

        finalActionsLayout.Controls.Add(outputViewLayout, 0, 0);
        var outputCard = FrameShiftEditorShellUi.CreateSidebarGroup("Output", outputActionsLayout);
        var viewCard = FrameShiftEditorShellUi.CreateSidebarGroup("View", finalActionsLayout);
        viewCard.Margin = new Padding(0);

        sideContent.Controls.Add(outputCard, 0, 0);
        sideContent.Controls.Add(libraryCard, 0, 1);
        sideContent.Controls.Add(arrangeCard, 0, 2);
        sideContent.Controls.Add(activeImageCard, 0, 3);
        sideContent.Controls.Add(pageCard, 0, 4);
        sideContent.Controls.Add(viewCard, 0, 5);
        sidePanel.Controls.Add(sideContent);

        leftHost.Controls.Add(previewSection);
        rightHost.Controls.Add(sidePanel);
        rootLayout.Controls.Add(headerPanel, 0, 0);
        rootLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
        rootLayout.Controls.Add(contentLayout, 0, 2);
        Controls.Add(rootLayout);
        EnsureInitialWindowHeight(rootLayout, sideContent);

        _buttonAddImage.Click += (_, _) => AddImagesFromDialog();
        _buttonDeleteImage.Click += (_, _) => DeleteSelectedImage();
        _buttonFitPage.Click += (_, _) => FitSelectedImageToPage();
        _buttonCenter.Click += (_, _) => CenterSelectedImage();
        _buttonMoveBackward.Click += (_, _) => MoveSelectedImageBackward();
        _buttonMoveForward.Click += (_, _) => MoveSelectedImageForward();
        _buttonSendToBack.Click += (_, _) => SendSelectedImageToBack();
        _buttonBringToFront.Click += (_, _) => BringSelectedImageToFront();
        _buttonClearSelection.Click += (_, _) => ClearAllImages();
        _buttonZoomFit.Click += (_, _) => FitPreviewToView();
        _buttonZoomIn.Click += (_, _) => SetPreviewZoom(_previewScale * 1.15f);
        _buttonZoomOut.Click += (_, _) => SetPreviewZoom(_previewScale / 1.15f);
        _buttonCrop.Click += (_, _) => ToggleCropMode();
        _buttonPrint.Click += (_, _) => PrintCurrentLayout();
        _buttonExport.Click += (_, _) => ConfirmExport();
        _buttonCancel.Click += (_, _) => CancelAndClose();

        AcceptButton = _buttonExport;

        InitializePageControls();
        FitPreviewToView();

        var loadedAnyInitialItem = false;
        string? firstInitialError = null;

        for (var index = 0; index < inputPaths.Count; index++)
        {
            var inputPath = inputPaths[index];
            if (TryAddImageInternal(inputPath, index == 0, out var initialError, out _))
            {
                loadedAnyInitialItem = true;
                continue;
            }

            firstInitialError ??= initialError ?? MediaActionMessages.ImageToPdfItemLoadFailed(inputPath);
        }

        if (!loadedAnyInitialItem)
        {
            throw new InvalidOperationException(firstInitialError ?? MediaActionMessages.ImageToPdfItemLoadFailed(inputPaths[0]));
        }

        RefreshImageList(_items.Count - 1);
        UpdateSelectionState(_items.Count - 1);
        FitPreviewToView();
        FormClosed += (_, _) =>
        {
            DisposeClipboardItems();
            DisposeLoadedImages();
            CleanupTemporarySourcePaths();
        };
    }

    private Panel CreateHeaderPanel(IReadOnlyList<string> inputPaths)
    {
        var subtitle = inputPaths.Count == 1
            ? $"Source: {Path.GetFileName(inputPaths[0])}"
            : $"{inputPaths.Count} images selected";

        return FrameShiftUiFactory.CreateFillHeader(
            "FrameShift - Image to PDF",
            subtitle,
            IconPaths.ImageToPdfIco("image-to-pdf-image-icon.ico"),
            IconPaths.AppIcon,
            "PDF",
            980);
    }

    private Panel CreatePreviewSection(out Panel contentHost)
    {
        return FrameShiftUiFactory.CreateFillSection("Preview", out contentHost);
    }

    private void EnsureInitialWindowHeight(TableLayoutPanel rootLayout, Control sideContent)
    {
        rootLayout.PerformLayout();
        sideContent.PerformLayout();

        var nonClientHeight = Height - ClientSize.Height;
        var requiredClientHeight =
            rootLayout.Padding.Vertical +
            FrameShiftUiMetrics.HeaderHeight +
            FrameShiftUiMetrics.OuterPadding +
            sideContent.PreferredSize.Height;

        ClientSize = new Size(ClientSize.Width, requiredClientHeight);
        MinimumSize = new Size(
            MinimumSize.Width,
            requiredClientHeight + nonClientHeight);
    }

    public ImageToPdfSettings? Settings { get; private set; }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down) &&
            TryNudgeSelectedImage(keyData))
        {
            return true;
        }

        if (keyData == (Keys.Control | Keys.Z))
        {
            UndoLastAction();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z))
        {
            RedoLastAction();
            return true;
        }

        if (keyData == (Keys.Control | Keys.O))
        {
            AddImagesFromDialog();
            return true;
        }

        if (keyData == (Keys.Control | Keys.C))
        {
            CopySelectedImagesToClipboard();
            return true;
        }

        if (keyData == (Keys.Control | Keys.X))
        {
            CutSelectedImagesToClipboard();
            return true;
        }

        if (keyData == (Keys.Control | Keys.V))
        {
            PasteClipboardContents();
            return true;
        }

        if (keyData == (Keys.Control | Keys.S) || keyData == (Keys.Control | Keys.E))
        {
            ConfirmExport();
            return true;
        }

        if (keyData == Keys.Delete)
        {
            DeleteSelectedImage();
            return true;
        }

        if (keyData == Keys.Escape)
        {
            CancelAndClose();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void AddImagesFromDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Add image",
            Multiselect = true,
            CheckFileExists = true,
            Filter = "Supported images (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|WebP (*.webp)|*.webp|Bitmap (*.bmp)|*.bmp"
        };

        if (_items.Count > 0)
        {
            try
            {
                dialog.InitialDirectory = Path.GetDirectoryName(_items[GetSelectedIndex() >= 0 ? GetSelectedIndex() : 0].Settings.SourcePath);
            }
            catch
            {
            }
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        AddImagesFromPaths(dialog.FileNames);
    }

    private IReadOnlyList<int> GetSelectedItemIndexes()
    {
        return _selectedIndices.Count == 0
            ? []
            : _selectedIndices.ToArray();
    }

    public void AddPathsThreadSafe(IEnumerable<string> paths)
    {
        if (IsDisposed) return;
        BeginInvoke(() => AddImagesFromPaths(paths));
    }

    private void AddImagesFromPaths(IEnumerable<string> filePaths)
    {
        var paths = filePaths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        var historyBefore = CaptureEditorHistoryState();
        var anyAdded = false;
        foreach (var fileName in paths)
        {
            if (!TryAddImageInternal(fileName, false, out var errorMessage, out _))
            {
                ShowError(errorMessage ?? MediaActionMessages.ImageToPdfItemLoadFailed(fileName));
                continue;
            }

            anyAdded = true;
        }

        if (anyAdded)
        {
            CommitHistoryIfChanged(historyBefore);
            RefreshImageList(_items.Count - 1);
            UpdateSelectionState(_items.Count - 1);
        }
    }

    private bool TryAddImageInternal(string path, bool isInitialItem, out string? errorMessage, out bool selectedNewItem)
    {
        errorMessage = null;
        selectedNewItem = false;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            errorMessage = MediaActionMessages.ImageFileInaccessible(path);
            return false;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!IsSupportedExtension(extension))
        {
            errorMessage = MediaActionMessages.UnsupportedSourceFormat(extension, ImageCropSupport.GetSupportedExtensionsText());
            return false;
        }

        try
        {
            if (!TryGetOrLoadBitmap(path, out var bitmap, out errorMessage) || bitmap is null)
            {
                return false;
            }

            return TryAddBitmapItemInternal(path, bitmap, null, isInitialItem, out errorMessage, out selectedNewItem);
        }
        catch (UnauthorizedAccessException)
        {
            errorMessage = MediaActionMessages.ImageFileInaccessible(path);
            return false;
        }
        catch (IOException)
        {
            errorMessage = MediaActionMessages.ImageFileInaccessible(path);
            return false;
        }
        catch (ArgumentException)
        {
            errorMessage = MediaActionMessages.ImageInvalid(path);
            return false;
        }
        catch (ExternalException)
        {
            errorMessage = MediaActionMessages.ImageInvalid(path);
            return false;
        }
        catch (OutOfMemoryException)
        {
            errorMessage = MediaActionMessages.ImageInvalid(path);
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.ImageLoadFailed(path));
            return false;
        }
    }

    private bool TryAddBitmapItemInternal(
        string sourcePath,
        Bitmap bitmap,
        ImageToPdfItemSettings? templateSettings,
        bool isInitialItem,
        out string? errorMessage,
        out bool selectedNewItem)
    {
        errorMessage = null;
        selectedNewItem = false;

        if (bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            errorMessage = MediaActionMessages.ImageInvalid(sourcePath);
            return false;
        }

        if (bitmap.Width > MaximumImageDimension ||
            bitmap.Height > MaximumImageDimension ||
            (long)bitmap.Width * bitmap.Height > MaximumImagePixels)
        {
            errorMessage = MediaActionMessages.ImageTooLarge(sourcePath, bitmap.Width, bitmap.Height);
            return false;
        }

        var normalizedRect = isInitialItem
            ? ImageToPdfGeometry.CreateInitialRectNormalized(bitmap.Size, _pageDefinition)
            : ImageToPdfGeometry.CreateAddedRectNormalized(bitmap.Size, _pageDefinition, _items.Count);

        var settings = templateSettings is null
            ? new ImageToPdfItemSettings
            {
                SourcePath = sourcePath,
                X = normalizedRect.X,
                Y = normalizedRect.Y,
                Width = normalizedRect.Width,
                Height = normalizedRect.Height,
                RotationQuarterTurns = 0,
                Crop = ImageToPdfCropSettings.CreateDefault()
            }
            : CloneItemSettings(templateSettings, sourcePath);

        _bitmapCache[sourcePath] = bitmap;
        _items.Add(new ImageCanvasItem(settings, bitmap));
        selectedNewItem = true;
        return true;
    }

    private IReadOnlyList<int> GetClipboardSelectedIndexes()
    {
        return GetSelectedItemIndexes();
    }

    private bool IsItemSelected(int index)
    {
        return _selectedIndices.Contains(index);
    }

    private void SetSelectedIndexes(IEnumerable<int> indexes, int? primaryIndex, bool updateUi = true)
    {
        var normalized = indexes
            .Where(index => index >= 0 && index < _items.Count)
            .Distinct()
            .OrderBy(index => index)
            .ToList();

        _selectedIndices.Clear();
        _selectedIndices.AddRange(normalized);

        if (_selectedIndices.Count == 0)
        {
            _selectedIndex = -1;
        }
        else if (primaryIndex is not null && _selectedIndices.Contains(primaryIndex.Value))
        {
            _selectedIndex = primaryIndex.Value;
        }
        else
        {
            _selectedIndex = _selectedIndices[^1];
        }

        _interactionStartSelectionItems = [];
        _interactionStartSelectionBounds = RectangleF.Empty;

        if (updateUi)
        {
            UpdateSelectionState(_selectedIndex);
        }
    }

    private void ToggleSelectionAtIndex(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        var nextSelection = _selectedIndices.ToList();
        if (!nextSelection.Remove(index))
        {
            nextSelection.Add(index);
        }

        SetSelectedIndexes(nextSelection, index, updateUi: true);
    }

    private void RestoreSelectionFromHistory(EditorHistoryState state)
    {
        var normalized = state.SelectedIndexes
            .Where(index => index >= 0 && index < _items.Count)
            .Distinct()
            .OrderBy(index => index)
            .ToList();

        if (normalized.Count == 0 && state.SelectedIndex >= 0 && state.SelectedIndex < _items.Count)
        {
            normalized.Add(state.SelectedIndex);
        }

        SetSelectedIndexes(normalized, state.SelectedIndex >= 0 ? state.SelectedIndex : null, updateUi: false);
    }

    private void CaptureInteractionSelectionState()
    {
        _interactionStartSelectionItems = _selectedIndices
            .Where(index => index >= 0 && index < _items.Count)
            .Select(index => new SelectionInteractionItemState(
                index,
                _items[index].Settings.ToRectangleF(),
                _items[index].Settings.GetRotationAngleDegrees()))
            .ToList();

        _interactionStartSelectionBounds = GetSelectionBounds(_interactionStartSelectionItems.Select(item => item.Rect));
    }

    private bool TryGetSelectedItemAtPoint(PointF point, RectangleF previewPageRect, out int hitIndex)
    {
        for (var index = _items.Count - 1; index >= 0; index--)
        {
            if (!IsItemSelected(index))
            {
                continue;
            }

            var previewRect = ImageToPdfGeometry.ToPreviewRect(_items[index].Settings.ToRectangleF(), previewPageRect);
            var angle = _items[index].Settings.GetRotationAngleDegrees();
            if (ImageToPdfGeometry.TestPreviewPointInRotatedRect(previewRect, angle, point))
            {
                hitIndex = index;
                return true;
            }
        }

        hitIndex = -1;
        return false;
    }

    private bool TryGetSelectedResizeHandleAtPoint(PointF point, RectangleF previewPageRect, out int itemIndex, out ResizeHandle handle)
    {
        for (var index = _items.Count - 1; index >= 0; index--)
        {
            if (!IsItemSelected(index))
            {
                continue;
            }

            var item = _items[index];
            var previewRect = ImageToPdfGeometry.ToPreviewRect(item.Settings.ToRectangleF(), previewPageRect);
            var rotationAngle = item.Settings.GetRotationAngleDegrees();
            var hit = ImageToPdfGeometry.GetPreviewResizeHandleHit(previewRect, rotationAngle, point, 10f);
            if (!string.IsNullOrWhiteSpace(hit))
            {
                itemIndex = index;
                handle = ParseResizeHandle(hit);
                return true;
            }
        }

        itemIndex = -1;
        handle = ResizeHandle.TopLeft;
        return false;
    }

    private bool TryGetSelectedRotationHandleAtPoint(PointF point, RectangleF previewPageRect, out int itemIndex)
    {
        for (var index = _items.Count - 1; index >= 0; index--)
        {
            if (!IsItemSelected(index))
            {
                continue;
            }

            var item = _items[index];
            var previewRect = ImageToPdfGeometry.ToPreviewRect(item.Settings.ToRectangleF(), previewPageRect);
            var rotationAngle = item.Settings.GetRotationAngleDegrees();
            if (ImageToPdfGeometry.GetPreviewRotationHandleHit(previewRect, rotationAngle, point, 12f))
            {
                itemIndex = index;
                return true;
            }
        }

        itemIndex = -1;
        return false;
    }

    private static RectangleF GetSelectionBounds(IEnumerable<RectangleF> rects)
    {
        var enumerator = rects.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return RectangleF.Empty;
        }

        var bounds = enumerator.Current;
        while (enumerator.MoveNext())
        {
            bounds = RectangleF.Union(bounds, enumerator.Current);
        }

        return bounds;
    }

    private void MoveSelectionByDelta(int deltaX, int deltaY, RectangleF previewPageRect)
    {
        if (_interactionStartSelectionItems.Count == 0)
        {
            return;
        }

        if (!_snapImagesCheckBox.Checked)
        {
            ClearSnapGuides();
        }

        var selectedSet = _selectedIndices.ToHashSet();
        var candidateRects = new List<RectangleF>
        {
            ImageToPdfGeometry.MoveNormalizedRect(_interactionStartSelectionBounds, deltaX, deltaY, previewPageRect)
        };

        for (var index = 0; index < _items.Count; index++)
        {
            if (!selectedSet.Contains(index))
            {
                candidateRects.Add(_items[index].Settings.ToRectangleF());
            }
        }

        var snappedGroupRect = candidateRects.Count > 1
            ? ImageToPdfGeometry.ApplySnapToNormalizedRect(candidateRects[0], candidateRects, 0, previewPageRect, _pageDefinition, SnapTolerancePreview).Rect
            : candidateRects[0];

        snappedGroupRect = ApplyPageCenterSnapIfEnabled(snappedGroupRect, previewPageRect);

        var deltaRectX = snappedGroupRect.X - _interactionStartSelectionBounds.X;
        var deltaRectY = snappedGroupRect.Y - _interactionStartSelectionBounds.Y;

        foreach (var item in _interactionStartSelectionItems)
        {
            var nextRect = new RectangleF(
                item.Rect.X + deltaRectX,
                item.Rect.Y + deltaRectY,
                item.Rect.Width,
                item.Rect.Height);
            ApplyRect(_items[item.Index].Settings, ImageToPdfGeometry.ClampNormalizedRect(nextRect));
        }
    }

    private void RotateSelectionByMouse(PointF currentMousePoint, RectangleF previewPageRect)
    {
        if (_interactionStartSelectionItems.Count == 0 || _activeRotateSourceIndex < 0)
        {
            return;
        }

        var sourceItem = _interactionStartSelectionItems.FirstOrDefault(item => item.Index == _activeRotateSourceIndex);
        if (sourceItem is null)
        {
            return;
        }

        var sourcePreviewRect = ImageToPdfGeometry.ToPreviewRect(sourceItem.Rect, previewPageRect);
        var currentAngle = ImageToPdfGeometry.GetRotationAngleFromPreviewPoint(sourcePreviewRect, currentMousePoint);
        if (_snapImagesCheckBox.Checked)
        {
            currentAngle = ImageToPdfGeometry.SnapRotationAngle(currentAngle, 4.0);
        }

        var angleDelta = currentAngle - _interactionStartAngle;

        foreach (var item in _interactionStartSelectionItems)
        {
            var settings = _items[item.Index].Settings;
            settings.RotationAngleDegrees = ImageToPdfGeometry.NormalizeRotationAngle(item.AngleDegrees + angleDelta);
            settings.RotationQuarterTurns = (int)Math.Round(settings.RotationAngleDegrees / 90.0, MidpointRounding.AwayFromZero) % 4;
        }
    }

    private void ResizeSelectionFromHandle(ResizeHandle handle, PointF currentPreviewPoint, RectangleF previewPageRect)
    {
        if (_interactionStartSelectionItems.Count == 0 || _interactionStartSelectionBounds.IsEmpty)
        {
            return;
        }

        ClearSnapGuides();
        if (!TryGetSelectionResizeTransform(handle, currentPreviewPoint, previewPageRect, out var scale))
        {
            return;
        }

        foreach (var item in _interactionStartSelectionItems)
        {
            var absoluteRect = ImageToPdfGeometry.ToAbsoluteRect(item.Rect, _pageDefinition);
            var anchorPoint = GetResizeAnchorPoint(absoluteRect, handle);
            var nextRectAbsolute = new RectangleF(
                anchorPoint.X + ((float)absoluteRect.X - anchorPoint.X) * scale,
                anchorPoint.Y + ((float)absoluteRect.Y - anchorPoint.Y) * scale,
                (float)absoluteRect.Width * scale,
                (float)absoluteRect.Height * scale);

            ApplyRect(_items[item.Index].Settings, ClampResizeRectToPage(ToNormalizedRect(nextRectAbsolute)));
        }
    }

    private bool TryGetSelectionResizeTransform(
        ResizeHandle handle,
        PointF currentPreviewPoint,
        RectangleF previewPageRect,
        out float scale)
    {
        var sourceItem = _interactionStartSelectionItems.FirstOrDefault(item => item.Index == _activeResizeSourceIndex);
        if (sourceItem is null)
        {
            scale = 1f;
            return false;
        }

        var sourceResizedRect = ResizeRectFromHandle(
            sourceItem.Rect,
            handle,
            currentPreviewPoint,
            previewPageRect,
            sourceItem.AngleDegrees);

        var sourceAbsolute = ImageToPdfGeometry.ToAbsoluteRect(sourceItem.Rect, _pageDefinition);
        var resizedSourceAbsolute = ImageToPdfGeometry.ToAbsoluteRect(sourceResizedRect, _pageDefinition);
        var rawScale = handle is ResizeHandle.Top or ResizeHandle.Bottom
            ? resizedSourceAbsolute.Height / Math.Max(12.0, sourceAbsolute.Height)
            : resizedSourceAbsolute.Width / Math.Max(12.0, sourceAbsolute.Width);

        var globalMinimumScale = 0.0;
        var globalMaximumScale = double.PositiveInfinity;
        foreach (var item in _interactionStartSelectionItems)
        {
            var itemAbsolute = ImageToPdfGeometry.ToAbsoluteRect(item.Rect, _pageDefinition);
            var minimumScale = Math.Max(12.0 / Math.Max(12.0, itemAbsolute.Width), 12.0 / Math.Max(12.0, itemAbsolute.Height));
            globalMinimumScale = Math.Max(globalMinimumScale, minimumScale);
            globalMaximumScale = Math.Min(globalMaximumScale, GetMaximumResizeScale(itemAbsolute, GetResizeAnchorPoint(itemAbsolute, handle)));
        }

        var boundedScale = Math.Max(globalMinimumScale, Math.Min(rawScale, globalMaximumScale));
        scale = (float)Math.Max(0.0001, boundedScale);
        return true;
    }

    private PointF GetResizeAnchorPoint(ImageToPdfGeometry.AbsoluteRect rect, ResizeHandle handle)
    {
        var left = (float)rect.X;
        var top = (float)rect.Y;
        var right = (float)(rect.X + rect.Width);
        var bottom = (float)(rect.Y + rect.Height);
        var centerX = (left + right) / 2f;
        var centerY = (top + bottom) / 2f;

        return handle switch
        {
            ResizeHandle.TopLeft => new PointF(right, bottom),
            ResizeHandle.Top => new PointF(centerX, bottom),
            ResizeHandle.TopRight => new PointF(left, bottom),
            ResizeHandle.Right => new PointF(left, centerY),
            ResizeHandle.BottomRight => new PointF(left, top),
            ResizeHandle.Bottom => new PointF(centerX, top),
            ResizeHandle.BottomLeft => new PointF(right, top),
            ResizeHandle.Left => new PointF(right, centerY),
            _ => new PointF(left, top)
        };
    }

    private double GetMaximumResizeScale(ImageToPdfGeometry.AbsoluteRect rect, PointF anchorPoint)
    {
        var leftDelta = rect.X - anchorPoint.X;
        var rightDelta = (rect.X + rect.Width) - anchorPoint.X;
        var topDelta = rect.Y - anchorPoint.Y;
        var bottomDelta = (rect.Y + rect.Height) - anchorPoint.Y;
        var maxScale = double.PositiveInfinity;

        if (leftDelta < 0.0)
        {
            maxScale = Math.Min(maxScale, anchorPoint.X / -leftDelta);
        }

        if (rightDelta > 0.0)
        {
            maxScale = Math.Min(maxScale, (_pageDefinition.WidthPoints - anchorPoint.X) / rightDelta);
        }

        if (topDelta < 0.0)
        {
            maxScale = Math.Min(maxScale, anchorPoint.Y / -topDelta);
        }

        if (bottomDelta > 0.0)
        {
            maxScale = Math.Min(maxScale, (_pageDefinition.HeightPoints - anchorPoint.Y) / bottomDelta);
        }

        return maxScale;
    }

    private RectangleF ClampResizeRectToPage(RectangleF rect)
    {
        var minimumWidth = Math.Max(0.0001f, (float)(12.0 / _pageDefinition.WidthPoints));
        var minimumHeight = Math.Max(0.0001f, (float)(12.0 / _pageDefinition.HeightPoints));
        var width = Math.Min(1f, Math.Max(minimumWidth, rect.Width));
        var height = Math.Min(1f, Math.Max(minimumHeight, rect.Height));
        var x = Math.Min(Math.Max(0f, rect.X), 1f - width);
        var y = Math.Min(Math.Max(0f, rect.Y), 1f - height);
        return new RectangleF(x, y, width, height);
    }

    private RectangleF GetSelectionBoundsInPreview(RectangleF previewPageRect)
    {
        var rects = new List<RectangleF>();
        foreach (var index in _selectedIndices)
        {
            if (index < 0 || index >= _items.Count)
            {
                continue;
            }

            rects.Add(ImageToPdfGeometry.ToPreviewRect(_items[index].Settings.ToRectangleF(), previewPageRect));
        }

        return GetSelectionBounds(rects);
    }

    private void DeleteSelectedImage()
    {
        if (_selectedIndices.Count == 0)
        {
            return;
        }

        var historyBefore = CaptureEditorHistoryState();
        SetCropMode(false);
        var removedIndexes = _selectedIndices
            .Where(index => index >= 0 && index < _items.Count)
            .OrderByDescending(index => index)
            .ToList();

        if (removedIndexes.Count == 0)
        {
            return;
        }

        var firstRemovedIndex = removedIndexes.Min();
        foreach (var index in removedIndexes)
        {
            _items.RemoveAt(index);
        }

        CommitHistoryIfChanged(historyBefore);

        if (_items.Count == 0)
        {
            RefreshImageList(null);
            UpdateSelectionState(-1);
            return;
        }

        var nextIndex = Math.Min(firstRemovedIndex, _items.Count - 1);
        RefreshImageList(nextIndex);
        UpdateSelectionState(nextIndex);
    }

    private void CopySelectedImagesToClipboard()
    {
        var selectedIndexes = GetClipboardSelectedIndexes();
        if (selectedIndexes.Count == 0)
        {
            return;
        }

        DisposeClipboardItems();
        _clipboardPasteCount = 0;

        foreach (var index in selectedIndexes)
        {
            if (index < 0 || index >= _items.Count)
            {
                continue;
            }

            var item = _items[index];
            var bitmapCopy = new Bitmap(item.Bitmap);
            _clipboardItems.Add(new ClipboardImageItem(CloneItemSettings(item.Settings), bitmapCopy));
        }

        if (_clipboardItems.Count == 0)
        {
            return;
        }

        try
        {
            using var clipboardPreview = new Bitmap(_clipboardItems[0].Bitmap);
            Clipboard.SetImage(clipboardPreview);
        }
        catch
        {
        }
    }

    private void CutSelectedImagesToClipboard()
    {
        var selectedIndexes = GetClipboardSelectedIndexes();
        if (selectedIndexes.Count == 0)
        {
            return;
        }

        CopySelectedImagesToClipboard();
        DeleteSelectedImage();
    }

    private void CreateCopiesOfSelectedItems()
    {
        var originalIndexes = _selectedIndices
            .Where(index => index >= 0 && index < _items.Count)
            .ToList();

        if (originalIndexes.Count == 0)
        {
            return;
        }

        var newIndexes = new List<int>();
        foreach (var index in originalIndexes)
        {
            var original = _items[index];
            var newSettings = CloneItemSettings(original.Settings);
            _items.Add(new ImageCanvasItem(newSettings, original.Bitmap));
            newIndexes.Add(_items.Count - 1);
        }

        SetSelectedIndexes(newIndexes, newIndexes[^1], updateUi: false);
    }

    private void PasteClipboardContents()
    {
        try
        {
            var data = Clipboard.GetDataObject();

            // 1. FileDrop — fichiers copiés depuis l'Explorateur ou toute app
            if (data is not null && data.GetDataPresent(DataFormats.FileDrop))
            {
                if (data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                {
                    AddImagesFromPaths(paths);
                    return;
                }
            }

            // 2. Format PNG nommé — navigateurs, Photoshop, etc.
            if (data is not null && data.GetDataPresent("PNG"))
            {
                if (data.GetData("PNG") is Stream pngStream)
                {
                    using (pngStream)
                    {
                        using var ms = new MemoryStream();
                        pngStream.CopyTo(ms);
                        ms.Position = 0;
                        using var pngImage = Image.FromStream(ms);
                        PasteImageFromClipboard(pngImage);
                    }
                    return;
                }
            }

            // 3. Bitmap générique — captures d'écran, etc.
            if (Clipboard.ContainsImage())
            {
                using var clipboardImage = Clipboard.GetImage();
                if (clipboardImage is not null)
                {
                    PasteImageFromClipboard(clipboardImage);
                    return;
                }
            }
        }
        catch (ExternalException)
        {
        }
        catch (ThreadStateException)
        {
        }

        // 4. Fallback — presse-papiers interne de l'app
        if (_clipboardItems.Count > 0)
        {
            PasteClipboardItems();
        }
    }

    private void PasteImageFromClipboard(Image image)
    {
        if (image.Width <= 0 ||
            image.Height <= 0 ||
            image.Width > MaximumImageDimension ||
            image.Height > MaximumImageDimension ||
            (long)image.Width * image.Height > MaximumImagePixels)
        {
            ShowError(MediaActionMessages.ImageTooLarge("Clipboard image", image.Width, image.Height));
            return;
        }

        var historyBefore = CaptureEditorHistoryState();
        var sourcePath = CreateTemporarySourcePath();
        using var bitmap = new Bitmap(image);
        try
        {
            bitmap.Save(sourcePath, ImageFormat.Png);
        }
        catch (Exception ex)
        {
            if (File.Exists(sourcePath))
                File.Delete(sourcePath);
            ShowError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.ImageLoadFailed("Clipboard image")));
            return;
        }

        if (!TryAddBitmapItemInternal(sourcePath, new Bitmap(bitmap), null, false, out var errorMessage, out _))
        {
            if (File.Exists(sourcePath))
                File.Delete(sourcePath);
            ShowError(errorMessage ?? MediaActionMessages.ImageLoadFailed("Clipboard image"));
            return;
        }

        _temporarySourcePaths.Add(sourcePath);
        CommitHistoryIfChanged(historyBefore);
        RefreshImageList(_items.Count - 1);
        UpdateSelectionState(_items.Count - 1);
    }

    private void PasteClipboardItems()
    {
        if (_clipboardItems.Count == 0)
        {
            return;
        }

        var historyBefore = CaptureEditorHistoryState();
        var offsetPoints = (float)ImageToPdfGeometry.CentimetersToPoints(0.35 + (0.10 * Math.Min(_clipboardPasteCount, 4)));
        var pastedIndexes = new List<int>();

        foreach (var clipboardItem in _clipboardItems)
        {
            var sourcePath = CreateTemporarySourcePath();
            var bitmapCopy = new Bitmap(clipboardItem.Bitmap);
            try
            {
                bitmapCopy.Save(sourcePath, ImageFormat.Png);
            }
            catch (Exception ex)
            {
                bitmapCopy.Dispose();
                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }

                ShowError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.ImageLoadFailed(sourcePath)));
                continue;
            }

            var settings = CloneItemSettings(clipboardItem.Settings, sourcePath);
            var pastedRect = OffsetNormalizedRect(settings.ToRectangleF(), offsetPoints);
            settings.X = pastedRect.X;
            settings.Y = pastedRect.Y;
            settings.Width = pastedRect.Width;
            settings.Height = pastedRect.Height;

            if (!TryAddBitmapItemInternal(sourcePath, bitmapCopy, settings, false, out var errorMessage, out _))
            {
                bitmapCopy.Dispose();
                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }

                ShowError(errorMessage ?? MediaActionMessages.ImageLoadFailed(sourcePath));
                continue;
            }

            _temporarySourcePaths.Add(sourcePath);
            pastedIndexes.Add(_items.Count - 1);
        }

        if (pastedIndexes.Count == 0)
        {
            return;
        }

        _clipboardPasteCount++;
        CommitHistoryIfChanged(historyBefore);
        SetSelectedIndexes(pastedIndexes, pastedIndexes[^1]);
    }

    private string CreateTemporarySourcePath()
    {
        return Path.Combine(Path.GetTempPath(), $"frameshift_image_to_pdf_clip_{Guid.NewGuid():N}.png");
    }

    private static ImageToPdfItemSettings CloneItemSettings(ImageToPdfItemSettings settings, string sourcePath)
    {
        var cloned = EditorHistoryItemState.FromSettings(settings).ToSettings();
        cloned.SourcePath = sourcePath;
        return cloned;
    }

    private static ImageToPdfItemSettings CloneItemSettings(ImageToPdfItemSettings settings)
    {
        return CloneItemSettings(settings, settings.SourcePath);
    }

    private RectangleF OffsetNormalizedRect(RectangleF rect, float offsetPoints)
    {
        if (offsetPoints <= 0f)
        {
            return ImageToPdfGeometry.ClampNormalizedRect(rect);
        }

        var absoluteRect = ImageToPdfGeometry.ToAbsoluteRect(rect, _pageDefinition);
        absoluteRect = new ImageToPdfGeometry.AbsoluteRect(
            absoluteRect.X + offsetPoints,
            absoluteRect.Y + offsetPoints,
            absoluteRect.Width,
            absoluteRect.Height);

        return ImageToPdfGeometry.ToNormalizedRect(new RectangleF(
            (float)absoluteRect.X,
            (float)absoluteRect.Y,
            (float)absoluteRect.Width,
            (float)absoluteRect.Height),
            _pageDefinition);
    }

    private void DisposeClipboardItems()
    {
        foreach (var item in _clipboardItems)
        {
            item.Dispose();
        }

        _clipboardItems.Clear();
    }

    private void CleanupTemporarySourcePaths()
    {
        foreach (var path in _temporarySourcePaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        _temporarySourcePaths.Clear();
    }

    private void MoveSelectedImageBackward()
    {
        MoveSelectedImageByOffset(-1);
    }

    private void MoveSelectedImageForward()
    {
        MoveSelectedImageByOffset(1);
    }

    private void SendSelectedImageToBack()
    {
        MoveSelectedImageToIndex(0);
    }

    private void BringSelectedImageToFront()
    {
        MoveSelectedImageToIndex(_items.Count - 1);
    }

    private void MoveSelectedImageByOffset(int offset)
    {
        if (_cropModeEnabled)
        {
            return;
        }

        if (_selectedIndices.Count != 1)
        {
            return;
        }

        var index = GetSelectedIndex();
        if (index < 0)
        {
            return;
        }

        MoveSelectedImageToIndex(index + offset);
    }

    private void MoveSelectedImageToIndex(int targetIndex)
    {
        if (_selectedIndices.Count != 1)
        {
            return;
        }

        var currentIndex = GetSelectedIndex();
        if (currentIndex < 0 || currentIndex >= _items.Count)
        {
            return;
        }

        if (targetIndex < 0 || targetIndex >= _items.Count || targetIndex == currentIndex)
        {
            return;
        }

        var historyBefore = CaptureEditorHistoryState();
        var item = _items[currentIndex];
        _items.RemoveAt(currentIndex);
        _items.Insert(targetIndex, item);
        CommitHistoryIfChanged(historyBefore);
        RefreshImageList(targetIndex);
        UpdateSelectionState(targetIndex);
    }

    private void FitSelectedImageToPage()
    {
        if (_cropModeEnabled)
        {
            return;
        }

        if (_selectedIndices.Count != 1)
        {
            return;
        }

        var activeItem = GetActiveItem();
        if (activeItem is null)
        {
            return;
        }

        var historyBefore = CaptureEditorHistoryState();
        var rect = ImageToPdfGeometry.CreateInitialRectNormalized(activeItem.Bitmap.Size, _pageDefinition);
        ApplyRect(activeItem.Settings, rect);
        CommitHistoryIfChanged(historyBefore);
        UpdateResizeFieldsFromSelection();
        _previewPanel.Invalidate();
    }

    private void CenterSelectedImage()
    {
        if (_cropModeEnabled)
        {
            return;
        }

        if (_selectedIndices.Count != 1)
        {
            return;
        }

        var activeItem = GetActiveItem();
        if (activeItem is null)
        {
            return;
        }

        var historyBefore = CaptureEditorHistoryState();
        var rect = ImageToPdfGeometry.CenterNormalizedRect(activeItem.Settings.ToRectangleF());
        ApplyRect(activeItem.Settings, rect);
        CommitHistoryIfChanged(historyBefore);
        UpdateResizeFieldsFromSelection();
        _previewPanel.Invalidate();
    }

    private void FitAllImagesToPage()
    {
        if (_cropModeEnabled)
        {
            return;
        }

        if (_items.Count == 0)
        {
            return;
        }

        var historyBefore = CaptureEditorHistoryState();
        for (var index = 0; index < _items.Count; index++)
        {
            var item = _items[index];
            var rect = ImageToPdfGeometry.CreateAddedRectNormalized(item.Bitmap.Size, _pageDefinition, index);
            ApplyRect(item.Settings, rect);
        }

        CommitHistoryIfChanged(historyBefore);
        UpdateResizeFieldsFromSelection();
        _previewPanel.Invalidate();
    }

    private void ClearSelection()
    {
        SetCropMode(false);
        DeselectAllItems();
        UpdateSelectionState(-1);
    }

    private void ClearAllImages()
    {
        if (_items.Count == 0)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            "Remove all images from the current page?",
            "Clear all",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        var historyBefore = CaptureEditorHistoryState();
        SetCropMode(false);
        _items.Clear();
        CommitHistoryIfChanged(historyBefore);
        RefreshImageList(null);
        UpdateSelectionState(-1);
    }

    private void ToggleCropMode()
    {
        if (_selectedIndices.Count != 1 || GetActiveItem() is null)
        {
            return;
        }

        SetCropMode(!_cropModeEnabled);
    }

    private void SetCropMode(bool enabled)
    {
        if (_cropModeEnabled == enabled)
        {
            UpdateCropModeVisualState();
            return;
        }

        _cropModeEnabled = enabled;
        _interactionMode = ImageInteractionMode.None;
        _activeResizeHandle = null;
        _activeResizeSourceIndex = -1;
        _activeRotateSourceIndex = -1;
        _activeCropHandle = null;
        _previewPanel.Cursor = Cursors.Default;
        UpdateCropModeVisualState();
        _previewPanel.Invalidate();
    }

    private void UpdateCropModeVisualState()
    {
        if (_cropModeEnabled)
        {
            _buttonCrop.BackColor = FrameShiftTheme.AccentSoft;
            _buttonCrop.FlatAppearance.BorderColor = FrameShiftTheme.SecondaryBlue;
        }
        else
        {
            _buttonCrop.BackColor = FrameShiftTheme.Surface;
            _buttonCrop.FlatAppearance.BorderColor = FrameShiftTheme.PrimaryBlue;
        }
    }

    private static ImageToPdfCropSettings CopyCrop(ImageToPdfCropSettings crop)
    {
        var normalized = ImageToPdfGeometry.NormalizeCrop(crop);
        return new ImageToPdfCropSettings
        {
            Left = normalized.Left,
            Top = normalized.Top,
            Right = normalized.Right,
            Bottom = normalized.Bottom
        };
    }

    private void ConfirmExport()
    {
        if (_items.Count == 0)
        {
            ShowError(MediaActionMessages.ImageToPdfRequiresAtLeastOneImage());
            return;
        }

        if (_cropModeEnabled)
        {
            SetCropMode(false);
        }

        Settings = new ImageToPdfSettings
        {
            PageFormat = _pageDefinition.Format,
            CustomPageWidthCm = _pageDefinition.Format.Equals("CUSTOM", StringComparison.OrdinalIgnoreCase) ? _pageDefinition.WidthCentimeters : 0,
            CustomPageHeightCm = _pageDefinition.Format.Equals("CUSTOM", StringComparison.OrdinalIgnoreCase) ? _pageDefinition.HeightCentimeters : 0,
            Items = _items.Select(item => new ImageToPdfItemSettings
            {
                SourcePath = item.Settings.SourcePath,
                X = item.Settings.X,
                Y = item.Settings.Y,
                Width = item.Settings.Width,
                Height = item.Settings.Height,
                RotationQuarterTurns = ImageToPdfGeometry.NormalizeQuarterTurns(item.Settings.RotationQuarterTurns),
                RotationAngleDegrees = item.Settings.GetRotationAngleDegrees(),
                Crop = CopyCrop(item.Settings.GetCrop())
            }).ToList()
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    private void CancelAndClose()
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void PreviewPanelOnPaint(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(_previewPanel.BackColor);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        e.Graphics.TranslateTransform(_previewPanel.AutoScrollPosition.X, _previewPanel.AutoScrollPosition.Y);

        var previewPageRect = GetCurrentPreviewPageRect();
        using var shadowBrush = new SolidBrush(Color.FromArgb(32, 0, 0, 0));
        using var pageBrush = new SolidBrush(Color.White);
        using var pagePen = new Pen(Color.FromArgb(182, 188, 194), 1.2f);
        using var activePen = new Pen(Color.FromArgb(34, 118, 227), 2f);
        using var inactivePen = new Pen(Color.FromArgb(130, 135, 140), 1f);
        using var snapGuidePen = new Pen(Color.FromArgb(120, 52, 120, 246), 1f)
        {
            DashStyle = DashStyle.Dash
        };
        using var snapCenterGuidePen = new Pen(Color.FromArgb(200, 210, 40, 40), 1f)
        {
            DashStyle = DashStyle.Dash
        };

        DrawPreviewRulers(e.Graphics, previewPageRect);
        e.Graphics.FillRectangle(shadowBrush, previewPageRect.X + 8f, previewPageRect.Y + 8f, previewPageRect.Width, previewPageRect.Height);
        e.Graphics.FillRectangle(pageBrush, previewPageRect);
        e.Graphics.DrawRectangle(pagePen, previewPageRect.X, previewPageRect.Y, previewPageRect.Width, previewPageRect.Height);

        for (var index = 0; index < _items.Count; index++)
        {
            var item = _items[index];
            var previewRect = ImageToPdfGeometry.ToPreviewRect(item.Settings.ToRectangleF(), previewPageRect);
            var rotationAngle = item.Settings.GetRotationAngleDegrees();
            var crop = item.Settings.GetCrop();
            if (index == GetSelectedIndex() && _cropModeEnabled)
            {
                var fullRect = ImageToPdfGeometry.GetFullRectFromVisibleRectAndCrop(previewRect, crop);
                DrawPreviewItem(e.Graphics, item.Bitmap, fullRect, rotationAngle, null, 0.25f);
                DrawPreviewItem(e.Graphics, item.Bitmap, previewRect, rotationAngle, crop, 1.0f);
                DrawCropOverlay(e.Graphics, fullRect, previewRect, rotationAngle);

                var fullPoints = ImageToPdfGeometry.GetRotatedPreviewPointsForRect(fullRect, rotationAngle);
                e.Graphics.DrawPolygon(pagePen, fullPoints);
                var visiblePoints = ImageToPdfGeometry.GetRotatedPreviewPointsForRect(previewRect, rotationAngle);
                e.Graphics.DrawPolygon(activePen, visiblePoints);

                using var handleBrush = new SolidBrush(Color.White);
                using var handleBorderPen = new Pen(Color.FromArgb(34, 118, 227), 1.2f);
                var handles = ImageToPdfGeometry.GetPreviewCropHandleRects(fullRect, crop, rotationAngle, 10f);
                DrawCropPreviewHandle(e.Graphics, handles.TopLeft, "TopLeft", handleBrush, handleBorderPen);
                DrawCropPreviewHandle(e.Graphics, handles.Top, "Top", handleBrush, handleBorderPen);
                DrawCropPreviewHandle(e.Graphics, handles.TopRight, "TopRight", handleBrush, handleBorderPen);
                DrawCropPreviewHandle(e.Graphics, handles.Right, "Right", handleBrush, handleBorderPen);
                DrawCropPreviewHandle(e.Graphics, handles.BottomRight, "BottomRight", handleBrush, handleBorderPen);
                DrawCropPreviewHandle(e.Graphics, handles.Bottom, "Bottom", handleBrush, handleBorderPen);
                DrawCropPreviewHandle(e.Graphics, handles.BottomLeft, "BottomLeft", handleBrush, handleBorderPen);
                DrawCropPreviewHandle(e.Graphics, handles.Left, "Left", handleBrush, handleBorderPen);
            }
            else
            {
                DrawPreviewItem(e.Graphics, item.Bitmap, previewRect, rotationAngle, crop, 1.0f);
                var borderPen = IsItemSelected(index) ? activePen : inactivePen;
                var previewPoints = ImageToPdfGeometry.GetRotatedPreviewPointsForRect(previewRect, rotationAngle);
                e.Graphics.DrawPolygon(borderPen, previewPoints);

                if (IsItemSelected(index) && !_cropModeEnabled)
                {
                    using var resizeHandleBrush = new SolidBrush(Color.White);
                    using var resizeHandleBorderPen = new Pen(Color.FromArgb(34, 118, 227), 1.2f);
                    var resizeHandles = ImageToPdfGeometry.GetPreviewResizeHandleRects(previewRect, rotationAngle, 10f);
                    foreach (var handleRect in new[] { resizeHandles.TopLeft, resizeHandles.Top, resizeHandles.TopRight, resizeHandles.Right, resizeHandles.BottomRight, resizeHandles.Bottom, resizeHandles.BottomLeft, resizeHandles.Left })
                    {
                        e.Graphics.FillRectangle(resizeHandleBrush, handleRect);
                        e.Graphics.DrawRectangle(resizeHandleBorderPen, handleRect.X, handleRect.Y, handleRect.Width, handleRect.Height);
                    }

                    using var rotationHandlePen = new Pen(Color.FromArgb(34, 118, 227), 1.6f);
                    using var rotationHandleBrush = new SolidBrush(Color.White);
                    var rotationInfo = ImageToPdfGeometry.GetPreviewRotationHandleInfo(previewRect, rotationAngle, 22f, 12f);
                    e.Graphics.DrawLine(rotationHandlePen, rotationInfo.AxisStart, rotationInfo.HandleCenter);
                    e.Graphics.FillEllipse(rotationHandleBrush, rotationInfo.HandleBounds);
                    e.Graphics.DrawEllipse(rotationHandlePen, rotationInfo.HandleBounds);
                }
            }
        }

        if (_snapGuideX is not null)
        {
            var guidePreviewX = previewPageRect.X + (float)(_snapGuideX.Value / _pageDefinition.WidthPoints * previewPageRect.Width);
            e.Graphics.DrawLine(snapGuidePen, guidePreviewX, previewPageRect.Y, guidePreviewX, previewPageRect.Bottom);
        }

        if (_snapGuideY is not null)
        {
            var guidePreviewY = previewPageRect.Y + (float)(_snapGuideY.Value / _pageDefinition.HeightPoints * previewPageRect.Height);
            e.Graphics.DrawLine(snapGuidePen, previewPageRect.X, guidePreviewY, previewPageRect.Right, guidePreviewY);
        }

        if (_snapCenterGuideX is not null)
        {
            var guidePreviewX = previewPageRect.X + (float)(_snapCenterGuideX.Value / _pageDefinition.WidthPoints * previewPageRect.Width);
            e.Graphics.DrawLine(snapCenterGuidePen, guidePreviewX, previewPageRect.Y, guidePreviewX, previewPageRect.Bottom);
        }

        if (_snapCenterGuideY is not null)
        {
            var guidePreviewY = previewPageRect.Y + (float)(_snapCenterGuideY.Value / _pageDefinition.HeightPoints * previewPageRect.Height);
            e.Graphics.DrawLine(snapCenterGuidePen, previewPageRect.X, guidePreviewY, previewPageRect.Right, guidePreviewY);
        }
    }

    private void DrawPreviewRulers(Graphics graphics, RectangleF previewPageRect)
    {
        if (!_rulersCheckBox.Checked)
        {
            return;
        }

        const float rulerThickness = 16f;
        const float rulerGap = 4f;
        const float majorTick = 7f;
        const float minorTick = 4f;

        if (previewPageRect.Width <= 1f || previewPageRect.Height <= 1f)
        {
            return;
        }

        var topRect = new RectangleF(
            previewPageRect.X,
            previewPageRect.Y - rulerThickness - rulerGap,
            previewPageRect.Width,
            rulerThickness);
        var leftRect = new RectangleF(
            previewPageRect.X - rulerThickness - rulerGap,
            previewPageRect.Y,
            rulerThickness,
            previewPageRect.Height);

        if (topRect.Top < 0f || leftRect.Left < 0f)
        {
            return;
        }

        var pixelsPerCentimeter = previewPageRect.Width / (float)_pageDefinition.WidthCentimeters;
        var (majorStepCentimeters, showHalfCentimeters) = GetRulerSpacing(pixelsPerCentimeter);
        var halfStepCentimeters = showHalfCentimeters ? majorStepCentimeters / 2.0 : 0.0;

        using var backgroundBrush = new SolidBrush(Color.FromArgb(244, 248, 252));
        using var borderPen = new Pen(Color.FromArgb(198, 210, 222), 1f);
        using var tickPen = new Pen(Color.FromArgb(156, 170, 184), 1f);
        using var textBrush = new SolidBrush(Color.FromArgb(98, 108, 118));
        using var font = new Font("Segoe UI", 6.5F, FontStyle.Regular, GraphicsUnit.Point);
        using var textFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near
        };
        using var verticalTextFormat = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center
        };

        graphics.FillRectangle(backgroundBrush, topRect);
        graphics.FillRectangle(backgroundBrush, leftRect);
        graphics.DrawRectangle(borderPen, topRect.X, topRect.Y, topRect.Width, topRect.Height);
        graphics.DrawRectangle(borderPen, leftRect.X, leftRect.Y, leftRect.Width, leftRect.Height);

        if (showHalfCentimeters)
        {
            DrawHorizontalRulerTicks(graphics, previewPageRect, topRect, tickPen, halfStepCentimeters, minorTick, false);
            DrawVerticalRulerTicks(graphics, previewPageRect, leftRect, tickPen, halfStepCentimeters, minorTick, false);
        }

        DrawHorizontalRulerTicks(graphics, previewPageRect, topRect, tickPen, majorStepCentimeters, majorTick, true);
        DrawVerticalRulerTicks(graphics, previewPageRect, leftRect, tickPen, majorStepCentimeters, majorTick, true);

        DrawHorizontalRulerLabels(graphics, previewPageRect, topRect, font, textBrush, textFormat, majorStepCentimeters);
        DrawVerticalRulerLabels(graphics, previewPageRect, leftRect, font, textBrush, verticalTextFormat, majorStepCentimeters);
    }

    private static (double MajorStepCentimeters, bool ShowHalfCentimeters) GetRulerSpacing(float pixelsPerCentimeter)
    {
        if (pixelsPerCentimeter >= 24f)
        {
            return (1.0, true);
        }

        if (pixelsPerCentimeter >= 14f)
        {
            return (1.0, false);
        }

        if (pixelsPerCentimeter >= 8f)
        {
            return (2.0, false);
        }

        return (5.0, false);
    }

    private void DrawHorizontalRulerTicks(
        Graphics graphics,
        RectangleF previewPageRect,
        RectangleF rulerRect,
        Pen tickPen,
        double stepCentimeters,
        float tickHeight,
        bool skipZero)
    {
        foreach (var value in EnumerateRulerSteps(_pageDefinition.WidthCentimeters, stepCentimeters, skipZero))
        {
            var x = previewPageRect.X + (float)(value / _pageDefinition.WidthCentimeters * previewPageRect.Width);
            graphics.DrawLine(tickPen, x, rulerRect.Bottom, x, rulerRect.Bottom - tickHeight);
        }
    }

    private void DrawVerticalRulerTicks(
        Graphics graphics,
        RectangleF previewPageRect,
        RectangleF rulerRect,
        Pen tickPen,
        double stepCentimeters,
        float tickWidth,
        bool skipZero)
    {
        foreach (var value in EnumerateRulerSteps(_pageDefinition.HeightCentimeters, stepCentimeters, skipZero))
        {
            var y = previewPageRect.Y + (float)(value / _pageDefinition.HeightCentimeters * previewPageRect.Height);
            graphics.DrawLine(tickPen, rulerRect.Right, y, rulerRect.Right - tickWidth, y);
        }
    }

    private void DrawHorizontalRulerLabels(
        Graphics graphics,
        RectangleF previewPageRect,
        RectangleF rulerRect,
        Font font,
        Brush textBrush,
        StringFormat textFormat,
        double stepCentimeters)
    {
        foreach (var value in EnumerateRulerSteps(_pageDefinition.WidthCentimeters, stepCentimeters, false))
        {
            var x = previewPageRect.X + (float)(value / _pageDefinition.WidthCentimeters * previewPageRect.Width);
            var labelRect = new RectangleF(x - 14f, rulerRect.Top + 1f, 28f, rulerRect.Height - 2f);
            graphics.DrawString(((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture), font, textBrush, labelRect, textFormat);
        }
    }

    private void DrawVerticalRulerLabels(
        Graphics graphics,
        RectangleF previewPageRect,
        RectangleF rulerRect,
        Font font,
        Brush textBrush,
        StringFormat textFormat,
        double stepCentimeters)
    {
        foreach (var value in EnumerateRulerSteps(_pageDefinition.HeightCentimeters, stepCentimeters, false))
        {
            var y = previewPageRect.Y + (float)(value / _pageDefinition.HeightCentimeters * previewPageRect.Height);
            var labelRect = new RectangleF(rulerRect.Left + 1f, y - 6f, rulerRect.Width - 2f, 12f);
            graphics.DrawString(((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture), font, textBrush, labelRect, textFormat);
        }
    }

    private static IEnumerable<double> EnumerateRulerSteps(double maxCentimeters, double stepCentimeters, bool skipZero)
    {
        if (stepCentimeters <= 0.0)
        {
            yield break;
        }

        var epsilon = stepCentimeters / 10.0;
        var current = skipZero ? stepCentimeters : 0.0;
        while (current <= maxCentimeters + epsilon)
        {
            yield return current;
            current += stepCentimeters;
        }
    }

    private void PreviewPanelOnMouseDown(object? sender, MouseEventArgs e)
    {
        if (!_previewPanel.ContainsFocus)
        {
            _previewPanel.Focus();
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var previewPageRect = GetCurrentPreviewPageRect();
        var mousePoint = new PointF(e.X - _previewPanel.AutoScrollPosition.X, e.Y - _previewPanel.AutoScrollPosition.Y);
        var ctrlPressed = (ModifierKeys & Keys.Control) == Keys.Control;
        var hitIndex = HitTestItem(mousePoint, previewPageRect);
        var activeItem = GetActiveItem();

        if (_cropModeEnabled)
        {
            if (activeItem is null)
            {
                SetCropMode(false);
                return;
            }

            var visibleRect = ImageToPdfGeometry.ToPreviewRect(activeItem.Settings.ToRectangleF(), previewPageRect);
            var crop = activeItem.Settings.GetCrop();
            var fullRect = ImageToPdfGeometry.GetFullRectFromVisibleRectAndCrop(visibleRect, crop);
            var visiblePageRect = ImageToPdfGeometry.ToAbsoluteRect(activeItem.Settings.ToRectangleF(), _pageDefinition);
            var fullPageRect = ImageToPdfGeometry.GetFullRectFromVisibleRectAndCrop(
                new RectangleF((float)visiblePageRect.X, (float)visiblePageRect.Y, (float)visiblePageRect.Width, (float)visiblePageRect.Height),
                crop);
            var rotationAngle = activeItem.Settings.GetRotationAngleDegrees();
            var cropHandle = ImageToPdfGeometry.GetPreviewCropHandleHit(fullRect, crop, rotationAngle, mousePoint, 10f);

            if (!string.IsNullOrWhiteSpace(cropHandle))
            {
                _interactionMode = ImageInteractionMode.Crop;
                _interactionStartHistoryState = CaptureEditorHistoryState();
                _activeCropHandle = ParseCropHandle(cropHandle);
                _interactionStartPoint = e.Location;
                _interactionStartRect = activeItem.Settings.ToRectangleF();
                _interactionStartPreviewRect = visibleRect;
                _interactionStartCropFullPreviewRect = fullRect;
                _interactionStartCropFullPageRect = fullPageRect;
                _interactionStartCrop = CopyCrop(crop);
                _interactionStartAngle = rotationAngle;
                _previewPanel.Cursor = GetCropCursor(_activeCropHandle!.Value);
                return;
            }

            if (!ImageToPdfGeometry.TestPreviewPointInRotatedRect(fullRect, rotationAngle, mousePoint))
            {
                SetCropMode(false);
                return;
            }

            return;
        }

        if (ctrlPressed)
        {
            if (hitIndex >= 0 && IsItemSelected(hitIndex))
            {
                _isDragCopyMode = true;
                _copyDragActivated = false;
                _copyDragHitIndex = hitIndex;
                _interactionMode = ImageInteractionMode.Drag;
                _interactionStartHistoryState = CaptureEditorHistoryState();
                _interactionStartPoint = e.Location;
                CaptureInteractionSelectionState();
                var activeForCopy = GetActiveItem();
                if (activeForCopy is not null)
                {
                    _interactionStartRect = activeForCopy.Settings.ToRectangleF();
                    _interactionStartPreviewRect = ImageToPdfGeometry.ToPreviewRect(_interactionStartRect, previewPageRect);
                }
                _previewPanel.Cursor = Cursors.SizeAll;
                return;
            }

            if (hitIndex >= 0)
            {
                ToggleSelectionAtIndex(hitIndex);
            }

            return;
        }

        if (_selectedIndices.Count == 1 && activeItem is not null)
        {
            var activeRectBeforeHitTest = ImageToPdfGeometry.ToPreviewRect(activeItem.Settings.ToRectangleF(), previewPageRect);
            var activeRotationAngleBeforeHitTest = activeItem.Settings.GetRotationAngleDegrees();

            if (TryGetSelectedResizeHandleAtPoint(mousePoint, previewPageRect, out var preselectedResizeItemIndex, out var preselectedResizeHandle))
            {
                _interactionMode = ImageInteractionMode.Resize;
                _interactionStartHistoryState = CaptureEditorHistoryState();
                _activeResizeSourceIndex = preselectedResizeItemIndex;
                _activeResizeHandle = preselectedResizeHandle;
                _interactionStartPoint = e.Location;
                _interactionStartRect = activeItem.Settings.ToRectangleF();
                _interactionStartPreviewRect = activeRectBeforeHitTest;
                _interactionStartAngle = activeRotationAngleBeforeHitTest;
                _previewPanel.Cursor = GetResizeCursor(_activeResizeHandle.Value);
                return;
            }

            if (ImageToPdfGeometry.GetPreviewRotationHandleHit(activeRectBeforeHitTest, activeRotationAngleBeforeHitTest, mousePoint, 12f))
            {
                _interactionMode = ImageInteractionMode.Rotate;
                _interactionStartHistoryState = CaptureEditorHistoryState();
                _activeRotateSourceIndex = GetSelectedIndex();
                _interactionStartPoint = e.Location;
                _interactionStartRect = activeItem.Settings.ToRectangleF();
                _interactionStartPreviewRect = activeRectBeforeHitTest;
                _interactionStartAngle = activeRotationAngleBeforeHitTest;
                _previewPanel.Cursor = Cursors.Hand;
                return;
            }
        }

        if (_selectedIndices.Count > 1)
        {
            if (hitIndex >= 0 && !IsItemSelected(hitIndex))
            {
                SelectImage(hitIndex);
                hitIndex = GetSelectedIndex();
            }
            else
            {
                CaptureInteractionSelectionState();
                var selectionPreviewBounds = ImageToPdfGeometry.ToPreviewRect(_interactionStartSelectionBounds, previewPageRect);

                if (TryGetSelectedResizeHandleAtPoint(mousePoint, previewPageRect, out var selectedResizeItemIndex, out var selectedResizeHandle))
                {
                    _interactionMode = ImageInteractionMode.Resize;
                    _interactionStartHistoryState = CaptureEditorHistoryState();
                    _activeResizeSourceIndex = selectedResizeItemIndex;
                    _activeResizeHandle = selectedResizeHandle;
                    _interactionStartPoint = e.Location;
                    _interactionStartRect = _interactionStartSelectionBounds;
                    _interactionStartPreviewRect = selectionPreviewBounds;
                    _interactionStartAngle = 0.0;
                    _previewPanel.Cursor = GetResizeCursor(_activeResizeHandle.Value);
                    return;
                }

                if (TryGetSelectedRotationHandleAtPoint(mousePoint, previewPageRect, out var selectedRotateItemIndex))
                {
                    var selectedRotateItem = _items[selectedRotateItemIndex];
                    _interactionMode = ImageInteractionMode.Rotate;
                    _interactionStartHistoryState = CaptureEditorHistoryState();
                    _activeRotateSourceIndex = selectedRotateItemIndex;
                    _interactionStartPoint = e.Location;
                    _interactionStartAngle = selectedRotateItem.Settings.GetRotationAngleDegrees();
                    _previewPanel.Cursor = Cursors.Hand;
                    return;
                }

                if (TryGetSelectedItemAtPoint(mousePoint, previewPageRect, out var selectedHitIndex))
                {
                    _interactionMode = ImageInteractionMode.Drag;
                    _interactionStartHistoryState = CaptureEditorHistoryState();
                    _interactionStartPoint = e.Location;
                    _previewPanel.Cursor = Cursors.SizeAll;
                    return;
                }

                if (previewPageRect.Contains(mousePoint))
                {
                    _interactionMode = ImageInteractionMode.PanView;
                    _interactionStartPoint = e.Location;
                    _interactionStartScrollOffset = GetPreviewScrollOffset();
                    _panViewMoved = false;
                    _previewPanel.Cursor = Cursors.Hand;
                    return;
                }

                ClearSelection();
                return;
            }
        }

        if (hitIndex < 0)
        {
            if (previewPageRect.Contains(mousePoint))
            {
                _interactionMode = ImageInteractionMode.PanView;
                _interactionStartPoint = e.Location;
                _interactionStartScrollOffset = GetPreviewScrollOffset();
                _panViewMoved = false;
                _previewPanel.Cursor = Cursors.Hand;
            }
            else
            {
                ClearSelection();
            }

            return;
        }

        if (!IsItemSelected(hitIndex))
        {
            SelectImage(hitIndex);
            activeItem = GetActiveItem();
        }

        activeItem = GetActiveItem();
        if (activeItem is null)
        {
            return;
        }

        var activeRectAfterSelection = ImageToPdfGeometry.ToPreviewRect(activeItem.Settings.ToRectangleF(), previewPageRect);
        var rotationAngleAfterSelection = activeItem.Settings.GetRotationAngleDegrees();

        if (TryGetSelectedResizeHandleAtPoint(mousePoint, previewPageRect, out var singleResizeItemIndex, out var singleResizeHandle))
        {
            _interactionMode = ImageInteractionMode.Resize;
            _interactionStartHistoryState = CaptureEditorHistoryState();
            _activeResizeSourceIndex = singleResizeItemIndex;
            _activeResizeHandle = singleResizeHandle;
            _interactionStartPoint = e.Location;
            _interactionStartRect = activeItem.Settings.ToRectangleF();
            _interactionStartPreviewRect = activeRectAfterSelection;
            _interactionStartAngle = rotationAngleAfterSelection;
            _previewPanel.Cursor = GetResizeCursor(_activeResizeHandle.Value);
            return;
        }

        if (ImageToPdfGeometry.GetPreviewRotationHandleHit(activeRectAfterSelection, rotationAngleAfterSelection, mousePoint, 12f))
        {
            _interactionMode = ImageInteractionMode.Rotate;
            _interactionStartHistoryState = CaptureEditorHistoryState();
            _activeRotateSourceIndex = GetSelectedIndex();
            _interactionStartPoint = e.Location;
            _interactionStartRect = activeItem.Settings.ToRectangleF();
            _interactionStartPreviewRect = activeRectAfterSelection;
            _interactionStartAngle = rotationAngleAfterSelection;
            _previewPanel.Cursor = Cursors.Hand;
            return;
        }

        if (ImageToPdfGeometry.TestPreviewPointInRotatedRect(activeRectAfterSelection, rotationAngleAfterSelection, mousePoint))
        {
            _interactionMode = ImageInteractionMode.Drag;
            _interactionStartHistoryState = CaptureEditorHistoryState();
            _interactionStartPoint = e.Location;
            _interactionStartRect = activeItem.Settings.ToRectangleF();
            _interactionStartPreviewRect = activeRectAfterSelection;
            _interactionStartAngle = rotationAngleAfterSelection;
            _previewPanel.Cursor = Cursors.SizeAll;
            return;
        }

        _interactionMode = ImageInteractionMode.None;
        _activeResizeHandle = null;
    }

    private void PreviewPanelOnMouseMove(object? sender, MouseEventArgs e)
    {
        var previewPageRect = GetCurrentPreviewPageRect();
        if (_interactionMode == ImageInteractionMode.Crop)
        {
            var currentActiveItem = GetActiveItem();
            if (currentActiveItem is null || _activeCropHandle is null)
            {
                return;
            }

            var currentMousePoint = new PointF(e.X - _previewPanel.AutoScrollPosition.X, e.Y - _previewPanel.AutoScrollPosition.Y);
            var currentPagePoint = ImageToPdfGeometry.ToAbsolutePoint(currentMousePoint, previewPageRect, _pageDefinition);
            var cropUpdate = ImageToPdfGeometry.UpdateCropFromPreviewHandle(
                _interactionStartCrop,
                _activeCropHandle.Value.ToString(),
                _interactionStartCropFullPageRect,
                _interactionStartAngle,
                currentPagePoint);

            currentActiveItem.Settings.Crop = CopyCrop(cropUpdate.Crop);
            ApplyRect(currentActiveItem.Settings, ImageToPdfGeometry.ToNormalizedRect(cropUpdate.VisibleRect, _pageDefinition));
            ClearSnapGuides();
            _previewPanel.Invalidate();
            return;
        }

        if (_interactionMode == ImageInteractionMode.Drag)
        {
            if (_isDragCopyMode && !_copyDragActivated)
            {
                if (Math.Abs(e.X - _interactionStartPoint.X) < 3 &&
                    Math.Abs(e.Y - _interactionStartPoint.Y) < 3)
                {
                    return;
                }

                CreateCopiesOfSelectedItems();
                _copyDragActivated = true;
                CaptureInteractionSelectionState();
                var newActive = GetActiveItem();
                if (newActive is not null)
                {
                    _interactionStartRect = newActive.Settings.ToRectangleF();
                }
            }

            var dragDeltaX = e.X - _interactionStartPoint.X;
            var dragDeltaY = e.Y - _interactionStartPoint.Y;
            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                if (Math.Abs(dragDeltaX) >= Math.Abs(dragDeltaY))
                {
                    dragDeltaY = 0;
                    _previewPanel.Cursor = Cursors.SizeWE;
                }
                else
                {
                    dragDeltaX = 0;
                    _previewPanel.Cursor = Cursors.SizeNS;
                }
            }
            else
            {
                _previewPanel.Cursor = Cursors.SizeAll;
            }

            if (_selectedIndices.Count > 1 && _interactionStartSelectionItems.Count > 0)
            {
                MoveSelectionByDelta(dragDeltaX, dragDeltaY, previewPageRect);
            }
            else
            {
                var currentActiveItem = GetActiveItem();
                if (currentActiveItem is null)
                {
                    return;
                }

                var rect = ImageToPdfGeometry.MoveNormalizedRect(
                    _interactionStartRect,
                    dragDeltaX,
                    dragDeltaY,
                    previewPageRect);
                rect = ApplySnapIfEnabled(rect, previewPageRect);
                ApplyRect(currentActiveItem.Settings, rect);
            }
            UpdateResizeFieldsFromSelection();
            _previewPanel.Invalidate();
            return;
        }

        if (_interactionMode == ImageInteractionMode.PanView)
        {
            var deltaX = e.X - _interactionStartPoint.X;
            var deltaY = e.Y - _interactionStartPoint.Y;
            if (!_panViewMoved && (Math.Abs(deltaX) > 2 || Math.Abs(deltaY) > 2))
            {
                _panViewMoved = true;
            }

            SetPreviewScrollOffset(new Point(
                _interactionStartScrollOffset.X - deltaX,
                _interactionStartScrollOffset.Y - deltaY));
            return;
        }

        if (_interactionMode == ImageInteractionMode.Resize)
        {
            if (_activeResizeHandle is null)
            {
                return;
            }

            var currentMousePoint = new PointF(e.X - _previewPanel.AutoScrollPosition.X, e.Y - _previewPanel.AutoScrollPosition.Y);
            if (_selectedIndices.Count > 1 && _interactionStartSelectionItems.Count > 0)
            {
                ResizeSelectionFromHandle(_activeResizeHandle.Value, currentMousePoint, previewPageRect);
            }
            else
            {
                var currentActiveItem = GetActiveItem();
                if (currentActiveItem is null)
                {
                    return;
                }

                var resizeRect = ResizeRectFromHandle(
                    _interactionStartRect,
                    _activeResizeHandle.Value,
                    currentMousePoint,
                    previewPageRect,
                    _interactionStartAngle);
                resizeRect = ApplyResizeSnapIfEnabled(resizeRect, _activeResizeHandle.Value, previewPageRect);
                ApplyRect(currentActiveItem.Settings, resizeRect);
            }
            UpdateResizeFieldsFromSelection();
            _previewPanel.Invalidate();
            return;
        }

        if (_interactionMode == ImageInteractionMode.Rotate)
        {
            var currentMousePoint = new PointF(e.X - _previewPanel.AutoScrollPosition.X, e.Y - _previewPanel.AutoScrollPosition.Y);
            if (_selectedIndices.Count > 1 && _interactionStartSelectionItems.Count > 0)
            {
                RotateSelectionByMouse(currentMousePoint, previewPageRect);
            }
            else
            {
                var currentActiveItem = GetActiveItem();
                if (currentActiveItem is null)
                {
                    return;
                }

                var rotationAngle = ImageToPdfGeometry.GetRotationAngleFromPreviewPoint(_interactionStartPreviewRect, currentMousePoint);
                if (_snapImagesCheckBox.Checked)
                {
                    rotationAngle = ImageToPdfGeometry.SnapRotationAngle(rotationAngle, 4.0);
                }
                currentActiveItem.Settings.RotationAngleDegrees = rotationAngle;
                currentActiveItem.Settings.RotationQuarterTurns = (int)Math.Round(ImageToPdfGeometry.NormalizeRotationAngle(rotationAngle) / 90.0, MidpointRounding.AwayFromZero) % 4;
            }
            ClearSnapGuides();
            _previewPanel.Cursor = Cursors.Hand;
            _previewPanel.Invalidate();
            return;
        }

        var mousePoint = new PointF(e.X - _previewPanel.AutoScrollPosition.X, e.Y - _previewPanel.AutoScrollPosition.Y);
        var activeItem = GetActiveItem();
        if (activeItem is not null)
        {
            var hasMultiSelection = _selectedIndices.Count > 1;
            var activeRect = ImageToPdfGeometry.ToPreviewRect(activeItem.Settings.ToRectangleF(), previewPageRect);
            var rotationAngle = activeItem.Settings.GetRotationAngleDegrees();

            if (_cropModeEnabled)
            {
                var crop = activeItem.Settings.GetCrop();
                var fullRect = ImageToPdfGeometry.GetFullRectFromVisibleRectAndCrop(activeRect, crop);
                var cropHandle = ImageToPdfGeometry.GetPreviewCropHandleHit(fullRect, crop, rotationAngle, mousePoint, 10f);
                if (!string.IsNullOrWhiteSpace(cropHandle))
                {
                    _previewPanel.Cursor = GetCropCursor(ParseCropHandle(cropHandle));
                    return;
                }

                _previewPanel.Cursor = ImageToPdfGeometry.TestPreviewPointInRotatedRect(fullRect, rotationAngle, mousePoint)
                    ? Cursors.Default
                    : Cursors.Default;
                return;
            }

            if (hasMultiSelection)
            {
                if (TryGetSelectedResizeHandleAtPoint(mousePoint, previewPageRect, out _, out var multiResizeHandle))
                {
                    _previewPanel.Cursor = GetResizeCursor(multiResizeHandle);
                    return;
                }

                foreach (var selectedIndex in _selectedIndices)
                {
                    if (selectedIndex < 0 || selectedIndex >= _items.Count)
                    {
                        continue;
                    }

                    var selectedItem = _items[selectedIndex];
                    var selectedRect = ImageToPdfGeometry.ToPreviewRect(selectedItem.Settings.ToRectangleF(), previewPageRect);
                    var selectedAngle = selectedItem.Settings.GetRotationAngleDegrees();
                    if (ImageToPdfGeometry.GetPreviewRotationHandleHit(selectedRect, selectedAngle, mousePoint, 12f))
                    {
                        _previewPanel.Cursor = Cursors.Hand;
                        return;
                    }
                }

                if (TryGetSelectedItemAtPoint(mousePoint, previewPageRect, out _))
                {
                    _previewPanel.Cursor = Cursors.SizeAll;
                    return;
                }
            }

            if (ImageToPdfGeometry.GetPreviewRotationHandleHit(activeRect, rotationAngle, mousePoint, 12f))
            {
                _previewPanel.Cursor = Cursors.Hand;
                return;
            }

            if (!hasMultiSelection)
            {
                var activeResizeHandle = ImageToPdfGeometry.GetPreviewResizeHandleHit(activeRect, rotationAngle, mousePoint, 10f);
                if (!string.IsNullOrWhiteSpace(activeResizeHandle))
                {
                    _previewPanel.Cursor = GetResizeCursor(ParseResizeHandle(activeResizeHandle));
                    return;
                }

                if (ImageToPdfGeometry.TestPreviewPointInRotatedRect(activeRect, rotationAngle, mousePoint))
                {
                    _previewPanel.Cursor = Cursors.SizeAll;
                    return;
                }
            }

            var selectedHitIndex = HitTestItem(mousePoint, previewPageRect);
            if (selectedHitIndex >= 0 && IsItemSelected(selectedHitIndex))
            {
                _previewPanel.Cursor = Cursors.SizeAll;
                return;
            }
        }

        var hoveredItemIndex = HitTestItem(mousePoint, previewPageRect);
        if (hoveredItemIndex >= 0)
        {
            _previewPanel.Cursor = Cursors.Hand;
            return;
        }

        _previewPanel.Cursor = previewPageRect.Contains(mousePoint)
            ? Cursors.Hand
            : Cursors.Default;
    }

    private void PreviewPanelOnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var shouldClearSelection = _interactionMode == ImageInteractionMode.PanView && !_panViewMoved;
        var shouldRefreshFields = _interactionMode == ImageInteractionMode.Crop ||
                                 _interactionMode == ImageInteractionMode.Drag ||
                                 _interactionMode == ImageInteractionMode.Resize ||
                                 _interactionMode == ImageInteractionMode.Rotate;
        CommitInteractionHistoryIfChanged();
        if (shouldClearSelection)
        {
            ClearSelection();
        }

        if (_isDragCopyMode && !_copyDragActivated && _copyDragHitIndex >= 0)
        {
            ToggleSelectionAtIndex(_copyDragHitIndex);
        }

        _interactionMode = ImageInteractionMode.None;
        _activeResizeHandle = null;
        _activeResizeSourceIndex = -1;
        _activeRotateSourceIndex = -1;
        _activeCropHandle = null;
        _interactionStartHistoryState = null;
        _panViewMoved = false;
        _isDragCopyMode = false;
        _copyDragActivated = false;
        _copyDragHitIndex = -1;
        ClearSnapGuides();
        if (shouldRefreshFields)
        {
            UpdateResizeFieldsFromSelection();
        }
        _previewPanel.Cursor = Cursors.Default;
        _previewPanel.Invalidate();
    }

    private void PreviewPanelOnMouseWheel(object? sender, MouseEventArgs e)
    {
        if (_items.Count == 0 ||
            _interactionMode != ImageInteractionMode.None ||
            e.Delta == 0)
        {
            return;
        }

        var zoomFactor = (float)Math.Pow(PreviewZoomStep, e.Delta / 120.0);
        ZoomPreviewAtPoint(_previewScale * zoomFactor, e.Location);
    }

    private void PreviewPanelOnDragEnter(object? sender, DragEventArgs e)
    {
        UpdatePreviewDropEffect(e);
    }

    private void PreviewPanelOnDragOver(object? sender, DragEventArgs e)
    {
        UpdatePreviewDropEffect(e);
    }

    private void PreviewPanelOnDragDrop(object? sender, DragEventArgs e)
    {
        if (!TryGetDroppedImagePaths(e.Data, out var filePaths) || filePaths.Length == 0)
        {
            return;
        }

        AddImagesFromPaths(filePaths);
    }

    private int HitTestItem(PointF point, RectangleF previewPageRect)
    {
        for (var index = _items.Count - 1; index >= 0; index--)
        {
            var previewRect = ImageToPdfGeometry.ToPreviewRect(_items[index].Settings.ToRectangleF(), previewPageRect);
            var angle = _items[index].Settings.GetRotationAngleDegrees();
            if (ImageToPdfGeometry.TestPreviewPointInRotatedRect(previewRect, angle, point))
            {
                return index;
            }
        }

        return -1;
    }

    private void UpdatePreviewDropEffect(DragEventArgs e)
    {
        if (e.Data is null ||
            !TryGetDroppedImagePaths(e.Data, out var filePaths) ||
            filePaths.Length == 0)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        e.Effect = filePaths.All(IsSupportedPath) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private static bool TryGetDroppedImagePaths(IDataObject? data, out string[] filePaths)
    {
        filePaths = [];
        if (data is null || !data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        if (data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return false;
        }

        filePaths = paths;
        return true;
    }

    private static bool IsSupportedPath(string path)
    {
        return IsSupportedExtension(Path.GetExtension(path).ToLowerInvariant());
    }

    private void UpdateSelectionState(int index)
    {
        var selectionCount = _selectedIndices.Count;
        var hasSelection = selectionCount > 0;
        var hasSingleSelection = selectionCount == 1 && index >= 0 && index < _items.Count;
        _buttonDeleteImage.Enabled = hasSelection;
        _buttonCrop.Enabled = hasSingleSelection;
        _buttonMoveBackward.Enabled = hasSingleSelection && index > 0;
        _buttonMoveForward.Enabled = hasSingleSelection && index < _items.Count - 1;
        _buttonSendToBack.Enabled = hasSingleSelection && index > 0;
        _buttonBringToFront.Enabled = hasSingleSelection && index < _items.Count - 1;
        _buttonFitPage.Enabled = hasSingleSelection;
        _buttonCenter.Enabled = hasSingleSelection;
        _buttonClearSelection.Enabled = _items.Count > 0;
        var hasItems = _items.Count > 0;
        _buttonPrint.Enabled = hasItems;
        _buttonExport.Enabled = hasItems;
        _buttonZoomFit.Enabled = hasItems;
        _buttonZoomIn.Enabled = hasItems;
        _buttonZoomOut.Enabled = hasItems;
        _heightUpDown.Enabled = hasSingleSelection;
        _lockRatioCheckBox.Enabled = hasSingleSelection;
        _snapImagesCheckBox.Enabled = hasSelection;

        _selectionLabel.Text = hasSelection
            ? (selectionCount == 1
                ? $"Active image: {index + 1}/{_items.Count}"
                : $"Active images: {selectionCount} selected")
            : "Active image: none";

        if (!hasSingleSelection)
        {
            SetCropMode(false);
        }

        UpdateResizeFieldsFromSelection();
        UpdateCropModeVisualState();
        _previewPanel.Invalidate();
    }

    private void InitializePageControls()
    {
        _isUpdatingPageControls = true;
        try
        {
            SelectPageFormatOption(ImageToPdfSettings.DefaultPageFormat);
            UpdatePageControlsFromDefinition(_pageDefinition);
            UpdatePreviewCanvasLayout();
        }
        finally
        {
            _isUpdatingPageControls = false;
        }
    }

    private void ApplyPageFormatSelection()
    {
        if (_isUpdatingPageControls)
        {
            return;
        }

        var historyBefore = CaptureEditorHistoryState();
        UpdatePageDefinitionFromControls();
        CommitHistoryIfChanged(historyBefore);
        UpdateResizeFieldsFromSelection();
        FitPreviewToView();
        _previewPanel.Invalidate();
    }

    private void ApplyCustomPageSizeFromFields()
    {
        if (_isUpdatingPageControls)
        {
            return;
        }

        var historyBefore = CaptureEditorHistoryState();
        UpdatePageDefinitionFromControls();
        CommitHistoryIfChanged(historyBefore);
        UpdateResizeFieldsFromSelection();
        FitPreviewToView();
        _previewPanel.Invalidate();
    }

    private void UpdatePageDefinitionFromControls()
    {
        var selectedFormat = GetSelectedPageFormat();
        var customWidth = (double)_customPageWidthUpDown.Value;
        var customHeight = (double)_customPageHeightUpDown.Value;
        var nextPageDefinition = ImageToPdfGeometry.GetPageDefinition(selectedFormat, customWidth, customHeight);
        if (!_pageDefinition.Equals(nextPageDefinition))
        {
            var previousPageDefinition = _pageDefinition;
            _pageDefinition = nextPageDefinition;
            RemapItemsToCurrentPage(previousPageDefinition, nextPageDefinition);
        }
        else
        {
            _pageDefinition = nextPageDefinition;
        }
        UpdatePageControlsFromDefinition(_pageDefinition);
    }

    private void UpdatePageControlsFromDefinition(ImageToPdfGeometry.PageDefinition pageDefinition)
    {
        _isUpdatingPageControls = true;
        try
        {
            SelectPageFormatOption(pageDefinition.Format);
            var isCustom = ImageToPdfGeometry.IsCustomPageFormat(pageDefinition.Format);
            _customPageWidthUpDown.Enabled = isCustom;
            _customPageHeightUpDown.Enabled = isCustom;
            _customPageWidthUpDown.Value = ClampToNumeric(pageDefinition.WidthCentimeters, _customPageWidthUpDown);
            _customPageHeightUpDown.Value = ClampToNumeric(pageDefinition.HeightCentimeters, _customPageHeightUpDown);
        }
        finally
        {
            _isUpdatingPageControls = false;
        }
    }

    private string GetSelectedPageFormat()
    {
        if (_pageFormatComboBox.SelectedItem is PageFormatOption option)
        {
            return option.Value;
        }

        return ImageToPdfSettings.DefaultPageFormat;
    }

    private void SelectPageFormatOption(string? pageFormat)
    {
        var normalized = ImageToPdfSettings.NormalizePageFormat(pageFormat);
        for (var index = 0; index < _pageFormatComboBox.Items.Count; index++)
        {
            if (_pageFormatComboBox.Items[index] is PageFormatOption option &&
                string.Equals(option.Value, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _pageFormatComboBox.SelectedIndex = index;
                return;
            }
        }

        _pageFormatComboBox.SelectedIndex = 0;
    }

    private void FitPreviewToView()
    {
        _previewScale = ImageToPdfGeometry.CalculateFitPreviewScale(_previewPanel.ClientSize, _pageDefinition);
        UpdatePreviewCanvasLayout();
        _previewPanel.Invalidate();
    }

    private void SetPreviewZoom(float scale)
    {
        if (_items.Count == 0)
        {
            return;
        }

        var anchor = new Point(_previewPanel.ClientSize.Width / 2, _previewPanel.ClientSize.Height / 2);
        ZoomPreviewAtPoint(scale, anchor);
    }

    private void UpdatePreviewCanvasLayout()
    {
        if (_previewScale <= 0.01f)
        {
            _previewScale = 1f;
        }

        _previewPanel.AutoScrollMinSize = ImageToPdfGeometry.GetPreviewCanvasSize(_previewPanel.ClientSize, _pageDefinition, _previewScale);
    }

    private RectangleF GetCurrentPreviewPageRect()
    {
        var canvasSize = ImageToPdfGeometry.GetPreviewCanvasSize(_previewPanel.ClientSize, _pageDefinition, _previewScale);
        return ImageToPdfGeometry.GetPreviewPageRect(canvasSize, _pageDefinition, _previewScale);
    }

    private void ZoomPreviewAtPoint(float scale, Point clientAnchor)
    {
        if (_items.Count == 0)
        {
            return;
        }

        var clampedScale = Math.Clamp(scale, MinimumPreviewScale, MaximumPreviewScale);
        if (Math.Abs(clampedScale - _previewScale) < 0.0001f)
        {
            return;
        }

        var previousScale = _previewScale;
        var previousPageRect = GetCurrentPreviewPageRect();
        var previousScroll = GetPreviewScrollOffset();
        var anchorCanvasPoint = new PointF(previousScroll.X + clientAnchor.X, previousScroll.Y + clientAnchor.Y);

        _previewPanel.SuspendLayout();
        try
        {
            _previewScale = clampedScale;
            UpdatePreviewCanvasLayout();
        }
        finally
        {
            _previewPanel.ResumeLayout(true);
        }

        var nextPageRect = GetCurrentPreviewPageRect();
        var nextScroll = GetZoomedScrollOffset(clientAnchor, anchorCanvasPoint, previousPageRect, nextPageRect, previousScale, clampedScale);
        SetPreviewScrollOffset(nextScroll);
        _previewPanel.Invalidate();
    }

    private Point GetZoomedScrollOffset(
        Point clientAnchor,
        PointF anchorCanvasPoint,
        RectangleF previousPageRect,
        RectangleF nextPageRect,
        float previousScale,
        float nextScale)
    {
        PointF nextCanvasPoint;
        if (previousPageRect.Contains(anchorCanvasPoint))
        {
            var relativeX = previousPageRect.Width <= 0f
                ? 0f
                : (anchorCanvasPoint.X - previousPageRect.X) / previousPageRect.Width;
            var relativeY = previousPageRect.Height <= 0f
                ? 0f
                : (anchorCanvasPoint.Y - previousPageRect.Y) / previousPageRect.Height;
            nextCanvasPoint = new PointF(
                nextPageRect.X + (relativeX * nextPageRect.Width),
                nextPageRect.Y + (relativeY * nextPageRect.Height));
        }
        else
        {
            var scaleRatio = previousScale <= 0.0001f ? 1f : nextScale / previousScale;
            nextCanvasPoint = new PointF(
                anchorCanvasPoint.X * scaleRatio,
                anchorCanvasPoint.Y * scaleRatio);
        }

        return new Point(
            (int)Math.Round(nextCanvasPoint.X - clientAnchor.X, MidpointRounding.AwayFromZero),
            (int)Math.Round(nextCanvasPoint.Y - clientAnchor.Y, MidpointRounding.AwayFromZero));
    }

    private Point GetPreviewScrollOffset()
    {
        return new Point(
            Math.Max(0, -_previewPanel.AutoScrollPosition.X),
            Math.Max(0, -_previewPanel.AutoScrollPosition.Y));
    }

    private void SetPreviewScrollOffset(Point scrollOffset)
    {
        var maxX = Math.Max(0, _previewPanel.AutoScrollMinSize.Width - _previewPanel.ClientSize.Width);
        var maxY = Math.Max(0, _previewPanel.AutoScrollMinSize.Height - _previewPanel.ClientSize.Height);
        var clampedX = Math.Clamp(scrollOffset.X, 0, maxX);
        var clampedY = Math.Clamp(scrollOffset.Y, 0, maxY);
        _previewPanel.AutoScrollPosition = new Point(clampedX, clampedY);
    }

    private void PrintCurrentLayout()
    {
        if (_items.Count == 0)
        {
            ShowError(MediaActionMessages.ImageToPdfRequiresAtLeastOneImage());
            return;
        }

        try
        {
            using var printDocument = new PrintDocument
            {
                DocumentName = "FrameShift - Image to PDF"
            };
            printDocument.PrintPage += PrintDocumentOnPrintPage;

            using var printDialog = new PrintDialog
            {
                AllowCurrentPage = false,
                AllowSomePages = false,
                UseEXDialog = false,
                Document = printDocument
            };

            if (printDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }
            printDocument.Print();
        }
        catch (Exception ex)
        {
            ShowError(ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.ImageToPdfPrintFailed()));
        }
    }

    private void PrintDocumentOnPrintPage(object? sender, PrintPageEventArgs e)
    {
        var graphics = e.Graphics;
        if (graphics is null)
        {
            e.HasMorePages = false;
            return;
        }

        graphics.Clear(Color.White);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        var marginBounds = e.MarginBounds;
        if (marginBounds.Width <= 0 || marginBounds.Height <= 0)
        {
            marginBounds = e.PageBounds;
        }

        var targetRect = GetCenteredPageRectangle(marginBounds);

        foreach (var item in _items)
        {
            var drawRect = ImageToPdfGeometry.ToPreviewRect(item.Settings.ToRectangleF(), targetRect);
            DrawPreviewItem(graphics, item.Bitmap, drawRect, item.Settings.GetRotationAngleDegrees(), item.Settings.GetCrop(), 1.0f);
        }

        e.HasMorePages = false;
    }

    private RectangleF GetCenteredPageRectangle(Rectangle bounds)
    {
        var boundsRect = RectangleF.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        var pageAspect = (float)(_pageDefinition.WidthPoints / _pageDefinition.HeightPoints);
        var boundsAspect = boundsRect.Width / boundsRect.Height;

        float width;
        float height;
        if (boundsAspect > pageAspect)
        {
            height = boundsRect.Height;
            width = height * pageAspect;
        }
        else
        {
            width = boundsRect.Width;
            height = width / pageAspect;
        }

        var x = boundsRect.X + ((boundsRect.Width - width) / 2f);
        var y = boundsRect.Y + ((boundsRect.Height - height) / 2f);
        return new RectangleF(x, y, width, height);
    }

    private static decimal ClampToNumeric(double value, NumericUpDown numericUpDown)
    {
        var clamped = Math.Clamp(value, (double)numericUpDown.Minimum, (double)numericUpDown.Maximum);
        return (decimal)Math.Round(clamped, numericUpDown.DecimalPlaces, MidpointRounding.AwayFromZero);
    }

    private void UpdateResizeFieldsFromSelection()
    {
        var activeItem = GetActiveItem();
        _isUpdatingSizeFields = true;
        try
        {
            if (activeItem is null)
            {
                return;
            }

            var rect = activeItem.Settings.ToRectangleF();
            _heightUpDown.Value = NormalizePercentage(rect.Height * 100f);
        }
        finally
        {
            _isUpdatingSizeFields = false;
        }
    }

    private void ApplyResizeFromFields(bool heightChanged = false)
    {
        if (_isUpdatingSizeFields)
        {
            return;
        }

        if (_selectedIndices.Count != 1)
        {
            return;
        }

        var activeItem = GetActiveItem();
        if (activeItem is null)
        {
            return;
        }

        var rect = activeItem.Settings.ToRectangleF();
        var previousHeight = rect.Height;
        var newHeight = (float)_heightUpDown.Value / 100f;
        newHeight = Math.Clamp(newHeight, 0.1f, 1f);
        var newWidth = rect.Width;

        if (_lockRatioCheckBox.Checked)
        {
            var ratio = previousHeight > 0.001f ? rect.Width / previousHeight : 1f;
            newWidth = newHeight * ratio;
        }

        newWidth = Math.Clamp(newWidth, 0.1f, 1f);

        var centerX = rect.X + (rect.Width / 2f);
        var centerY = rect.Y + (rect.Height / 2f);
        rect.Width = newWidth;
        rect.Height = newHeight;
        rect.X = centerX - (rect.Width / 2f);
        rect.Y = centerY - (rect.Height / 2f);
        rect = ImageToPdfGeometry.ClampNormalizedRect(rect);
        ClearSnapGuides();

        var historyBefore = CaptureEditorHistoryState();
        ApplyRect(activeItem.Settings, rect);
        CommitHistoryIfChanged(historyBefore);
        UpdateResizeFieldsFromSelection();
        _previewPanel.Invalidate();
    }

    private void RemapItemsToCurrentPage(ImageToPdfGeometry.PageDefinition sourcePage, ImageToPdfGeometry.PageDefinition targetPage)
    {
        if (_items.Count == 0)
        {
            return;
        }

        for (var index = 0; index < _items.Count; index++)
        {
            var item = _items[index];
            var mapped = ImageToPdfGeometry.RemapRectToPage(item.Settings.ToRectangleF(), sourcePage, targetPage);
            ApplyRect(item.Settings, mapped);
        }

        UpdateResizeFieldsFromSelection();
        _previewPanel.Invalidate();
    }

    private void ResizeFieldOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        ApplyResizeFromFields(heightChanged: sender == _heightUpDown);

        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private int GetSelectedIndex()
    {
        return _selectedIndex >= 0 && _selectedIndex < _items.Count
            ? _selectedIndex
            : -1;
    }

    private ImageCanvasItem? GetActiveItem()
    {
        var index = GetSelectedIndex();
        if (index < 0 || index >= _items.Count)
        {
            return null;
        }

        return _items[index];
    }

    private void RefreshImageList(int? selectedIndex)
    {
        if (selectedIndex is not null && selectedIndex.Value >= 0 && selectedIndex.Value < _items.Count)
        {
            SetSelectedIndexes([selectedIndex.Value], selectedIndex.Value, updateUi: false);
            return;
        }

        if (_items.Count == 0)
        {
            SetSelectedIndexes(Array.Empty<int>(), null, updateUi: false);
            return;
        }

        if (_selectedIndex >= _items.Count)
        {
            SetSelectedIndexes([_items.Count - 1], _items.Count - 1, updateUi: false);
        }
    }

    private void SelectImage(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        SetSelectedIndexes([index], index);
    }

    private void DeselectAllItems()
    {
        SetSelectedIndexes(Array.Empty<int>(), null, updateUi: false);
    }

    private static void ApplyRect(ImageToPdfItemSettings settings, RectangleF rect)
    {
        settings.X = rect.X;
        settings.Y = rect.Y;
        settings.Width = rect.Width;
        settings.Height = rect.Height;
    }

    private bool TryNudgeSelectedImage(Keys keyData)
    {
        if (_cropModeEnabled ||
            _interactionMode != ImageInteractionMode.None ||
            !_previewPanel.ContainsFocus)
        {
            return false;
        }

        var activeItem = GetActiveItem();
        if (activeItem is null)
        {
            return false;
        }

        var previewPageRect = GetCurrentPreviewPageRect();
        if (previewPageRect.Width <= 1f || previewPageRect.Height <= 1f)
        {
            return false;
        }

        var deltaX = 0f;
        var deltaY = 0f;
        switch (keyData)
        {
            case Keys.Left:
                deltaX = -1f / previewPageRect.Width;
                break;
            case Keys.Right:
                deltaX = 1f / previewPageRect.Width;
                break;
            case Keys.Up:
                deltaY = -1f / previewPageRect.Height;
                break;
            case Keys.Down:
                deltaY = 1f / previewPageRect.Height;
                break;
            default:
                return false;
        }

        var historyBefore = CaptureEditorHistoryState();
        var rect = activeItem.Settings.ToRectangleF();
        rect = new RectangleF(rect.X + deltaX, rect.Y + deltaY, rect.Width, rect.Height);
        rect = ImageToPdfGeometry.ClampNormalizedRect(rect);
        rect = ApplySnapIfEnabled(rect, previewPageRect);
        ApplyRect(activeItem.Settings, rect);
        CommitHistoryIfChanged(historyBefore);
        UpdateResizeFieldsFromSelection();
        _previewPanel.Invalidate();
        return true;
    }

    private void UndoLastAction()
    {
        if (_interactionMode != ImageInteractionMode.None || _undoHistory.Count == 0)
        {
            return;
        }

        var currentState = CaptureEditorHistoryState();
        var previousState = _undoHistory.Pop();
        _redoHistory.Push(currentState);
        RestoreEditorHistoryState(previousState);
    }

    private void RedoLastAction()
    {
        if (_interactionMode != ImageInteractionMode.None || _redoHistory.Count == 0)
        {
            return;
        }

        var currentState = CaptureEditorHistoryState();
        var nextState = _redoHistory.Pop();
        _undoHistory.Push(currentState);
        RestoreEditorHistoryState(nextState);
    }

    private void CommitInteractionHistoryIfChanged()
    {
        if (_interactionStartHistoryState is null)
        {
            return;
        }

        CommitHistoryIfChanged(_interactionStartHistoryState);
    }

    private void CommitHistoryIfChanged(EditorHistoryState beforeState)
    {
        if (_isRestoringHistory)
        {
            return;
        }

        var currentState = CaptureEditorHistoryState();
        if (EditorHistoryState.Equals(beforeState, currentState))
        {
            return;
        }

        _undoHistory.Push(beforeState);
        TrimHistoryStack(_undoHistory);
        _redoHistory.Clear();
    }

    private EditorHistoryState CaptureEditorHistoryState()
    {
        return new EditorHistoryState(
            _pageDefinition.Format,
            _pageDefinition.WidthCentimeters,
            _pageDefinition.HeightCentimeters,
            Math.Clamp(GetSelectedIndex(), -1, _items.Count - 1),
            _selectedIndices.ToList(),
            _cropModeEnabled,
            _items.Select(item => EditorHistoryItemState.FromSettings(item.Settings)).ToList());
    }

    private void RestoreEditorHistoryState(EditorHistoryState state)
    {
        _isRestoringHistory = true;
        try
        {
            SetCropMode(false);
            _interactionMode = ImageInteractionMode.None;
            _activeResizeHandle = null;
            _activeResizeSourceIndex = -1;
            _activeRotateSourceIndex = -1;
            _activeCropHandle = null;
            _interactionStartHistoryState = null;
            ClearSnapGuides();

            _pageDefinition = ImageToPdfGeometry.GetPageDefinition(
                state.PageFormat,
                state.CustomPageWidthCentimeters,
                state.CustomPageHeightCentimeters);
            UpdatePageControlsFromDefinition(_pageDefinition);

            _items.Clear();
            foreach (var itemState in state.Items)
            {
                if (!TryGetOrLoadBitmap(itemState.SourcePath, out var bitmap, out var errorMessage) || bitmap is null)
                {
                    throw new InvalidOperationException(errorMessage ?? MediaActionMessages.ImageToPdfItemLoadFailed(itemState.SourcePath));
                }

                _items.Add(new ImageCanvasItem(itemState.ToSettings(), bitmap));
            }

            UpdatePreviewCanvasLayout();

            RestoreSelectionFromHistory(state);

            var selectedIndex = GetSelectedIndex();
            if (state.CropModeEnabled && selectedIndex >= 0 && _selectedIndices.Count == 1)
            {
                SetCropMode(true);
            }

            _previewPanel.Invalidate();
        }
        finally
        {
            _isRestoringHistory = false;
        }
    }

    private static void TrimHistoryStack(Stack<EditorHistoryState> history)
    {
        while (history.Count > MaximumHistoryEntries)
        {
            var retained = history.Take(MaximumHistoryEntries).Reverse().ToArray();
            history.Clear();
            foreach (var state in retained)
            {
                history.Push(state);
            }
        }
    }

    private bool TryGetOrLoadBitmap(string path, out Bitmap? bitmap, out string? errorMessage)
    {
        if (_bitmapCache.TryGetValue(path, out bitmap))
        {
            errorMessage = null;
            return true;
        }

        if (!TryLoadBitmapFromPath(path, out bitmap, out errorMessage) || bitmap is null)
        {
            return false;
        }

        _bitmapCache[path] = bitmap;
        return true;
    }

    private bool TryLoadBitmapFromPath(string path, out Bitmap? bitmap, out string? errorMessage)
    {
        bitmap = null;
        errorMessage = null;

        var extension = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (extension == ".webp")
            {
                if (!TryConvertWebpToTemporaryPng(path, out var temporaryPngPath, out errorMessage))
                {
                    return false;
                }

                try
                {
                    using var previewImage = Image.FromFile(temporaryPngPath);
                    if (previewImage.Width <= 0 || previewImage.Height <= 0)
                    {
                        errorMessage = MediaActionMessages.ImageInvalid(path);
                        return false;
                    }

                    if (previewImage.Width > MaximumImageDimension ||
                        previewImage.Height > MaximumImageDimension ||
                        (long)previewImage.Width * previewImage.Height > MaximumImagePixels)
                    {
                        errorMessage = MediaActionMessages.ImageTooLarge(path, previewImage.Width, previewImage.Height);
                        return false;
                    }

                    bitmap = new Bitmap(previewImage);
                    return true;
                }
                finally
                {
                    if (File.Exists(temporaryPngPath))
                    {
                        File.Delete(temporaryPngPath);
                    }
                }
            }

            using var sourceImage = Image.FromFile(path);
            if (sourceImage.Width <= 0 || sourceImage.Height <= 0)
            {
                errorMessage = MediaActionMessages.ImageInvalid(path);
                return false;
            }

            if (sourceImage.Width > MaximumImageDimension ||
                sourceImage.Height > MaximumImageDimension ||
                (long)sourceImage.Width * sourceImage.Height > MaximumImagePixels)
            {
                errorMessage = MediaActionMessages.ImageTooLarge(path, sourceImage.Width, sourceImage.Height);
                return false;
            }

            bitmap = new Bitmap(sourceImage);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            errorMessage = MediaActionMessages.ImageFileInaccessible(path);
            return false;
        }
        catch (IOException)
        {
            errorMessage = MediaActionMessages.ImageFileInaccessible(path);
            return false;
        }
        catch (ArgumentException)
        {
            errorMessage = MediaActionMessages.ImageInvalid(path);
            return false;
        }
        catch (ExternalException)
        {
            errorMessage = MediaActionMessages.ImageInvalid(path);
            return false;
        }
        catch (OutOfMemoryException)
        {
            errorMessage = MediaActionMessages.ImageInvalid(path);
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.ImageLoadFailed(path));
            return false;
        }
    }

    private bool TryConvertWebpToTemporaryPng(string path, out string temporaryPngPath, out string? errorMessage)
    {
        temporaryPngPath = string.Empty;
        errorMessage = null;

        var tempPath = Path.Combine(Path.GetTempPath(), $"frameshift_image_to_pdf_{Guid.NewGuid():N}.png");
        var arguments = new[]
        {
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-i", path,
            "-frames:v", "1",
            tempPath
        };

        var result = _ffmpegRunner.RunAsync(
            _ffmpegPath,
            arguments,
            null,
            null,
            null,
            path,
            "Image to PDF WebP Preview",
            CancellationToken.None).GetAwaiter().GetResult();

        if (result.Canceled || result.ExitCode != 0 || !File.Exists(tempPath))
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            errorMessage = ImageCropSupport.GetFriendlyError(result.StandardError);
            return false;
        }

        temporaryPngPath = tempPath;
        return true;
    }

    private RectangleF ApplySnapIfEnabled(RectangleF rect, RectangleF previewPageRect)
    {
        if (!_snapImagesCheckBox.Checked)
        {
            ClearSnapGuides();
            return rect;
        }

        var activeIndex = GetSelectedIndex();
        if (activeIndex < 0 || activeIndex >= _items.Count)
        {
            ClearSnapGuides();
            return rect;
        }

        var allRects = _items.Select(item => item.Settings.ToRectangleF()).ToList();
        var snapResult = ImageToPdfGeometry.ApplySnapToNormalizedRect(
            rect,
            allRects,
            activeIndex,
            previewPageRect,
            _pageDefinition,
            SnapTolerancePreview);

        _snapGuideX = snapResult.GuideX;
        _snapGuideY = snapResult.GuideY;
        return ApplyPageCenterSnapIfEnabled(snapResult.Rect, previewPageRect);
    }

    private RectangleF ApplyPageCenterSnapIfEnabled(RectangleF rect, RectangleF previewPageRect)
    {
        _snapCenterGuideX = null;
        _snapCenterGuideY = null;

        if (!_snapImagesCheckBox.Checked || previewPageRect.Width <= 0f || previewPageRect.Height <= 0f)
            return rect;

        var toleranceNormX = SnapTolerancePreview / previewPageRect.Width;
        var toleranceNormY = SnapTolerancePreview / previewPageRect.Height;
        var rectCenterX = rect.X + rect.Width / 2f;
        var rectCenterY = rect.Y + rect.Height / 2f;
        const float pageCenterNorm = 0.5f;

        var snappedX = rect.X;
        var snappedY = rect.Y;

        if (Math.Abs(rectCenterX - pageCenterNorm) <= toleranceNormX)
        {
            snappedX = pageCenterNorm - rect.Width / 2f;
            _snapCenterGuideX = _pageDefinition.WidthPoints / 2.0;
        }

        if (Math.Abs(rectCenterY - pageCenterNorm) <= toleranceNormY)
        {
            snappedY = pageCenterNorm - rect.Height / 2f;
            _snapCenterGuideY = _pageDefinition.HeightPoints / 2.0;
        }

        if (_snapCenterGuideX is null && _snapCenterGuideY is null)
            return rect;

        return ImageToPdfGeometry.ClampNormalizedRect(new RectangleF(snappedX, snappedY, rect.Width, rect.Height));
    }

    private RectangleF ApplyResizeSnapIfEnabled(RectangleF rect, ResizeHandle handle, RectangleF previewPageRect)
    {
        if (!_snapImagesCheckBox.Checked)
        {
            ClearSnapGuides();
            return rect;
        }

        if (_lockRatioCheckBox.Checked)
        {
            return ApplyResizeSnapWithRatioIfEnabled(rect, handle, previewPageRect);
        }

        var activeIndex = GetSelectedIndex();
        if (activeIndex < 0 || activeIndex >= _items.Count)
        {
            ClearSnapGuides();
            return rect;
        }

        var candidatePreviewRect = ImageToPdfGeometry.ToPreviewRect(rect, previewPageRect);
        var tolerancePreview = SnapTolerancePreview;
        var minimumPreviewWidth = previewPageRect.Width <= 0f
            ? 1f
            : (12f / _pageDefinition.WidthPoints) * previewPageRect.Width;
        var minimumPreviewHeight = previewPageRect.Height <= 0f
            ? 1f
            : (12f / _pageDefinition.HeightPoints) * previewPageRect.Height;

        var snapLeft = handle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft;
        var snapRight = handle is ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight;
        var snapTop = handle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight;
        var snapBottom = handle is ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight;

        double? bestDeltaX = null;
        double? guideX = null;
        if (snapLeft || snapRight)
        {
            var candidateEdgeX = snapLeft ? candidatePreviewRect.Left : candidatePreviewRect.Right;
            for (var index = 0; index < _items.Count; index++)
            {
                if (index == activeIndex)
                {
                    continue;
                }

                var otherRect = ImageToPdfGeometry.ToPreviewRect(_items[index].Settings.ToRectangleF(), previewPageRect);
                if (otherRect.Width <= 0f || otherRect.Height <= 0f)
                {
                    continue;
                }

                foreach (var otherEdgeX in new[] { (double)otherRect.Left, (double)otherRect.Right })
                {
                    var delta = otherEdgeX - candidateEdgeX;
                    if (Math.Abs(delta) > tolerancePreview)
                    {
                        continue;
                    }

                    if (bestDeltaX is null || Math.Abs(delta) < Math.Abs(bestDeltaX.Value))
                    {
                        bestDeltaX = delta;
                        guideX = otherEdgeX;
                    }
                }
            }
        }

        double? bestDeltaY = null;
        double? guideY = null;
        if (snapTop || snapBottom)
        {
            var candidateEdgeY = snapTop ? candidatePreviewRect.Top : candidatePreviewRect.Bottom;
            for (var index = 0; index < _items.Count; index++)
            {
                if (index == activeIndex)
                {
                    continue;
                }

                var otherRect = ImageToPdfGeometry.ToPreviewRect(_items[index].Settings.ToRectangleF(), previewPageRect);
                if (otherRect.Width <= 0f || otherRect.Height <= 0f)
                {
                    continue;
                }

                foreach (var otherEdgeY in new[] { (double)otherRect.Top, (double)otherRect.Bottom })
                {
                    var delta = otherEdgeY - candidateEdgeY;
                    if (Math.Abs(delta) > tolerancePreview)
                    {
                        continue;
                    }

                    if (bestDeltaY is null || Math.Abs(delta) < Math.Abs(bestDeltaY.Value))
                    {
                        bestDeltaY = delta;
                        guideY = otherEdgeY;
                    }
                }
            }
        }

        if (bestDeltaX is null && bestDeltaY is null)
        {
            ClearSnapGuides();
            return rect;
        }

        double left = candidatePreviewRect.Left;
        double top = candidatePreviewRect.Top;
        double right = candidatePreviewRect.Right;
        double bottom = candidatePreviewRect.Bottom;

        if (bestDeltaX is not null)
        {
            if (snapLeft)
            {
                left += bestDeltaX.Value;
                left = Math.Min(left, right - minimumPreviewWidth);
            }
            else
            {
                right += bestDeltaX.Value;
                right = Math.Max(right, left + minimumPreviewWidth);
            }
        }

        if (bestDeltaY is not null)
        {
            if (snapTop)
            {
                top += bestDeltaY.Value;
                top = Math.Min(top, bottom - minimumPreviewHeight);
            }
            else
            {
                bottom += bestDeltaY.Value;
                bottom = Math.Max(bottom, top + minimumPreviewHeight);
            }
        }

        var snappedPreviewRect = RectangleF.FromLTRB((float)left, (float)top, (float)right, (float)bottom);
        _snapGuideX = guideX is not null
            ? PreviewXToPagePoints((float)guideX.Value, previewPageRect)
            : null;
        _snapGuideY = guideY is not null
            ? PreviewYToPagePoints((float)guideY.Value, previewPageRect)
            : null;
        return ImageToPdfGeometry.ToNormalizedRect(snappedPreviewRect, previewPageRect);
    }

    private RectangleF ApplyResizeSnapWithRatioIfEnabled(RectangleF rect, ResizeHandle handle, RectangleF previewPageRect)
    {
        if (!_snapImagesCheckBox.Checked)
        {
            ClearSnapGuides();
            return rect;
        }

        var activeIndex = GetSelectedIndex();
        if (activeIndex < 0 || activeIndex >= _items.Count)
        {
            ClearSnapGuides();
            return rect;
        }

        var candidatePreviewRect = ImageToPdfGeometry.ToPreviewRect(rect, previewPageRect);
        var tolerancePreview = SnapTolerancePreview;

        var snapLeft = handle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft;
        var snapRight = handle is ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight;
        var snapTop = handle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight;
        var snapBottom = handle is ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight;

        double? bestDeltaX = null;
        double? guideX = null;
        if (snapLeft || snapRight)
        {
            var candidateEdgeX = snapLeft ? candidatePreviewRect.Left : candidatePreviewRect.Right;
            for (var index = 0; index < _items.Count; index++)
            {
                if (index == activeIndex)
                {
                    continue;
                }

                var otherRect = ImageToPdfGeometry.ToPreviewRect(_items[index].Settings.ToRectangleF(), previewPageRect);
                if (otherRect.Width <= 0f || otherRect.Height <= 0f)
                {
                    continue;
                }

                foreach (var otherEdgeX in new[] { (double)otherRect.Left, (double)otherRect.Right })
                {
                    var delta = otherEdgeX - candidateEdgeX;
                    if (Math.Abs(delta) > tolerancePreview)
                    {
                        continue;
                    }

                    if (bestDeltaX is null || Math.Abs(delta) < Math.Abs(bestDeltaX.Value))
                    {
                        bestDeltaX = delta;
                        guideX = otherEdgeX;
                    }
                }
            }
        }

        double? bestDeltaY = null;
        double? guideY = null;
        if (snapTop || snapBottom)
        {
            var candidateEdgeY = snapTop ? candidatePreviewRect.Top : candidatePreviewRect.Bottom;
            for (var index = 0; index < _items.Count; index++)
            {
                if (index == activeIndex)
                {
                    continue;
                }

                var otherRect = ImageToPdfGeometry.ToPreviewRect(_items[index].Settings.ToRectangleF(), previewPageRect);
                if (otherRect.Width <= 0f || otherRect.Height <= 0f)
                {
                    continue;
                }

                foreach (var otherEdgeY in new[] { (double)otherRect.Top, (double)otherRect.Bottom })
                {
                    var delta = otherEdgeY - candidateEdgeY;
                    if (Math.Abs(delta) > tolerancePreview)
                    {
                        continue;
                    }

                    if (bestDeltaY is null || Math.Abs(delta) < Math.Abs(bestDeltaY.Value))
                    {
                        bestDeltaY = delta;
                        guideY = otherEdgeY;
                    }
                }
            }
        }

        if (bestDeltaX is null && bestDeltaY is null)
        {
            ClearSnapGuides();
            return rect;
        }

        var ratio = candidatePreviewRect.Height > 0.0001f
            ? candidatePreviewRect.Width / candidatePreviewRect.Height
            : 1f;
        if (ratio <= 0.0001f)
        {
            ratio = 1f;
        }

        var snappedPreviewRect = candidatePreviewRect;
        var useX = bestDeltaX is not null && (bestDeltaY is null || Math.Abs(bestDeltaX.Value) <= Math.Abs(bestDeltaY.Value));

        if (useX && bestDeltaX is not null)
        {
            if (snapLeft)
            {
                var newLeft = candidatePreviewRect.Left + (float)bestDeltaX.Value;
                var newWidth = candidatePreviewRect.Right - newLeft;
                newWidth = Math.Max(1f, newWidth);
                var newHeight = Math.Max(1f, newWidth / ratio);
                snappedPreviewRect = RectangleF.FromLTRB(
                    newLeft,
                    candidatePreviewRect.Bottom - newHeight,
                    candidatePreviewRect.Right,
                    candidatePreviewRect.Bottom);
            }
            else
            {
                var newRight = candidatePreviewRect.Right + (float)bestDeltaX.Value;
                var newWidth = newRight - candidatePreviewRect.Left;
                newWidth = Math.Max(1f, newWidth);
                var newHeight = Math.Max(1f, newWidth / ratio);
                snappedPreviewRect = RectangleF.FromLTRB(
                    candidatePreviewRect.Left,
                    candidatePreviewRect.Bottom - newHeight,
                    newRight,
                    candidatePreviewRect.Bottom);
            }
        }
        else if (bestDeltaY is not null)
        {
            if (snapTop)
            {
                var newTop = candidatePreviewRect.Top + (float)bestDeltaY.Value;
                var newHeight = candidatePreviewRect.Bottom - newTop;
                newHeight = Math.Max(1f, newHeight);
                var newWidth = Math.Max(1f, newHeight * ratio);
                snappedPreviewRect = RectangleF.FromLTRB(
                    candidatePreviewRect.Right - newWidth,
                    newTop,
                    candidatePreviewRect.Right,
                    candidatePreviewRect.Bottom);
            }
            else
            {
                var newBottom = candidatePreviewRect.Bottom + (float)bestDeltaY.Value;
                var newHeight = newBottom - candidatePreviewRect.Top;
                newHeight = Math.Max(1f, newHeight);
                var newWidth = Math.Max(1f, newHeight * ratio);
                snappedPreviewRect = RectangleF.FromLTRB(
                    candidatePreviewRect.Right - newWidth,
                    candidatePreviewRect.Top,
                    candidatePreviewRect.Right,
                    newBottom);
            }
        }

        snappedPreviewRect = ClampPreviewRectToPage(snappedPreviewRect, previewPageRect);
        _snapGuideX = guideX is not null
            ? PreviewXToPagePoints((float)guideX.Value, previewPageRect)
            : null;
        _snapGuideY = guideY is not null
            ? PreviewYToPagePoints((float)guideY.Value, previewPageRect)
            : null;
        return ImageToPdfGeometry.ToNormalizedRect(snappedPreviewRect, previewPageRect);
    }

    private static RectangleF ClampPreviewRectToPage(RectangleF rect, RectangleF previewPageRect)
    {
        var x = rect.X;
        var y = rect.Y;
        var right = rect.Right;
        var bottom = rect.Bottom;

        if (x < previewPageRect.X)
        {
            var diff = previewPageRect.X - x;
            x += diff;
            right += diff;
        }

        if (y < previewPageRect.Y)
        {
            var diff = previewPageRect.Y - y;
            y += diff;
            bottom += diff;
        }

        if (right > previewPageRect.Right)
        {
            var diff = right - previewPageRect.Right;
            x -= diff;
            right -= diff;
        }

        if (bottom > previewPageRect.Bottom)
        {
            var diff = bottom - previewPageRect.Bottom;
            y -= diff;
            bottom -= diff;
        }

        if (right <= x)
        {
            right = x + 1f;
        }

        if (bottom <= y)
        {
            bottom = y + 1f;
        }

        return RectangleF.FromLTRB(x, y, right, bottom);
    }

    private double PreviewXToPagePoints(float previewX, RectangleF previewPageRect)
    {
        if (previewPageRect.Width <= 0f)
        {
            return 0.0;
        }

        return ((previewX - previewPageRect.X) / previewPageRect.Width) * _pageDefinition.WidthPoints;
    }

    private double PreviewYToPagePoints(float previewY, RectangleF previewPageRect)
    {
        if (previewPageRect.Height <= 0f)
        {
            return 0.0;
        }

        return ((previewY - previewPageRect.Y) / previewPageRect.Height) * _pageDefinition.HeightPoints;
    }

    private void ClearSnapGuides()
    {
        _snapGuideX = null;
        _snapGuideY = null;
        _snapCenterGuideX = null;
        _snapCenterGuideY = null;
    }

    private static void DrawPreviewItem(
        Graphics graphics,
        Bitmap bitmap,
        RectangleF previewRect,
        double rotationAngleDegrees,
        ImageToPdfCropSettings? crop,
        float alpha)
    {
        var angle = (float)ImageToPdfGeometry.NormalizeRotationAngle(rotationAngleDegrees);
        var centerX = previewRect.X + (previewRect.Width / 2f);
        var centerY = previewRect.Y + (previewRect.Height / 2f);
        var state = graphics.Save();

        try
        {
            graphics.TranslateTransform(centerX, centerY);
            if (Math.Abs(angle) > 0.001f)
            {
                graphics.RotateTransform(angle);
            }

            var sourceRect = ImageToPdfGeometry.GetBitmapSourceRectFromCrop(bitmap.Size, crop);
            var drawRect = new Rectangle(
                (int)Math.Round(-previewRect.Width / 2f),
                (int)Math.Round(-previewRect.Height / 2f),
                (int)Math.Round(previewRect.Width),
                (int)Math.Round(previewRect.Height));
            DrawBitmapWithAlpha(graphics, bitmap, sourceRect, drawRect, alpha);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private static void DrawBitmapWithAlpha(Graphics graphics, Bitmap bitmap, Rectangle sourceRect, Rectangle destinationRect, float alpha)
    {
        alpha = Math.Clamp(alpha, 0.0f, 1.0f);
        using var imageAttributes = new System.Drawing.Imaging.ImageAttributes();
        var matrix = new System.Drawing.Imaging.ColorMatrix
        {
            Matrix33 = alpha
        };
        imageAttributes.SetColorMatrix(matrix, System.Drawing.Imaging.ColorMatrixFlag.Default, System.Drawing.Imaging.ColorAdjustType.Bitmap);
        graphics.DrawImage(
            bitmap,
            destinationRect,
            sourceRect.X,
            sourceRect.Y,
            sourceRect.Width,
            sourceRect.Height,
            GraphicsUnit.Pixel,
            imageAttributes);
    }

    private static void DrawCropOverlay(Graphics graphics, RectangleF outerRect, RectangleF cropRect, double rotationAngleDegrees)
    {
        var outerPoints = ImageToPdfGeometry.GetRotatedPreviewPointsForRect(outerRect, rotationAngleDegrees);
        var cropPoints = ImageToPdfGeometry.GetRotatedPreviewPointsForRect(cropRect, rotationAngleDegrees);

        using var path = new GraphicsPath(FillMode.Alternate);
        path.AddPolygon(outerPoints);
        path.AddPolygon(cropPoints);
        using var overlayBrush = new SolidBrush(Color.FromArgb(155, 60, 60, 60));
        graphics.FillPath(overlayBrush, path);
    }

    private static void DrawCropPreviewHandle(
        Graphics graphics,
        RectangleF handleRect,
        string handleName,
        Brush fillBrush,
        Pen borderPen)
    {
        var x = handleRect.X;
        var y = handleRect.Y;
        var width = handleRect.Width;
        var height = handleRect.Height;
        var size = Math.Min(width, height);
        var thickness = Math.Max(2.0f, (float)Math.Round(size * 0.32f));
        var segmentLength = Math.Max(thickness + 4.0f, (float)Math.Round(size * 1.56f));

        switch (handleName)
        {
            case "TopLeft":
                DrawCropHandleSegment(graphics, x, y, segmentLength, thickness, fillBrush, borderPen);
                DrawCropHandleSegment(graphics, x, y, thickness, segmentLength, fillBrush, borderPen);
                break;
            case "TopRight":
                DrawCropHandleSegment(graphics, x + width - segmentLength, y, segmentLength, thickness, fillBrush, borderPen);
                DrawCropHandleSegment(graphics, x + width - thickness, y, thickness, segmentLength, fillBrush, borderPen);
                break;
            case "BottomRight":
                DrawCropHandleSegment(graphics, x + width - segmentLength, y + height - thickness, segmentLength, thickness, fillBrush, borderPen);
                DrawCropHandleSegment(graphics, x + width - thickness, y + height - segmentLength, thickness, segmentLength, fillBrush, borderPen);
                break;
            case "BottomLeft":
                DrawCropHandleSegment(graphics, x, y + height - thickness, segmentLength, thickness, fillBrush, borderPen);
                DrawCropHandleSegment(graphics, x, y + height - segmentLength, thickness, segmentLength, fillBrush, borderPen);
                break;
            case "Top":
                DrawCropHandleSegment(graphics, x + ((width - segmentLength) / 2.0f), y, segmentLength, thickness, fillBrush, borderPen);
                break;
            case "Right":
                DrawCropHandleSegment(graphics, x + width - thickness, y + ((height - segmentLength) / 2.0f), thickness, segmentLength, fillBrush, borderPen);
                break;
            case "Bottom":
                DrawCropHandleSegment(graphics, x + ((width - segmentLength) / 2.0f), y + height - thickness, segmentLength, thickness, fillBrush, borderPen);
                break;
            case "Left":
                DrawCropHandleSegment(graphics, x, y + ((height - segmentLength) / 2.0f), thickness, segmentLength, fillBrush, borderPen);
                break;
            default:
                graphics.FillRectangle(fillBrush, x, y, width, height);
                graphics.DrawRectangle(borderPen, x, y, width, height);
                break;
        }
    }

    private static void DrawCropHandleSegment(
        Graphics graphics,
        float x,
        float y,
        float width,
        float height,
        Brush fillBrush,
        Pen borderPen)
    {
        if (width <= 0.0f || height <= 0.0f)
        {
            return;
        }

        graphics.FillRectangle(fillBrush, x, y, width, height);
        graphics.DrawRectangle(borderPen, x, y, width, height);
    }

    private RectangleF ResizeRectFromHandle(
        RectangleF originalRect,
        ResizeHandle handle,
        PointF currentPreviewPoint,
        RectangleF previewPageRect,
        double interactionStartAngle)
    {
        var originalAbsolute = ImageToPdfGeometry.ToAbsoluteRect(originalRect, _pageDefinition);
        var centerX = originalAbsolute.X + (originalAbsolute.Width / 2.0);
        var centerY = originalAbsolute.Y + (originalAbsolute.Height / 2.0);

        var currentPdfPoint = ImageToPdfGeometry.ToAbsolutePoint(currentPreviewPoint, previewPageRect, _pageDefinition);
        var localPoint = ImageToPdfGeometry.RotatePoint(currentPdfPoint, new PointF((float)centerX, (float)centerY), -interactionStartAngle);
        const double minimumSize = 12.0;

        double anchorX = 0.0;
        double anchorY = 0.0;
        double maxWidth = _pageDefinition.WidthPoints;
        double maxHeight = _pageDefinition.HeightPoints;
        double proposedWidth = originalAbsolute.Width;
        double proposedHeight = originalAbsolute.Height;

        switch (handle)
        {
            case ResizeHandle.TopLeft:
                anchorX = originalAbsolute.X + originalAbsolute.Width;
                anchorY = originalAbsolute.Y + originalAbsolute.Height;
                maxWidth = anchorX;
                maxHeight = anchorY;
                proposedWidth = anchorX - localPoint.X;
                proposedHeight = anchorY - localPoint.Y;
                break;
            case ResizeHandle.TopRight:
                anchorX = originalAbsolute.X;
                anchorY = originalAbsolute.Y + originalAbsolute.Height;
                maxWidth = _pageDefinition.WidthPoints - anchorX;
                maxHeight = anchorY;
                proposedWidth = localPoint.X - anchorX;
                proposedHeight = anchorY - localPoint.Y;
                break;
            case ResizeHandle.BottomLeft:
                anchorX = originalAbsolute.X + originalAbsolute.Width;
                anchorY = originalAbsolute.Y;
                maxWidth = anchorX;
                maxHeight = _pageDefinition.HeightPoints - anchorY;
                proposedWidth = anchorX - localPoint.X;
                proposedHeight = localPoint.Y - anchorY;
                break;
            case ResizeHandle.BottomRight:
                anchorX = originalAbsolute.X;
                anchorY = originalAbsolute.Y;
                maxWidth = _pageDefinition.WidthPoints - anchorX;
                maxHeight = _pageDefinition.HeightPoints - anchorY;
                proposedWidth = localPoint.X - anchorX;
                proposedHeight = localPoint.Y - anchorY;
                break;
            case ResizeHandle.Left:
                anchorX = originalAbsolute.X + originalAbsolute.Width;
                proposedWidth = anchorX - localPoint.X;
                break;
            case ResizeHandle.Top:
                anchorY = originalAbsolute.Y + originalAbsolute.Height;
                proposedHeight = anchorY - localPoint.Y;
                break;
            case ResizeHandle.Right:
                anchorX = originalAbsolute.X;
                proposedWidth = localPoint.X - anchorX;
                break;
            case ResizeHandle.Bottom:
                anchorY = originalAbsolute.Y;
                proposedHeight = localPoint.Y - anchorY;
                break;
        }

        if (handle is ResizeHandle.Left or ResizeHandle.Right or ResizeHandle.Top or ResizeHandle.Bottom)
        {
            if (_lockRatioCheckBox.Checked)
            {
                double scale;
                if (handle is ResizeHandle.Left or ResizeHandle.Right)
                {
                    proposedWidth = Math.Max(minimumSize, proposedWidth);
                    scale = proposedWidth / originalAbsolute.Width;
                }
                else
                {
                    proposedHeight = Math.Max(minimumSize, proposedHeight);
                    scale = proposedHeight / originalAbsolute.Height;
                }

                var maxScaleX = _pageDefinition.WidthPoints / originalAbsolute.Width;
                var maxScaleY = _pageDefinition.HeightPoints / originalAbsolute.Height;
                var minScaleX = minimumSize / originalAbsolute.Width;
                var minScaleY = minimumSize / originalAbsolute.Height;
                scale = Math.Max(scale, Math.Max(minScaleX, minScaleY));
                scale = Math.Min(scale, Math.Min(maxScaleX, maxScaleY));

                var newWidth = originalAbsolute.Width * scale;
                var newHeight = originalAbsolute.Height * scale;
                var newX = centerX - (newWidth / 2.0);
                var newY = centerY - (newHeight / 2.0);

                return ToNormalizedRect(ClampAbsoluteRectToPage(new RectangleF(
                    (float)newX,
                    (float)newY,
                    (float)newWidth,
                    (float)newHeight)));
            }

            double edgeWidth;
            double edgeHeight;
            double edgeX;
            double edgeY;

            if (handle is ResizeHandle.Left or ResizeHandle.Right)
            {
                edgeWidth = Math.Max(minimumSize, Math.Min(proposedWidth, _pageDefinition.WidthPoints));
                edgeHeight = originalAbsolute.Height;
                edgeX = handle == ResizeHandle.Left ? anchorX - edgeWidth : anchorX;
                edgeY = originalAbsolute.Y;
            }
            else
            {
                edgeWidth = originalAbsolute.Width;
                edgeHeight = Math.Max(minimumSize, Math.Min(proposedHeight, _pageDefinition.HeightPoints));
                edgeX = originalAbsolute.X;
                edgeY = handle == ResizeHandle.Top ? anchorY - edgeHeight : anchorY;
            }

            return ToNormalizedRect(ClampAbsoluteRectToPage(new RectangleF(
                (float)edgeX,
                (float)edgeY,
                (float)edgeWidth,
                (float)edgeHeight)));
        }

        proposedWidth = Math.Max(minimumSize, proposedWidth);
        proposedHeight = Math.Max(minimumSize, proposedHeight);

        double newCornerWidth;
        double newCornerHeight;

        if (_lockRatioCheckBox.Checked)
        {
            var ratio = originalAbsolute.Width / originalAbsolute.Height;
            var widthFromHeight = proposedHeight * ratio;
            var heightFromWidth = proposedWidth / ratio;

            if (widthFromHeight <= proposedWidth)
            {
                newCornerWidth = widthFromHeight;
                newCornerHeight = proposedHeight;
            }
            else
            {
                newCornerWidth = proposedWidth;
                newCornerHeight = heightFromWidth;
            }

            newCornerWidth = Math.Max(minimumSize, Math.Min(newCornerWidth, maxWidth));
            newCornerHeight = newCornerWidth / ratio;

            if (newCornerHeight > maxHeight)
            {
                newCornerHeight = maxHeight;
                newCornerWidth = newCornerHeight * ratio;
            }

            newCornerWidth = Math.Max(minimumSize, newCornerWidth);
            newCornerHeight = Math.Max(minimumSize, newCornerHeight);
        }
        else
        {
            newCornerWidth = Math.Max(minimumSize, Math.Min(proposedWidth, maxWidth));
            newCornerHeight = Math.Max(minimumSize, Math.Min(proposedHeight, maxHeight));
        }

        double newCornerX = originalAbsolute.X;
        double newCornerY = originalAbsolute.Y;
        switch (handle)
        {
            case ResizeHandle.TopLeft:
                newCornerX = anchorX - newCornerWidth;
                newCornerY = anchorY - newCornerHeight;
                break;
            case ResizeHandle.TopRight:
                newCornerX = anchorX;
                newCornerY = anchorY - newCornerHeight;
                break;
            case ResizeHandle.BottomLeft:
                newCornerX = anchorX - newCornerWidth;
                newCornerY = anchorY;
                break;
            case ResizeHandle.BottomRight:
                newCornerX = anchorX;
                newCornerY = anchorY;
                break;
        }

        return ToNormalizedRect(new RectangleF(
            (float)newCornerX,
            (float)newCornerY,
            (float)newCornerWidth,
            (float)newCornerHeight));
    }

    private RectangleF ToNormalizedRect(RectangleF absoluteRect)
    {
        return ImageToPdfGeometry.ToNormalizedRect(absoluteRect, _pageDefinition);
    }

    private RectangleF ClampAbsoluteRectToPage(RectangleF rect)
    {
        var x = rect.X;
        var y = rect.Y;
        var width = rect.Width;
        var height = rect.Height;

        if (x < 0f)
        {
            x = 0f;
        }
        if (y < 0f)
        {
            y = 0f;
        }
        if (x + width > _pageDefinition.WidthPoints)
        {
            x = (float)(_pageDefinition.WidthPoints - width);
        }
        if (y + height > _pageDefinition.HeightPoints)
        {
            y = (float)(_pageDefinition.HeightPoints - height);
        }

        return new RectangleF(x, y, width, height);
    }

    private Button CreateToolbarButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 6, 6),
            FlatStyle = FlatStyle.Flat,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary,
            TextImageRelation = TextImageRelation.ImageAboveText,
            ImageAlign = ContentAlignment.TopCenter,
            TextAlign = ContentAlignment.BottomCenter,
            Padding = new Padding(2, 4, 2, 3),
            Height = 64,
            Font = new Font("Segoe UI", 7.75F, FontStyle.Regular, GraphicsUnit.Point)
        };

        button.FlatAppearance.BorderColor = FrameShiftTheme.PrimaryBlue;
        button.FlatAppearance.MouseOverBackColor = FrameShiftTheme.AccentSoft;
        button.FlatAppearance.MouseDownBackColor = FrameShiftTheme.AccentSoftHover;

        return button;
    }

    private static void ApplyOrderTileStyle(Button button)
    {
        button.Height = 64;
        button.Margin = new Padding(0, 0, 4, 0);
        button.Padding = new Padding(1, 1, 1, 0);
        button.Font = new Font("Segoe UI", 7F, FontStyle.Regular, GraphicsUnit.Point);
        button.TextAlign = ContentAlignment.TopCenter;
    }

    private static TableLayoutPanel CreateTileGrid(int columns, int rows)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = columns,
            RowCount = rows,
            Margin = Padding.Empty
        };

        for (var column = 0; column < columns; column++)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columns));
        }

        for (var row = 0; row < rows; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        return layout;
    }

    private static TableLayoutPanel CreateFixedHeightTileGrid(int columns, int rowHeight)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = columns,
            RowCount = 1,
            Margin = Padding.Empty,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        for (var column = 0; column < columns; column++)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columns));
        }

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
        return layout;
    }

    private void ConfigureToolTip(Control control, string text)
    {
        _toolTip.SetToolTip(control, text);
    }

    private void ConfigureTileButton(
        Button button,
        string iconFileName,
        string toolTipText,
        bool wideBadge = false,
        bool compactBadge = false)
    {
        button.Image = CreateButtonBadge(iconFileName, wideBadge, compactBadge);
        ConfigureToolTip(button, toolTipText);
    }

    private static Bitmap CreateButtonBadge(string iconFileName, bool wideBadge, bool compactBadge = false)
    {
        var badgeSize = compactBadge ? new Size(30, 30) : new Size(38, 38);
        var bitmap = new Bitmap(badgeSize.Width, badgeSize.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        var iconPath = IconPaths.ImageToPdfIco(iconFileName);
        if (!File.Exists(iconPath))
        {
            iconPath = IconPaths.ContextMenuIco(iconFileName);
        }

        if (File.Exists(iconPath))
        {
            using var icon = new Icon(iconPath);
            var iconSize = compactBadge ? new Size(20, 20) : new Size(26, 26);
            using var iconBitmap = new Bitmap(icon.ToBitmap(), iconSize);
            var iconX = (badgeSize.Width - iconBitmap.Width) / 2;
            var iconY = (badgeSize.Height - iconBitmap.Height) / 2;
            graphics.DrawImage(iconBitmap, iconX, iconY, iconBitmap.Width, iconBitmap.Height);
        }

        return bitmap;
    }

    private static ResizeHandle ParseResizeHandle(string handle)
    {
        return handle switch
        {
            "TopLeft" => ResizeHandle.TopLeft,
            "Top" => ResizeHandle.Top,
            "TopRight" => ResizeHandle.TopRight,
            "Right" => ResizeHandle.Right,
            "BottomRight" => ResizeHandle.BottomRight,
            "Bottom" => ResizeHandle.Bottom,
            "BottomLeft" => ResizeHandle.BottomLeft,
            "Left" => ResizeHandle.Left,
            _ => ResizeHandle.TopLeft
        };
    }

    private static CropHandle ParseCropHandle(string handle)
    {
        return handle switch
        {
            "TopLeft" => CropHandle.TopLeft,
            "Top" => CropHandle.Top,
            "TopRight" => CropHandle.TopRight,
            "Right" => CropHandle.Right,
            "BottomRight" => CropHandle.BottomRight,
            "Bottom" => CropHandle.Bottom,
            "BottomLeft" => CropHandle.BottomLeft,
            "Left" => CropHandle.Left,
            _ => CropHandle.TopLeft
        };
    }

    private static Cursor GetResizeCursor(ResizeHandle handle)
    {
        return handle is ResizeHandle.Top or ResizeHandle.Bottom
            ? Cursors.SizeNS
            : handle is ResizeHandle.Left or ResizeHandle.Right
                ? Cursors.SizeWE
                : handle is ResizeHandle.TopLeft or ResizeHandle.BottomRight
                    ? Cursors.SizeNWSE
                    : Cursors.SizeNESW;
    }

    private static Cursor GetCropCursor(CropHandle handle)
    {
        return handle is CropHandle.Top or CropHandle.Bottom
            ? Cursors.SizeNS
            : handle is CropHandle.Left or CropHandle.Right
                ? Cursors.SizeWE
                : handle is CropHandle.TopLeft or CropHandle.BottomRight
                    ? Cursors.SizeNWSE
                    : Cursors.SizeNESW;
    }

    private static NumericUpDown CreatePercentageNumericUpDown()
    {
        return new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 5M,
            Maximum = 100M,
            DecimalPlaces = 1,
            Increment = 1M,
            ThousandsSeparator = false,
            TextAlign = HorizontalAlignment.Right
        };
    }

    private static NumericUpDown CreateCentimeterNumericUpDown()
    {
        return new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 10M,
            Maximum = 200M,
            DecimalPlaces = 1,
            Increment = 0.1M,
            ThousandsSeparator = false,
            TextAlign = HorizontalAlignment.Right
        };
    }

    private static decimal NormalizePercentage(float value)
    {
        var percentage = Math.Clamp(value, 0.1f, 1f) * 100f;
        return Math.Round((decimal)percentage, 1, MidpointRounding.AwayFromZero);
    }

    private static bool IsSupportedExtension(string extension)
    {
        return ImageCropSupport.IsSupportedExtension(extension);
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(
            message,
            "FrameShift",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void DisposeLoadedImages()
    {
        foreach (var bitmap in _bitmapCache.Values)
        {
            bitmap.Dispose();
        }

        _items.Clear();
        _bitmapCache.Clear();
    }

    private sealed class ImageCanvasItem
    {
        public ImageCanvasItem(ImageToPdfItemSettings settings, Bitmap bitmap)
        {
            Settings = settings;
            Bitmap = bitmap;
        }

        public ImageToPdfItemSettings Settings { get; }

        public Bitmap Bitmap { get; }
    }

    private sealed class ClipboardImageItem : IDisposable
    {
        public ClipboardImageItem(ImageToPdfItemSettings settings, Bitmap bitmap)
        {
            Settings = settings;
            Bitmap = bitmap;
        }

        public ImageToPdfItemSettings Settings { get; }

        public Bitmap Bitmap { get; }

        public void Dispose()
        {
            Bitmap.Dispose();
        }
    }

    private sealed class SelectionInteractionItemState
    {
        public SelectionInteractionItemState(int index, RectangleF rect, double angleDegrees)
        {
            Index = index;
            Rect = rect;
            AngleDegrees = angleDegrees;
        }

        public int Index { get; }

        public RectangleF Rect { get; }

        public double AngleDegrees { get; }
    }

    private sealed class EditorHistoryState
    {
        public EditorHistoryState(
            string pageFormat,
            double customPageWidthCentimeters,
            double customPageHeightCentimeters,
            int selectedIndex,
            List<int> selectedIndexes,
            bool cropModeEnabled,
            List<EditorHistoryItemState> items)
        {
            PageFormat = pageFormat;
            CustomPageWidthCentimeters = customPageWidthCentimeters;
            CustomPageHeightCentimeters = customPageHeightCentimeters;
            SelectedIndex = selectedIndex;
            SelectedIndexes = selectedIndexes;
            CropModeEnabled = cropModeEnabled;
            Items = items;
        }

        public string PageFormat { get; }

        public double CustomPageWidthCentimeters { get; }

        public double CustomPageHeightCentimeters { get; }

        public int SelectedIndex { get; }

        public List<int> SelectedIndexes { get; }

        public bool CropModeEnabled { get; }

        public List<EditorHistoryItemState> Items { get; }

        public static bool Equals(EditorHistoryState left, EditorHistoryState right)
        {
            if (!string.Equals(left.PageFormat, right.PageFormat, StringComparison.OrdinalIgnoreCase) ||
                left.CustomPageWidthCentimeters != right.CustomPageWidthCentimeters ||
                left.CustomPageHeightCentimeters != right.CustomPageHeightCentimeters ||
                left.SelectedIndex != right.SelectedIndex ||
                left.SelectedIndexes.Count != right.SelectedIndexes.Count ||
                !left.SelectedIndexes.SequenceEqual(right.SelectedIndexes) ||
                left.CropModeEnabled != right.CropModeEnabled ||
                left.Items.Count != right.Items.Count)
            {
                return false;
            }

            for (var index = 0; index < left.Items.Count; index++)
            {
                if (!EditorHistoryItemState.Equals(left.Items[index], right.Items[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class EditorHistoryItemState
    {
        public required string SourcePath { get; init; }

        public float X { get; init; }

        public float Y { get; init; }

        public float Width { get; init; }

        public float Height { get; init; }

        public int RotationQuarterTurns { get; init; }

        public double RotationAngleDegrees { get; init; }

        public required ImageToPdfCropSettings Crop { get; init; }

        public static EditorHistoryItemState FromSettings(ImageToPdfItemSettings settings)
        {
            return new EditorHistoryItemState
            {
                SourcePath = settings.SourcePath,
                X = settings.X,
                Y = settings.Y,
                Width = settings.Width,
                Height = settings.Height,
                RotationQuarterTurns = settings.RotationQuarterTurns,
                RotationAngleDegrees = settings.GetRotationAngleDegrees(),
                Crop = CopyCrop(settings.GetCrop())
            };
        }

        public ImageToPdfItemSettings ToSettings()
        {
            return new ImageToPdfItemSettings
            {
                SourcePath = SourcePath,
                X = X,
                Y = Y,
                Width = Width,
                Height = Height,
                RotationQuarterTurns = RotationQuarterTurns,
                RotationAngleDegrees = RotationAngleDegrees,
                Crop = CopyCrop(Crop)
            };
        }

        public static bool Equals(EditorHistoryItemState left, EditorHistoryItemState right)
        {
            return string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase) &&
                   left.X == right.X &&
                   left.Y == right.Y &&
                   left.Width == right.Width &&
                   left.Height == right.Height &&
                   left.RotationQuarterTurns == right.RotationQuarterTurns &&
                   left.RotationAngleDegrees == right.RotationAngleDegrees &&
                   left.Crop.Left == right.Crop.Left &&
                   left.Crop.Top == right.Crop.Top &&
                   left.Crop.Right == right.Crop.Right &&
                   left.Crop.Bottom == right.Crop.Bottom;
        }
    }

    private sealed class PageFormatOption
    {
        public PageFormatOption(string display, string value)
        {
            Display = display;
            Value = value;
        }

        public string Display { get; }

        public string Value { get; }

        public override string ToString() => Display;
    }

    private enum ImageInteractionMode
    {
        None,
        PanView,
        Drag,
        Resize,
        Rotate,
        Crop
    }

    private enum ResizeHandle
    {
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left
    }

    private enum CropHandle
    {
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left
    }
}
