using System;
using System.Drawing;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class MainForm : Form
{
    private static readonly Font s_titleFont     = new("Segoe UI Semibold", 20F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font s_subtitleFont  = new("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font s_tileTitleFont = new("Segoe UI Semibold", 11F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font s_tileBodyFont  = new("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font s_hintFont      = new("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

    public MainForm()
    {
        FrameShiftWindowChrome.Apply(this, "FrameShift");
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 440);
        Size = new Size(680, 480);
        BackColor = FrameShiftTheme.PageBackground;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 3,
            BackColor = FrameShiftTheme.PageBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildTileGrid(), 0, 1);
        root.Controls.Add(BuildHint(), 0, 2);

        Controls.Add(root);
    }

    private static Panel BuildHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FrameShiftTheme.PageBackground
        };

        var title = new Label
        {
            AutoSize = true,
            Location = new Point(0, 0),
            Text = "FrameShift",
            Font = s_titleFont,
            ForeColor = FrameShiftTheme.TextPrimary
        };

        var subtitle = new Label
        {
            AutoSize = true,
            Location = new Point(2, 36),
            Text = $"Local multimedia processing for Windows  ·  v{Application.ProductVersion}",
            Font = s_subtitleFont,
            ForeColor = FrameShiftTheme.TextMuted
        };

        panel.Controls.Add(title);
        panel.Controls.Add(subtitle);
        return panel;
    }

    private static TableLayoutPanel BuildTileGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = FrameShiftTheme.PageBackground,
            Padding = new Padding(0, 8, 0, 8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        var tiles = new[]
        {
            ("Video",    "Convert · Cut · Crop · GIF · Resize · Extract · Rotate · Interpolate"),
            ("Audio",    "Convert · Cut · Reverse · Pitch · Speed · Compress · Separate"),
            ("Image",    "Convert · Crop · Resize · Rotate · Compress · PDF · Icon"),
            ("AI tools", "Remove Background · Remove Noise · Separate Audio"),
        };

        for (var i = 0; i < tiles.Length; i++)
        {
            var (name, actions) = tiles[i];
            var col = i % 2;
            var row = i / 2;
            var tile = BuildTile(name, actions);
            tile.Margin = new Padding(
                col == 1 ? 6 : 0,
                row == 1 ? 6 : 0,
                col == 0 ? 6 : 0,
                0);
            grid.Controls.Add(tile, col, row);
        }

        return grid;
    }

    private static Panel BuildTile(string title, string actionsText)
    {
        var panel = FrameShiftUiFactory.CreateFramedPanel(
            FrameShiftTheme.Surface,
            FrameShiftTheme.PrimaryBlue,
            FrameShiftUiMetrics.PanelCornerRadius);
        panel.Dock = DockStyle.Fill;
        panel.Padding = new Padding(20, 14, 20, 14);

        var titleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Text = title,
            Font = s_tileTitleFont,
            ForeColor = FrameShiftTheme.TextPrimary
        };

        var actionsLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = actionsText,
            Font = s_tileBodyFont,
            ForeColor = FrameShiftTheme.TextSecondary,
            AutoEllipsis = true
        };

        panel.Controls.Add(actionsLabel);
        panel.Controls.Add(titleLabel);

        // Hover effect — propagate to child labels so the entire tile reacts
        void SetHover(bool hovered)
            => panel.BackColor = hovered ? FrameShiftTheme.AccentSoft : FrameShiftTheme.Surface;

        panel.MouseEnter       += (_, _) => SetHover(true);
        panel.MouseLeave       += (_, _) => SetHover(false);
        titleLabel.MouseEnter  += (_, _) => SetHover(true);
        titleLabel.MouseLeave  += (_, _) => SetHover(false);
        actionsLabel.MouseEnter += (_, _) => SetHover(true);
        actionsLabel.MouseLeave += (_, _) => SetHover(false);

        return panel;
    }

    private static Label BuildHint()
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = "Right-click files in Windows Explorer to use FrameShift.",
            Font = s_hintFont,
            ForeColor = FrameShiftTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }
}
