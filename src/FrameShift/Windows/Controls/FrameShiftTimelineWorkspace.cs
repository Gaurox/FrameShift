using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Controls;

/// <summary>Preview above temporal controls. Controls scroll only when the working area is short.</summary>
public sealed class FrameShiftTimelineWorkspace : TableLayoutPanel
{
    private readonly Control _options;
    private bool _arranging;
    // Opt-in for Cut/Loop: reserve a usable preview, then give controls the remaining height.
    // Other timeline editors keep their existing 40/60 split.
    internal int MinimumPreviewHeight { get; set; }

    public FrameShiftTimelineWorkspace(Control preview, Control options)
    {
        _options = options;
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        ColumnCount = 1;
        RowCount = 2;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        preview.Dock = DockStyle.Fill;
        Controls.Add(preview, 0, 0);
        var viewport = new TimelineViewport(this) { AutoScroll = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        options.Dock = DockStyle.Top;
        options.Margin = Padding.Empty;
        viewport.Controls.Add(options);
        Controls.Add(viewport, 0, 1);
    }

    private sealed class TimelineViewport(FrameShiftTimelineWorkspace owner) : Panel
    {
        protected override Point ScrollToControl(Control activeControl)
        {
            // Focusing a partly visible candidate grid must not snap its entire top
            // into view. Keyboard navigation still scrolls rows inside the grid;
            // controls outside the viewport retain normal focus/reveal behavior.
            if (owner.MinimumPreviewHeight > 0 && activeControl is DataGridView)
            {
                // Use layout coordinates: a child's native window position can still
                // be provisional while an ancestor is being resized or shown.
                var bounds = activeControl.Bounds;
                var parent = activeControl.Parent;
                while (parent is not null && parent != this)
                {
                    bounds.Offset(parent.Location);
                    parent = parent.Parent;
                }
                if (parent == this && ClientRectangle.IntersectsWith(bounds)) return DisplayRectangle.Location;
            }
            return base.ScrollToControl(activeControl);
        }
    }

    internal int MeasureOptionsHeight(int width)
    {
        // Reserve scrollbar width in this mode so wrapping does not alternate when it appears.
        if (MinimumPreviewHeight > 0) width -= SystemInformation.VerticalScrollBarWidth;
        return _options.GetPreferredSize(new Size(Math.Max(1, width), 0)).Height;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (_arranging || _options is null || Controls.Count < 2) { base.OnLayout(e); return; }
        _arranging = true;
        try
        {
            var gap = FrameShiftUiMetrics.ToPixels(this, FrameShiftUiMetrics.OuterPadding);
            Controls[0].Margin = new Padding(0, 0, 0, gap);
            var height = MeasureOptionsHeight(ClientSize.Width);
            var available = MinimumPreviewHeight > 0
                ? Math.Max(ClientSize.Height * 0.6f,
                    ClientSize.Height - FrameShiftUiMetrics.ToPixels(this, MinimumPreviewHeight) - gap)
                : ClientSize.Height * 0.6f;
            var optionsHeight = Math.Min(height, Math.Max(0, available));
            if (RowStyles[1].Height != optionsHeight) RowStyles[1].Height = optionsHeight;
            base.OnLayout(e);
        }
        finally { _arranging = false; }
    }
}
