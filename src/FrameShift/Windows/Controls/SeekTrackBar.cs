using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace FrameShift.Windows.Controls;

public sealed class SeekTrackBar : TrackBar
{
    private bool _dragging;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, ref NativeRect rectangle);

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture) _dragging = false;
        base.OnMouseCaptureChanged(e);
    }

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

        // The native channel follows DPI/theme sizing and leaves room for the thumb at both ends.
        var channel = new NativeRect();
        SendMessage(Handle, 0x0400 + 26, IntPtr.Zero, ref channel); // TBM_GETCHANNELRECT
        var thumb = new NativeRect();
        SendMessage(Handle, 0x0400 + 25, IntPtr.Zero, ref thumb); // TBM_GETTHUMBRECT
        var halfThumb = (thumb.Right - thumb.Left) / 2;
        var left = channel.Left + halfThumb;
        var right = Math.Max(left + 1, channel.Right - halfThumb - 1);
        var clamped = Math.Clamp(x, left, right);
        var value = Minimum + (int)Math.Round((Maximum - Minimum) * ((clamped - left) / (double)(right - left)));
        value = Math.Max(Minimum, Math.Min(Maximum, value));

        if (Value != value)
        {
            Value = value;
        }
    }
}
