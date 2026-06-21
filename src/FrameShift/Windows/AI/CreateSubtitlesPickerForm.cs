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
    private string _selectedModelId;

    public CreateSubtitlesPickerForm(string actionTitle, string sourceLabel)
    {
        var models = CreateSubtitlesModelCatalog.GetAll();
        _selectedModelId = CreateSubtitlesModelCatalog.GetDefault().Id;

        FrameShiftWindowChrome.Apply(this, $"FrameShift - {actionTitle}", IconPaths.CreateSubtitlesAiIcon, IconPaths.AppIcon);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = FrameShiftTheme.PageBackground;
        ClientSize = new Size(560, 414);

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

        var infoCard = FrameShiftUiFactory.CreateFixedInfoCard(new Point(12, 302), new Size(536, 56));
        Controls.Add(infoCard);
        infoCard.Controls.Add(new Label
        {
            Location = new Point(12, 8),
            Size = new Size(512, 38),
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = "FrameShift prepares mono 16 kHz audio, transcribes it locally with Whisper, then writes a unique .srt file next to the source."
        });

        var cancelButton = FrameShiftUiFactory.CreateFixedActionButton("Cancel", new Point(278, 368), new Size(120, 34), primary: false);
        cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);

        var createButton = FrameShiftUiFactory.CreateFixedActionButton("Create SRT", new Point(408, 368), new Size(140, 34), primary: true);
        createButton.DialogResult = DialogResult.OK;
        Controls.Add(createButton);

        AcceptButton = createButton;
        CancelButton = cancelButton;
    }

    public string SelectedModelId => _selectedModelId;

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
}
