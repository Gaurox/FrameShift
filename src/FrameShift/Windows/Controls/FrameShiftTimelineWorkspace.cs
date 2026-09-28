using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Controls;

/// <summary>Preview above temporal controls. Controls scroll only when the working area is short.</summary>
public sealed class FrameShiftTimelineWorkspace : TableLayoutPanel
{
    private readonly Control _options;
    private bool _arranging;

    public FrameShiftTimelineWorkspace(Control preview, Control options)
    {
        _options = options;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        ColumnCount = 1;
        RowCount = 2;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        preview.Dock = DockStyle.Fill;
        Controls.Add(preview, 0, 0);
        Controls.Add(FrameShiftDialogLayout.CreateScrollBody(options), 0, 1);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (_arranging || _options is null || Controls.Count < 2) { base.OnLayout(e); return; }
        _arranging = true;
        try
        {
            var gap = FrameShiftUiMetrics.ToPixels(this, FrameShiftUiMetrics.OuterPadding);
            Controls[0].Margin = new Padding(0, 0, 0, gap);
            var height = _options.GetPreferredSize(new Size(Math.Max(1, ClientSize.Width), 0)).Height;
            RowStyles[1].Height = Math.Min(height, Math.Max(0, ClientSize.Height * 0.6f));
            base.OnLayout(e);
        }
        finally { _arranging = false; }
    }
}
