using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class CompressVideoForm : Form
{
    private const string ProfileHigh = "high";
    private const string ProfileBalanced = "balanced";
    private const string ProfileSmall = "small";

    private readonly RadioButton _radioHigh;
    private readonly RadioButton _radioBalanced;
    private readonly RadioButton _radioSmall;
    private readonly CheckBox _checkTarget;
    private readonly TextBox _textTarget;
    private readonly Button _cancelButton;
    private readonly Button _compressButton;
    private Panel _unitSelector = null!;
    private Label _unitSelectorLabel = null!;
    private Label _unitArrowLabel = null!;
    private ContextMenuStrip _unitMenu = null!;
    private string _selectedProfileId = ProfileHigh;
    private string _selectedUnit = "MB";
    private bool _updatingProfileSelection;

    public CompressVideoForm(string sourcePath, string sourceExtension, long sourceBytes, string? resolutionText)
    {
        FrameShiftWindowChrome.Apply(this, "FrameShift - Compress Video");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 420);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var header = CreateHeader(sourcePath, sourceExtension, sourceBytes, resolutionText);
        Controls.Add(header);

        var presetSection = CreateSectionPanel("Compression level", new Point(12, 82), new Size(536, 124));
        Controls.Add(presetSection);

        var highTile = CreateProfileTile("High quality", "Preserves more detail", new Point(18, 32), 160, 52, ProfileHigh, true, out _radioHigh);
        var balancedTile = CreateProfileTile("Balanced", "Good balance", new Point(188, 32), 160, 52, ProfileBalanced, false, out _radioBalanced);
        var smallTile = CreateProfileTile("Small file", "Reduces size", new Point(358, 32), 160, 52, ProfileSmall, false, out _radioSmall);
        presetSection.Controls.Add(highTile);
        presetSection.Controls.Add(balancedTile);
        presetSection.Controls.Add(smallTile);

        var presetHint = new Label
        {
            AutoSize = false,
            Location = new Point(18, 92),
            Size = new Size(490, 18),
            ForeColor = FrameShiftTheme.AccentText,
            Text = "High quality preserves more detail. Small file reduces size more aggressively."
        };
        presetSection.Controls.Add(presetHint);

        _radioHigh.CheckedChanged += (_, _) => SelectProfile(ProfileHigh, highTile, balancedTile, smallTile);
        _radioBalanced.CheckedChanged += (_, _) => SelectProfile(ProfileBalanced, highTile, balancedTile, smallTile);
        _radioSmall.CheckedChanged += (_, _) => SelectProfile(ProfileSmall, highTile, balancedTile, smallTile);

        var targetSection = CreateSectionPanel("Optional target size", new Point(12, 218), new Size(536, 76));
        Controls.Add(targetSection);

        _checkTarget = new CheckBox
        {
            Text = "Target file size",
            Location = new Point(18, 31),
            Size = new Size(120, 22),
            ForeColor = FrameShiftTheme.TextPrimary,
            FlatStyle = FlatStyle.Standard
        };
        targetSection.Controls.Add(_checkTarget);

        _textTarget = FrameShiftUiFactory.CreateValueTextBox(readOnly: true);
        _textTarget.Dock = DockStyle.Fill;
        _textTarget.ForeColor = FrameShiftTheme.TextMuted;

        var targetSizeFrame = FrameShiftUiFactory.CreateFixedTextInputHost(_textTarget, new Point(138, 26), new Size(90, 30));
        targetSection.Controls.Add(targetSizeFrame);

        _unitSelector = CreateUnitSelector(new Point(238, 26), new Size(78, 30));
        targetSection.Controls.Add(_unitSelector);

        var targetHint = new Label
        {
            Location = new Point(332, 31),
            Size = new Size(188, 18),
            Text = "Best-effort target",
            ForeColor = FrameShiftTheme.AccentText
        };
        targetSection.Controls.Add(targetHint);

        _checkTarget.CheckedChanged += (_, _) =>
        {
            _textTarget.ReadOnly = !_checkTarget.Checked;
            _textTarget.ForeColor = _checkTarget.Checked ? FrameShiftTheme.TextPrimary : FrameShiftTheme.TextMuted;
            _unitSelectorLabel.ForeColor = _checkTarget.Checked ? FrameShiftTheme.TextPrimary : FrameShiftTheme.TextMuted;
            _unitArrowLabel.ForeColor = _checkTarget.Checked ? FrameShiftTheme.TextSecondary : FrameShiftTheme.TextMuted;
        };

        var infoCard = CreateInfoCard(new Point(12, 306), new Size(536, 44));
        Controls.Add(infoCard);

        var infoLabel = new Label
        {
            Location = new Point(16, 11),
            Size = new Size(504, 18),
            Text = "The compressed video is created next to the original file. The format stays the same.",
            ForeColor = FrameShiftTheme.TextSecondary
        };
        infoCard.Controls.Add(infoLabel);

        _cancelButton = CreateActionButton("Cancel", new Point(278, 374), new Size(120, 34), primary: false);
        _cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(_cancelButton);

        _compressButton = CreateActionButton("Compress", new Point(408, 374), new Size(140, 34), primary: true);
        _compressButton.DialogResult = DialogResult.OK;
        Controls.Add(_compressButton);

        AcceptButton = _compressButton;
        CancelButton = _cancelButton;
        SelectProfile(ProfileHigh, highTile, balancedTile, smallTile);
    }

    public string SelectedProfileId => _selectedProfileId;

    public long? TargetBytes { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FrameShiftUiLayout.AutoSizeAndPositionFooterButtons(this, _cancelButton, _compressButton);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            if (_checkTarget.Checked)
            {
                if (!TryParseTargetBytes(out var targetBytes))
                {
                    MessageBox.Show(
                        "Target file size must be a positive number.",
                        "FrameShift",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    e.Cancel = true;
                    return;
                }

                TargetBytes = targetBytes;
            }
            else
            {
                TargetBytes = null;
            }
        }

        base.OnFormClosing(e);
    }

    private static Control CreateHeader(string sourcePath, string sourceExtension, long sourceBytes, string? resolutionText)
    {
        var fileName = Path.GetFileName(sourcePath);
        return FrameShiftUiFactory.CreateFixedHeader(
            "FrameShift - Compress Video",
            $"{fileName}    Source: {sourceExtension.TrimStart('.').ToUpperInvariant()}    Size: {FormatFileSize(sourceBytes)}    Video: {resolutionText ?? "unknown"}",
            IconPaths.CompressVideoIcon,
            IconPaths.AppIcon,
            "▶");
    }

    private static Panel CreateSectionPanel(string title, Point location, Size size)
    {
        return FrameShiftUiFactory.CreateFixedSection(location, size, title);
    }

    private Panel CreateInfoCard(Point location, Size size)
    {
        return FrameShiftUiFactory.CreateFixedInfoCard(location, size);
    }

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
        tile.Paint += (_, e) => DrawTileBorder(tile, e.Graphics, _selectedProfileId == profileId);
        tile.BackColor = selected ? FrameShiftTheme.AccentSoft : FrameShiftTheme.Surface;

        localRadio.CheckedChanged += (_, _) => tile.Invalidate();
        tile.Controls.Add(localRadio);

        var titleLabel = new Label
        {
            AutoSize = true,
            Location = new Point(38, 10),
            Text = title,
            ForeColor = FrameShiftTheme.TextPrimary,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Regular, GraphicsUnit.Point)
        };
        tile.Controls.Add(titleLabel);

        var descriptionLabel = new Label
        {
            AutoSize = false,
            Location = new Point(38, 30),
            Size = new Size(width - 48, 16),
            Text = description,
            ForeColor = FrameShiftTheme.TextSecondary
        };
        tile.Controls.Add(descriptionLabel);

        foreach (Control child in tile.Controls)
        {
            child.Click += (_, _) => SelectProfile(profileId);
            child.Cursor = Cursors.Hand;
        }

        return tile;
    }

    private Panel CreateUnitSelector(Point location, Size size)
    {
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

        var panel = FrameShiftUiFactory.CreateFramedPanel(location, size, FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.InputCornerRadius);
        panel.Padding = new Padding(8, 5, 8, 5);
        panel.Cursor = Cursors.Hand;
        panel.Click += (_, _) => ShowUnitMenu(panel);

        _unitSelectorLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Left,
            Width = 34,
            Text = _selectedUnit,
            ForeColor = FrameShiftTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(_unitSelectorLabel);

        _unitArrowLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Right,
            Size = new Size(16, 16),
            Text = "▾",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = FrameShiftTheme.TextMuted,
            Cursor = Cursors.Hand
        };
        _unitArrowLabel.Click += (_, _) => ShowUnitMenu(panel);
        panel.Controls.Add(_unitArrowLabel);

        foreach (Control child in panel.Controls)
        {
            child.Click += (_, _) => ShowUnitMenu(panel);
            child.Cursor = Cursors.Hand;
        }

        return panel;
    }

    private static void DrawTileBorder(Panel tile, Graphics graphics, bool selected)
    {
        FrameShiftUiPainter.DrawRoundedBorder(tile, graphics, selected ? FrameShiftTheme.SecondaryBlue : FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.PanelCornerRadius, selected ? 2F : 1F);
    }

    private static void InvalidateProfileTiles(params Panel[] tiles)
    {
        foreach (var tile in tiles)
        {
            tile.Invalidate();
        }
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
            _radioHigh.Checked = profileId == ProfileHigh;
            _radioBalanced.Checked = profileId == ProfileBalanced;
            _radioSmall.Checked = profileId == ProfileSmall;

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
