using System.Drawing;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Controls;

/// <summary>A wrapping, selectable message. Long details scroll without displacing the footer.</summary>
public sealed class FrameShiftStatusMessage : TextBox
{
    public FrameShiftStatusMessage()
    {
        Multiline = true;
        ReadOnly = true;
        WordWrap = true;
        ScrollBars = ScrollBars.Vertical;
        BorderStyle = BorderStyle.None;
        AutoSize = false;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        BackColor = FrameShiftTheme.PageBackground;
        ForeColor = FrameShiftTheme.TextSecondary;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width > 0 ? proposedSize.Width : Math.Max(1, Width);
        var measured = TextRenderer.MeasureText(Text, Font,
            new Size(Math.Max(1, width - SystemInformation.VerticalScrollBarWidth), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
        var height = Math.Max(Font.Height, measured.Height + FrameShiftUiMetrics.ToPixels(this, 4));
        return new Size(width, Math.Min(height, FrameShiftUiMetrics.ToPixels(this, 96)));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Parent?.PerformLayout();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Parent?.PerformLayout();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Parent?.PerformLayout();
    }
}
