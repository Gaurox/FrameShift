using System.Drawing;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Controls;

internal sealed class FrameShiftActionBar : FlowLayoutPanel
{
    protected override void OnLayout(LayoutEventArgs e)
    {
        foreach (Control button in Controls)
        {
            var maximum = new Size(Math.Max(1, ClientSize.Width - Padding.Horizontal - button.Margin.Horizontal), 0);
            if (button.MaximumSize != maximum) button.MaximumSize = maximum;
        }
        base.OnLayout(e);
    }
}
