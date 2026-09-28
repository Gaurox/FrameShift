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
        FrameShiftWindowChrome.Apply(this, "FrameShift - Interpolate Video");
        var sourceFpsText = sourceFps.ToString("0.###", CultureInfo.InvariantCulture);
        var header = FrameShiftUiFactory.CreateHeader("FrameShift - Interpolate Video",
            $"Source: {Path.GetFileName(inputPath)} — {sourceFpsText} fps",
            IconPaths.ContextMenuIco("interpolate-video-icon.ico"), IconPaths.AppIcon, "▶");
        _textFps = new TextBox { Text = sourceFpsText, Name = "targetFps" };
        var presets = FrameShiftUiFactory.CreateChoiceRow();
        foreach (var multiplier in new[] { 2, 3, 4 })
        {
            var button = FrameShiftUiFactory.CreateMeasuredActionButton($"× {multiplier}", false, 84);
            button.Click += (_, _) => SetFpsText(_sourceFps * multiplier);
            presets.Controls.Add(button);
        }
        var content = FrameShiftUiFactory.CreateSection("Frame rate", FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateWrappingLabel($"Source: {sourceFpsText} fps. Choose a multiplier or enter a custom frame rate."),
            presets, FrameShiftUiFactory.CreateFieldRow("&Custom FPS", _textFps, "fps", logicalEditorWidth: 140)));
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        cancel.DialogResult = DialogResult.Cancel;
        var start = FrameShiftUiFactory.CreateMeasuredActionButton("Interpolate", true);
        start.Click += OnInterpolateClicked;
        var layout = FrameShiftDialogLayout.Create(header, content, FrameShiftDialogLayout.CreateActions(cancel, start),
            FrameShiftUiFactory.CreateStatusMessage("Creates a new video next to the source, in the same container format."));
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
                "Please enter a target FPS.",
                "FrameShift",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var targetFps) || targetFps <= 0)
        {
            MessageBox.Show(
                "Invalid FPS value. Enter a number greater than 0.",
                "FrameShift",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (targetFps < _sourceFps)
        {
            var confirm = MessageBox.Show(
                $"The target FPS ({targetFps.ToString("0.###", CultureInfo.InvariantCulture)}) is lower than the source FPS ({_sourceFps.ToString("0.###", CultureInfo.InvariantCulture)}).\n" +
                "This will reduce the frame rate instead of interpolating.\n\nContinue?",
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
