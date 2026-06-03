using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FrameShift.Core.AI.Upscale;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.AI;

/// <summary>
/// Compact model + scale picker shown before the Upscale Image action runs. Uses the standard
/// FrameShift styled dropdown for the model, plus a scale row (x2 / x3 / x4 / custom size). The
/// custom width/height fields keep the source aspect ratio locked (editing one updates the other).
/// </summary>
public sealed class UpscaleImagePickerForm : Form
{
    private readonly ContextMenuStrip _modelMenu;
    private readonly Label _selectorLabel;
    private readonly Label _descriptionLabel;
    private string _selectedModelId;

    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private readonly double _ratio;
    private readonly bool _allowCustom;
    private bool _updatingFields;

    private readonly RadioButton _scale2;
    private readonly RadioButton _scale3;
    private readonly RadioButton _scale4;
    private readonly RadioButton _scaleCustom;
    private readonly TextBox _widthBox;
    private readonly TextBox _heightBox;

    public UpscaleImagePickerForm(
        string sourceLabel,
        int sourceWidth,
        int sourceHeight,
        bool allowCustomSize,
        string? initialModelId = null)
    {
        var models = UpscaleModelCatalog.GetAll();
        var initial = UpscaleModelCatalog.GetById(initialModelId) ?? UpscaleModelCatalog.GetDefault();
        _selectedModelId = initial.Id;

        _sourceWidth = sourceWidth;
        _sourceHeight = sourceHeight;
        _allowCustom = allowCustomSize && sourceWidth > 0 && sourceHeight > 0;
        _ratio = _allowCustom ? (double)sourceWidth / sourceHeight : 1d;

        FrameShiftWindowChrome.Apply(this, "FrameShift - Upscale Image", IconPaths.UpscaleAiIcon, IconPaths.AppIcon);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = FrameShiftTheme.PageBackground;

        const int modelTop = 82;
        const int modelHeight = 116;
        int scaleTop = modelTop + modelHeight + 12;
        const int scaleHeight = 128;
        int infoTop = scaleTop + scaleHeight + 12;
        int buttonsTop = infoTop + 46 + 12;
        ClientSize = new Size(560, buttonsTop + 34 + 12);

        Controls.Add(FrameShiftUiFactory.CreateFixedHeader(
            "FrameShift - Upscale Image",
            $"Source: {sourceLabel}",
            IconPaths.UpscaleAiIcon,
            IconPaths.FrameShiftAiIcon,
            "AI"));

        // --- Model section -----------------------------------------------------------------------
        var modelSection = FrameShiftUiFactory.CreateFixedSection(new Point(12, modelTop), new Size(536, modelHeight), "Model");
        Controls.Add(modelSection);

        _modelMenu = new ContextMenuStrip
        {
            AutoSize = false,
            ShowImageMargin = false,
            RenderMode = ToolStripRenderMode.System,
            Width = 160
        };

        var selectorPanel = CreateSelectorPanel(new Point(18, 32), new Size(500, 30), out _selectorLabel, _modelMenu);
        modelSection.Controls.Add(selectorPanel);

        _descriptionLabel = new Label
        {
            Location = new Point(18, 70),
            Size = new Size(500, 36),
            ForeColor = FrameShiftTheme.TextSecondary,
            AutoEllipsis = true
        };
        modelSection.Controls.Add(_descriptionLabel);

        foreach (var model in models)
        {
            var captured = model;
            var item = new ToolStripMenuItem($"{captured.DisplayName}  —  x{captured.ScaleFactor}") { Tag = captured.Id };
            item.Click += (_, _) => SelectModel(captured.Id);
            _modelMenu.Items.Add(item);
        }

        SelectModel(initial.Id);

        // --- Scale section -----------------------------------------------------------------------
        var scaleSection = FrameShiftUiFactory.CreateFixedSection(new Point(12, scaleTop), new Size(536, scaleHeight), "Scale");
        Controls.Add(scaleSection);

        _scale2 = CreateScaleRadio("2x", new Point(18, 32), 56);
        _scale3 = CreateScaleRadio("3x", new Point(82, 32), 56);
        _scale4 = CreateScaleRadio("4x", new Point(146, 32), 56);
        _scaleCustom = CreateScaleRadio("Custom size", new Point(210, 32), 120);
        _scale4.Checked = true;
        scaleSection.Controls.AddRange([_scale2, _scale3, _scale4, _scaleCustom]);

        scaleSection.Controls.Add(CreateFieldLabel("Width", 18, 69));
        _widthBox = CreateValueTextBox();
        scaleSection.Controls.Add(FrameShiftUiFactory.CreateFixedTextInputHost(_widthBox, new Point(66, 64), new Size(80, 30)));
        scaleSection.Controls.Add(CreateUnitLabel("px", 150, 69));

        scaleSection.Controls.Add(CreateFieldLabel("Height", 190, 69));
        _heightBox = CreateValueTextBox();
        scaleSection.Controls.Add(FrameShiftUiFactory.CreateFixedTextInputHost(_heightBox, new Point(246, 64), new Size(80, 30)));
        scaleSection.Controls.Add(CreateUnitLabel("px", 330, 69));

        var hint = new Label
        {
            Location = new Point(18, 102),
            Size = new Size(500, 18),
            ForeColor = FrameShiftTheme.TextSecondary,
            AutoEllipsis = true,
            Text = _allowCustom
                ? $"Aspect locked to the source. Maximum {_sourceWidth * 4} x {_sourceHeight * 4} px (x4)."
                : "Custom size needs a single image; x2 / x3 / x4 still apply to every selected file."
        };
        scaleSection.Controls.Add(hint);

        _scaleCustom.Enabled = _allowCustom;
        _scale2.CheckedChanged += (_, _) => RefreshCustomEnabled();
        _scale3.CheckedChanged += (_, _) => RefreshCustomEnabled();
        _scale4.CheckedChanged += (_, _) => RefreshCustomEnabled();
        _scaleCustom.CheckedChanged += (_, _) => RefreshCustomEnabled();
        _widthBox.TextChanged += (_, _) => OnWidthTyped();
        _heightBox.TextChanged += (_, _) => OnHeightTyped();
        RefreshCustomEnabled();

        // --- Info + buttons ----------------------------------------------------------------------
        var infoCard = FrameShiftUiFactory.CreateFixedInfoCard(new Point(12, infoTop), new Size(536, 46));
        Controls.Add(infoCard);
        infoCard.Controls.Add(new Label
        {
            Location = new Point(12, 8),
            Size = new Size(512, 30),
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = "The upscaled image is saved as a new PNG next to the source. " +
                   "The AI model is downloaded once if it is not already installed."
        });

        var cancelButton = FrameShiftUiFactory.CreateFixedActionButton("Cancel", new Point(278, buttonsTop), new Size(120, 34), primary: false);
        cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);

