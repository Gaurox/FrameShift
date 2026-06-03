using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FrameShift.Core.AI.Upscale;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.AI;

/// <summary>
/// Compact model picker shown before the Upscale Image action runs. Uses the standard FrameShift
/// styled dropdown ("Model" selector) plus a description line that updates with the selection.
/// The list is built from the catalog so new models appear without UI changes.
/// </summary>
public sealed class UpscaleImagePickerForm : Form
{
    private readonly ContextMenuStrip _modelMenu;
    private readonly Label _selectorLabel;
    private readonly Label _descriptionLabel;
    private string _selectedModelId;

    public UpscaleImagePickerForm(string sourceLabel, string? initialModelId = null)
    {
        var models = UpscaleModelCatalog.GetAll();
        var initial = UpscaleModelCatalog.GetById(initialModelId) ?? UpscaleModelCatalog.GetDefault();
        _selectedModelId = initial.Id;

        FrameShiftWindowChrome.Apply(this, "FrameShift - Upscale Image", IconPaths.UpscaleAiIcon, IconPaths.AppIcon);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = FrameShiftTheme.PageBackground;

        const int sectionTop = 82;
        const int sectionHeight = 116;
        int infoTop = sectionTop + sectionHeight + 12;
        int buttonsTop = infoTop + 46 + 12;
        ClientSize = new Size(560, buttonsTop + 34 + 12);

        var header = FrameShiftUiFactory.CreateFixedHeader(
            "FrameShift - Upscale Image",
            $"Source: {sourceLabel}",
            IconPaths.UpscaleAiIcon,
            IconPaths.FrameShiftAiIcon,
            "AI");
        Controls.Add(header);

        var modelSection = FrameShiftUiFactory.CreateFixedSection(
            new Point(12, sectionTop),
            new Size(536, sectionHeight),
            "Model");
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
            var item = new ToolStripMenuItem($"{captured.DisplayName}  —  x{captured.ScaleFactor}")
            {
                Tag = captured.Id
            };
            item.Click += (_, _) => SelectModel(captured.Id);
            _modelMenu.Items.Add(item);
        }

        SelectModel(initial.Id);

        var infoCard = FrameShiftUiFactory.CreateFixedInfoCard(
            new Point(12, infoTop),
            new Size(536, 46));
        Controls.Add(infoCard);
        infoCard.Controls.Add(new Label
        {
            Location = new Point(12, 8),
            Size = new Size(512, 30),
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = "The upscaled image is saved as a new PNG next to the source (suffix _upscaled_4x). " +
                   "The AI model is downloaded once if it is not already installed."
        });

        var cancelButton = FrameShiftUiFactory.CreateFixedActionButton(
            "Cancel",
            new Point(278, buttonsTop),
            new Size(120, 34),
            primary: false);
        cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);

        var upscaleButton = FrameShiftUiFactory.CreateFixedActionButton(
            "Upscale",
            new Point(408, buttonsTop),
            new Size(140, 34),
            primary: true);
        upscaleButton.DialogResult = DialogResult.OK;
        Controls.Add(upscaleButton);

        AcceptButton = upscaleButton;
        CancelButton = cancelButton;
    }

    /// <summary>The chosen model id, or null when the dialog was cancelled.</summary>
    public string? SelectedModelId => DialogResult == DialogResult.OK ? _selectedModelId : null;

    public static string BuildSourceLabel(IReadOnlyList<string> inputPaths)
    {
        if (inputPaths.Count <= 1)
        {
            return Path.GetFileName(inputPaths[0]);
        }

        return $"{inputPaths.Count} selected files";
    }

    private void SelectModel(string modelId)
    {
        var model = UpscaleModelCatalog.GetById(modelId) ?? UpscaleModelCatalog.GetDefault();
        _selectedModelId = model.Id;
        _selectorLabel.Text = $"{model.DisplayName}  —  x{model.ScaleFactor}";
        _descriptionLabel.Text = model.Summary;
    }

    private Panel CreateSelectorPanel(Point location, Size size, out Label valueLabel, ContextMenuStrip menu)
    {
        var panel = FrameShiftUiFactory.CreateFramedPanel(
            location,
            size,
            FrameShiftTheme.Surface,
            FrameShiftTheme.PrimaryBlue,
            FrameShiftUiMetrics.InputCornerRadius);
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
