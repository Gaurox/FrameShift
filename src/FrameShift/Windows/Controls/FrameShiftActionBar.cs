using System.Drawing;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Controls;

internal sealed class FrameShiftActionBar : FlowLayoutPanel
{
    private bool _measuring;

    private Size MeasureButtons(int width)
    {
        var largestMargin = Controls.Cast<Control>().Select(c => c.Margin.Horizontal).DefaultIfEmpty().Max();
        var maximumWidth = Math.Max(1, width - Padding.Horizontal - largestMargin);
        var common = Size.Empty;
        foreach (Control button in Controls)
        {
            var preferred = button is FrameShiftActionButton action
                ? action.MeasureForWidth(maximumWidth)
                : button.GetPreferredSize(new Size(maximumWidth, 0));
            common.Width = Math.Max(common.Width, preferred.Width);
            common.Height = Math.Max(common.Height, preferred.Height);
        }
        return common;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        // Measure the equal-sized pair before parent tables allocate the footer row.
        // Depending on the previous button bounds here can clip a newly wrapped row.
        var width = Math.Max(1, proposedSize.Width > 1 ? proposedSize.Width : ClientSize.Width);
        var common = MeasureButtons(width);
        var x = 0;
        var height = 0;
        var rowHeight = 0;
        foreach (Control button in Controls)
        {
            var itemWidth = common.Width + button.Margin.Horizontal;
            if (x > 0 && x + itemWidth > width - Padding.Horizontal)
            { height += rowHeight; x = 0; rowHeight = 0; }
            x += itemWidth;
            rowHeight = Math.Max(rowHeight, common.Height + button.Margin.Vertical);
        }
        return new Size(width, height + rowHeight + Padding.Vertical);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (_measuring) return;
        _measuring = true;
        try
        {
            // Equal dimensions for both roles, including when text grows or wraps.
            var commonSize = MeasureButtons(ClientSize.Width);
            foreach (Control button in Controls)
            {
                button.AutoSize = false;
                button.MaximumSize = new Size(commonSize.Width, 0);
            }
            foreach (Control button in Controls)
                if (button.Size != commonSize) button.Size = commonSize;
            base.OnLayout(e);
        }
        finally { _measuring = false; }
    }
}
