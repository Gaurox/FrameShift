using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

internal sealed class AddSubtitlesToVideoPickerForm : Form
{
    private readonly RadioButton _selectableTrackRadio;
    private readonly RadioButton _burnIntoVideoRadio;
    private readonly TextBox _subtitlePathTextBox;
    private readonly Label _subtitleFormatsLabel;
    private readonly string? _initialDirectory;

    public AddSubtitlesToVideoPickerForm(
        string sourceLabel,
        AddSubtitlesToVideoMode initialMode,
        string? initialSubtitleFilePath,
        string? initialDirectory)
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(680, 500), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, L.Get("AddSubtitlesToVideoPickerForm_FrameShift_Add_Subtitles_to_Video_e0d2fd13"));
        _initialDirectory = initialDirectory;
        var header = FrameShiftUiFactory.CreateHeader(L.Get("AddSubtitlesToVideoPickerForm_FrameShift_Add_Subtitles_to_Video_e0d2fd13"), L.Format("AddSubtitlesToVideoPickerForm_Source_0_653ab450",sourceLabel),
            IconPaths.AddSubtitlesVideoAiIcon, IconPaths.FrameShiftAiIcon, "S");
        _selectableTrackRadio = new RadioButton { Name = "selectableTrack", Text = L.Get("AddSubtitlesToVideoPickerForm_Selectable_Subtitle_Track_94e43d8c"), AutoSize = true,
            Checked = initialMode == AddSubtitlesToVideoMode.SelectableTrack };
        _burnIntoVideoRadio = new RadioButton { Name = "burnIntoVideo", Text = L.Get("AddSubtitlesToVideoPickerForm_Burn_Subtitles_Into_Video_50d88f09"), AutoSize = true,
            Checked = initialMode == AddSubtitlesToVideoMode.BurnIntoVideo };
        var modes = FrameShiftUiFactory.CreateSection(L.Get("AddSubtitlesToVideoPickerForm_Mode_f5e5ac60"), FrameShiftUiFactory.CreateVerticalStack(
            _selectableTrackRadio, FrameShiftUiFactory.CreateWrappingLabel(L.Get("AddSubtitlesToVideoPickerForm_Adds_a_subtitle_track_without_re_enc_50a5ea6c")),
            _burnIntoVideoRadio, FrameShiftUiFactory.CreateWrappingLabel(L.Get("AddSubtitlesToVideoPickerForm_Renders_subtitles_into_the_image_and_d585b9ee"))));
        _subtitlePathTextBox = new TextBox { Name = "subtitlePath", ReadOnly = true, Text = initialSubtitleFilePath ?? "" };
        var browse = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("AddSubtitlesToVideoPickerForm_Browse_2a9dd9af"), false);
        browse.Click += (_, _) => BrowseSubtitleFile();
        _subtitleFormatsLabel = FrameShiftUiFactory.CreateWrappingLabel("");
        var content = FrameShiftUiFactory.CreateVerticalStack(modes,
            FrameShiftUiFactory.CreateSection(L.Get("AddSubtitlesToVideoPickerForm_Subtitle_file_c2ef60a6"), FrameShiftUiFactory.CreateVerticalStack(
                FrameShiftUiFactory.CreateFieldRow(L.Get("AddSubtitlesToVideoPickerForm_File_cdda298d"), _subtitlePathTextBox), browse, _subtitleFormatsLabel)));
        _selectableTrackRadio.CheckedChanged += (_, _) => RefreshSubtitleFormatHint();
        _burnIntoVideoRadio.CheckedChanged += (_, _) => RefreshSubtitleFormatHint();
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("AddSubtitlesToVideoPickerForm_Cancel_998b9c48"), false);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Name = "cancelButton";
        var primary = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("AddSubtitlesToVideoPickerForm_Continue_fd19155b"), true);
        primary.Name = "primaryButton";
        primary.DialogResult = DialogResult.OK;
        AcceptButton = primary;
        CancelButton = cancel;
        var layout = FrameShiftDialogLayout.Create(header, content,
            FrameShiftDialogLayout.CreateActions(cancel, primary),
            FrameShiftUiFactory.CreateStatusMessage(L.Get("AddSubtitlesToVideoPickerForm_Creates_a_new_file_next_to_the_sourc_57e3a3f7")));
        Controls.Add(layout);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, layout);
        primary.Click += (_, _) => { if (!ValidateSelection()) DialogResult = DialogResult.None; };
        RefreshSubtitleFormatHint();
        ResumeLayout(true);
    }

    public AddSubtitlesToVideoSettings SelectedSettings =>
        new(_subtitlePathTextBox.Text.Trim(), SelectedMode);

    private AddSubtitlesToVideoMode SelectedMode =>
        _burnIntoVideoRadio.Checked
            ? AddSubtitlesToVideoMode.BurnIntoVideo
            : AddSubtitlesToVideoMode.SelectableTrack;

    private void RefreshSubtitleFormatHint()
    {
        _subtitleFormatsLabel.Text = L.Format("AddSubtitlesToVideoPickerForm_Supported_files_0_92e5136e",AddSubtitlesToVideoSettings.GetSupportedSubtitleFormatsText(SelectedMode));
    }

    private void BrowseSubtitleFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = L.Get("AddSubtitlesToVideoPickerForm_Select_subtitle_file_12034b67"),
            Multiselect = false,
            CheckFileExists = true,
            Filter = BuildFilter(SelectedMode)
        };

        if (!string.IsNullOrWhiteSpace(_subtitlePathTextBox.Text))
        {
            try
            {
                dialog.InitialDirectory = Path.GetDirectoryName(_subtitlePathTextBox.Text);
            }
            catch
            {
            }
        }
        else if (!string.IsNullOrWhiteSpace(_initialDirectory))
        {
            dialog.InitialDirectory = _initialDirectory;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.FileName))
        {
            _subtitlePathTextBox.Text = dialog.FileName;
        }
    }

    private bool ValidateSelection()
    {
        var settings = SelectedSettings;
        if (string.IsNullOrWhiteSpace(settings.SubtitleFilePath))
        {
            MessageBox.Show(this, MediaActionMessages.AddSubtitlesToVideoSettingsMissing(), "FrameShift", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        if (!File.Exists(settings.SubtitleFilePath))
        {
            MessageBox.Show(this, MediaActionMessages.InputFileNotFound(settings.SubtitleFilePath), "FrameShift", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        if (!AddSubtitlesToVideoSettings.IsSupportedSubtitleFilePath(settings.SubtitleFilePath, settings.Mode))
        {
            var message = settings.Mode == AddSubtitlesToVideoMode.BurnIntoVideo
                ? MediaActionMessages.AddSubtitlesToVideoBurnSubtitleFormatInvalid()
                : MediaActionMessages.AddSubtitlesToVideoSelectableSubtitleFormatInvalid();
            MessageBox.Show(this, message, "FrameShift", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        return true;
    }

    private static string BuildFilter(AddSubtitlesToVideoMode mode)
    {
        return mode == AddSubtitlesToVideoMode.BurnIntoVideo
            ? L.Get("AddSubtitlesToVideoPickerForm_Subtitle_files_srt_ass_frameshift_su_208c0351")
            : L.Get("AddSubtitlesToVideoPickerForm_SubRip_subtitle_srt_srt_All_files_82a8dc5d");
    }
}
