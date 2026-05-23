using System;
using System.Drawing;
using System.Windows.Forms;

namespace FrameShift.Windows.Helpers;

public static class FrameShiftUiLayout
{
    public static void LayoutTitledSection(Control section, Control titleLabel, Control contentHost)
    {
        var left = section.Padding.Left;
        var top = section.Padding.Top;
        var width = Math.Max(0, section.ClientSize.Width - section.Padding.Horizontal);
        var contentTop = top + FrameShiftUiMetrics.SectionTitleHeight;
        var contentHeight = Math.Max(0, section.ClientSize.Height - contentTop - section.Padding.Bottom);

        titleLabel.SetBounds(left, top, width, FrameShiftUiMetrics.SectionTitleHeight);
        contentHost.SetBounds(left, contentTop, width, contentHeight);
    }

    public static void LayoutFooterButtons(Control footerPanel, Button cancelButton, Button okButton, int gap)
    {
        if (footerPanel.ClientSize.Width <= 0 || footerPanel.ClientSize.Height <= 0)
        {
            return;
        }

        var bottomY = footerPanel.ClientSize.Height - FrameShiftUiMetrics.FooterButtonHeight;
        var okX = footerPanel.ClientSize.Width - FrameShiftUiMetrics.PrimaryButtonWidth;
        var cancelX = okX - gap - FrameShiftUiMetrics.SecondaryButtonWidth;

        cancelButton.SetBounds(cancelX, bottomY, FrameShiftUiMetrics.SecondaryButtonWidth, FrameShiftUiMetrics.FooterButtonHeight);
        okButton.SetBounds(okX, bottomY, FrameShiftUiMetrics.PrimaryButtonWidth, FrameShiftUiMetrics.FooterButtonHeight);
    }

    public static void AutoSizeAndPositionFooterButtons(
        Control host,
        Button cancelButton,
        Button okButton,
        int minimumCancelWidth = FrameShiftUiMetrics.SecondaryButtonWidth,
        int minimumOkWidth = FrameShiftUiMetrics.PrimaryButtonWidth,
        int gap = FrameShiftUiMetrics.FooterButtonGap,
        int rightPadding = FrameShiftUiMetrics.FooterRightPadding,
        int bottomPadding = FrameShiftUiMetrics.FooterBottomPadding)
    {
        if (host.ClientSize.Width <= 0 || host.ClientSize.Height <= 0)
        {
            return;
        }

        AutoSizeButton(cancelButton, minimumCancelWidth);
        AutoSizeButton(okButton, minimumOkWidth);

        var maxHeight = Math.Max(cancelButton.Height, okButton.Height);
        var bottomY = Math.Max(0, host.ClientSize.Height - bottomPadding - maxHeight);
        var okX = Math.Max(0, host.ClientSize.Width - rightPadding - okButton.Width);
        var cancelX = Math.Max(0, okX - gap - cancelButton.Width);

        cancelButton.SetBounds(cancelX, bottomY, cancelButton.Width, maxHeight);
        okButton.SetBounds(okX, bottomY, okButton.Width, maxHeight);
    }

    private static void AutoSizeButton(Button button, int minimumWidth)
    {
        var measured = TextRenderer.MeasureText(
            button.Text,
            button.Font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        button.Width = Math.Max(minimumWidth, measured.Width + 28);
        button.Height = Math.Max(FrameShiftUiMetrics.FooterButtonHeight, measured.Height + 14);
    }

    public static void LayoutEvenButtons(Control hostPanel, int gap, params Button[] buttons)
    {
        if (hostPanel.ClientSize.Width <= 0 || hostPanel.ClientSize.Height <= 0 || buttons.Length == 0)
        {
            return;
        }

        var totalGap = gap * (buttons.Length - 1);
        var buttonWidth = Math.Max(0, (hostPanel.ClientSize.Width - totalGap) / buttons.Length);
        var x = 0;

        foreach (var button in buttons)
        {
            button.SetBounds(x, 0, buttonWidth, FrameShiftUiMetrics.FooterButtonHeight);
            x += buttonWidth + gap;
        }
    }
}
