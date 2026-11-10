using FrameShift.Windows.Controls;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed partial class CutVideoForm
{
    private const int CutClientHeight = 900;
    private TableLayoutPanel _cutRoot = null!;
    private FrameShiftTimelineWorkspace _cutWorkspace = null!;
    private bool _fittingCutWindow;

    // Explicit content changes only. Selection, preview text and ordinary Layout/Resize
    // must not resize the window or steal its scroll/focus position.
    internal void FitCutWindowToContent(Rectangle? workingArea = null)
    {
        if (_cutRoot is null || _closing || IsDisposed || _fittingCutWindow || WindowState != FormWindowState.Normal) return;
        _fittingCutWindow = true;
        try
        {
            var area = workingArea ?? Screen.FromControl(this).WorkingArea;
            for (var pass = 0; pass < 3; pass++)
            {
                _cutRoot.PerformLayout();
                var chrome = Height - ClientSize.Height;
                var desired = FrameShiftUiMetrics.ToPixels(this, CutClientHeight);
                if (_loopContext != LoopContext.Cut)
                {
                    // Fit the complete current loop UI, including its fixed-height
                    // candidate grid, not one row per candidate. Cut keeps its base
                    // size. Only the monitor working area limits this explicit fit.
                    var content = _cutWorkspace.MeasureOptionsHeight(_cutWorkspace.ClientSize.Width);
                    var surrounding = ClientSize.Height - _cutWorkspace.Height;
                    var preview = FrameShiftUiMetrics.ToPixels(this,
                        _cutWorkspace.MinimumPreviewHeight + FrameShiftUiMetrics.OuterPadding);
                    desired = Math.Max(desired, content + surrounding + preview);
                }
                var target = Math.Min(desired + chrome, area.Height);
                var bounds = FrameShiftWindowPolicy.FitBounds(new Rectangle(Location, new Size(Width, target)), area);
                if (Bounds == bounds) break;
                Bounds = bounds;
            }
        }
        finally { _fittingCutWindow = false; }
    }

    private static void BufferCutControls(Control control)
    {
        if (control is Panel or Label or PictureBox or DataGridView) ControlHelper.SetDoubleBuffered(control);
        foreach (Control child in control.Controls) BufferCutControls(child);
    }

    // Reserve the longest normal selection/preview message at this width. Loading
    // and decoded-frame updates then repaint text without moving the controls below.
    // Actual longer errors can still wrap; no message is truncated to a fixed height.
    private sealed class CutMessageLabel : Label
    {
        private readonly string _reserveText;
        private readonly Dictionary<int, int> _reservedHeights = new();

        internal CutMessageLabel(string reserveText)
        {
            _reserveText = reserveText;
            AutoSize = true;
            Dock = DockStyle.Top;
            UseMnemonic = false;
            ForeColor = FrameShiftTheme.TextSecondary;
            DoubleBuffered = true;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = Math.Max(1, proposedSize.Width > 1 ? proposedSize.Width : Width);
            var preferred = base.GetPreferredSize(new Size(width, 0));
            if (!_reservedHeights.TryGetValue(width, out var reserved))
            {
                reserved = TextRenderer.MeasureText(_reserveText, Font, new Size(width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
                if (_reservedHeights.Count >= 8) _reservedHeights.Clear();
                _reservedHeights[width] = reserved;
            }
            return new Size(preferred.Width, Math.Max(preferred.Height, reserved + Padding.Vertical));
        }

        protected override void OnFontChanged(EventArgs e)
        {
            _reservedHeights?.Clear();
            base.OnFontChanged(e);
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            _reservedHeights.Clear();
            base.OnDpiChangedAfterParent(e);
        }
    }
}
