using System.Drawing;
using System.Windows.Forms;
using FrameShift.Windows.Controls;

namespace FrameShift.Windows.Helpers;

/// <summary>Shared header/body/status/actions structure. Only the body can scroll.</summary>
public static class FrameShiftDialogLayout
{
    /// <summary>Fits at Load after native scaling, or on an explicit content variant change. Not a Resize handler.</summary>
    internal static void FitInitialHeight(Form form, TableLayoutPanel root)
        => FitInitialHeight(form, root, Screen.FromControl(form).WorkingArea.Size);

    internal static void FitInitialHeight(Form form, TableLayoutPanel root, Size workingArea)
    {
        var viewport = root.GetControlFromPosition(0, 1) as Panel;
        var restoreScroll = viewport?.AutoScroll == true;
        // A scrollbar left over from the provisional height narrows wrapping content,
        // which can keep that same scrollbar alive after the window has grown to fit.
        if (restoreScroll) viewport!.AutoScroll = false;
        try
        {
            // Wrapping choices can settle to a shorter height once the full width is restored.
            // Bound convergence to this explicit fit request; never call this from Resize/Layout.
            for (var pass = 0; pass < 3; pass++)
            {
                form.PerformLayout();
                var width = Math.Max(1, root.ClientSize.Width - root.Padding.Horizontal);
                var height = root.Padding.Vertical;
                for (var row = 0; row < root.RowCount; row++)
                {
                    var control = root.GetControlFromPosition(0, row);
                    if (control is null) continue;
                    var available = Math.Max(1, width - control.Margin.Horizontal);
                    if (control == viewport && restoreScroll && viewport!.Controls.Count > 0)
                        height += viewport.Controls[0].GetPreferredSize(new Size(available, 0)).Height + control.Margin.Vertical;
                    else
                    {
                        // Native editors can keep a minimum actual height larger than their text measurement.
                        var preferred = control.GetPreferredSize(new Size(available, 0));
                        var measuredHeight = control is FrameShiftStatusMessage
                            ? preferred.Height : Math.Max(control.Height, preferred.Height);
                        height += measuredHeight + control.Margin.Vertical;
                    }
                }
                var maximum = Math.Max(1, workingArea.Height - (form.Height - form.ClientSize.Height));
                var targetHeight = Math.Min(height, maximum);
                if (pass > 0 && form.ClientSize.Height == targetHeight) break;
                form.ClientSize = new Size(form.ClientSize.Width, targetHeight);
                root.PerformLayout();
            }
        }
        finally { if (restoreScroll) viewport!.AutoScroll = true; }
    }

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
        header.TabIndex = 0;
        body.TabIndex = 1;
        actions.TabIndex = 3;
        header.Margin = actions.Margin = Padding.Empty;
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(body, 0, 1);
        if (status is not null)
        {
            status.Dock = DockStyle.Fill;
            status.TabIndex = 2;
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

    public static FlowLayoutPanel CreateActions(params Button[] buttons)
    {
        var actions = new FrameShiftActionBar
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true, Margin = Padding.Empty
        };
        for (var index = buttons.Length - 1; index >= 0; index--)
        {
            buttons[index].TabIndex = index;
            actions.Controls.Add(buttons[index]);
        }
        void Metrics()
        {
            var gap = FrameShiftUiMetrics.ToPixels(actions, FrameShiftUiMetrics.FooterButtonGap);
            for (var index = 0; index < buttons.Length; index++)
                buttons[index].Margin = new Padding(index == 0 ? 0 : gap, 0, 0, gap);
        }
        actions.HandleCreated += (_, _) => Metrics();
        actions.DpiChangedAfterParent += (_, _) => Metrics();
        Metrics();
        return actions;
    }
}
