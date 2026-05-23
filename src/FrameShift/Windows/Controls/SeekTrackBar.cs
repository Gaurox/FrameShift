using System;
using System.Windows.Forms;

namespace FrameShift.Windows.Controls;

public sealed class SeekTrackBar : TrackBar
{
    private bool _dragging;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            Capture = true;
            SetValueFromPoint(e.X);
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging && (e.Button & MouseButtons.Left) == MouseButtons.Left)
        {
            SetValueFromPoint(e.X);
            return;
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            SetValueFromPoint(e.X);
            _dragging = false;
            Capture = false;
            return;
        }

        base.OnMouseUp(e);
    }

    private void SetValueFromPoint(int x)
    {
        if (Maximum <= Minimum)
        {
            return;
        }

        var width = Math.Max(1, ClientSize.Width - 1);
        var clamped = Math.Max(0, Math.Min(width, x));
        var value = Minimum + (int)Math.Round((Maximum - Minimum) * (clamped / (double)width));
        value = Math.Max(Minimum, Math.Min(Maximum, value));

        if (Value != value)
        {
            Value = value;
        }
    }
}
