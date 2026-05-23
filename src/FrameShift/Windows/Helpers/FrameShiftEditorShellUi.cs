using System.Windows.Forms;

namespace FrameShift.Windows.Helpers;

public static class FrameShiftEditorShellUi
{
    public static TableLayoutPanel CreateRootLayout()
    {
        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(FrameShiftUiMetrics.OuterPadding),
            ColumnCount = 1,
            RowCount = 3
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.HeaderHeight));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        return rootLayout;
    }

    public static TableLayoutPanel CreateTwoPaneContentLayout(int rightRailWidth, out Panel leftHost, out Panel rightHost)
    {
        var contentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = 1
        };
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, rightRailWidth));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        leftHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, FrameShiftUiMetrics.OuterPadding, 0)
        };

        rightHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        contentLayout.Controls.Add(leftHost, 0, 0);
        contentLayout.Controls.Add(rightHost, 1, 0);
        return contentLayout;
    }

    public static Panel CreateSidebarGroup(string title, Control content)
    {
        var card = FrameShiftUiFactory.CreateFramedPanel(
            FrameShiftTheme.Surface,
            FrameShiftTheme.PrimaryBlue,
            FrameShiftUiMetrics.PanelCornerRadius);
        card.Dock = DockStyle.Top;
        card.AutoSize = true;
        card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        card.Padding = new Padding(FrameShiftUiMetrics.LineGap);
        card.Margin = new Padding(0, 0, 0, 6);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(FrameShiftUiFactory.CreateInnerPanelTitle(title), 0, 0);

        content.Dock = DockStyle.Fill;
        content.Margin = new Padding(0, 4, 0, 0);
        layout.Controls.Add(content, 0, 1);
        card.Controls.Add(layout);

        return card;
    }
}
