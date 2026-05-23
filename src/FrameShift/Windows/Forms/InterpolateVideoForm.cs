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

        var iconPath = IconPaths.ContextMenuIco("interpolate-video-icon.ico");
        FrameShiftWindowChrome.Apply(this, "FrameShift - Interpolate Video");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(440, 384);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var sourceFpsText = sourceFps.ToString("0.###", CultureInfo.InvariantCulture);

        var header = FrameShiftUiFactory.CreateFixedHeader(
            "FrameShift - Interpolate Video",
            $"Source: {Path.GetFileName(inputPath)}  —  {sourceFpsText} fps",
            iconPath,
            IconPaths.AppIcon,
            "▶");
        Controls.Add(header);

        // Source section: y=82, h=56 — content at y=28 (standard single-line compact section)
        var sourceSection = FrameShiftUiFactory.CreateFixedSection(
            new Point(12, 82), new Size(416, 56), "Source");
        Controls.Add(sourceSection);

        var sourceFpsLabel = new Label
        {
            Location = new Point(18, 28),
            Size = new Size(380, 18),
            Text = $"Detected frame rate: {sourceFpsText} fps",
            ForeColor = FrameShiftTheme.TextSecondary
        };
        sourceSection.Controls.Add(sourceFpsLabel);

        // Target FPS section: y=150 (82+56+12), h=120
        var targetSection = FrameShiftUiFactory.CreateFixedSection(
            new Point(12, 150), new Size(416, 120), "Target FPS");
        Controls.Add(targetSection);

        // Quick preset buttons row
        var btnX2 = FrameShiftUiFactory.CreateFixedActionButton("× 2", new Point(18, 28), new Size(80, 34), primary: false);
        btnX2.Click += (_, _) => SetFpsText(_sourceFps * 2);
        targetSection.Controls.Add(btnX2);

        var btnX3 = FrameShiftUiFactory.CreateFixedActionButton("× 3", new Point(110, 28), new Size(80, 34), primary: false);
        btnX3.Click += (_, _) => SetFpsText(_sourceFps * 3);
        targetSection.Controls.Add(btnX3);

        var btnX4 = FrameShiftUiFactory.CreateFixedActionButton("× 4", new Point(202, 28), new Size(80, 34), primary: false);
        btnX4.Click += (_, _) => SetFpsText(_sourceFps * 4);
        targetSection.Controls.Add(btnX4);

        var presetHint = new Label
        {
            Location = new Point(294, 28),
            Size = new Size(110, 34),
            Text = $"Source: {sourceFpsText} fps",
            ForeColor = FrameShiftTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        targetSection.Controls.Add(presetHint);

        // Custom FPS input row
        var fpsLabel = new Label
        {
            Location = new Point(18, 76),
            Size = new Size(80, 30),
            Text = "Custom FPS:",
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        targetSection.Controls.Add(fpsLabel);

        _textFps = FrameShiftUiFactory.CreateValueTextBox(textAlign: HorizontalAlignment.Left);
        _textFps.Text = sourceFpsText;
        var fpsHost = FrameShiftUiFactory.CreateFixedTextInputHost(_textFps, new Point(102, 72), new Size(120, 30));
        targetSection.Controls.Add(fpsHost);

        var fpsSuffix = new Label
        {
            Location = new Point(228, 76),
            Size = new Size(28, 30),
            Text = "fps",
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        targetSection.Controls.Add(fpsSuffix);

        // Info card: y=282 (150+120+12), h=44
        var infoCard = FrameShiftUiFactory.CreateFixedInfoCard(new Point(12, 282), new Size(416, 44));
        Controls.Add(infoCard);

        var infoLabel = new Label
        {
            Location = new Point(12, 13),
            Size = new Size(392, 18),
            Text = "The interpolated video is created next to the original file. Same container format.",
            ForeColor = FrameShiftTheme.TextSecondary,
            AutoEllipsis = true
        };
        infoCard.Controls.Add(infoLabel);

        // Footer buttons: y=338 (282+44+12) — Interpolate right-aligned at x=308 (440-12-120), Cancel left of it with gap=10
        var cancelButton = FrameShiftUiFactory.CreateFixedActionButton("Cancel", new Point(178, 338), new Size(120, 34), primary: false);
        cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);

        var interpolateButton = FrameShiftUiFactory.CreateFixedActionButton("Interpolate", new Point(308, 338), new Size(120, 34), primary: true);
        interpolateButton.Click += OnInterpolateClicked;
        Controls.Add(interpolateButton);

        AcceptButton = interpolateButton;
        CancelButton = cancelButton;
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
