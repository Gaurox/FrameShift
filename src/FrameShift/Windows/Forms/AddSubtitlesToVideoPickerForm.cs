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
        FrameShiftWindowChrome.Apply(this, "FrameShift - Add Subtitles to Video");
        _initialDirectory = initialDirectory;
        var header = FrameShiftUiFactory.CreateHeader("FrameShift - Add Subtitles to Video", $"Source: {sourceLabel}",
            IconPaths.AddSubtitlesVideoAiIcon, IconPaths.FrameShiftAiIcon, "S");
        _selectableTrackRadio = new RadioButton { Name = "selectableTrack", Text = "Selectable Subtitle Track", AutoSize = true,
            Checked = initialMode == AddSubtitlesToVideoMode.SelectableTrack };
        _burnIntoVideoRadio = new RadioButton { Name = "burnIntoVideo", Text = "Burn Subtitles Into Video", AutoSize = true,
            Checked = initialMode == AddSubtitlesToVideoMode.BurnIntoVideo };
        var modes = FrameShiftUiFactory.CreateSection("Mode", FrameShiftUiFactory.CreateVerticalStack(
            _selectableTrackRadio, FrameShiftUiFactory.CreateWrappingLabel("Adds a subtitle track without re-encoding video or audio in the normal case."),
            _burnIntoVideoRadio, FrameShiftUiFactory.CreateWrappingLabel("Renders subtitles into the image and re-encodes the video.")));
        _subtitlePathTextBox = new TextBox { Name = "subtitlePath", ReadOnly = true, Text = initialSubtitleFilePath ?? "" };
        var browse = FrameShiftUiFactory.CreateMeasuredActionButton("Browse…", false);
        browse.Click += (_, _) => BrowseSubtitleFile();
        _subtitleFormatsLabel = FrameShiftUiFactory.CreateWrappingLabel("");
        var content = FrameShiftUiFactory.CreateVerticalStack(modes,
            FrameShiftUiFactory.CreateSection("Subtitle file", FrameShiftUiFactory.CreateVerticalStack(
                FrameShiftUiFactory.CreateFieldRow("&File", _subtitlePathTextBox), browse, _subtitleFormatsLabel)));
        _selectableTrackRadio.CheckedChanged += (_, _) => RefreshSubtitleFormatHint();
        _burnIntoVideoRadio.CheckedChanged += (_, _) => RefreshSubtitleFormatHint();
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Name = "cancelButton";
        var primary = FrameShiftUiFactory.CreateMeasuredActionButton("Continue", true);
        primary.Name = "primaryButton";
        primary.DialogResult = DialogResult.OK;
        AcceptButton = primary;
        CancelButton = cancel;
        var layout = FrameShiftDialogLayout.Create(header, content,
            FrameShiftDialogLayout.CreateActions(cancel, primary),
            FrameShiftUiFactory.CreateStatusMessage("Creates a new file next to the source video. The original is preserved."));
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
        _subtitleFormatsLabel.Text = $"Supported files: {AddSubtitlesToVideoSettings.GetSupportedSubtitleFormatsText(SelectedMode)}";
    }

    private void BrowseSubtitleFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select subtitle file",
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
            ? "Subtitle files (*.srt;*.ass;*.frameshift-subtitles.json)|*.srt;*.ass;*.frameshift-subtitles.json|All files (*.*)|*.*"
            : "SubRip subtitle (*.srt)|*.srt|All files (*.*)|*.*";
    }
}
