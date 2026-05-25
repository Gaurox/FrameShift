using System;
using System.Drawing;
using System.Windows.Forms;

namespace FrameShift.Windows.Helpers;

public static class FrameShiftCropEditorUi
{
    public static TableLayoutPanel CreateRootLayout()
    {
        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(FrameShiftUiMetrics.OuterPadding),
            ColumnCount = 1,
            RowCount = 4
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.HeaderHeight));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.CompactFooterHeight));
        return rootLayout;
    }

    public static TableLayoutPanel CreateContentLayout(out TableLayoutPanel leftLayout, out TableLayoutPanel rightLayout)
    {
        var contentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = 1
        };
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FrameShiftUiMetrics.EditorRailWidth));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        leftLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, FrameShiftUiMetrics.OuterPadding, 0),
            ColumnCount = 1,
            RowCount = 1
        };
        leftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        rightLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 4
        };
        rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 188F));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86F));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 114F));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        contentLayout.Controls.Add(leftLayout, 0, 0);
        contentLayout.Controls.Add(rightLayout, 1, 0);
        return contentLayout;
    }

    public static Panel CreatePreviewPanel()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(32, 32, 32),
            Margin = Padding.Empty
        };
    }

    public static Panel CreateRatioSection(string[] options, Action onSelected, out RadioButton[] buttons, out int preferredHeight)
    {
        var section = FrameShiftUiFactory.CreateFillSection("Ratio", out var contentHost);
        section.Margin = new Padding(0, 0, 0, FrameShiftUiMetrics.OuterPadding);
        section.Padding = FrameShiftUiMetrics.StandardSectionPadding;

        var ratioPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        buttons = new RadioButton[options.Length];
        for (var index = 0; index < options.Length; index++)
        {
            var radio = new RadioButton
            {
                Text = options[index],
                Tag = options[index],
                Margin = new Padding(0, 0, 0, index == options.Length - 1 ? 0 : FrameShiftUiMetrics.LineGap),
                Size = new Size(160, 24),
                Checked = index == 0
            };
            radio.CheckedChanged += (_, _) =>
            {
                if (radio.Checked)
                {
                    onSelected();
                }
            };
            ratioPanel.Controls.Add(radio);
            buttons[index] = radio;
        }

        contentHost.Controls.Add(ratioPanel);
        preferredHeight = FrameShiftUiFactory.GetTitledSectionHeight(ratioPanel.PreferredSize.Height) + section.Margin.Vertical;
        return section;
    }

    public static Panel CreateMediaInfoCard(int lineCount)
    {
        var panel = FrameShiftUiFactory.CreateFillInfoCardWithMargin(bottomMargin: FrameShiftUiMetrics.OuterPadding);
        panel.Padding = new Padding(FrameShiftUiMetrics.OuterPadding, FrameShiftUiMetrics.LineGap, FrameShiftUiMetrics.OuterPadding, FrameShiftUiMetrics.LineGap);
        panel.Height = FrameShiftUiFactory.GetInfoCardHeight(lineCount);
        return panel;
    }

    public static Panel CreateToolsSection(out Panel toolsButtonHost, params Button[] toolButtons)
    {
        var section = FrameShiftUiFactory.CreateFillSection("Tools", out var contentHost);
        section.Margin = Padding.Empty;
        section.Padding = FrameShiftUiMetrics.StandardSectionPadding;

        toolsButtonHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        foreach (var button in toolButtons)
        {
            toolsButtonHost.Controls.Add(button);
        }

        contentHost.Controls.Add(toolsButtonHost);

        return section;
    }

    public static void WireFooterLayout(Form form, Panel footerPanel, Button cancelButton, Button okButton)
    {
        footerPanel.Resize += (_, _) => FrameShiftUiLayout.LayoutFooterButtons(footerPanel, cancelButton, okButton, FrameShiftUiMetrics.BlockGap);
        form.Shown += (_, _) => FrameShiftUiLayout.LayoutFooterButtons(footerPanel, cancelButton, okButton, FrameShiftUiMetrics.BlockGap);
    }

    public static void WireToolLayout(Form form, Panel toolsButtonHost, params Button[] toolButtons)
    {
        toolsButtonHost.Resize += (_, _) => FrameShiftUiLayout.LayoutEvenButtons(toolsButtonHost, FrameShiftUiMetrics.BlockGap, toolButtons);
        form.Shown += (_, _) => FrameShiftUiLayout.LayoutEvenButtons(toolsButtonHost, FrameShiftUiMetrics.BlockGap, toolButtons);
    }
}
