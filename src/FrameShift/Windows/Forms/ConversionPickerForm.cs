using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class ConversionPickerForm : Form
{
    private readonly ComboBox _targetCombo;
    private readonly ComboBox? _profileCombo;
    private readonly Label _descriptionLabel;
    private readonly Label? _profileDescriptionLabel;

    public ConversionPickerForm(
        string title,
        string sourceLabel,
        string description,
        IReadOnlyList<IConversionChoice> targets,
        IReadOnlyList<IConversionChoice> profiles,
        string? initialTargetId = null,
        string? initialProfileId = null,
        string primaryButtonText = "Convert")
    {
        FrameShiftWindowChrome.Apply(this, title);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = FrameShiftTheme.PageBackground;
        var formHeight = profiles.Count > 0 ? 402 : 304;
        ClientSize = new Size(560, formHeight);

        var header = CreateHeader(title, sourceLabel);
        Controls.Add(header);

        var targetSection = CreateSectionPanel("Target format", new Point(12, 82), new Size(536, 92));
        Controls.Add(targetSection);

        var targetLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 33),
            Text = "Target",
            ForeColor = FrameShiftTheme.TextPrimary
        };
        targetSection.Controls.Add(targetLabel);

        _targetCombo = FrameShiftUiFactory.CreateFixedComboBox(new Point(96, 27), new Size(412, 24));
        foreach (var target in targets)
        {
            _targetCombo.Items.Add(new ComboItem(target.DisplayName, target.Id, target.Description));
        }
        _targetCombo.SelectedIndex = FindIndexById(_targetCombo, initialTargetId);
        _targetCombo.SelectedIndexChanged += (_, _) => UpdateDescription();
        targetSection.Controls.Add(_targetCombo);

        _descriptionLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 60),
            Size = new Size(504, 18),
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = description
        };
        targetSection.Controls.Add(_descriptionLabel);

        Label? profileLabel = null;
        if (profiles.Count > 0)
        {
            profileLabel = new Label
            {
                AutoSize = true,
                Location = new Point(16, 33),
                Text = "Profile",
                ForeColor = FrameShiftTheme.TextPrimary
            };

            var profileSection = CreateSectionPanel("Encoding profile", new Point(12, 186), new Size(536, 92));
            profileSection.Controls.Add(profileLabel);

            _profileCombo = FrameShiftUiFactory.CreateFixedComboBox(new Point(96, 27), new Size(412, 24));
            foreach (var profile in profiles)
            {
                _profileCombo.Items.Add(new ComboItem(profile.DisplayName, profile.Id, profile.Description));
            }
            _profileCombo.SelectedIndex = FindIndexById(_profileCombo, initialProfileId);
            _profileCombo.SelectedIndexChanged += (_, _) => UpdateDescription();
            profileSection.Controls.Add(_profileCombo);

            _profileDescriptionLabel = new Label
            {
                AutoSize = false,
                Location = new Point(16, 60),
                Size = new Size(504, 18),
                ForeColor = FrameShiftTheme.TextSecondary,
                Text = profiles[0].Description
            };
            profileSection.Controls.Add(_profileDescriptionLabel);

            Controls.Add(profileSection);
        }

        var infoY = profiles.Count > 0 ? 290 : 186;
        var infoCard = CreateInfoCard(new Point(12, infoY), new Size(536, 44));
        Controls.Add(infoCard);

        var infoLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 12),
            Size = new Size(504, 18),
            Text = "The output is created next to the original file.",
            ForeColor = FrameShiftTheme.TextSecondary
        };
        infoCard.Controls.Add(infoLabel);

        var cancelY = formHeight - 46;
        var cancelButton = FrameShiftUiFactory.CreateFixedActionButton("Cancel", new Point(278, cancelY), new Size(120, 34), primary: false);
        cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);

        var convertButton = FrameShiftUiFactory.CreateFixedActionButton(primaryButtonText, new Point(408, cancelY), new Size(140, 34), primary: true);
        convertButton.DialogResult = DialogResult.OK;
        Controls.Add(convertButton);

        AcceptButton = convertButton;
        CancelButton = cancelButton;
        UpdateDescription();
    }

    public ConversionSelection? Selection
    {
        get
        {
            if (DialogResult != DialogResult.OK)
            {
                return null;
            }

            var target = (_targetCombo.SelectedItem as ComboItem)?.Value ?? "mp4";
            var profile = _profileCombo is null ? null : (_profileCombo.SelectedItem as ComboItem)?.Value;
            return new ConversionSelection(target, profile);
        }
    }

    private static Panel CreateHeader(string title, string sourceLabel)
    {
        return FrameShiftUiFactory.CreateFixedHeader(
            title,
            $"Source: {sourceLabel}",
            IconPaths.ConvertVideoIcon,
            IconPaths.AppIcon,
            "▶");
    }

    private static Panel CreateSectionPanel(string title, Point location, Size size)
    {
        return FrameShiftUiFactory.CreateFixedSection(location, size, title);
    }

    private static Panel CreateInfoCard(Point location, Size size)
    {
        return FrameShiftUiFactory.CreateFixedInfoCard(location, size);
    }

    private void UpdateDescription()
    {
        var target = _targetCombo.SelectedItem as ComboItem;
        _descriptionLabel.Text = target?.Description ?? string.Empty;
        if (_profileCombo is not null && _profileDescriptionLabel is not null)
        {
            var profile = _profileCombo.SelectedItem as ComboItem;
            _profileDescriptionLabel.Text = profile?.Description ?? string.Empty;
        }
    }

    private static int FindIndexById(ComboBox comboBox, string? initialId)
    {
        if (!string.IsNullOrWhiteSpace(initialId))
        {
            for (var index = 0; index < comboBox.Items.Count; index++)
            {
                if (comboBox.Items[index] is ComboItem item &&
                    string.Equals(item.Value, initialId, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
        }

        return comboBox.Items.Count > 0 ? 0 : -1;
    }

    private sealed record ComboItem(string Text, string Value, string Description)
    {
        public override string ToString() => Text;
    }
}
