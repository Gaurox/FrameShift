using System.Drawing;
using System.Windows.Forms;

namespace FrameShift.Windows.Helpers;

/// <summary>Metrics for the two queue grids; rows follow text and fixed columns follow DPI.</summary>
internal static class FrameShiftGridUi
{
    public static void Apply(DataGridView grid, params (string Name, int Width)[] columns)
    {
        void Metrics()
        {
            if (grid.IsDisposed) return;
            var inset = FrameShiftUiMetrics.ToPixels(grid, 6);
            grid.DefaultCellStyle.Padding = new Padding(0, inset, 0, inset);
            var height = Math.Max(FrameShiftUiMetrics.ToPixels(grid, 36), grid.Font.Height + 2 * inset);
            grid.RowTemplate.Height = height;
            foreach (DataGridViewRow row in grid.Rows) row.Height = height;
            foreach (var (name, width) in columns)
                grid.Columns[name]!.Width = FrameShiftUiMetrics.ToPixels(grid, width);
        }
        grid.ScrollBars = ScrollBars.Both;
        grid.HandleCreated += (_, _) => Metrics();
        grid.DpiChangedAfterParent += (_, _) => Metrics();
        grid.FontChanged += (_, _) => Metrics();
        Metrics();
    }
}
