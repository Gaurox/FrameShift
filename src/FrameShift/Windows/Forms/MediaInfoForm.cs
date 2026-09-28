using System.Drawing;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class MediaInfoForm : Form
{
    public MediaInfoForm(string text, MediaKind kind, string fileName)
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(680, kind == MediaKind.Video ? 680 : 500), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, "FrameShift - Media Info");
        var header = FrameShiftUiFactory.CreateHeader("FrameShift - Media Info", $"File: {fileName}",
            IconPaths.MediaInfoIcon, IconPaths.AppIcon, "ℹ");
        var monoFont = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point);
        var textBox = new TextBox
        {
            Name = "mediaInformation", Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Both, WordWrap = false, Font = monoFont,
            BackColor = FrameShiftTheme.Surface, ForeColor = FrameShiftTheme.TextPrimary,
            BorderStyle = BorderStyle.None, Text = text, AccessibleName = "Media information"
        };
        Disposed += (_, _) => monoFont.Dispose();
        var copy = FrameShiftUiFactory.CreateMeasuredActionButton("Copy", false);
        copy.Click += (_, _) => { if (textBox.TextLength > 0) Clipboard.SetText(textBox.Text); };
        var close = FrameShiftUiFactory.CreateMeasuredActionButton("Close", true);
        close.DialogResult = DialogResult.Cancel;
        close.Click += (_, _) => Close();
        CancelButton = AcceptButton = close;
        Controls.Add(FrameShiftDialogLayout.CreateShell(header,
            FrameShiftUiFactory.CreateSection("Information", textBox, fill: true),
            FrameShiftDialogLayout.CreateActions(copy, close), null));
        Shown += (_, _) =>
        {
            textBox.Select(0, 0);
            textBox.ScrollToCaret();
            close.Focus();
        };
        ResumeLayout(true);
    }
}