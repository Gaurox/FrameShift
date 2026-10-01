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
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(640, 500), new Size(400, 300));
        FrameShiftWindowChrome.Apply(this, "FrameShift - Audio Separation", IconPaths.FrameShiftAiIcon, IconPaths.AppIcon);
        var header = FrameShiftUiFactory.CreateHeader("FrameShift - Audio Separation", $"Source: {sourceLabel}",
            IconPaths.SeparateAudioAiIcon, IconPaths.FrameShiftAiIcon, "AI");
        _vocalsCheckBox = CreateOptionCheckBox("Vocals");
        _drumsCheckBox = CreateOptionCheckBox("Drums");
        _bassCheckBox = CreateOptionCheckBox("Bass");
        _otherCheckBox = CreateOptionCheckBox("Other");
        _instrumentalCheckBox = CreateOptionCheckBox("Instrumental (drums + bass + other)");
        var stemsSection = FrameShiftUiFactory.CreateSection("Output stems", FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateChoiceRow(_vocalsCheckBox, _drumsCheckBox, _bassCheckBox, _otherCheckBox),
            FrameShiftUiFactory.CreateChoiceRow(_instrumentalCheckBox),
            FrameShiftUiFactory.CreateWrappingLabel("The model computes the 4 base stems internally. Unchecked stems are simply not written to disk.")));
        _autoRadioButton = CreateEngineRadioButton("Automatic");
        _gpuRadioButton = CreateEngineRadioButton("GPU (DirectML)");
        _cpuRadioButton = CreateEngineRadioButton("CPU only");
        _gpuRadioButton.Enabled = dmlAvailable;
        _engineHintLabel = FrameShiftUiFactory.CreateWrappingLabel("");
        var engineSection = FrameShiftUiFactory.CreateSection("Engine", FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateChoiceRow(_autoRadioButton, _gpuRadioButton, _cpuRadioButton), _engineHintLabel));
        var cancelButton = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        cancelButton.DialogResult = DialogResult.Cancel;
        _separateButton = FrameShiftUiFactory.CreateMeasuredActionButton("Separate", true);
        _separateButton.DialogResult = DialogResult.OK;
        AcceptButton = _separateButton;
        CancelButton = cancelButton;
        var root = FrameShiftDialogLayout.Create(header, FrameShiftUiFactory.CreateVerticalStack(stemsSection, engineSection),
            FrameShiftDialogLayout.CreateActions(cancelButton, _separateButton),
            FrameShiftUiFactory.CreateStatusMessage($"Duration: {durationLabel}\r\nOutput WAV files are created next to the source with unique naming."));
        Controls.Add(root);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, root);
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
        ResumeLayout(true);
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

    private static CheckBox CreateOptionCheckBox(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = FrameShiftTheme.TextPrimary, UseVisualStyleBackColor = true
    };

    private static RadioButton CreateEngineRadioButton(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = FrameShiftTheme.TextPrimary, UseVisualStyleBackColor = true
    };
}