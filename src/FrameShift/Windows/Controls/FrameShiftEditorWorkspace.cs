using System.Drawing;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Controls;

/// <summary>Flexible preview and independently scrolling options; stacked when too narrow.</summary>
public sealed class FrameShiftEditorWorkspace : TableLayoutPanel
{
    private readonly Control _preview;
    private readonly Panel _optionsViewport;
    private readonly int _logicalRailWidth;
    private bool _arranging;
    private bool? _stacked;

    public FrameShiftEditorWorkspace(Control preview, Control options, int logicalRailWidth)
    {
        _preview = preview;
        _logicalRailWidth = logicalRailWidth;
        _optionsViewport = FrameShiftDialogLayout.CreateScrollBody(options);
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        preview.Dock = DockStyle.Fill;
        preview.Margin = Padding.Empty;
        Controls.Add(preview);
        Controls.Add(_optionsViewport);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (_preview is null || _arranging) { base.OnLayout(e); return; }
        _arranging = true;
        try
        {
            var gap = FrameShiftUiMetrics.ToPixels(this, FrameShiftUiMetrics.OuterPadding);
            var rail = FrameShiftUiMetrics.ToPixels(this, _logicalRailWidth);
            var stacked = ClientSize.Width < rail + FrameShiftUiMetrics.ToPixels(this, 320) + gap;
            if (_stacked != stacked)
            {
                _stacked = stacked;
                ColumnStyles.Clear();
                RowStyles.Clear();
                ColumnCount = stacked ? 1 : 2;
                RowCount = stacked ? 2 : 1;
                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                RowStyles.Add(new RowStyle(SizeType.Percent, stacked ? 60 : 100));
                if (stacked) RowStyles.Add(new RowStyle(SizeType.Percent, 40));
                else ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, rail));
                SetCellPosition(_preview, new TableLayoutPanelCellPosition(0, 0));
                SetCellPosition(_optionsViewport, new TableLayoutPanelCellPosition(stacked ? 0 : 1, stacked ? 1 : 0));
            }
            if (!stacked) ColumnStyles[1].Width = rail;
            _preview.Margin = stacked ? new Padding(0, 0, 0, gap) : new Padding(0, 0, gap, 0);
            base.OnLayout(e);
        }
        finally { _arranging = false; }
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PerformLayout();
    }
}
