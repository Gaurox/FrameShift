using System.Drawing;
using System.Windows.Forms;

namespace FrameShift.Windows.Helpers;

/// <summary>Opt-in policy for migrated forms. Call once under SuspendLayout, before adding controls.</summary>
public static class FrameShiftWindowPolicy
{
    public static void Initialize(Form form, Size initialClientSize, Size minimumClientSize)
    {
        if (form.IsHandleCreated || form.Controls.Count != 0)
            throw new InvalidOperationException("Initialize the window policy before creating its controls or handle.");

        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        var font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        form.Font = font;
        form.Disposed += (_, _) => font.Dispose();
        form.BackColor = FrameShiftTheme.PageBackground;
        form.ClientSize = initialClientSize;
        form.StartPosition = FormStartPosition.CenterParent;

        var constraining = false;
        void Constrain()
        {
            if (form.IsDisposed || !form.IsHandleCreated || constraining) return;
            constraining = true;
            try
            {
                var area = Screen.FromControl(form).WorkingArea;
                var clientMinimum = FrameShiftUiMetrics.ToPixels(minimumClientSize, form.DeviceDpi);
                var minimum = clientMinimum + (form.Size - form.ClientSize);
                // Recompute from the logical minimum, never from a previously clamped/scaled value.
                form.MinimumSize = new Size(Math.Min(minimum.Width, area.Width), Math.Min(minimum.Height, area.Height));
                if (form.WindowState == FormWindowState.Normal)
                    form.Bounds = FitBounds(form.Bounds, area);
            }
            finally { constraining = false; }
        }

        form.Load += (_, _) => Constrain();
        // Do not clamp LocationChanged: that would trap a dragged window on its current monitor.
        form.ResizeEnd += (_, _) => Constrain();
        form.Shown += (_, _) => Constrain();
        // WinForms applies the suggested DPI bounds after raising DpiChanged.
        form.DpiChanged += (_, _) => form.BeginInvoke((Action)Constrain);
    }

    internal static Rectangle FitBounds(Rectangle bounds, Rectangle workingArea)
    {
        var width = Math.Min(bounds.Width, workingArea.Width);
        var height = Math.Min(bounds.Height, workingArea.Height);
        return new Rectangle(
            Math.Clamp(bounds.X, workingArea.Left, workingArea.Right - width),
            Math.Clamp(bounds.Y, workingArea.Top, workingArea.Bottom - height), width, height);
    }
}
