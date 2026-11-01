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
        FrameShiftWindowChrome.Apply(this, L.Get("SeparateAudioPickerForm_FrameShift_Audio_Separation_662fde62"), IconPaths.FrameShiftAiIcon, IconPaths.AppIcon);
        var header = FrameShiftUiFactory.CreateHeader(L.Get("SeparateAudioPickerForm_FrameShift_Audio_Separation_662fde62"), L.Format("SeparateAudioPickerForm_Source_0_653ab450",sourceLabel),
            IconPaths.SeparateAudioAiIcon, IconPaths.FrameShiftAiIcon, "AI");
        _vocalsCheckBox = CreateOptionCheckBox(L.Get("SeparateAudioPickerForm_Vocals_89c9d0e6"));
        _drumsCheckBox = CreateOptionCheckBox(L.Get("SeparateAudioPickerForm_Drums_e094a8bb"));
        _bassCheckBox = CreateOptionCheckBox(L.Get("SeparateAudioPickerForm_Bass_7753a3fc"));
        _otherCheckBox = CreateOptionCheckBox(L.Get("SeparateAudioPickerForm_Other_71e98789"));
        _instrumentalCheckBox = CreateOptionCheckBox(L.Get("SeparateAudioPickerForm_Instrumental_drums_bass_other_74d208e0"));
        var stemsSection = FrameShiftUiFactory.CreateSection(L.Get("SeparateAudioPickerForm_Output_stems_144bf241"), FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateChoiceRow(_vocalsCheckBox, _drumsCheckBox, _bassCheckBox, _otherCheckBox),
            FrameShiftUiFactory.CreateChoiceRow(_instrumentalCheckBox),
            FrameShiftUiFactory.CreateWrappingLabel(L.Get("SeparateAudioPickerForm_The_model_computes_the_4_base_stems__ab16ef21"))));
        _autoRadioButton = CreateEngineRadioButton(L.Get("SeparateAudioPickerForm_Automatic_70aefbbf"));
        _gpuRadioButton = CreateEngineRadioButton("GPU (DirectML)");
        _cpuRadioButton = CreateEngineRadioButton(L.Get("SeparateAudioPickerForm_CPU_only_bbfae4f2"));
        _gpuRadioButton.Enabled = dmlAvailable;
        _engineHintLabel = FrameShiftUiFactory.CreateWrappingLabel("");
        var engineSection = FrameShiftUiFactory.CreateSection(L.Get("SeparateAudioPickerForm_Engine_506795b9"), FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateChoiceRow(_autoRadioButton, _gpuRadioButton, _cpuRadioButton), _engineHintLabel));
        var cancelButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("SeparateAudioPickerForm_Cancel_998b9c48"), false);
        cancelButton.DialogResult = DialogResult.Cancel;
        _separateButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("SeparateAudioPickerForm_Separate_a17d310d"), true);
        _separateButton.DialogResult = DialogResult.OK;
        AcceptButton = _separateButton;
        CancelButton = cancelButton;
        var root = FrameShiftDialogLayout.Create(header, FrameShiftUiFactory.CreateVerticalStack(stemsSection, engineSection),
            FrameShiftDialogLayout.CreateActions(cancelButton, _separateButton),
            FrameShiftUiFactory.CreateStatusMessage(L.Format("SeparateAudioPickerForm_Duration_0_Output_WAV_files_are_crea_027cc919",durationLabel)));
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

        return L.Format("SeparateAudioPickerForm_0_selected_files_dd068a4b",inputPaths.Count);
    }

    public static string BuildDurationLabel(MediaProbeResult probe, int inputCount)
    {
        if (inputCount > 1)
        {
            var durationText = probe.Duration is not null
                ? FrameShift.Core.Actions.CutAudioSettings.FormatDisplayTime(probe.Duration.Value.TotalSeconds)
                : L.Get("SeparateAudioPickerForm_Unknown_d748bba5");
            return L.Format("SeparateAudioPickerForm_First_file_0_ae34e498",durationText);
        }

        return probe.Duration is not null
            ? FrameShift.Core.Actions.CutAudioSettings.FormatDisplayTime(probe.Duration.Value.TotalSeconds)
            : L.Get("SeparateAudioPickerForm_Unknown_d748bba5");
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
            _engineHintLabel.Text = L.Get("SeparateAudioPickerForm_CPU_uses_the_standard_HTDemucs_model_dc09a2b3");
            return;
        }

        if (_gpuRadioButton.Checked)
        {
            _engineHintLabel.Text = L.Get("SeparateAudioPickerForm_GPU_uses_the_split_HTDemucs_model_th_ffbd38e4");
            return;
        }

        _engineHintLabel.Text = dmlAvailable
            ? L.Get("SeparateAudioPickerForm_Automatic_uses_DirectML_when_availab_a44b4622")
            : L.Get("SeparateAudioPickerForm_Automatic_uses_CPU_on_this_machine_b_9017910b");
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