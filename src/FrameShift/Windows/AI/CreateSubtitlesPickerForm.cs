using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FrameShift.Core.AI.CreateSubtitles;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.AI;

internal sealed class CreateSubtitlesPickerForm : Form
{
    private const int CompactClientHeight = 594;
    private const int ExpandedClientHeight = 754;

    private string _selectedModelId;
    private CreateSubtitlesOutputFormat _selectedOutputFormat;
    private CreateSubtitlesAssPreset _selectedAssPreset;
    private readonly Panel _assPresetSection;
    private readonly Panel _infoCard;
    private readonly Button _cancelButton;
    private readonly Button _createButton;

    public CreateSubtitlesPickerForm(
        string actionTitle,
        string sourceLabel,
        CreateSubtitlesOutputFormat initialOutputFormat = CreateSubtitlesOutputFormat.StandardSrt,
        CreateSubtitlesAssPreset initialAssPreset = CreateSubtitlesAssPreset.Classic)
    {
        var models = CreateSubtitlesModelCatalog.GetAll();
        var outputFormats = CreateSubtitlesOutputFormats.GetAll();
        var assPresets = CreateSubtitlesAssPresets.GetAll();
        _selectedModelId = CreateSubtitlesModelCatalog.GetDefault().Id;
        _selectedOutputFormat = initialOutputFormat;
        _selectedAssPreset = initialAssPreset;

        FrameShiftWindowChrome.Apply(this, $"FrameShift - {actionTitle}", IconPaths.CreateSubtitlesAiIcon, IconPaths.AppIcon);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = FrameShiftTheme.PageBackground;
        ClientSize = new Size(560, CompactClientHeight);

        Controls.Add(FrameShiftUiFactory.CreateFixedHeader(
            $"FrameShift - {actionTitle}",
            $"Source: {sourceLabel}",
            IconPaths.CreateSubtitlesAiIcon,
            IconPaths.FrameShiftAiIcon,
            "AI"));

        var modelSection = FrameShiftUiFactory.CreateFixedSection(new Point(12, 82), new Size(536, 208), "Model");
        Controls.Add(modelSection);

        for (var i = 0; i < models.Count; i++)
        {
            var model = models[i];
            var rowY = 30 + i * 58;

            var radio = new RadioButton
            {
                Location = new Point(18, rowY),
                Size = new Size(500, 22),
                Text = model.DisplayName,
                ForeColor = FrameShiftTheme.TextPrimary,
                Checked = model.Id == _selectedModelId,
                Tag = model.Id,
                AutoSize = false
            };
            var capturedId = model.Id;
            radio.CheckedChanged += (_, _) => { if (radio.Checked) _selectedModelId = capturedId; };
            modelSection.Controls.Add(radio);

            var descLabel = new Label
            {
                Location = new Point(40, rowY + 24),
                Size = new Size(478, 17),
                ForeColor = FrameShiftTheme.TextSecondary,
                Text = BuildRowDescription(model)
            };
            modelSection.Controls.Add(descLabel);
        }

        var outputSection = FrameShiftUiFactory.CreateFixedSection(new Point(12, 302), new Size(536, 148), "Output");
        Controls.Add(outputSection);

        for (var i = 0; i < outputFormats.Count; i++)
        {
            var outputFormat = outputFormats[i];
            var rowY = 30 + i * 38;

            var radio = new RadioButton
            {
                Location = new Point(18, rowY),
                Size = new Size(500, 22),
                Text = outputFormat.GetDisplayName(),
                ForeColor = FrameShiftTheme.TextPrimary,
                Checked = outputFormat == _selectedOutputFormat,
                AutoSize = false
            };
            var capturedOutputFormat = outputFormat;
            radio.CheckedChanged += (_, _) =>
            {
                if (radio.Checked)
                {
                    _selectedOutputFormat = capturedOutputFormat;
                    UpdateAssPresetVisibility();
                }
            };
            outputSection.Controls.Add(radio);

            outputSection.Controls.Add(new Label
            {
                Location = new Point(40, rowY + 20),
                Size = new Size(478, 16),
                ForeColor = FrameShiftTheme.TextSecondary,
                Text = outputFormat.GetDescription()
            });
        }

        _assPresetSection = FrameShiftUiFactory.CreateFixedSection(new Point(12, 462), new Size(536, 148), "ASS preset");
        Controls.Add(_assPresetSection);

        for (var i = 0; i < assPresets.Count; i++)
        {
            var assPreset = assPresets[i];
            var rowY = 30 + i * 38;

            var radio = new RadioButton
            {
                Location = new Point(18, rowY),
                Size = new Size(500, 22),
                Text = assPreset.GetDisplayName(),
                ForeColor = FrameShiftTheme.TextPrimary,
                Checked = assPreset == _selectedAssPreset,
                AutoSize = false
            };
            var capturedAssPreset = assPreset;
            radio.CheckedChanged += (_, _) =>
            {
                if (radio.Checked)
                {
                    _selectedAssPreset = capturedAssPreset;
                }
            };
            _assPresetSection.Controls.Add(radio);

            _assPresetSection.Controls.Add(new Label
            {
                Location = new Point(40, rowY + 20),
                Size = new Size(478, 16),
                ForeColor = FrameShiftTheme.TextSecondary,
                Text = assPreset.GetDescription()
            });
        }

        _infoCard = FrameShiftUiFactory.CreateFixedInfoCard(new Point(12, 462), new Size(536, 68));
        Controls.Add(_infoCard);
        _infoCard.Controls.Add(new Label
        {
            Location = new Point(12, 8),
            Size = new Size(512, 48),
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = "FrameShift prepares mono 16 kHz audio, transcribes it locally with Whisper, then writes a unique subtitle file next to the source."
        });

        _cancelButton = FrameShiftUiFactory.CreateFixedActionButton("Cancel", new Point(278, 540), new Size(120, 34), primary: false);
        _cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(_cancelButton);

        _createButton = FrameShiftUiFactory.CreateFixedActionButton("Create File", new Point(408, 540), new Size(140, 34), primary: true);
        _createButton.DialogResult = DialogResult.OK;
        Controls.Add(_createButton);

        AcceptButton = _createButton;
        CancelButton = _cancelButton;

        UpdateAssPresetVisibility();
    }

