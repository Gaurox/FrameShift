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
        FrameShiftWindowChrome.Apply(this, L.Get("MediaInfoForm_FrameShift_Media_Info_c5b0b2da"));
        var header = FrameShiftUiFactory.CreateHeader(L.Get("MediaInfoForm_FrameShift_Media_Info_c5b0b2da"), L.Format("MediaInfoForm_File_0_09030f15",fileName),
            IconPaths.MediaInfoIcon, IconPaths.AppIcon, "ℹ");
        var monoFont = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point);
        var textBox = new TextBox
        {
            Name = "mediaInformation", Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Both, WordWrap = false, Font = monoFont,
            BackColor = FrameShiftTheme.Surface, ForeColor = FrameShiftTheme.TextPrimary,
            BorderStyle = BorderStyle.None, Text = text, AccessibleName = L.Get("MediaInfoForm_Media_information_12bfdfab")
        };
        Disposed += (_, _) => monoFont.Dispose();
        var copy = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("MediaInfoForm_Copy_d6e96519"), false);
        copy.Click += (_, _) => { if (textBox.TextLength > 0) Clipboard.SetText(textBox.Text); };
        var close = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("MediaInfoForm_Close_6cf4a777"), true);
        close.DialogResult = DialogResult.Cancel;
        close.Click += (_, _) => Close();
        CancelButton = AcceptButton = close;
        Controls.Add(FrameShiftDialogLayout.CreateShell(header,
            FrameShiftUiFactory.CreateSection(L.Get("MediaInfoForm_Information_c852f3ee"), textBox, fill: true),
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