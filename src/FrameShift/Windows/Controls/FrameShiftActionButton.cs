using System.Drawing;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Controls;

/// <summary>Uses current text/font/DPI even when added after the form has been scaled.</summary>
public sealed class FrameShiftActionButton : Button
{
    public int LogicalMinimumWidth { get; }

    public FrameShiftActionButton(int logicalMinimumWidth)
    {
        LogicalMinimumWidth = logicalMinimumWidth;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine);
        var preferred = FrameShiftUiLayout.MeasureActionButton(text, DeviceDpi, LogicalMinimumWidth);
        if (MaximumSize.Width > 0 && preferred.Width > MaximumSize.Width)
        {
            var innerWidth = Math.Max(1, MaximumSize.Width - FrameShiftUiMetrics.ToPixels(this, 28));
            text = TextRenderer.MeasureText(Text, Font, new Size(innerWidth, int.MaxValue), TextFormatFlags.WordBreak);
            return new Size(MaximumSize.Width, FrameShiftUiLayout.MeasureActionButton(text, DeviceDpi, 0).Height);
        }
        return preferred;
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Parent?.PerformLayout();
    }
}
