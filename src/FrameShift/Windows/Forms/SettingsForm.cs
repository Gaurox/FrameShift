using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FrameShift.Core.AI;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

/// <summary>
/// Settings dialog opened from the main window. Currently hosts the AI models folder
/// controls that previously lived on the main window surface.
/// </summary>
public sealed class SettingsForm : Form
{
    private static readonly Font s_hintFont = new("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

    private readonly Label _pathLabel;

    public SettingsForm()
    {
        FrameShiftWindowChrome.Apply(this, "FrameShift settings");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(540, 214);
        BackColor = FrameShiftTheme.PageBackground;

        var section = FrameShiftUiFactory.CreateFramedPanel(
            FrameShiftTheme.Surface,
            FrameShiftTheme.PrimaryBlue,
            FrameShiftUiMetrics.PanelCornerRadius);
        section.Location = new Point(16, 16);
        section.Size = new Size(508, 150);
        section.Padding = new Padding(14, 12, 14, 12);

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = FrameShiftUiMetrics.SectionTitleHeight,
            Text = "AI models folder",
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = FrameShiftTheme.SecondaryBlue,
            Margin = Padding.Empty
        };

        var settings = AiModelSettings.Load();

        _pathLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 20,
            Text = settings.GetEffectiveModelsDirectory(),
            Font = s_hintFont,
            ForeColor = FrameShiftTheme.TextSecondary,
            AutoEllipsis = true,
            Margin = new Padding(0, 4, 0, 6)
        };

        var description = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Text = "Models are downloaded on first use and are never bundled with the installer.",
            Font = s_hintFont,
            ForeColor = FrameShiftTheme.TextMuted,
            AutoSize = false
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = FrameShiftTheme.Surface,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };

        var btnBrowse = CreateSecondaryButton("Browse…");
        var btnReset = CreateSecondaryButton("Reset to default");
        var btnOpen = CreateSecondaryButton("Open folder");
        btnBrowse.Click += (_, _) => BrowseModelsFolder();
        btnReset.Click += (_, _) => ResetModelsFolder();
        btnOpen.Click += (_, _) => OpenModelsFolder();
        buttons.Controls.Add(btnBrowse);
        buttons.Controls.Add(btnReset);
        buttons.Controls.Add(btnOpen);

        section.Controls.Add(buttons);
        section.Controls.Add(description);
        section.Controls.Add(_pathLabel);
        section.Controls.Add(title);

        var close = new Button
        {
            Text = "Close",
            Size = new Size(FrameShiftUiMetrics.PrimaryButtonWidth, FrameShiftUiMetrics.FooterButtonHeight),
            Location = new Point(ClientSize.Width - FrameShiftUiMetrics.PrimaryButtonWidth - 16, 174),
            FlatStyle = FlatStyle.Flat,
            BackColor = FrameShiftTheme.SecondaryBlue,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
        close.FlatAppearance.BorderColor = FrameShiftTheme.SecondaryBlue;
        close.Click += (_, _) => Close();
        AcceptButton = close;

        Controls.Add(section);
        Controls.Add(close);
    }

    private void BrowseModelsFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select AI models folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        var settings = AiModelSettings.Load();
        var current = settings.GetEffectiveModelsDirectory();
        if (Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (!AiModelDirectorySafety.TryNormalizeCustomDirectory(dialog.SelectedPath, out var modelsDirectory))
        {
            MessageBox.Show(
                "Choose a dedicated models folder. Drive roots, user-profile roots, Windows, Program Files, the FrameShift install folder, and their parents are not allowed.",
                "FrameShift",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        settings.ModelsDirectory = modelsDirectory;
        settings.Save();
        AiModelStorage.InvalidateCache();
        _pathLabel.Text = modelsDirectory;
    }

    private void ResetModelsFolder()
    {
        var settings = AiModelSettings.Load();
        settings.ModelsDirectory = null;
        settings.Save();
        AiModelStorage.InvalidateCache();
        _pathLabel.Text = settings.GetEffectiveModelsDirectory();
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

    private static Button CreateSecondaryButton(string text)
    {
        var button = new Button
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
            Padding = new Padding(10, 4, 10, 4),
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
        button.FlatAppearance.BorderColor = FrameShiftTheme.PrimaryBlue;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = FrameShiftTheme.AccentSoft;
        return button;
    }
}
