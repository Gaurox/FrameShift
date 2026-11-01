using FrameShift.Core.AI.RemoveNoise;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.AI;

// Shared presentation only. Preview execution and lifetime remain in each picker.
internal static class RemoveNoisePickerUi
{
    internal static Control CreateStrengthSection(Action<string> select, out RadioButton[] choices)
    {
        choices =
        [
            Choice(L.Get("RemoveNoisePickerUi_Light_7a54db24"), L.Get("RemoveNoisePickerUi_Light_reduction_3acd9198"), RemoveNoiseAudioSettings.StrengthLight),
            Choice(L.Get("RemoveNoisePickerUi_Normal_4d907516"), L.Get("RemoveNoisePickerUi_Balanced_cleanup_e29746ec"), RemoveNoiseAudioSettings.StrengthNormal),
            Choice(L.Get("RemoveNoisePickerUi_Strong_307c31a5"), L.Get("RemoveNoisePickerUi_Aggressive_reduction_c1a83b98"), RemoveNoiseAudioSettings.StrengthStrong),
            Choice(L.Get("RemoveNoisePickerUi_Maximum_9216586b"), L.Get("RemoveNoisePickerUi_Maximum_cleanup_a65caa46"), RemoveNoiseAudioSettings.StrengthMaximum)
        ];
        choices[3].Checked = true;
        foreach (var choice in choices)
            choice.CheckedChanged += (_, _) => { if (choice.Checked) select((string)choice.Tag!); };
        return FrameShiftUiFactory.CreateSection(L.Get("RemoveNoisePickerUi_Noise_reduction_strength_050ee419"),
            FrameShiftUiFactory.CreateVerticalStack(FrameShiftUiFactory.CreateChoiceRow(choices),
                FrameShiftUiFactory.CreateWrappingLabel(
                    L.Get("RemoveNoisePickerUi_Higher_strength_removes_more_noise_M_37a09ad6"))));
    }

    internal static Control CreateStereoSection(bool sourceIsStereo, out CheckBox stereo)
    {
        stereo = new CheckBox
        {
            Name = "stereo", Text = L.Get("RemoveNoisePickerUi_Process_stereo_channels_separately_a9547271"), AutoSize = true,
            Enabled = sourceIsStereo, ForeColor = FrameShiftTheme.TextPrimary,
            AccessibleDescription = L.Get("RemoveNoisePickerUi_Processes_left_and_right_independent_12834186")
        };
        return FrameShiftUiFactory.CreateSection(L.Get("RemoveNoisePickerUi_Channels_4e2a20ce"), FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateChoiceRow(stereo), FrameShiftUiFactory.CreateWrappingLabel(sourceIsStereo
                ? L.Get("RemoveNoisePickerUi_L_and_R_channels_are_denoised_indepe_1ef8c2d0")
                : L.Get("RemoveNoisePickerUi_Source_audio_is_mono_stereo_processi_8d1dc9b5"))));
    }

    private static RadioButton Choice(string title, string description, string id)
        => new FrameShiftChoiceCard(title, description, 138) { Name = id, Tag = id };
}
