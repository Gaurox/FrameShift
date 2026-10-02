using System.Drawing;
using System.Windows.Forms;

namespace FrameShift.Windows.Helpers;

public static class FrameShiftCropEditorUi
{
    // Crop shares the editor policy; only its options and media interaction remain specific.
    public static TableLayoutPanel Create(Control header, Control preview, Control options,
        Button cancel, Button primary, Control? status = null) =>
        FrameShiftEditorShellUi.Create(header, preview, FrameShiftDialogLayout.CreateActions(cancel, primary), options, status);

    public static Panel CreatePreviewPanel()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(32, 32, 32),
            Margin = Padding.Empty
        };
    }
}
