using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FrameShift.Core.AI;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class MainForm : Form
{
    private static readonly Font s_titleFont     = new("Segoe UI Semibold", 20F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font s_subtitleFont  = new("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font s_tileTitleFont = new("Segoe UI Semibold", 11F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font s_tileBodyFont  = new("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font s_hintFont      = new("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly string[] _startupPaths;

    private Label? _pathLabel;

    public MainForm()
        : this(Array.Empty<string>())
    {
    }

    public MainForm(IEnumerable<string> startupPaths)
    {
        _startupPaths = startupPaths?
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray() ?? [];

        FrameShiftWindowChrome.Apply(this, "FrameShift");
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 540);
        Size = new Size(680, 580);
        BackColor = FrameShiftTheme.PageBackground;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = FrameShiftTheme.PageBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));

        root.Controls.Add(BuildHeader(_startupPaths), 0, 0);
        root.Controls.Add(BuildTileGrid(), 0, 1);
        root.Controls.Add(BuildModelsSection(), 0, 2);
        root.Controls.Add(BuildHint(_startupPaths), 0, 3);

        Controls.Add(root);
    }

    private Panel BuildModelsSection()
    {
        var section = FrameShiftUiFactory.CreateFramedPanel(
            FrameShiftTheme.Surface,
            FrameShiftTheme.PrimaryBlue,
            FrameShiftUiMetrics.PanelCornerRadius);
        section.Dock = DockStyle.Fill;
        section.Padding = new Padding(12, 10, 12, 10);

        var titleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = FrameShiftUiMetrics.SectionTitleHeight,
            Text = "AI models folder",
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = FrameShiftTheme.SecondaryBlue,
            Margin = Padding.Empty
        };

        var settings = AiModelSettings.Load();
        var effectivePath = settings.GetEffectiveModelsDirectory();

        _pathLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 18,
            Text = effectivePath,
            Font = s_hintFont,
            ForeColor = FrameShiftTheme.TextSecondary,
            AutoEllipsis = true,
            Margin = new Padding(0, 2, 0, 6)
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = FrameShiftTheme.Surface,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };

        var btnBrowse = CreateSecondaryButton("Browse...");
        var btnReset  = CreateSecondaryButton("Reset to default");
        var btnOpen   = CreateSecondaryButton("Open folder");

        btnBrowse.Click += (_, _) => BrowseModelsFolder();
        btnReset.Click  += (_, _) => ResetModelsFolder();
        btnOpen.Click   += (_, _) => OpenModelsFolder();

        buttonPanel.Controls.Add(btnBrowse);
        buttonPanel.Controls.Add(btnReset);
        buttonPanel.Controls.Add(btnOpen);

        section.Controls.Add(buttonPanel);
        section.Controls.Add(_pathLabel);
        section.Controls.Add(titleLabel);

        return section;
    }

    private void BrowseModelsFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select AI models folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        var settings = AiModelSettings.Load();
        var current = settings.GetEffectiveModelsDirectory();
        if (Directory.Exists(current))
            dlg.InitialDirectory = current;

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        settings.ModelsDirectory = dlg.SelectedPath;
        settings.Save();
        AiModelStorage.InvalidateCache();

        UpdatePathLabel(dlg.SelectedPath);
    }

    private void ResetModelsFolder()
    {
        var settings = AiModelSettings.Load();
        settings.ModelsDirectory = null;
        settings.Save();
        AiModelStorage.InvalidateCache();

        var effective = settings.GetEffectiveModelsDirectory();
        UpdatePathLabel(effective);
    }

    private void OpenModelsFolder()
    {
        var settings = AiModelSettings.Load();
        var path = settings.GetEffectiveModelsDirectory();
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open folder:\n{ex.Message}",
                "FrameShift",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void UpdatePathLabel(string path)
    {
        if (_pathLabel is not null)
            _pathLabel.Text = path;
    }

    private static Button CreateSecondaryButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            Height = 30,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.SecondaryBlue,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 8, 0),
            Padding = new Padding(10, 4, 10, 4)
        };
        btn.FlatAppearance.BorderColor = FrameShiftTheme.PrimaryBlue;
        btn.FlatAppearance.BorderSize  = 1;
        btn.FlatAppearance.MouseOverBackColor = FrameShiftTheme.AccentSoft;
        btn.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point);
        return btn;
    }

    private static Panel BuildHeader(IReadOnlyList<string> startupPaths)
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
            Text = BuildSubtitle(startupPaths),
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
            ("Video",    "Convert · Cut · Crop · GIF · Resize · Extract · Rotate · Interpolate · Subtitles"),
            ("Audio",    "Convert · Cut · Reverse · Pitch · Speed · Compress · Separate"),
            ("Image",    "Convert · Crop · Resize · Rotate · Compress · PDF · Icon"),
            ("AI tools", "Remove Background · Remove Noise · Separate Audio · RIFE Interpolate · Create Subtitle File"),
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

    private static Label BuildHint(IReadOnlyList<string> startupPaths)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = BuildHintText(startupPaths),
            Font = s_hintFont,
            ForeColor = FrameShiftTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static string BuildSubtitle(IReadOnlyList<string> startupPaths)
    {
        if (startupPaths.Count == 0)
            return $"Local multimedia processing for Windows  ·  v{Application.ProductVersion}";

        return $"{FormatSelectionLabel(startupPaths)}  ·  v{Application.ProductVersion}";
    }

    private static string BuildHintText(IReadOnlyList<string> startupPaths)
    {
        if (startupPaths.Count == 0)
            return "Right-click files in Windows Explorer to use FrameShift.";

        return "UI launched from a file selection. Action routing is not wired here yet.";
    }

    private static string FormatSelectionLabel(IReadOnlyList<string> startupPaths)
    {
        if (startupPaths.Count == 1)
            return $"Selected: {Path.GetFileName(startupPaths[0])}";

        return $"{startupPaths.Count} selected items";
    }
}
