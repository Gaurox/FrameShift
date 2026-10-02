using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Controls;

internal sealed class JoinVideosTimelineControl : Control
{
    private const int TileHeight = 166;
    private const int TileTop = 8;
    private const int TilePadding = 8;
    private const int MinimumInteractiveWidth = 12;
    private const int ThumbnailHeight = 88;

    private IReadOnlyList<JoinVideoTimelineItem> _items = [];
    private readonly List<Rectangle> _tileBounds = [];
    private readonly ToolTip _itemToolTip = new() { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 5000 };
    private int _selectedIndex = -1;
    private int _draggedIndex = -1;
    private int _dragInsertionIndex = -1;
    private int _toolTipIndex = -1;
    private Point _mouseDownPoint;
    private bool _isDragging;
    private bool _isExternalDragHover;

    public JoinVideosTimelineControl()
    {
        BackColor = FrameShiftTheme.PageBackground;
        Height = TileHeight + (TileTop * 2);
        Cursor = Cursors.Default;
        TabStop = true;
        AccessibleRole = AccessibleRole.List;
        AccessibleName = "Video clips timeline";
        ControlHelper.SetDoubleBuffered(this);
        SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        UpdateAccessibleDescription();
    }

    private int Pixels(int logical) => FrameShiftUiMetrics.ToPixels(this, logical);
    private int MeasuredTileHeight => Math.Max(Pixels(TileHeight), Pixels(ThumbnailHeight + 30) + 2 * Font.Height);
    internal int PreferredTimelineHeight => MeasuredTileHeight + Pixels(TileTop * 2);

    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); RebuildLayout(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (_tileBounds is not null) RebuildLayout(); }

    public event EventHandler? SelectedIndexChanged;

    public event EventHandler<JoinVideosTimelineMoveEventArgs>? MoveRequested;
    public event EventHandler? RemoveRequested;

    public int SelectedIndex => _selectedIndex;

    public void SetItems(IReadOnlyList<JoinVideoTimelineItem> items)
    {
        _items = items;
        if (_selectedIndex >= _items.Count)
        {
            SelectIndex(_items.Count - 1);
        }

        RebuildLayout();
        UpdateAccessibleDescription();
    }

    public void SelectIndex(int index)
    {
        var normalized = index >= 0 && index < _items.Count ? index : -1;
        if (_selectedIndex == normalized)
        {
            return;
        }

        _selectedIndex = normalized;
        UpdateAccessibleDescription();
        if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.Selection, -1);
        EnsureSelectionVisible();
        Invalidate();
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateAccessibleDescription()
    {
        var selection = _selectedIndex >= 0 && _selectedIndex < _items.Count
            ? $"Clip {_selectedIndex + 1} of {_items.Count}: {_items[_selectedIndex].SourcePath}. {_items[_selectedIndex].LoadError}"
            : $"{_items.Count} clips. No clip selected.";
        AccessibleDescription = $"{selection} Left/Right, Home/End: select. Ctrl+Left/Right: move. Delete: remove selected clip.";
    }

    protected override bool IsInputKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        return key is Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.Enter or Keys.Space || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!Enabled) { base.OnKeyDown(e); return; }
        if (e.Modifiers == Keys.None && e.KeyCode is Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.Enter or Keys.Space)
        {
            if (_items.Count > 0)
            {
                var index = e.KeyCode switch
                {
                    Keys.Home => 0,
                    Keys.End => _items.Count - 1,
                    Keys.Left => Math.Max(0, _selectedIndex - 1),
                    Keys.Right => Math.Min(_items.Count - 1, _selectedIndex + 1),
                    _ => Math.Max(0, _selectedIndex)
                };
                SelectIndex(index);
            }
        }
        else if (e.Modifiers == Keys.Control && e.KeyCode is Keys.Left or Keys.Right)
        {
            var target = _selectedIndex + (e.KeyCode == Keys.Left ? -1 : 1);
            if (_selectedIndex >= 0 && target >= 0 && target < _items.Count)
                MoveRequested?.Invoke(this, new JoinVideosTimelineMoveEventArgs(_selectedIndex,
                    e.KeyCode == Keys.Left ? target : target + 1));
        }
        else if (e.Modifiers == Keys.None && e.KeyCode == Keys.Delete)
        {
            if (_selectedIndex >= 0) RemoveRequested?.Invoke(this, EventArgs.Empty);
        }
        else { base.OnKeyDown(e); return; }
        e.Handled = e.SuppressKeyPress = true;
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (_selectedIndex < 0 && _items.Count > 0) SelectIndex(0);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    private void EnsureSelectionVisible()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _tileBounds.Count) return;
        var bounds = _tileBounds[_selectedIndex];
        Control child = this;
        for (var parent = Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            bounds.Offset(child.Left, child.Top);
            if (parent is not ScrollableControl { AutoScroll: true } viewport || viewport.ClientSize.Width <= 0 || viewport.ClientSize.Height <= 0)
                continue;
            var dx = bounds.Left < 0 || bounds.Width > viewport.ClientSize.Width ? bounds.Left : Math.Max(0, bounds.Right - viewport.ClientSize.Width);
            var dy = bounds.Top < 0 || bounds.Height > viewport.ClientSize.Height ? bounds.Top : Math.Max(0, bounds.Bottom - viewport.ClientSize.Height);
            if (dx == 0 && dy == 0) continue;
            var oldPosition = viewport.AutoScrollPosition;
            viewport.AutoScrollPosition = new Point(-oldPosition.X + dx, -oldPosition.Y + dy);
            bounds.Offset(viewport.AutoScrollPosition.X - oldPosition.X, viewport.AutoScrollPosition.Y - oldPosition.Y);
        }
    }

    internal int ShowExternalDropIndicator(int clientX)
    {
        var insertion = GetInsertionIndex(clientX);
        _isExternalDragHover = true;
        if (_dragInsertionIndex != insertion)
        {
            _dragInsertionIndex = insertion;
            Invalidate();
        }

        return insertion;
    }

    internal void ClearExternalDropIndicator()
    {
        if (!_isExternalDragHover)
        {
            return;
        }

        _isExternalDragHover = false;
        _dragInsertionIndex = -1;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RebuildLayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (Focused && ShowFocusCues && _items.Count == 0)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3), FrameShiftTheme.AccentText, BackColor);

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(
                e.Graphics,
                "Add at least two videos to build the timeline.",
                Font,
                ClientRectangle,
                FrameShiftTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        for (var index = 0; index < _items.Count && index < _tileBounds.Count; index++)
        {
            DrawTile(e.Graphics, _tileBounds[index], _items[index], index == _selectedIndex, index == _draggedIndex && _isDragging);
        }
        if (Focused && ShowFocusCues && _selectedIndex >= 0 && _selectedIndex < _tileBounds.Count)
        {
            var bounds = Rectangle.Inflate(_tileBounds[_selectedIndex], -3, -3);
            if (bounds.Width > 0 && bounds.Height > 0)
                ControlPaint.DrawFocusRectangle(e.Graphics, bounds, FrameShiftTheme.AccentText, FrameShiftTheme.AccentSoft);
        }

        if ((_isDragging || _isExternalDragHover) && _dragInsertionIndex >= 0)
        {
            var markerX = GetInsertionMarkerX(_dragInsertionIndex);
            using var pen = new Pen(FrameShiftTheme.AccentText, Pixels(3));
            e.Graphics.DrawLine(pen, markerX, Pixels(TileTop + 2), markerX, Pixels(TileTop) + MeasuredTileHeight - Pixels(2));
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        Focus();
        var index = GetTileIndexAt(e.Location);
        SelectIndex(index);
        if (index >= 0)
        {
            _mouseDownPoint = e.Location;
            _draggedIndex = index;
            _dragInsertionIndex = index;
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateToolTip(GetTileIndexAt(e.Location));
        if (_draggedIndex < 0 || (e.Button & MouseButtons.Left) != MouseButtons.Left)
        {
            Cursor = GetTileIndexAt(e.Location) >= 0 ? Cursors.SizeWE : Cursors.Default;
            return;
        }

        if (!_isDragging && Math.Abs(e.X - _mouseDownPoint.X) < Pixels(5) && Math.Abs(e.Y - _mouseDownPoint.Y) < Pixels(5))
        {
            return;
        }

        _isDragging = true;
        Cursor = Cursors.SizeWE;
        var insertion = GetInsertionIndex(e.X);
        if (_dragInsertionIndex != insertion)
        {
            _dragInsertionIndex = insertion;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateToolTip(-1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _itemToolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        Capture = false;
        if (_isDragging && _draggedIndex >= 0 && _dragInsertionIndex >= 0)
        {
            MoveRequested?.Invoke(this, new JoinVideosTimelineMoveEventArgs(_draggedIndex, _dragInsertionIndex));
        }

        _draggedIndex = -1;
        _dragInsertionIndex = -1;
        _isDragging = false;
        Cursor = GetTileIndexAt(e.Location) >= 0 ? Cursors.SizeWE : Cursors.Default;
        Invalidate();
    }

    private void RebuildLayout()
    {
        _tileBounds.Clear();
        var durations = _items.Select(item => Math.Max(item.DurationSeconds, 0d)).ToArray();
        var widths = CalculateSegmentWidths(durations, Math.Max(ClientSize.Width, 0));
        var x = 0;
        for (var index = 0; index < _items.Count; index++)
        {
            _tileBounds.Add(new Rectangle(x, Pixels(TileTop), widths[index], MeasuredTileHeight));
            x += widths[index];
        }

        Invalidate();
    }

    internal static int[] CalculateSegmentWidths(IReadOnlyList<double> durations, int totalWidth)
    {
        var widths = new int[durations.Count];
        if (durations.Count == 0 || totalWidth <= 0)
        {
            return widths;
        }

        var normalizedDurations = durations
            .Select(duration => double.IsFinite(duration) && duration > 0d ? duration : 0d)
            .ToArray();
        var knownDurations = normalizedDurations.Where(duration => duration > 0d).ToArray();
        var unknownDuration = knownDurations.Length > 0 ? knownDurations.Average() : 1d;
        var weights = normalizedDurations
            .Select(duration => duration > 0d ? duration : unknownDuration)
            .ToArray();

        var minimumWidth = totalWidth >= durations.Count
            ? Math.Min(MinimumInteractiveWidth, totalWidth / durations.Count)
            : 0;
        var exactWidths = new double[durations.Count];
        var fixedWidths = new bool[durations.Count];
        var remainingWidth = (double)totalWidth;
        var remainingWeight = weights.Sum();

        while (remainingWeight > 0d)
        {
            var fixedAnotherSegment = false;
            for (var index = 0; index < weights.Length; index++)
            {
                if (fixedWidths[index])
                {
                    continue;
                }

                var proportionalWidth = remainingWidth * weights[index] / remainingWeight;
                if (proportionalWidth + 0.000001d >= minimumWidth)
                {
                    continue;
                }

                exactWidths[index] = minimumWidth;
                fixedWidths[index] = true;
                remainingWidth -= minimumWidth;
                remainingWeight -= weights[index];
                fixedAnotherSegment = true;
            }

            if (!fixedAnotherSegment)
            {
                break;
            }
        }

        for (var index = 0; index < weights.Length; index++)
        {
            if (!fixedWidths[index])
            {
                exactWidths[index] = remainingWeight > 0d
                    ? remainingWidth * weights[index] / remainingWeight
                    : 0d;
            }

            widths[index] = (int)Math.Floor(exactWidths[index]);
        }

        var remainingPixels = totalWidth - widths.Sum();
        foreach (var index in Enumerable.Range(0, widths.Length)
                     .OrderByDescending(index => exactWidths[index] - widths[index])
                     .ThenBy(index => index)
                     .Take(remainingPixels))
        {
            widths[index]++;
        }

        return widths;
    }

    private void DrawTile(Graphics graphics, Rectangle bounds, JoinVideoTimelineItem item, bool selected, bool dragging)
    {
        if (bounds.Width <= 0)
        {
            return;
        }

        var background = selected ? FrameShiftTheme.AccentSoft : FrameShiftTheme.Surface;
        var border = selected ? FrameShiftTheme.AccentText : FrameShiftTheme.SurfaceBorder;
        if (dragging)
        {
            background = FrameShiftTheme.AccentSoftHover;
        }

        if (bounds.Width < Pixels(4))
        {
            using var narrowFill = new SolidBrush(background);
            graphics.FillRectangle(narrowFill, bounds);
            return;
        }

        using var path = CreateRoundedRectangle(bounds, Math.Min(Pixels(7), Math.Max(1, bounds.Width / 2)));
        using var fill = new SolidBrush(background);
        using var borderPen = new Pen(border, selected ? Pixels(2) : Pixels(1));
        graphics.FillPath(fill, path);
        graphics.DrawPath(borderPen, path);

        if (bounds.Width <= Pixels(TilePadding) * 2)
        {
            return;
        }

        var previewBounds = new Rectangle(bounds.X + Pixels(TilePadding), bounds.Y + Pixels(TilePadding), bounds.Width - Pixels(TilePadding * 2), Pixels(ThumbnailHeight));
        DrawPreview(graphics, previewBounds, item.Thumbnail);

        var nameBounds = new Rectangle(bounds.X + Pixels(TilePadding), previewBounds.Bottom + Pixels(6), bounds.Width - Pixels(TilePadding * 2), Math.Max(Pixels(19), Font.Height));
        TextRenderer.DrawText(
            graphics,
            Path.GetFileName(item.SourcePath),
            Font,
            nameBounds,
            FrameShiftTheme.TextPrimary,
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.Left);

        var details = item.IsLoaded
            ? item.LoadError ?? FormatDuration(item.DurationSeconds)
            : "Inspecting...";
        var detailBounds = new Rectangle(bounds.X + Pixels(TilePadding), nameBounds.Bottom + Pixels(2), bounds.Width - Pixels(TilePadding * 2), Math.Max(Pixels(18), Font.Height));
        TextRenderer.DrawText(
            graphics,
            details,
            Font,
            detailBounds,
            item.LoadError is null ? FrameShiftTheme.TextSecondary : FrameShiftTheme.ErrorText,
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.Left);
    }

    private void DrawPreview(Graphics graphics, Rectangle bounds, Bitmap? bitmap)
    {
        using var path = CreateRoundedRectangle(bounds, Math.Min(Pixels(4), Math.Max(1, bounds.Width / 2)));
        var saved = graphics.Save();
        graphics.SetClip(path, CombineMode.Intersect);
        if (bitmap is null)
        {
            using var placeholder = new SolidBrush(FrameShiftTheme.AccentSoft);
            graphics.FillRectangle(placeholder, bounds);
            TextRenderer.DrawText(
                graphics,
                "No preview",
                Font,
                bounds,
                FrameShiftTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else
        {
            var ratio = Math.Min(bounds.Width / (float)bitmap.Width, bounds.Height / (float)bitmap.Height);
            var width = Math.Max(1, (int)Math.Round(bitmap.Width * ratio));
            var height = Math.Max(1, (int)Math.Round(bitmap.Height * ratio));
            var destination = new Rectangle(bounds.X + ((bounds.Width - width) / 2), bounds.Y + ((bounds.Height - height) / 2), width, height);
            graphics.DrawImage(bitmap, destination);
        }

        graphics.Restore(saved);
        using var border = new Pen(FrameShiftTheme.SurfaceBorder);
        graphics.DrawPath(border, path);
    }

    private int GetTileIndexAt(Point location)
    {
        for (var index = 0; index < _tileBounds.Count; index++)
        {
            if (_tileBounds[index].Contains(location))
            {
                return index;
            }
        }

        return -1;
    }

    private int GetInsertionIndex(int x)
    {
        for (var index = 0; index < _tileBounds.Count; index++)
        {
            if (x < _tileBounds[index].Left + (_tileBounds[index].Width / 2))
            {
                return index;
            }
        }

        return _tileBounds.Count;
    }

    private int GetInsertionMarkerX(int insertionIndex)
    {
        if (_tileBounds.Count == 0)
        {
            return 0;
        }

        return insertionIndex >= _tileBounds.Count
            ? _tileBounds[^1].Right
            : _tileBounds[insertionIndex].Left;
    }

    private void UpdateToolTip(int index)
    {
        if (_toolTipIndex == index)
        {
            return;
        }

        _toolTipIndex = index;
        if (index < 0 || index >= _items.Count)
        {
            _itemToolTip.SetToolTip(this, string.Empty);
            return;
        }

        var item = _items[index];
        var details = item.IsLoaded
            ? item.LoadError ?? FormatDuration(item.DurationSeconds)
            : "Inspecting...";
        var text = $"{Path.GetFileName(item.SourcePath)}\r\n{details}";
        var hint = BuildCompatibilityHint(item, index);
        if (hint is not null)
        {
            text += $"\r\n{hint}";
        }

        _itemToolTip.SetToolTip(this, text);
    }

    internal string? BuildCompatibilityHint(JoinVideoTimelineItem item, int index)
    {
        if (index <= 0 || index >= _items.Count || !item.IsLoaded || item.Probe is null || item.LoadError is not null)
        {
            return null;
        }

        var anchor = _items[0];
        if (!anchor.IsLoaded || anchor.Probe is null)
        {
            return null;
        }

        if (!item.Probe.HasAudio && anchor.Probe.HasAudio)
        {
            return "No audio on this clip.";
        }

        if (item.Probe.DisplayVideoWidth != anchor.Probe.DisplayVideoWidth ||
            item.Probe.DisplayVideoHeight != anchor.Probe.DisplayVideoHeight)
        {
            return "Different resolution than the first clip.";
        }

        return null;
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle rectangle, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string FormatDuration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return duration.TotalHours >= 1d
            ? duration.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : duration.ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }
}

internal sealed class JoinVideosTimelineMoveEventArgs(int sourceIndex, int insertionIndex) : EventArgs
{
    public int SourceIndex { get; } = sourceIndex;

    public int InsertionIndex { get; } = insertionIndex;
}
