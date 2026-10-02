using System;
using System.Drawing;
using System.Windows.Forms;

namespace FrameShift.Windows.Helpers;

public static class FrameShiftUiLayout
{
    // Text size is already measured in device pixels. Only logical padding/minima are converted.
    internal static Size MeasureActionButton(Size textPixels, int dpi, int logicalMinimumWidth) => new(
        Math.Max(FrameShiftUiMetrics.ToPixels(logicalMinimumWidth, dpi), textPixels.Width + FrameShiftUiMetrics.ToPixels(28, dpi)),
        Math.Max(FrameShiftUiMetrics.ToPixels(FrameShiftUiMetrics.FooterButtonHeight, dpi), textPixels.Height + FrameShiftUiMetrics.ToPixels(14, dpi)));

}
