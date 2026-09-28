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

    private readonly Label _pathLabel;
    private readonly ComboBox _themeSelector;
    private readonly Label _themeHint;

    public SettingsForm()
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(620, 440), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, "FrameShift - Settings");
        var header = FrameShiftUiFactory.CreateHeader("FrameShift - Settings", "Models and appearance",
            IconPaths.AppIcon, IconPaths.AppIcon, "⚙");
        _pathLabel = FrameShiftUiFactory.CreateWrappingLabel(AiModelSettings.Load().GetEffectiveModelsDirectory());
        _pathLabel.Name = "modelsDirectory";
        var browse = FrameShiftUiFactory.CreateMeasuredActionButton("Browse…", false);
        var reset = FrameShiftUiFactory.CreateMeasuredActionButton("Reset to default", false);
        var open = FrameShiftUiFactory.CreateMeasuredActionButton("Open folder", false);
        browse.Click += (_, _) => BrowseModelsFolder();
        reset.Click += (_, _) => ResetModelsFolder();
        open.Click += (_, _) => OpenModelsFolder();
        var models = FrameShiftUiFactory.CreateSection("AI models folder", FrameShiftUiFactory.CreateVerticalStack(
            _pathLabel,
            FrameShiftUiFactory.CreateWrappingLabel("Models are downloaded on first use and are never bundled with the installer."),
            FrameShiftUiFactory.CreateChoiceRow(browse, reset, open)));
        _themeSelector = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Name = "themePreference" };
        _themeSelector.Items.AddRange(new object[]
        {
            FrameShiftThemePreference.System, FrameShiftThemePreference.Light, FrameShiftThemePreference.Dark
        });
        _themeSelector.SelectedItem = FrameShiftUiSettings.Load().GetThemePreference();
        _themeSelector.SelectedIndexChanged += (_, _) => SaveThemePreference();
        _themeHint = FrameShiftUiFactory.CreateWrappingLabel("Changes apply immediately. System follows Windows now.");
        var appearance = FrameShiftUiFactory.CreateSection("Appearance", FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateFieldRow("Theme", _themeSelector, logicalEditorWidth: 180), _themeHint));
        var close = FrameShiftUiFactory.CreateMeasuredActionButton("Close", true);
        close.DialogResult = DialogResult.Cancel;
        close.Click += (_, _) => Close();
        AcceptButton = CancelButton = close;
        var layout = FrameShiftDialogLayout.Create(header, FrameShiftUiFactory.CreateVerticalStack(models, appearance),
            FrameShiftDialogLayout.CreateActions(close));
        Controls.Add(layout);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, layout);
        ResumeLayout(true);
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

}
