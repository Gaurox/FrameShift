using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Controls;

/// <summary>Spaced choices that wrap at the available width, including after a DPI change.</summary>
public sealed class FrameShiftFlowRow : FlowLayoutPanel
{
    private bool _arranging;

    public FrameShiftFlowRow()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        Margin = Padding.Empty;
        Size = Size.Empty;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        // A percent-width table also asks for an unconstrained preferred size.
        // Use our laid-out width then, rather than treating it as a one-item column.
        var width = proposedSize.Width > 1 ? proposedSize.Width : ClientSize.Width;
        if (width <= 1) width = Controls.Cast<Control>().Sum(c => c.GetPreferredSize(Size.Empty).Width + c.Margin.Horizontal);
        width = Math.Max(1, width - Padding.Horizontal);
        var x = 0;
        var height = 0;
        var rowHeight = 0;
        foreach (Control child in Controls)
        {
            var preferred = child.GetPreferredSize(new Size(width, 0));
            var itemWidth = preferred.Width + child.Margin.Horizontal;
            var itemHeight = Math.Max(preferred.Height, child.MinimumSize.Height) + child.Margin.Vertical;
            if (x > 0 && x + itemWidth > width) { height += rowHeight; x = 0; rowHeight = 0; }
            x += itemWidth;
            rowHeight = Math.Max(rowHeight, itemHeight);
        }
        return new Size(width + Padding.Horizontal, height + rowHeight + Padding.Vertical);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (_arranging) { base.OnLayout(e); return; }
        _arranging = true;
        try
        {
            var gap = FrameShiftUiMetrics.ToPixels(this, FrameShiftUiMetrics.LineGap);
            foreach (Control child in Controls)
            {
                child.Margin = new Padding(0, 0, gap, gap);
                child.MaximumSize = new Size(Math.Max(1, ClientSize.Width - Padding.Horizontal - gap), 0);
            }
            var cards = Controls.OfType<FrameShiftChoiceCard>().ToArray();
            if (cards.Length > 0)
            {
                var height = cards.Max(c => c.GetPreferredSize(Size.Empty).Height);
                foreach (var card in cards) card.MinimumSize = new Size(0, height);
            }
            base.OnLayout(e);
        }
        finally { _arranging = false; }
    }
}