        var upscaleButton = FrameShiftUiFactory.CreateFixedActionButton("Upscale", new Point(408, buttonsTop), new Size(140, 34), primary: true);
        upscaleButton.DialogResult = DialogResult.OK;
        Controls.Add(upscaleButton);

        AcceptButton = upscaleButton;
        CancelButton = cancelButton;
    }

    /// <summary>The chosen model id, or null when the dialog was cancelled.</summary>
    public string? SelectedModelId => DialogResult == DialogResult.OK ? _selectedModelId : null;

    /// <summary>Preset factor ("2", "3", "4") when a preset is chosen; null when custom size is chosen.</summary>
    public string? SelectedScale
    {
        get
        {
            if (_scale2.Checked) return "2";
            if (_scale3.Checked) return "3";
            if (_scale4.Checked) return "4";
            return null;
        }
    }

    /// <summary>The custom target size (aspect-locked, clamped to x1..x4), or null when not in custom mode.</summary>
    public (int Width, int Height)? CustomTarget
    {
        get
        {
            if (!_scaleCustom.Checked || !_allowCustom) return null;

            if (TryParsePositiveInt(_widthBox.Text) is int w)
            {
                double factor = Math.Clamp((double)w / _sourceWidth, 1d, 4d);
                return (Round(_sourceWidth * factor), Round(_sourceHeight * factor));
            }

            if (TryParsePositiveInt(_heightBox.Text) is int h)
            {
                double factor = Math.Clamp((double)h / _sourceHeight, 1d, 4d);
                return (Round(_sourceWidth * factor), Round(_sourceHeight * factor));
            }

            return null;
        }
    }

    public static string BuildSourceLabel(IReadOnlyList<string> inputPaths)
    {
        if (inputPaths.Count <= 1)
        {
            return Path.GetFileName(inputPaths[0]);
        }

        return $"{inputPaths.Count} selected files";
    }

    private void RefreshCustomEnabled()
    {
        bool on = _scaleCustom.Checked && _allowCustom;
        _widthBox.Enabled = on;
        _heightBox.Enabled = on;
        _widthBox.ForeColor = on ? FrameShiftTheme.TextPrimary : FrameShiftTheme.TextMuted;
        _heightBox.ForeColor = on ? FrameShiftTheme.TextPrimary : FrameShiftTheme.TextMuted;

        if (on && string.IsNullOrWhiteSpace(_widthBox.Text))
        {
            _updatingFields = true;
            _widthBox.Text = (_sourceWidth * 4).ToString(CultureInfo.InvariantCulture);
            _heightBox.Text = (_sourceHeight * 4).ToString(CultureInfo.InvariantCulture);
            _updatingFields = false;
        }
    }

    private void OnWidthTyped()
    {
        if (_updatingFields || !_scaleCustom.Checked) return;
        if (TryParsePositiveInt(_widthBox.Text) is not int w) return;

        _updatingFields = true;
        _heightBox.Text = Math.Max(1, (int)Math.Round(w / _ratio)).ToString(CultureInfo.InvariantCulture);
        _updatingFields = false;
    }

    private void OnHeightTyped()
    {
        if (_updatingFields || !_scaleCustom.Checked) return;
        if (TryParsePositiveInt(_heightBox.Text) is not int h) return;

        _updatingFields = true;
        _widthBox.Text = Math.Max(1, (int)Math.Round(h * _ratio)).ToString(CultureInfo.InvariantCulture);
        _updatingFields = false;
    }

    private void SelectModel(string modelId)
    {
        var model = UpscaleModelCatalog.GetById(modelId) ?? UpscaleModelCatalog.GetDefault();
        _selectedModelId = model.Id;
        _selectorLabel.Text = $"{model.DisplayName}  —  x{model.ScaleFactor}";
        _descriptionLabel.Text = model.Summary;
    }

    private static int Round(double value) => Math.Max(1, (int)Math.Round(value));

    private static int? TryParsePositiveInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;

    private static RadioButton CreateScaleRadio(string text, Point location, int width) => new()
    {
        Text = text,
        Location = location,
        Size = new Size(width, 24),
        ForeColor = FrameShiftTheme.TextPrimary,
        BackColor = Color.Transparent,
        UseVisualStyleBackColor = true
    };

    private static Label CreateFieldLabel(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(46, 23),
        ForeColor = FrameShiftTheme.TextPrimary
    };

    private static Label CreateUnitLabel(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(22, 23),
        ForeColor = FrameShiftTheme.TextSecondary
    };

    private static TextBox CreateValueTextBox() => new()
    {
        BorderStyle = BorderStyle.None,
        BackColor = FrameShiftTheme.Surface,
        ForeColor = FrameShiftTheme.TextPrimary,
        TextAlign = HorizontalAlignment.Right
    };

    private Panel CreateSelectorPanel(Point location, Size size, out Label valueLabel, ContextMenuStrip menu)
    {
        var panel = FrameShiftUiFactory.CreateFramedPanel(
            location, size, FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.InputCornerRadius);
        panel.Padding = new Padding(8, 5, 8, 5);
        panel.Cursor = Cursors.Hand;
        panel.Click += (_, _) => ShowSizedMenu(panel, menu);

        valueLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = FrameShiftTheme.TextPrimary,
            Cursor = Cursors.Hand
        };
        valueLabel.Click += (_, _) => ShowSizedMenu(panel, menu);
        panel.Controls.Add(valueLabel);

        var arrowLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Right,
            Width = 16,
            Text = "▾",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = FrameShiftTheme.TextSecondary,
            Cursor = Cursors.Hand
        };
        arrowLabel.Click += (_, _) => ShowSizedMenu(panel, menu);
        panel.Controls.Add(arrowLabel);

        return panel;
    }

    private static void ShowSizedMenu(Control anchor, ContextMenuStrip menu)
    {
        var menuWidth = Math.Max(anchor.Width, 160);
        menu.Width = menuWidth;

        foreach (ToolStripItem item in menu.Items)
        {
            item.AutoSize = false;
            item.Width = Math.Max(0, menuWidth - 2);
        }

        var preferredHeight = menu.GetPreferredSize(new Size(menuWidth, int.MaxValue)).Height;
        menu.Size = new Size(menuWidth, preferredHeight);

        menu.Show(anchor, new Point(0, anchor.Height));
    }
}
