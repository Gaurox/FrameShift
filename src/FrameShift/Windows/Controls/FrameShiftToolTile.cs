using System.Drawing;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Controls;

/// <summary>Native editor tool button with a measured caption and a DPI-sized icon.</summary>
internal sealed class FrameShiftToolTile : Button
{
    private string? _iconPath;
    private readonly string _heightReferenceCaption;
    internal FrameShiftToolTile(string text, string? heightReferenceCaption = null)
    {
        Text = text;
        _heightReferenceCaption = heightReferenceCaption ?? text;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlatStyle = FlatStyle.Flat;
        BackColor = FrameShiftTheme.Surface;
        ForeColor = FrameShiftTheme.TextPrimary;
        TextImageRelation = TextImageRelation.ImageAboveText;
        FlatAppearance.BorderColor = FrameShiftTheme.PrimaryBlue;
        FlatAppearance.MouseOverBackColor = FrameShiftTheme.AccentSoft;
        FlatAppearance.MouseDownBackColor = FrameShiftTheme.AccentSoftHover;
        Cursor = Cursors.Hand;
    }

    internal void SetIcon(string path) { _iconPath = path; UpdateIcon(); }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = FrameShiftUiMetrics.ToPixels(this, 88);
        if (MaximumSize.Width > 0) width = Math.Min(width, MaximumSize.Width);
        var padding = FrameShiftUiMetrics.ToPixels(this, 12);
        var captionSize = new Size(Math.Max(1, width - padding), int.MaxValue);
        var caption = TextRenderer.MeasureText(Text, Font, captionSize, TextFormatFlags.WordBreak);
        var reference = TextRenderer.MeasureText(_heightReferenceCaption, Font, captionSize, TextFormatFlags.WordBreak);
        // Reserve the same caption space across tools, including wrapped labels.
        var height = Math.Max(caption.Height, reference.Height) + FrameShiftUiMetrics.ToPixels(this, 38);
        return new Size(width, Math.Max(FrameShiftUiMetrics.ToPixels(this, 64), height));
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateIcon(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateIcon(); Parent?.PerformLayout(); }

    private void UpdateIcon()
    {
        var previous = Image;
        Image = null;
        previous?.Dispose();
        if (_iconPath is not null && File.Exists(_iconPath))
        {
            using var icon = new Icon(_iconPath, new Size(FrameShiftUiMetrics.ToPixels(this, 26), FrameShiftUiMetrics.ToPixels(this, 26)));
            Image = icon.ToBitmap();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { var image = Image; Image = null; image?.Dispose(); }
        base.Dispose(disposing);
    }
}
