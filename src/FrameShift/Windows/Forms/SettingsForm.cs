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
    private readonly string _uiSettingsPath;
    private readonly Label _languageHint;
    private bool _synchronizingMenus;

    public SettingsForm() : this(FrameShiftUiSettings.ConfigFilePath, ExplorerMenuLanguage.SynchronizeAsync)
    {
    }

    internal SettingsForm(string uiSettingsPath, Func<string, Task<ExplorerMenuSyncResult>> synchronizeMenus)
    {
        _uiSettingsPath = uiSettingsPath;
        var uiSettings = FrameShiftUiSettings.Load(_uiSettingsPath);
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(620, 440), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, L.Get("SettingsForm_FrameShift_Settings_c3e76047"));
        var header = FrameShiftUiFactory.CreateHeader(L.Get("SettingsForm_FrameShift_Settings_c3e76047"), L.Get("SettingsForm_Models_and_appearance_c659a74d"),
            IconPaths.AppIcon, IconPaths.AppIcon, "⚙");
        _pathLabel = FrameShiftUiFactory.CreateWrappingLabel(AiModelSettings.Load().GetEffectiveModelsDirectory());
        _pathLabel.Name = "modelsDirectory";
        var browse = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("SettingsForm_Browse_2a9dd9af"), false);
        var reset = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("SettingsForm_Reset_to_default_51536f93"), false);
        var open = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("SettingsForm_Open_folder_20cc8e52"), false);
        browse.Click += (_, _) => BrowseModelsFolder();
        reset.Click += (_, _) => ResetModelsFolder();
        open.Click += (_, _) => OpenModelsFolder();
        var models = FrameShiftUiFactory.CreateSection(L.Get("SettingsForm_AI_models_folder_9207080a"), FrameShiftUiFactory.CreateVerticalStack(
            _pathLabel,
            FrameShiftUiFactory.CreateWrappingLabel(L.Get("SettingsForm_Models_are_downloaded_on_first_use_a_67a0b93c")),
            FrameShiftUiFactory.CreateChoiceRow(browse, reset, open)));
        _themeSelector = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Name = "themePreference" };
        _themeSelector.Items.AddRange(new object[]
        {
            FrameShiftThemePreference.System, FrameShiftThemePreference.Light, FrameShiftThemePreference.Dark
        });
        _themeSelector.FormattingEnabled = true;
        _themeSelector.Format += (_, e) => e.Value = e.ListItem switch
        {
            FrameShiftThemePreference.System => L.Get("Theme_System"),
            FrameShiftThemePreference.Light => L.Get("Theme_Light"),
            FrameShiftThemePreference.Dark => L.Get("Theme_Dark"),
            _ => e.ListItem?.ToString() ?? string.Empty
        };
        _themeSelector.SelectedItem = uiSettings.GetThemePreference();
        _themeSelector.SelectedIndexChanged += (_, _) => SaveThemePreference();
        _themeHint = FrameShiftUiFactory.CreateWrappingLabel(L.Get("SettingsForm_Changes_apply_immediately_System_fol_f99a2e75"));
        var appearance = FrameShiftUiFactory.CreateSection(L.Get("SettingsForm_Appearance_7bcd3803"), FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateFieldRow(L.Get("SettingsForm_Theme_780ffaa9"), _themeSelector, logicalEditorWidth: 180), _themeHint));
        var language = new ComboBox
        {
            Name = "languagePreference", DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(AppLanguage.NativeName)
        };
        language.Items.AddRange(AppLanguages.Supported.Cast<object>().ToArray());
        // Show the next-launch preference while the current UI keeps its active language.
        var preferredLanguage = string.IsNullOrWhiteSpace(uiSettings.Language)
            ? System.Globalization.CultureInfo.CurrentUICulture.Name
            : uiSettings.Language;
        language.SelectedItem = AppLanguages.Resolve(preferredLanguage);
        _languageHint = FrameShiftUiFactory.CreateWrappingLabel(L.Get("Settings_ExplorerLanguageHint"));
        _languageHint.Name = "explorerLanguageStatus";
        var close = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("SettingsForm_Close_6cf4a777"), true);
        language.SelectedIndexChanged += async (_, _) =>
        {
            if (_synchronizingMenus || language.SelectedItem is not AppLanguage selectedLanguage) return;
            var settings = FrameShiftUiSettings.Load(_uiSettingsPath);
            settings.Language = selectedLanguage.Code;
            _synchronizingMenus = true;
            language.Enabled = close.Enabled = false;
            try
            {
                if (!settings.TrySave(_uiSettingsPath))
                {
                    // Suppress the selection event while restoring the last saved
                    // preference, so retrying the same language remains possible.
                    language.SelectedItem = AppLanguages.Resolve(preferredLanguage);
                    _languageHint.Text = L.Get("Settings_LanguageSaveFailed");
                    return;
                }
                preferredLanguage = selectedLanguage.Code;
                _languageHint.Text = L.Get("Settings_ExplorerLanguageUpdating");
                var result = await synchronizeMenus(selectedLanguage.Code);
                _languageHint.Text = L.Get(result switch
                {
                    ExplorerMenuSyncResult.Updated => "Settings_ExplorerLanguageUpdated",
                    ExplorerMenuSyncResult.Cancelled => "Settings_ExplorerLanguageCancelled",
                    _ => "Settings_ExplorerLanguageFailed"
                });
            }
            catch (Exception ex)
            {
                FrameShift.Core.Logging.AppLogger.LogStatic($"Settings menu language synchronization failed: {ex}");
                _languageHint.Text = L.Get("Settings_ExplorerLanguageFailed");
            }
            finally
            {
                _synchronizingMenus = false;
                language.Enabled = close.Enabled = true;
                if (IsHandleCreated)
                    FrameShiftDialogLayout.FitInitialHeight(this, Controls.OfType<TableLayoutPanel>().Single());
            }
        };
        var languageSection = FrameShiftUiFactory.CreateSection(L.Get("Settings_Language"),
            FrameShiftUiFactory.CreateVerticalStack(language, _languageHint));
        FormClosing += (_, e) => { if (_synchronizingMenus) e.Cancel = true; };
        close.DialogResult = DialogResult.Cancel;
        close.Click += (_, _) => Close();
        AcceptButton = CancelButton = close;
        var layout = FrameShiftDialogLayout.Create(header, FrameShiftUiFactory.CreateVerticalStack(models, appearance, languageSection),
            FrameShiftDialogLayout.CreateActions(close));
        Controls.Add(layout);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, layout);
        ResumeLayout(true);
    }
    private void BrowseModelsFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = L.Get("SettingsForm_Select_AI_models_folder_3670efb2"),
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
                L.Get("SettingsForm_Choose_a_dedicated_models_folder_Dri_8e51b3e8"),
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
                L.Format("SettingsForm_Could_not_open_folder_0_e5457a1b",ex.Message),
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

        var settings = FrameShiftUiSettings.Load(_uiSettingsPath);
        settings.Theme = preference.ToString();
        settings.Save(_uiSettingsPath);
        FrameShiftTheme.ApplyPreference(preference);
        _themeHint.Text = L.Get("SettingsForm_Changes_apply_immediately_System_fol_f99a2e75");
    }

}
