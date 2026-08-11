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
        ControlHelper.SetDoubleBuffered(this);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
    }

    public event EventHandler? SelectedIndexChanged;

    public event EventHandler<JoinVideosTimelineMoveEventArgs>? MoveRequested;

    public int SelectedIndex => _selectedIndex;

    public void SetItems(IReadOnlyList<JoinVideoTimelineItem> items)
    {
        _items = items;
        if (_selectedIndex >= _items.Count)
        {
            _selectedIndex = _items.Count - 1;
        }

        RebuildLayout();
    }

    public void SelectIndex(int index)
    {
        var normalized = index >= 0 && index < _items.Count ? index : -1;
        if (_selectedIndex == normalized)
        {
            return;
        }

        _selectedIndex = normalized;
        Invalidate();
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
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

        if ((_isDragging || _isExternalDragHover) && _dragInsertionIndex >= 0)
        {
            var markerX = GetInsertionMarkerX(_dragInsertionIndex);
            using var pen = new Pen(FrameShiftTheme.SecondaryBlue, 3F);
            e.Graphics.DrawLine(pen, markerX, TileTop + 2, markerX, TileTop + TileHeight - 2);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

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

        if (!_isDragging && Math.Abs(e.X - _mouseDownPoint.X) < 5 && Math.Abs(e.Y - _mouseDownPoint.Y) < 5)
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
            _tileBounds.Add(new Rectangle(x, TileTop, widths[index], TileHeight));
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

    private static void DrawTile(Graphics graphics, Rectangle bounds, JoinVideoTimelineItem item, bool selected, bool dragging)
    {
        if (bounds.Width <= 0)
        {
            return;
        }

        var background = selected ? FrameShiftTheme.AccentSoft : FrameShiftTheme.Surface;
        var border = selected ? FrameShiftTheme.SecondaryBlue : FrameShiftTheme.SurfaceBorder;
        if (dragging)
        {
            background = FrameShiftTheme.AccentSoftHover;
        }

        if (bounds.Width < 4)
        {
            using var narrowFill = new SolidBrush(background);
            graphics.FillRectangle(narrowFill, bounds);
            return;
        }

        using var path = CreateRoundedRectangle(bounds, Math.Min(7, Math.Max(1, bounds.Width / 2)));
        using var fill = new SolidBrush(background);
        using var borderPen = new Pen(border, selected ? 2F : 1F);
        graphics.FillPath(fill, path);
        graphics.DrawPath(borderPen, path);

        if (bounds.Width <= TilePadding * 2)
        {
            return;
        }

        var previewBounds = new Rectangle(bounds.X + TilePadding, bounds.Y + TilePadding, bounds.Width - (TilePadding * 2), ThumbnailHeight);
        DrawPreview(graphics, previewBounds, item.Thumbnail);

        var nameBounds = new Rectangle(bounds.X + TilePadding, previewBounds.Bottom + 6, bounds.Width - (TilePadding * 2), 19);
        TextRenderer.DrawText(
            graphics,
            Path.GetFileName(item.SourcePath),
            SystemFonts.MessageBoxFont,
            nameBounds,
            FrameShiftTheme.TextPrimary,
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.Left);

        var details = item.IsLoaded
            ? item.LoadError ?? FormatDuration(item.DurationSeconds)
            : "Inspecting...";
        var detailBounds = new Rectangle(bounds.X + TilePadding, nameBounds.Bottom + 2, bounds.Width - (TilePadding * 2), 18);
        TextRenderer.DrawText(
            graphics,
            details,
            SystemFonts.MessageBoxFont,
            detailBounds,
            item.LoadError is null ? FrameShiftTheme.TextSecondary : Color.Firebrick,
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.Left);
    }

    private static void DrawPreview(Graphics graphics, Rectangle bounds, Bitmap? bitmap)
    {
        using var path = CreateRoundedRectangle(bounds, 4);
        graphics.SetClip(path);
        if (bitmap is null)
        {
            using var placeholder = new SolidBrush(FrameShiftTheme.AccentSoft);
            graphics.FillRectangle(placeholder, bounds);
            TextRenderer.DrawText(
                graphics,
                "No preview",
                SystemFonts.MessageBoxFont,
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

        graphics.ResetClip();
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
