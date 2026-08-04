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
    private readonly ComboBox _themeSelector;
    private readonly Label _themeHint;

    public SettingsForm()
    {
        FrameShiftWindowChrome.Apply(this, "FrameShift settings");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(540, 330);
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
            ForeColor = FrameShiftTheme.AccentText,
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

        var appearanceSection = FrameShiftUiFactory.CreateFramedPanel(
            FrameShiftTheme.Surface,
            FrameShiftTheme.PrimaryBlue,
            FrameShiftUiMetrics.PanelCornerRadius);
        appearanceSection.Location = new Point(16, 174);
        appearanceSection.Size = new Size(508, 92);
        appearanceSection.Padding = new Padding(14, 12, 14, 12);

        var appearanceTitle = new Label
        {
            AutoSize = true,
            Location = new Point(14, 12),
            Text = "Appearance",
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = FrameShiftTheme.AccentText
        };

        _themeSelector = FrameShiftUiFactory.CreateFixedComboBox(new Point(14, 38), new Size(140, 28));
        _themeSelector.Items.AddRange(new object[]
        {
            FrameShiftThemePreference.System,
            FrameShiftThemePreference.Light,
            FrameShiftThemePreference.Dark
        });
        _themeSelector.SelectedItem = FrameShiftUiSettings.Load().GetThemePreference();
        _themeSelector.SelectedIndexChanged += (_, _) => SaveThemePreference();

        _themeHint = new Label
        {
            AutoSize = false,
            Location = new Point(168, 38),
            Size = new Size(310, 28),
            Text = "Changes apply immediately. System follows Windows now.",
            Font = s_hintFont,
            ForeColor = FrameShiftTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        };

        appearanceSection.Controls.Add(appearanceTitle);
        appearanceSection.Controls.Add(_themeSelector);
        appearanceSection.Controls.Add(_themeHint);

        var close = new Button
        {
            Text = "Close",
            Size = new Size(FrameShiftUiMetrics.PrimaryButtonWidth, FrameShiftUiMetrics.FooterButtonHeight),
            Location = new Point(ClientSize.Width - FrameShiftUiMetrics.PrimaryButtonWidth - 16, 280),
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
        Controls.Add(appearanceSection);
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

    private void SaveThemePreference()
    {
        if (_themeSelector.SelectedItem is not FrameShiftThemePreference preference)
        {
            return;
        }

        var settings = FrameShiftUiSettings.Load();
        settings.Theme = preference.ToString();
        settings.Save();
        FrameShiftTheme.ApplyPreference(preference);
        _themeHint.Text = "Changes apply immediately. System follows Windows now.";
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
            ForeColor = FrameShiftTheme.AccentText,
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
