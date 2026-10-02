using System.Windows.Forms;
using FrameShift.Windows.Controls;

namespace FrameShift.Windows.Helpers;

public static class FrameShiftEditorShellUi
{
    public static TableLayoutPanel CreateTimeline(Control header, Control preview, Control selection, Control actions)
        => FrameShiftDialogLayout.CreateShell(header, new FrameShiftTimelineWorkspace(preview, selection), actions, null);

    public static TableLayoutPanel Create(Control header, Control workspace, Control actions,
        Control? options = null, Control? status = null, int logicalRailWidth = FrameShiftUiMetrics.EditorRailWidth)
    {
        Control body = options is null ? workspace : new FrameShiftEditorWorkspace(workspace, options, logicalRailWidth);
        return FrameShiftDialogLayout.CreateShell(header, body, actions, status);
    }

}
