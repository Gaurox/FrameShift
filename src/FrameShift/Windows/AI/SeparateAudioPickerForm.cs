using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.AI.SeparateAudio;
using FrameShift.Core.FFprobe;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.AI;

public sealed class SeparateAudioPickerForm : Form
{
    private readonly CheckBox _vocalsCheckBox;
    private readonly CheckBox _drumsCheckBox;
    private readonly CheckBox _bassCheckBox;
    private readonly CheckBox _otherCheckBox;
    private readonly CheckBox _instrumentalCheckBox;
    private readonly RadioButton _autoRadioButton;
    private readonly RadioButton _gpuRadioButton;
    private readonly RadioButton _cpuRadioButton;
    private readonly Label _engineHintLabel;
    private readonly Button _separateButton;

    public SeparateAudioPickerForm(
        string sourceLabel,
        string durationLabel,
        bool dmlAvailable,
        IReadOnlyDictionary<string, string>? initialOptions = null)
    {
        FrameShiftWindowChrome.Apply(this, "FrameShift - Audio Separation");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 458);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var header = FrameShiftUiFactory.CreateFixedHeader(
            "FrameShift - Audio Separation",
            $"Source: {sourceLabel}",
            IconPaths.SeparateAudioAiIcon,
            IconPaths.AppIcon,
            "AI");
        Controls.Add(header);

        var stemsSection = FrameShiftUiFactory.CreateFixedSection(
            new Point(12, 82),
            new Size(536, 150),
            "Output stems");
        Controls.Add(stemsSection);

        _vocalsCheckBox = CreateOptionCheckBox("Vocals", new Point(18, 32));
        _drumsCheckBox = CreateOptionCheckBox("Drums", new Point(18, 58));
        _bassCheckBox = CreateOptionCheckBox("Bass", new Point(18, 84));
        _otherCheckBox = CreateOptionCheckBox("Other", new Point(18, 110));
        _instrumentalCheckBox = CreateOptionCheckBox("Instrumental (drums + bass + other)", new Point(264, 32));

        stemsSection.Controls.AddRange([
            _vocalsCheckBox,
            _drumsCheckBox,
            _bassCheckBox,
            _otherCheckBox,
            _instrumentalCheckBox
        ]);

        var stemsHintLabel = new Label
        {
            Location = new Point(264, 62),
            Size = new Size(246, 44),
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = "The model computes the 4 base stems internally. Unchecked stems are simply not written to disk."
        };
        stemsSection.Controls.Add(stemsHintLabel);

        var engineSection = FrameShiftUiFactory.CreateFixedSection(
            new Point(12, 244),
            new Size(536, 82),
            "Engine");
        Controls.Add(engineSection);

        _autoRadioButton = CreateEngineRadioButton("Automatic", new Point(18, 32), 110);
        _gpuRadioButton = CreateEngineRadioButton("GPU (DirectML)", new Point(150, 32), 140);
        _cpuRadioButton = CreateEngineRadioButton("CPU only", new Point(320, 32), 110);

        if (!dmlAvailable)
        {
            _gpuRadioButton.Enabled = false;
        }

        engineSection.Controls.AddRange([
            _autoRadioButton,
            _gpuRadioButton,
            _cpuRadioButton
        ]);

        _engineHintLabel = new Label
        {
            Location = new Point(18, 56),
            Size = new Size(500, 18),
            ForeColor = FrameShiftTheme.TextSecondary
        };
        engineSection.Controls.Add(_engineHintLabel);

        var infoCard = FrameShiftUiFactory.CreateFixedInfoCard(
            new Point(12, 338),
            new Size(536, 56));
        Controls.Add(infoCard);

        infoCard.Controls.Add(CreateInfoLabel(
            new Point(12, 8),
            new Size(512, 18),
            $"Duration: {durationLabel}"));
        infoCard.Controls.Add(CreateInfoLabel(
            new Point(12, 28),
            new Size(512, 18),
            "Output WAV files are created next to the source with unique naming."));

