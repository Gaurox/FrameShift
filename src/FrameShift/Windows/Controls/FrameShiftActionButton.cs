using System.Drawing;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Controls;

/// <summary>Uses current text/font/DPI even when added after the form has been scaled.</summary>
public sealed class FrameShiftActionButton : Button
{
    private bool _hovered;
    private bool _pressed;
    public int LogicalMinimumWidth { get; }

    public FrameShiftActionButton(int logicalMinimumWidth)
    {
        LogicalMinimumWidth = logicalMinimumWidth;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public override Size GetPreferredSize(Size proposedSize)
        => MeasureForWidth(MaximumSize.Width);

    internal Size MeasureForWidth(int maximumWidth)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine);
        var preferred = FrameShiftUiLayout.MeasureActionButton(text, DeviceDpi, LogicalMinimumWidth);
        if (maximumWidth > 0 && preferred.Width > maximumWidth)
        {
            var innerWidth = Math.Max(1, maximumWidth - FrameShiftUiMetrics.ToPixels(this, 28));
            text = TextRenderer.MeasureText(Text, Font, new Size(innerWidth, int.MaxValue), TextFormatFlags.WordBreak);
            return new Size(maximumWidth, FrameShiftUiLayout.MeasureActionButton(text, DeviceDpi, 0).Height);
        }
        return preferred;
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Parent?.PerformLayout();
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = e.Button == MouseButtons.Left; base.OnMouseDown(e); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; base.OnMouseUp(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) _pressed = true; base.OnKeyDown(e); Invalidate(); }
    protected override void OnKeyUp(KeyEventArgs e) { _pressed = false; base.OnKeyUp(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { _pressed = false; base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { _hovered = _pressed = false; base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = FrameShiftUiPainter.CreateRoundedPath(bounds, FrameShiftUiMetrics.ToPixels(this, 6));
        var color = !Enabled ? FrameShiftTheme.PageBackground : _pressed ? FlatAppearance.MouseDownBackColor
            : _hovered ? FlatAppearance.MouseOverBackColor : BackColor;
        if (color.IsEmpty) color = BackColor;
        using var fill = new SolidBrush(color);
        using var pen = new Pen(!Enabled ? FrameShiftTheme.SurfaceBorder : Focused ? FrameShiftTheme.AccentText
            : FlatAppearance.BorderColor, FrameShiftUiMetrics.ToPixels(this, 1));
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
        var textBounds = Rectangle.Inflate(bounds, -FrameShiftUiMetrics.ToPixels(this, 10), -FrameShiftUiMetrics.ToPixels(this, 5));
        if (textBounds.Width <= 0 || textBounds.Height <= 0) return;
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : FrameShiftTheme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
        if (Enabled && Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -4, -4), ForeColor, color);
    }
}
