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
        _initialDirectory = initialDirectory;

        FrameShiftWindowChrome.Apply(this, "FrameShift - Add Subtitles to Video", IconPaths.AddSubtitlesVideoAiIcon, IconPaths.AppIcon);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = FrameShiftTheme.PageBackground;
        ClientSize = new Size(560, 420);

        Controls.Add(FrameShiftUiFactory.CreateFixedHeader(
            "FrameShift - Add Subtitles to Video",
            $"Source: {sourceLabel}",
            IconPaths.AddSubtitlesVideoAiIcon,
            IconPaths.FrameShiftAiIcon,
            "S"));

        var modeSection = FrameShiftUiFactory.CreateFixedSection(new Point(12, 82), new Size(536, 118), "Mode");
        Controls.Add(modeSection);

        _selectableTrackRadio = new RadioButton
        {
            Location = new Point(18, 32),
            Size = new Size(500, 22),
            Text = "Selectable Subtitle Track",
            ForeColor = FrameShiftTheme.TextPrimary,
            Checked = initialMode == AddSubtitlesToVideoMode.SelectableTrack
        };
        _selectableTrackRadio.CheckedChanged += (_, _) => RefreshSubtitleFormatHint();
        modeSection.Controls.Add(_selectableTrackRadio);

        modeSection.Controls.Add(new Label
        {
            Location = new Point(40, 54),
            Size = new Size(478, 16),
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = "Adds a subtitle track without re-encoding video or audio in the normal case."
        });

        _burnIntoVideoRadio = new RadioButton
        {
            Location = new Point(18, 76),
            Size = new Size(500, 22),
            Text = "Burn Subtitles Into Video",
            ForeColor = FrameShiftTheme.TextPrimary,
            Checked = initialMode == AddSubtitlesToVideoMode.BurnIntoVideo
        };
        _burnIntoVideoRadio.CheckedChanged += (_, _) => RefreshSubtitleFormatHint();
        modeSection.Controls.Add(_burnIntoVideoRadio);

        modeSection.Controls.Add(new Label
        {
            Location = new Point(40, 98),
            Size = new Size(478, 16),
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = "Renders subtitles into the image and re-encodes the video."
        });

        var subtitleSection = FrameShiftUiFactory.CreateFixedSection(new Point(12, 212), new Size(536, 106), "Subtitle File");
        Controls.Add(subtitleSection);

        _subtitlePathTextBox = FrameShiftUiFactory.CreateValueTextBox(readOnly: true);
        _subtitlePathTextBox.Text = initialSubtitleFilePath ?? string.Empty;
        subtitleSection.Controls.Add(FrameShiftUiFactory.CreateFixedTextInputHost(_subtitlePathTextBox, new Point(18, 34), new Size(384, 32)));

        var browseButton = FrameShiftUiFactory.CreateFixedActionButton("Browse...", new Point(412, 34), new Size(106, 32), primary: false);
        browseButton.Click += (_, _) => BrowseSubtitleFile();
        subtitleSection.Controls.Add(browseButton);

        _subtitleFormatsLabel = new Label
        {
            Location = new Point(18, 74),
            Size = new Size(500, 18),
            ForeColor = FrameShiftTheme.TextSecondary
        };
        subtitleSection.Controls.Add(_subtitleFormatsLabel);

        var infoCard = FrameShiftUiFactory.CreateFixedInfoCard(new Point(12, 330), new Size(536, 42));
        Controls.Add(infoCard);
        infoCard.Controls.Add(new Label
        {
            Location = new Point(12, 10),
            Size = new Size(512, 20),
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = "FrameShift always writes a unique output next to the source video and cleans partial files on failure."
        });

        var cancelButton = FrameShiftUiFactory.CreateFixedActionButton("Cancel", new Point(278, 380), new Size(120, 34), primary: false);
        cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);

        var applyButton = FrameShiftUiFactory.CreateFixedActionButton("Continue", new Point(408, 380), new Size(140, 34), primary: true);
        applyButton.DialogResult = DialogResult.OK;
        applyButton.Click += (_, e) =>
        {
            if (!ValidateSelection())
            {
                DialogResult = DialogResult.None;
            }
        };
        Controls.Add(applyButton);

        AcceptButton = applyButton;
        CancelButton = cancelButton;

        RefreshSubtitleFormatHint();
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
