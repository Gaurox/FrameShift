using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class CompressImageForm : Form
{
    private readonly RadioButton _radioHigh;
    private readonly RadioButton _radioBalanced;
    private readonly RadioButton _radioSmall;
    private readonly ComboBox _formatCombo;
    private readonly CheckBox _checkTarget;
    private readonly TextBox _textTarget;
    private readonly Label _targetHint;
    private readonly Panel _unitSelector;
    private readonly Label _unitSelectorLabel;
    private readonly Label _unitArrowLabel;
    private readonly ContextMenuStrip _unitMenu;
    private readonly Panel _infoCard;
    private readonly Button _cancelButton;
    private readonly Button _compressButton;

    private string _selectedProfileId = CompressImageSettings.ProfileHigh;
    private string _selectedUnit = "KB";
    private bool _updatingProfileSelection;

    public CompressImageForm(string sourcePath, string sourceExtension, long sourceBytes)
    {
        FrameShiftWindowChrome.Apply(this, "FrameShift - Compress Image");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 492);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        Controls.Add(CreateHeader(sourcePath, sourceExtension, sourceBytes));

        var presetSection = CreateSectionPanel("Compression profile", new Point(12, 82), new Size(536, 124));
        Controls.Add(presetSection);

        var highTile = CreateProfileTile("High quality", "Preserves more detail", new Point(18, 32), 160, 52, CompressImageSettings.ProfileHigh, true, out _radioHigh);
        var balancedTile = CreateProfileTile("Balanced", "Good balance", new Point(188, 32), 160, 52, CompressImageSettings.ProfileBalanced, false, out _radioBalanced);
        var smallTile = CreateProfileTile("Small file", "Reduces size", new Point(358, 32), 160, 52, CompressImageSettings.ProfileSmall, false, out _radioSmall);
        presetSection.Controls.Add(highTile);
        presetSection.Controls.Add(balancedTile);
        presetSection.Controls.Add(smallTile);

        presetSection.Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(18, 92),
            Size = new Size(500, 18),
            ForeColor = FrameShiftTheme.SecondaryBlue,
            Text = "PNG stays lossless. JPG and WEBP usually give the strongest file size reduction."
        });

        _radioHigh.CheckedChanged += (_, _) => SelectProfile(CompressImageSettings.ProfileHigh, highTile, balancedTile, smallTile);
        _radioBalanced.CheckedChanged += (_, _) => SelectProfile(CompressImageSettings.ProfileBalanced, highTile, balancedTile, smallTile);
        _radioSmall.CheckedChanged += (_, _) => SelectProfile(CompressImageSettings.ProfileSmall, highTile, balancedTile, smallTile);

        var formatSection = CreateSectionPanel("Output format", new Point(12, 218), new Size(536, 76));
        Controls.Add(formatSection);

        formatSection.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(18, 33),
            Text = "Format",
            ForeColor = FrameShiftTheme.TextPrimary
        });

        _formatCombo = FrameShiftUiFactory.CreateFixedComboBox(new Point(96, 27), new Size(120, 24));
        _formatCombo.Items.AddRange(["PNG", "JPG", "WEBP"]);
        _formatCombo.SelectedItem = CompressImageSettings.GetDefaultOutputFormat(sourceExtension).ToUpperInvariant();
        _formatCombo.SelectedIndexChanged += (_, _) => UpdateTargetAvailability();
        formatSection.Controls.Add(_formatCombo);

        var targetSection = CreateSectionPanel("Optional target size", new Point(12, 306), new Size(536, 76));
        Controls.Add(targetSection);

        _checkTarget = new CheckBox
        {
            Text = "Target file size",
            Location = new Point(18, 31),
            Size = new Size(120, 22),
            ForeColor = FrameShiftTheme.TextPrimary,
            FlatStyle = FlatStyle.Standard
        };
        _checkTarget.CheckedChanged += (_, _) => UpdateTargetAvailability();
        targetSection.Controls.Add(_checkTarget);

        _textTarget = FrameShiftUiFactory.CreateValueTextBox(readOnly: true);
        _textTarget.Dock = DockStyle.Fill;
        _textTarget.ForeColor = FrameShiftTheme.TextMuted;
        targetSection.Controls.Add(FrameShiftUiFactory.CreateFixedTextInputHost(_textTarget, new Point(138, 26), new Size(90, 30)));

        _unitMenu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            RenderMode = ToolStripRenderMode.System
        };
        var kbItem = new ToolStripMenuItem("KB");
        kbItem.Click += (_, _) => SetSelectedUnit("KB");
        var mbItem = new ToolStripMenuItem("MB");
        mbItem.Click += (_, _) => SetSelectedUnit("MB");
        _unitMenu.Items.Add(kbItem);
        _unitMenu.Items.Add(mbItem);

        _unitSelector = CreateUnitSelector(new Point(238, 26), new Size(78, 30), out _unitSelectorLabel, out _unitArrowLabel);
        targetSection.Controls.Add(_unitSelector);

        _targetHint = new Label
        {
            Location = new Point(332, 31),
            Size = new Size(188, 18),
            ForeColor = FrameShiftTheme.SecondaryBlue
        };
        targetSection.Controls.Add(_targetHint);

        _infoCard = FrameShiftUiFactory.CreateFixedInfoCard(new Point(12, 394), new Size(536, 44));
        Controls.Add(_infoCard);
        _infoCard.Controls.Add(new Label
        {
            Location = new Point(16, 11),
            Size = new Size(504, 18),
            Text = "The compressed image is created next to the original file.",
            ForeColor = FrameShiftTheme.TextSecondary
        });

        _cancelButton = CreateActionButton("Cancel", new Point(278, 424), new Size(120, 34), primary: false);
        _cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(_cancelButton);

        _compressButton = CreateActionButton("Compress", new Point(408, 424), new Size(140, 34), primary: true);
        _compressButton.DialogResult = DialogResult.OK;
        Controls.Add(_compressButton);

        AcceptButton = _compressButton;
        CancelButton = _cancelButton;

        SelectProfile(CompressImageSettings.ProfileHigh, highTile, balancedTile, smallTile);
        UpdateTargetAvailability();
    }

    public CompressImageSettings? Selection { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FrameShiftUiLayout.AutoSizeAndPositionFooterButtons(this, _cancelButton, _compressButton);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            var format = ((_formatCombo.SelectedItem as string) ?? "PNG").Trim().ToLowerInvariant();
            long? targetBytes = null;

            if (_checkTarget.Checked)
            {
                if (!TryParseTargetBytes(out var parsedTargetBytes))
                {
                    MessageBox.Show(
                        "Target file size must be a positive number.",
                        "FrameShift",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    e.Cancel = true;
                    return;
                }

                targetBytes = parsedTargetBytes;
            }

            Selection = new CompressImageSettings(_selectedProfileId, format, targetBytes);
        }

        base.OnFormClosing(e);
    }

    private static Control CreateHeader(string sourcePath, string sourceExtension, long sourceBytes)
    {
        _ = sourcePath;
        return FrameShiftUiFactory.CreateFixedHeader(
            "FrameShift - Compress Image",
            $"Source: {sourceExtension.TrimStart('.').ToUpperInvariant()}    Size: {FormatFileSize(sourceBytes)}",
            IconPaths.CompressVideoIcon,
            IconPaths.AppIcon,
            "▶");
    }

    private static Panel CreateSectionPanel(string title, Point location, Size size) =>
        FrameShiftUiFactory.CreateFixedSection(location, size, title);

    private static Button CreateActionButton(string text, Point location, Size size, bool primary)
    {
        var button = FrameShiftUiFactory.CreateFixedActionButton(text, location, size, primary);
        button.Name = $"{text.ToLowerInvariant()}Button";
        return button;
    }

    private Panel CreateProfileTile(
        string title,
        string description,
        Point location,
        int width,
        int height,
        string profileId,
        bool selected,
        out RadioButton radioButton)
    {
        var localRadio = new RadioButton
        {
            Checked = selected,
            AutoSize = false,
            Size = new Size(18, 18),
            Location = new Point(12, 17),
            Cursor = Cursors.Hand
        };
        radioButton = localRadio;

        var tile = FrameShiftUiFactory.CreateFramedPanel(location, new Size(width, height), FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.PanelCornerRadius);
        tile.Cursor = Cursors.Hand;
        tile.Tag = profileId;
        tile.Paint += (_, paintArgs) => FrameShiftUiPainter.DrawRoundedBorder(tile, paintArgs.Graphics, string.Equals(_selectedProfileId, profileId, StringComparison.OrdinalIgnoreCase) ? FrameShiftTheme.SecondaryBlue : FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.PanelCornerRadius, string.Equals(_selectedProfileId, profileId, StringComparison.OrdinalIgnoreCase) ? 2F : 1F);
        tile.BackColor = selected ? FrameShiftTheme.AccentSoft : FrameShiftTheme.Surface;

        localRadio.CheckedChanged += (_, _) => tile.Invalidate();
        tile.Controls.Add(localRadio);

        tile.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(38, 10),
            Text = title,
            ForeColor = FrameShiftTheme.TextPrimary,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Regular, GraphicsUnit.Point)
        });

        tile.Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(38, 30),
            Size = new Size(width - 48, 16),
            Text = description,
            ForeColor = FrameShiftTheme.TextSecondary
        });

        foreach (Control child in tile.Controls)
        {
            child.Click += (_, _) => SelectProfile(profileId);
            child.Cursor = Cursors.Hand;
        }

        return tile;
    }

    private Panel CreateUnitSelector(Point location, Size size, out Label label, out Label arrowLabel)
    {
        var panel = FrameShiftUiFactory.CreateFramedPanel(location, size, FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.InputCornerRadius);
        panel.Padding = new Padding(8, 5, 8, 5);
        panel.Cursor = Cursors.Hand;
        panel.Click += (_, _) => ShowUnitMenu(panel);

        label = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Left,
            Width = 34,
            Text = _selectedUnit,
            ForeColor = FrameShiftTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(label);

        arrowLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Right,
            Size = new Size(16, 16),
            Text = "▾",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = FrameShiftTheme.TextMuted,
            Cursor = Cursors.Hand
        };
        arrowLabel.Click += (_, _) => ShowUnitMenu(panel);
        panel.Controls.Add(arrowLabel);

        foreach (Control child in panel.Controls)
        {
            child.Click += (_, _) => ShowUnitMenu(panel);
            child.Cursor = Cursors.Hand;
        }

        return panel;
    }

    private void SelectProfile(string profileId, params Panel[] tiles)
    {
        if (_updatingProfileSelection)
        {
            return;
        }

        _updatingProfileSelection = true;
        try
        {
            _selectedProfileId = profileId;
            _radioHigh.Checked = profileId == CompressImageSettings.ProfileHigh;
            _radioBalanced.Checked = profileId == CompressImageSettings.ProfileBalanced;
            _radioSmall.Checked = profileId == CompressImageSettings.ProfileSmall;

            foreach (var tile in tiles)
            {
                if (tile.Tag is string tileProfileId)
                {
                    tile.BackColor = string.Equals(tileProfileId, _selectedProfileId, StringComparison.OrdinalIgnoreCase)
                        ? FrameShiftTheme.AccentSoft
                        : FrameShiftTheme.Surface;
                }

                tile.Invalidate();
            }
        }
        finally
        {
            _updatingProfileSelection = false;
        }
    }

    private void UpdateTargetAvailability()
    {
        var format = ((_formatCombo.SelectedItem as string) ?? "PNG").Trim().ToLowerInvariant();
        var supportsTarget = CompressImageSettings.IsTargetSizeSupportedForFormat(format);

        if (!supportsTarget)
        {
            _checkTarget.Checked = false;
        }

        _checkTarget.Enabled = supportsTarget;
        _checkTarget.ForeColor = supportsTarget ? FrameShiftTheme.TextPrimary : FrameShiftTheme.TextMuted;
        _textTarget.ReadOnly = !_checkTarget.Checked;
        _textTarget.ForeColor = supportsTarget && _checkTarget.Checked ? FrameShiftTheme.TextPrimary : FrameShiftTheme.TextMuted;
        _unitSelector.Enabled = supportsTarget && _checkTarget.Checked;
        _unitSelectorLabel.ForeColor = supportsTarget && _checkTarget.Checked ? FrameShiftTheme.TextPrimary : FrameShiftTheme.TextMuted;
        _unitArrowLabel.ForeColor = supportsTarget && _checkTarget.Checked ? FrameShiftTheme.TextSecondary : FrameShiftTheme.TextMuted;
        _targetHint.Text = supportsTarget ? "Best-effort target" : "Only available for JPG/WEBP";
    }

    private void SetSelectedUnit(string unit)
    {
        _selectedUnit = unit;
        _unitSelectorLabel.Text = unit;
        _unitMenu.Close();
    }

    private void ShowUnitMenu(Control anchor)
    {
        if (!anchor.Enabled)
        {
            return;
        }

        _unitMenu.Show(anchor, new Point(0, anchor.Height));
    }

    private bool TryParseTargetBytes(out long targetBytes)
    {
        targetBytes = 0;
        var raw = _textTarget.Text.Trim().Replace(',', '.');
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return false;
        }

        targetBytes = _selectedUnit == "MB"
            ? (long)Math.Round(value * 1_048_576d)
            : (long)Math.Round(value * 1024d);
        return targetBytes > 0;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1L << 30)
        {
            return $"{Math.Round(bytes / (double)(1L << 30), 2):0.##} GB";
        }

        if (bytes >= 1L << 20)
        {
            return $"{Math.Round(bytes / (double)(1L << 20), 2):0.##} MB";
        }

        return $"{Math.Round(bytes / 1024d):0} KB";
    }
}