    public string SelectedModelId => _selectedModelId;

    public CreateSubtitlesOutputFormat SelectedOutputFormat => _selectedOutputFormat;

    public CreateSubtitlesAssPreset SelectedAssPreset => _selectedAssPreset;

    internal bool IsAssPresetSectionVisible => _selectedOutputFormat == CreateSubtitlesOutputFormat.AdvancedAss;

    public static string BuildSourceLabel(IReadOnlyList<string> inputPaths)
    {
        if (inputPaths.Count <= 1)
        {
            return Path.GetFileName(inputPaths[0]);
        }

        return $"{inputPaths.Count} selected files";
    }

    private static string BuildRowDescription(CreateSubtitlesModelDefinition model)
    {
        var size = FormatDownloadSize(model.ExpectedTotalSizeBytes);
        return model.Id switch
        {
            "whisper-base" => $"{size} · CPU only · Good for testing and short clips",
            "whisper-small" => $"{size} · CPU only · Best balance of speed and accuracy",
            "whisper-turbo" => $"{size} · CPU only · Highest accuracy — allow extra processing time on CPU",
            _ => $"{size} · CPU only"
        };
    }

    private static string FormatDownloadSize(long bytes)
    {
        const double gb = 1_073_741_824d;
        const double mb = 1_048_576d;
        return bytes >= gb
            ? $"~{bytes / gb:F1} GB"
            : $"~{(long)Math.Round(bytes / mb)} MB";
    }

    private void UpdateAssPresetVisibility()
    {
        var showAssPresets = _selectedOutputFormat == CreateSubtitlesOutputFormat.AdvancedAss;
        _assPresetSection.Visible = showAssPresets;

        if (showAssPresets)
        {
            ClientSize = new Size(560, ExpandedClientHeight);
            _infoCard.Location = new Point(12, 622);
            _cancelButton.Location = new Point(278, 700);
            _createButton.Location = new Point(408, 700);
            return;
        }

        ClientSize = new Size(560, CompactClientHeight);
        _infoCard.Location = new Point(12, 462);
        _cancelButton.Location = new Point(278, 540);
        _createButton.Location = new Point(408, 540);
    }
}
