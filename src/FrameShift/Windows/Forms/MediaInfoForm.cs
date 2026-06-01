using System.Drawing;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class MediaInfoForm : Form
{
    public MediaInfoForm(string text, MediaKind kind, string fileName)
    {
        var iconPath = IconPaths.MediaInfoIcon;

        FrameShiftWindowChrome.Apply(this, "FrameShift - Media Info");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var isLarge = kind == MediaKind.Video;
        ClientSize = isLarge ? new Size(560, 680) : new Size(560, 500);

        const int outerPad = FrameShiftUiMetrics.OuterPadding;
        const int buttonH = FrameShiftUiMetrics.FooterButtonHeight;
        const int headerH = FrameShiftUiMetrics.HeaderHeight;
        const int sectionStartY = outerPad + headerH + outerPad;
        var buttonY = ClientSize.Height - outerPad - buttonH;
        var sectionH = buttonY - outerPad - sectionStartY;
        var sectionW = ClientSize.Width - 2 * outerPad;

        var header = FrameShiftUiFactory.CreateFixedHeader(
            "FrameShift - Media Info",
            $"File: {fileName}",
            iconPath,
            IconPaths.AppIcon,
            "ℹ");
        Controls.Add(header);

        var section = FrameShiftUiFactory.CreateFixedSection(
            new Point(outerPad, sectionStartY),
            new Size(sectionW, sectionH),
            "Information");
        Controls.Add(section);

        const int textPadX = 12;
        const int textStartY = 34;
        const int textPadBottom = 12;

        var textBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point),
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary,
            BorderStyle = BorderStyle.None,
            Location = new Point(textPadX, textStartY),
            Size = new Size(sectionW - 2 * textPadX, sectionH - textStartY - textPadBottom),
            Text = text
        };
        section.Controls.Add(textBox);

        var closeX = ClientSize.Width - outerPad - FrameShiftUiMetrics.PrimaryButtonWidth;
        var copyX = closeX - FrameShiftUiMetrics.FooterButtonGap - FrameShiftUiMetrics.SecondaryButtonWidth;

        var copyButton = FrameShiftUiFactory.CreateFixedActionButton(
            "Copy",
            new Point(copyX, buttonY),
            new Size(FrameShiftUiMetrics.SecondaryButtonWidth, buttonH),
            primary: false);
        copyButton.Click += (_, _) => Clipboard.SetText(textBox.Text);
        Controls.Add(copyButton);

        var closeButton = FrameShiftUiFactory.CreateFixedActionButton(
            "Close",
            new Point(closeX, buttonY),
            new Size(FrameShiftUiMetrics.PrimaryButtonWidth, buttonH),
            primary: true);
        closeButton.DialogResult = DialogResult.Cancel;
        Controls.Add(closeButton);

        CancelButton = closeButton;
        AcceptButton = closeButton;

        Shown += (_, _) =>
        {
            textBox.SelectionStart = 0;
            textBox.SelectionLength = 0;
            textBox.ScrollToCaret();
            closeButton.Focus();
        };
    }
}