        var cancelButton = FrameShiftUiFactory.CreateFixedActionButton(
            "Cancel",
            new Point(278, 412),
            new Size(120, 34),
            primary: false);
        cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);

        _separateButton = FrameShiftUiFactory.CreateFixedActionButton(
            "Separate",
            new Point(408, 412),
            new Size(140, 34),
            primary: true);
        _separateButton.DialogResult = DialogResult.OK;
        Controls.Add(_separateButton);

        AcceptButton = _separateButton;
        CancelButton = cancelButton;

        _vocalsCheckBox.CheckedChanged += (_, _) => RefreshValidationState();
        _drumsCheckBox.CheckedChanged += (_, _) => RefreshValidationState();
        _bassCheckBox.CheckedChanged += (_, _) => RefreshValidationState();
        _otherCheckBox.CheckedChanged += (_, _) => RefreshValidationState();
        _instrumentalCheckBox.CheckedChanged += (_, _) => RefreshValidationState();
        _autoRadioButton.CheckedChanged += (_, _) => RefreshEngineHint(dmlAvailable);
        _gpuRadioButton.CheckedChanged += (_, _) => RefreshEngineHint(dmlAvailable);
        _cpuRadioButton.CheckedChanged += (_, _) => RefreshEngineHint(dmlAvailable);

        ApplyInitialOptions(initialOptions, dmlAvailable);
        RefreshEngineHint(dmlAvailable);
        RefreshValidationState();
    }

    public IReadOnlyDictionary<string, string>? SelectionOptions
    {
        get
        {
            if (DialogResult != DialogResult.OK)
            {
                return null;
            }

            var selection = new StemSelection
            {
                Vocals = _vocalsCheckBox.Checked,
                Drums = _drumsCheckBox.Checked,
                Bass = _bassCheckBox.Checked,
                Other = _otherCheckBox.Checked,
                Instrumental = _instrumentalCheckBox.Checked
            };

            var options = new Dictionary<string, string>(selection.ToOptions(), StringComparer.OrdinalIgnoreCase)
            {
                [ActionOptionKeys.SeparateEngine] = GetSelectedEngineValue()
            };

            return options;
        }
    }

    public static string BuildSourceLabel(IReadOnlyList<string> inputPaths)
    {
        if (inputPaths.Count <= 1)
        {
            return Path.GetFileName(inputPaths[0]);
        }

        return $"{inputPaths.Count} selected files";
    }

    public static string BuildDurationLabel(MediaProbeResult probe, int inputCount)
    {
        if (inputCount > 1)
        {
            var durationText = probe.Duration is not null
                ? FrameShift.Core.Actions.CutAudioSettings.FormatDisplayTime(probe.Duration.Value.TotalSeconds)
                : "Unknown";
            return $"First file: {durationText}";
        }

        return probe.Duration is not null
            ? FrameShift.Core.Actions.CutAudioSettings.FormatDisplayTime(probe.Duration.Value.TotalSeconds)
            : "Unknown";
    }

    private void ApplyInitialOptions(IReadOnlyDictionary<string, string>? initialOptions, bool dmlAvailable)
    {
        var selection = StemSelection.FromOptions(initialOptions ?? new Dictionary<string, string>());
        _vocalsCheckBox.Checked = selection.Vocals;
        _drumsCheckBox.Checked = selection.Drums;
        _bassCheckBox.Checked = selection.Bass;
        _otherCheckBox.Checked = selection.Other;
        _instrumentalCheckBox.Checked = selection.Instrumental;

        var engine = "auto";
        if (initialOptions is not null &&
            initialOptions.TryGetValue(ActionOptionKeys.SeparateEngine, out var engineValue) &&
            !string.IsNullOrWhiteSpace(engineValue))
        {
            engine = engineValue;
        }

        if (string.Equals(engine, "cpu", StringComparison.OrdinalIgnoreCase))
        {
            _cpuRadioButton.Checked = true;
        }
        else if (string.Equals(engine, "gpu", StringComparison.OrdinalIgnoreCase) && dmlAvailable)
        {
            _gpuRadioButton.Checked = true;
        }
        else
        {
            _autoRadioButton.Checked = true;
        }
    }

    private void RefreshValidationState()
    {
        _separateButton.Enabled =
            _vocalsCheckBox.Checked ||
            _drumsCheckBox.Checked ||
            _bassCheckBox.Checked ||
            _otherCheckBox.Checked ||
            _instrumentalCheckBox.Checked;
    }

    private void RefreshEngineHint(bool dmlAvailable)
    {
        if (_cpuRadioButton.Checked)
        {
            _engineHintLabel.Text = "CPU uses the standard HTDemucs model and works on all supported machines.";
            return;
        }

        if (_gpuRadioButton.Checked)
        {
            _engineHintLabel.Text = "GPU uses the split HTDemucs model through DirectML for faster inference when available.";
            return;
        }

        _engineHintLabel.Text = dmlAvailable
            ? "Automatic uses DirectML when available, otherwise it falls back to CPU."
            : "Automatic uses CPU on this machine because DirectML is not currently available.";
    }

    private string GetSelectedEngineValue()
    {
        if (_cpuRadioButton.Checked)
        {
            return "cpu";
        }

        if (_gpuRadioButton.Checked)
        {
            return "gpu";
        }

        return "auto";
    }

    private static CheckBox CreateOptionCheckBox(string text, Point location)
    {
        return new CheckBox
        {
            Location = location,
            Size = new Size(230, 22),
            Text = text,
            ForeColor = FrameShiftTheme.TextPrimary,
            BackColor = Color.Transparent,
            UseVisualStyleBackColor = true
        };
    }

    private static RadioButton CreateEngineRadioButton(string text, Point location, int width)
    {
        return new RadioButton
        {
            Location = location,
            Size = new Size(width, 22),
            Text = text,
            ForeColor = FrameShiftTheme.TextPrimary,
            BackColor = FrameShiftTheme.Surface,
            UseVisualStyleBackColor = true
        };
    }

    private static Label CreateInfoLabel(Point location, Size size, string text)
    {
        return new Label
        {
            Location = location,
            Size = size,
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = text,
            AutoEllipsis = true
        };
    }
}
