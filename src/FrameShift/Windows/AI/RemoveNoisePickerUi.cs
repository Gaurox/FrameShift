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
            Choice("Light", "Light reduction", RemoveNoiseAudioSettings.StrengthLight),
            Choice("Normal", "Balanced cleanup", RemoveNoiseAudioSettings.StrengthNormal),
            Choice("Strong", "Aggressive reduction", RemoveNoiseAudioSettings.StrengthStrong),
            Choice("Maximum", "Maximum cleanup", RemoveNoiseAudioSettings.StrengthMaximum)
        ];
        choices[3].Checked = true;
        foreach (var choice in choices)
            choice.CheckedChanged += (_, _) => { if (choice.Checked) select((string)choice.Tag!); };
        return FrameShiftUiFactory.CreateSection("Noise reduction strength",
            FrameShiftUiFactory.CreateVerticalStack(FrameShiftUiFactory.CreateChoiceRow(choices),
                FrameShiftUiFactory.CreateWrappingLabel(
                    "Higher strength removes more noise. Maximum may affect natural audio characteristics.")));
    }

    internal static Control CreateStereoSection(bool sourceIsStereo, out CheckBox stereo)
    {
        stereo = new CheckBox
        {
            Name = "stereo", Text = "Process stereo channels separately", AutoSize = true,
            Enabled = sourceIsStereo, ForeColor = FrameShiftTheme.TextPrimary,
            AccessibleDescription = "Processes left and right independently; takes approximately twice as long."
        };
        return FrameShiftUiFactory.CreateSection("Channels", FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateChoiceRow(stereo), FrameShiftUiFactory.CreateWrappingLabel(sourceIsStereo
                ? "L and R channels are denoised independently, then merged back. Takes approximately twice as long."
                : "Source audio is mono — stereo processing is not available.")));
    }

    private static RadioButton Choice(string title, string description, string id)
        => new FrameShiftChoiceCard(title, description, 138) { Name = id, Tag = id };
}
