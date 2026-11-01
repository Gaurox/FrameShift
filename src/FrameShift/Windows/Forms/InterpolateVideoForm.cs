using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class InterpolateVideoForm : Form
{
    private readonly double _sourceFps;
    private readonly TextBox _textFps;

    public InterpolateVideoForm(string inputPath, double sourceFps)
    {
        _sourceFps = sourceFps;
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(560, 410), new Size(360, 280));
        FrameShiftWindowChrome.Apply(this, L.Get("InterpolateVideoForm_FrameShift_Interpolate_Video_cf802a37"));
        var sourceFpsText = sourceFps.ToString("0.###", CultureInfo.InvariantCulture);
        var header = FrameShiftUiFactory.CreateHeader(L.Get("InterpolateVideoForm_FrameShift_Interpolate_Video_cf802a37"),
            L.Format("InterpolateVideoForm_Source_0_1_fps_4ac163bc",Path.GetFileName(inputPath),sourceFpsText),
            IconPaths.ContextMenuIco("interpolate-video-icon.ico"), IconPaths.AppIcon, "▶");
        _textFps = new TextBox { Text = sourceFpsText, Name = "targetFps" };
        var presets = FrameShiftUiFactory.CreateChoiceRow();
        foreach (var multiplier in new[] { 2, 3, 4 })
        {
            var button = FrameShiftUiFactory.CreateMeasuredActionButton($"× {multiplier}", false, 84);
            button.Click += (_, _) => SetFpsText(_sourceFps * multiplier);
            presets.Controls.Add(button);
        }
        var content = FrameShiftUiFactory.CreateSection(L.Get("InterpolateVideoForm_Frame_rate_22210a15"), FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateWrappingLabel(L.Format("InterpolateVideoForm_Source_0_fps_Choose_a_multiplier_or__124df12b",sourceFpsText)),
            presets, FrameShiftUiFactory.CreateFieldRow(L.Get("InterpolateVideoForm_Custom_FPS_c2253f3b"), _textFps, "fps", logicalEditorWidth: 140)));
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("InterpolateVideoForm_Cancel_998b9c48"), false);
        cancel.DialogResult = DialogResult.Cancel;
        var start = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("InterpolateVideoForm_Interpolate_74e5cd3c"), true);
        start.Click += OnInterpolateClicked;
        var layout = FrameShiftDialogLayout.Create(header, content, FrameShiftDialogLayout.CreateActions(cancel, start),
            FrameShiftUiFactory.CreateStatusMessage(L.Get("InterpolateVideoForm_Creates_a_new_video_next_to_the_sour_b83ef6d5")));
        Controls.Add(layout);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, layout);
        AcceptButton = start;
        CancelButton = cancel;
        ResumeLayout(true);
    }

    public InterpolateVideoSettings? Selection { get; private set; }

    private void SetFpsText(double fps)
    {
        _textFps.Text = fps.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private void OnInterpolateClicked(object? sender, EventArgs e)
    {
        var raw = _textFps.Text.Trim().Replace(',', '.');

        if (string.IsNullOrWhiteSpace(raw))
        {
            MessageBox.Show(
                L.Get("InterpolateVideoForm_Please_enter_a_target_FPS_36ec2f02"),
                "FrameShift",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var targetFps) || targetFps <= 0)
        {
            MessageBox.Show(
                L.Get("InterpolateVideoForm_Invalid_FPS_value_Enter_a_number_gre_847f5314"),
                "FrameShift",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (targetFps < _sourceFps)
        {
            var confirm = MessageBox.Show(
                L.Format("InterpolateVideoForm_The_target_FPS_0_is_lower_than_the_s_90704629",targetFps.ToString("0.###", CultureInfo.InvariantCulture),_sourceFps.ToString("0.###", CultureInfo.InvariantCulture)) +
                L.Get("InterpolateVideoForm_This_will_reduce_the_frame_rate_inst_03805c7b"),
                "FrameShift",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
            {
                return;
            }
        }

        Selection = new InterpolateVideoSettings(targetFps);
        DialogResult = DialogResult.OK;
        Close();
    }
}
