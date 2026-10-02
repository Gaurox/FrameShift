using System.Drawing;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Controls;

/// <summary>Measured header with keyboard access to complete metadata.</summary>
public sealed class FrameShiftHeader : Panel
{
    public Label TitleLabel { get; }
    public Label SubtitleLabel { get; }
    private readonly PictureBox _icon;
    private readonly ToolTip _toolTip = new();
    private readonly ContextMenuStrip _metadataMenu = new();
    private readonly string _iconPath;
    private readonly Point _iconOffset;
    private readonly Font _titleFont = new("Segoe UI Semibold", 14F, FontStyle.Regular, GraphicsUnit.Point);
    private Size _iconSize;
    internal Action<string> CopyDetailsText { get; set; } = Clipboard.SetText;
    internal Action<Form, IWin32Window?> DisplayDetails { get; set; } = (dialog, owner) => dialog.ShowDialog(owner);

    public FrameShiftHeader(string title, string subtitle, string iconPath, string fallbackGlyph, Point iconOffset)
    {
        _iconPath = iconPath;
        _iconOffset = iconOffset;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        BackColor = FrameShiftTheme.Surface;
        SetStyle(ControlStyles.Selectable, true);
        AccessibleRole = AccessibleRole.Grouping;
        _icon = new PictureBox { BackColor = FrameShiftTheme.AccentSoft, SizeMode = PictureBoxSizeMode.CenterImage, TabStop = false };
        TitleLabel = new Label { Text = title, Font = _titleFont, ForeColor = FrameShiftTheme.TextPrimary, UseMnemonic = false };
        SubtitleLabel = new Label { Text = subtitle, ForeColor = FrameShiftTheme.TextSecondary, AutoEllipsis = true, UseMnemonic = false };
        Controls.AddRange([_icon, TitleLabel, SubtitleLabel]);
        if (string.IsNullOrEmpty(iconPath))
            _icon.Controls.Add(new Label { Text = fallbackGlyph, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
        _metadataMenu.Items.Add("View details", null, (_, _) => ShowDetails());
        var copy = new ToolStripMenuItem("Copy details") { ShortcutKeyDisplayString = "Ctrl+C" };
        copy.Click += (_, _) => CopyDetails();
        _metadataMenu.Items.Add(copy);
        ContextMenuStrip = _metadataMenu;
        SubtitleLabel.ContextMenuStrip = _metadataMenu;
        SubtitleLabel.TextChanged += (_, _) => RefreshContent();
        TitleLabel.TextChanged += (_, _) => RefreshContent();
        TitleLabel.FontChanged += (_, _) => RefreshContent();
        SubtitleLabel.FontChanged += (_, _) => RefreshContent();
        FrameShiftUiPainter.AttachRoundedBorder(this, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.PanelCornerRadius);
        RefreshContent();
    }

    private void RefreshContent()
    {
        _toolTip.SetToolTip(SubtitleLabel, SubtitleLabel.Text);
        TabStop = !string.IsNullOrEmpty(SubtitleLabel.Text);
        AccessibleName = TitleLabel.Text;
        AccessibleDescription = TabStop ? $"{SubtitleLabel.Text}. Enter: view details. Ctrl+C: copy details. Shift+F10: menu." : TitleLabel.Text;
        SubtitleLabel.AccessibleName = "Source details";
        SubtitleLabel.AccessibleDescription = SubtitleLabel.Text;
        foreach (ToolStripItem item in _metadataMenu.Items) item.Enabled = TabStop;
        PerformLayout();
        Parent?.PerformLayout();
    }

    private void CopyDetails()
    {
        if (Enabled && TabStop) CopyDetailsText(SubtitleLabel.Text);
    }

    private void ShowDetails()
    {
        if (!Enabled || !TabStop) return;
        using var dialog = new Form();
        dialog.SuspendLayout();
        FrameShiftWindowPolicy.Initialize(dialog, new Size(680, 320), new Size(360, 240));
        FrameShiftWindowChrome.Apply(dialog, "FrameShift - Source details");
        var header = new FrameShiftHeader("FrameShift - Source details", "", _iconPath, "", _iconOffset);
        var text = new TextBox
        {
            Text = SubtitleLabel.Text, Multiline = true, ReadOnly = true, WordWrap = true,
            ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            BackColor = FrameShiftTheme.Surface, ForeColor = FrameShiftTheme.TextPrimary,
            AccessibleName = "Complete source details"
        };
        var close = FrameShiftUiFactory.CreateMeasuredActionButton("Close", false);
        close.DialogResult = DialogResult.Cancel;
        dialog.AcceptButton = dialog.CancelButton = close;
        dialog.Controls.Add(FrameShiftDialogLayout.CreateShell(header,
            FrameShiftUiFactory.CreateSection("Source details — Ctrl+C to copy selected text", text, fill: true),
            FrameShiftDialogLayout.CreateActions(close), null));
        dialog.ResumeLayout(true);
        DisplayDetails(dialog, FindForm());
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.C) && Enabled && TabStop)
        {
            CopyDetails();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData is Keys.Enter or Keys.Space && Enabled && TabStop)
        {
            ShowDetails();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Enter or Keys.Space ? false : base.IsInputKey(keyData);

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Focused && ShowFocusCues && Enabled)
        {
            var bounds = Rectangle.Inflate(ClientRectangle, -3, -3);
            if (bounds.Width > 0 && bounds.Height > 0)
                ControlPaint.DrawFocusRectangle(e.Graphics, bounds, FrameShiftTheme.AccentText, BackColor);
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width > 0 ? proposedSize.Width : Math.Max(1, Width);
        return new Size(width, Measure(width).Height);
    }

    private (int Left, int Top, int TextWidth, int TitleHeight, int SubtitleHeight, int Icon, int Height) Measure(int width)
    {
        var pad = FrameShiftUiMetrics.ToPixels(this, FrameShiftUiMetrics.OuterPadding);
        var top = FrameShiftUiMetrics.ToPixels(this, FrameShiftUiMetrics.LineGap);
        var icon = FrameShiftUiMetrics.ToPixels(this, FrameShiftUiMetrics.HeaderIconSize);
        var left = pad + icon + FrameShiftUiMetrics.ToPixels(this, FrameShiftUiMetrics.BlockGap);
        var textWidth = Math.Max(1, width - left - pad);
        var titleHeight = TextRenderer.MeasureText(TitleLabel.Text, TitleLabel.Font,
            new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        var subtitleHeight = string.IsNullOrEmpty(SubtitleLabel.Text) ? 0 : TextRenderer.MeasureText(
            SubtitleLabel.Text, SubtitleLabel.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Height;
        return (left, top, textWidth, titleHeight, subtitleHeight, icon,
            Math.Max(FrameShiftUiMetrics.ToPixels(this, FrameShiftUiMetrics.HeaderHeight),
                2 * top + Math.Max(icon, titleHeight + subtitleHeight)));
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (TitleLabel is null) return;
        var m = Measure(ClientSize.Width);
        TitleLabel.SetBounds(m.Left, m.Top, m.TextWidth, m.TitleHeight);
        SubtitleLabel.SetBounds(m.Left, m.Top + m.TitleHeight, m.TextWidth, m.SubtitleHeight);
        _icon.SetBounds(FrameShiftUiMetrics.ToPixels(this, FrameShiftUiMetrics.OuterPadding), m.Top, m.Icon, m.Icon);
        if (_icon.Size != _iconSize && !string.IsNullOrEmpty(_iconPath))
        {
            var image = FrameShiftUiPainter.CreateCenteredIconBackground(_iconPath, _icon.Size,
                new Point(FrameShiftUiMetrics.ToPixels(this, _iconOffset.X), FrameShiftUiMetrics.ToPixels(this, _iconOffset.Y)), DeviceDpi);
            var old = _icon.Image;
            _icon.Image = image;
            old?.Dispose();
            _iconSize = _icon.Size;
        }
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        RefreshContent();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            _metadataMenu.Dispose();
            _icon.Image?.Dispose();
        }
        base.Dispose(disposing);
        if (disposing) _titleFont.Dispose();
    }
}
