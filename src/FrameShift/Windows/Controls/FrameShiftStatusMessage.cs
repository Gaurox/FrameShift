using System.Drawing;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Controls;

/// <summary>A wrapping, selectable message. Long details scroll without displacing the footer.</summary>
public sealed class FrameShiftStatusMessage : TextBox
{
    private bool _scrollUpdateQueued;
    private bool _updatingScrollBars;
    private bool _measuringHeight;
    public FrameShiftStatusMessage()
    {
        Multiline = true;
        ReadOnly = true;
        WordWrap = true;
        ScrollBars = ScrollBars.None;
        BorderStyle = BorderStyle.None;
        // Let auto-sized table rows use GetPreferredSize after font/width changes.
        AutoSize = true;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        BackColor = FrameShiftTheme.PageBackground;
        ForeColor = FrameShiftTheme.TextSecondary;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width > 0 ? proposedSize.Width : Math.Max(1, Width);
        var measured = TextRenderer.MeasureText(Text, Font,
            new Size(Math.Max(1, width), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
        var height = Math.Max(PreferredHeight, measured.Height + FrameShiftUiMetrics.ToPixels(this, 4));
        return new Size(width, Math.Min(height, FrameShiftUiMetrics.ToPixels(this, 96)));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        UpdateMeasuredHeight();
        Parent?.PerformLayout();
        UpdateScrollBars();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateMeasuredHeight();
        Parent?.PerformLayout();
        UpdateScrollBars();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateMeasuredHeight();
        Parent?.PerformLayout();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateMeasuredHeight();
        UpdateScrollBars();
    }

    private void UpdateMeasuredHeight()
    {
        if (_measuringHeight || IsDisposed || Disposing) return;
        _measuringHeight = true;
        try
        {
            // Native multiline edit controls may retain their old height in an AutoSize row.
            // Reserve the measured height explicitly, still capped by GetPreferredSize.
            var height = GetPreferredSize(new Size(Math.Max(1, Width), 0)).Height;
            if (MinimumSize.Height != height) MinimumSize = new Size(0, height);
            // A multiline native edit does not shrink itself when its minimum decreases.
            // Drop the provisional wrapped height as soon as the real width is known.
            if (Height != height) Height = height;
        }
        finally { _measuringHeight = false; }
    }

    private void UpdateScrollBars()
    {
        if (IsDisposed || Disposing || RecreatingHandle || _updatingScrollBars) return;
        if (IsHandleCreated)
        {
            // ScrollBars recreates the native edit handle. Never do that inside WM_WINDOWPOSCHANGED.
            if (_scrollUpdateQueued) return;
            _scrollUpdateQueued = true;
            BeginInvoke((Action)(() =>
            {
                _scrollUpdateQueued = false;
                if (!IsDisposed && !Disposing && IsHandleCreated) ApplyScrollBars();
            }));
        }
        else ApplyScrollBars();
    }

    private void ApplyScrollBars()
    {
        if (_updatingScrollBars) return;
        _updatingScrollBars = true;
        try
        {
            var measured = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, Width), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
            var scroll = measured.Height > Height ? ScrollBars.Vertical : ScrollBars.None;
            if (ScrollBars != scroll) ScrollBars = scroll;
        }
        finally { _updatingScrollBars = false; }
    }
}
