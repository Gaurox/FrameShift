using System.Drawing.Drawing2D;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Controls;

/// <summary>A native radio choice with a measured title/description and a visible selected state.</summary>
public sealed class FrameShiftChoiceCard : RadioButton
{
    public string Description { get; }
    private readonly int _logicalWidth;

    public FrameShiftChoiceCard(string title, string description, int logicalWidth = 164)
    {
        Text = title;
        Description = description;
        AccessibleDescription = description;
        _logicalWidth = logicalWidth;
        AutoSize = true;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = FrameShiftUiMetrics.ToPixels(this, _logicalWidth);
        if (MaximumSize.Width > 0) width = Math.Min(width, MaximumSize.Width);
        var pad = FrameShiftUiMetrics.ToPixels(this, 12);
        using var titleFont = new Font(Font, FontStyle.Bold);
        var title = TextRenderer.MeasureText(Text, titleFont, new Size(Math.Max(1, width - 2 * pad - FrameShiftUiMetrics.ToPixels(this, 22)), int.MaxValue), TextFormatFlags.WordBreak);
        var detail = TextRenderer.MeasureText(Description, Font, new Size(Math.Max(1, width - 2 * pad), int.MaxValue), TextFormatFlags.WordBreak);
        return new Size(width, 2 * pad + Math.Max(title.Height, FrameShiftUiMetrics.ToPixels(this, 16)) + FrameShiftUiMetrics.ToPixels(this, 8) + detail.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var pad = FrameShiftUiMetrics.ToPixels(this, 12);
        var glyph = FrameShiftUiMetrics.ToPixels(this, 16);
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = FrameShiftUiPainter.CreateRoundedPath(bounds, FrameShiftUiMetrics.ToPixels(this, 6));
        using var fill = new SolidBrush(Checked ? FrameShiftTheme.AccentSoft : FrameShiftTheme.Surface);
        using var pen = new Pen(Checked || Focused ? FrameShiftTheme.SecondaryBlue : FrameShiftTheme.SurfaceBorder, FrameShiftUiMetrics.ToPixels(this, 1));
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
        ControlPaint.DrawRadioButton(e.Graphics, new Rectangle(pad, pad, glyph, glyph), Checked ? ButtonState.Checked : ButtonState.Normal);
        using var titleFont = new Font(Font, FontStyle.Bold);
        var titleRect = new Rectangle(pad + glyph + FrameShiftUiMetrics.ToPixels(this, 6), pad, Math.Max(1, Width - 2 * pad - glyph - FrameShiftUiMetrics.ToPixels(this, 6)), Height);
        var titleHeight = TextRenderer.MeasureText(Text, titleFont, titleRect.Size, TextFormatFlags.WordBreak).Height;
        TextRenderer.DrawText(e.Graphics, Text, titleFont, titleRect, Enabled ? FrameShiftTheme.TextPrimary : SystemColors.GrayText, TextFormatFlags.WordBreak);
        var detailTop = pad + Math.Max(glyph, titleHeight) + FrameShiftUiMetrics.ToPixels(this, 8);
        TextRenderer.DrawText(e.Graphics, Description, Font, new Rectangle(pad, detailTop, Math.Max(1, Width - 2 * pad), Math.Max(1, Height - detailTop - pad)), Enabled ? FrameShiftTheme.TextSecondary : SystemColors.GrayText, TextFormatFlags.WordBreak);
        if (Focused && ShowFocusCues && bounds.Width > 6 && bounds.Height > 6)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -3, -3));
    }
}
