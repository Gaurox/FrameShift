using System.Drawing;
using System.Windows.Forms;

namespace FrameShift.Windows.Controls;

internal sealed class FrameShiftFieldRow : TableLayoutPanel
{
    protected override void OnLayout(LayoutEventArgs e)
    {
        // A long translated label wraps instead of consuming the editor's column.
        if (GetControlFromPosition(0, 0) is Label label)
        {
            var maximum = new Size(Math.Max(1, ClientSize.Width * 2 / 5), 0);
            if (label.MaximumSize != maximum) label.MaximumSize = maximum;
        }
        base.OnLayout(e);
    }
}
