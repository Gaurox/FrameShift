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
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(600, 500), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, title);
        var header = FrameShiftUiFactory.CreateHeader(title, $"Source: {sourceLabel}",
            IconPaths.ConvertVideoIcon, IconPaths.AppIcon, "▶");
        _targetCombo = new ComboBox { Name = "target", DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var target in targets)
            _targetCombo.Items.Add(new ComboItem(target.DisplayName, target.Id, target.Description));
        _targetCombo.SelectedIndex = FindIndexById(_targetCombo, initialTargetId);
        _descriptionLabel = FrameShiftUiFactory.CreateWrappingLabel(description);
        var content = FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateSection("Target format", FrameShiftUiFactory.CreateVerticalStack(
                FrameShiftUiFactory.CreateFieldRow("&Target", _targetCombo), _descriptionLabel)));
        if (profiles.Count > 0)
        {
            _profileCombo = new ComboBox { Name = "profile", DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var profile in profiles)
                _profileCombo.Items.Add(new ComboItem(profile.DisplayName, profile.Id, profile.Description));
            _profileCombo.SelectedIndex = FindIndexById(_profileCombo, initialProfileId);
            _profileDescriptionLabel = FrameShiftUiFactory.CreateWrappingLabel("");
            var section = FrameShiftUiFactory.CreateSection("Encoding profile", FrameShiftUiFactory.CreateVerticalStack(
                FrameShiftUiFactory.CreateFieldRow("&Profile", _profileCombo), _profileDescriptionLabel));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(section, 0, content.RowCount++);
            _profileCombo.SelectedIndexChanged += (_, _) => UpdateDescription();
        }
        _targetCombo.SelectedIndexChanged += (_, _) => UpdateDescription();
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Name = "cancelButton";
        var primary = FrameShiftUiFactory.CreateMeasuredActionButton(primaryButtonText, true);
        primary.Name = "primaryButton";
        primary.DialogResult = DialogResult.OK;
        AcceptButton = primary;
        CancelButton = cancel;
        var layout = FrameShiftDialogLayout.Create(header, content,
            FrameShiftDialogLayout.CreateActions(cancel, primary),
            FrameShiftUiFactory.CreateStatusMessage("The output is created next to the original file."));
        Controls.Add(layout);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, layout);
        UpdateDescription();
        ResumeLayout(true);
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
