using System.Drawing;
using System.Windows.Forms;
using FrameShift.Windows.Controls;

namespace FrameShift.Windows.Helpers;

/// <summary>Shared header/body/status/actions structure. Only the body can scroll.</summary>
public static class FrameShiftDialogLayout
{
    public static TableLayoutPanel Create(Control header, Control content, Control actions, Control? status = null)
        => CreateShell(header, CreateScrollBody(content), actions, status);

    internal static TableLayoutPanel CreateShell(Control header, Control body, Control actions, Control? status)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.Dock = body.Dock = actions.Dock = DockStyle.Fill;
        header.Margin = actions.Margin = Padding.Empty;
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(body, 0, 1);
        if (status is not null)
        {
            status.Dock = DockStyle.Fill;
            root.Controls.Add(status, 0, 2);
        }
        root.Controls.Add(actions, 0, 3);
        void Metrics()
        {
            var pad = FrameShiftUiMetrics.ToPixels(root, FrameShiftUiMetrics.OuterPadding);
            root.Padding = new Padding(pad);
            body.Margin = new Padding(0, pad, 0, pad);
            if (status is not null) status.Margin = new Padding(0, 0, 0, pad);
        }
        root.HandleCreated += (_, _) => Metrics();
        root.DpiChangedAfterParent += (_, _) => Metrics();
        Metrics();
        return root;
    }

    public static Panel CreateScrollBody(Control content)
    {
        var viewport = new Panel { AutoScroll = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        content.Dock = DockStyle.Top;
        content.Margin = Padding.Empty;
        viewport.Controls.Add(content);
        return viewport;
    }

    public static FlowLayoutPanel CreateActions(Button cancel, Button primary)
    {
        var actions = new FrameShiftActionBar
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true, Margin = Padding.Empty
        };
        cancel.TabIndex = 0;
        primary.TabIndex = 1;
        // The primary stays at the right; native flow wraps the secondary when space is short.
        actions.Controls.Add(primary);
        actions.Controls.Add(cancel);
        void Metrics()
        {
            var gap = FrameShiftUiMetrics.ToPixels(actions, FrameShiftUiMetrics.FooterButtonGap);
            primary.Margin = new Padding(gap, 0, 0, gap);
            cancel.Margin = new Padding(0, 0, 0, gap);
        }
        actions.HandleCreated += (_, _) => Metrics();
        actions.DpiChangedAfterParent += (_, _) => Metrics();
        Metrics();
        return actions;
    }
}
