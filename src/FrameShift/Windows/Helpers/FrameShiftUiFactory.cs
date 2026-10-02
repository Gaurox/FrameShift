using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FrameShift.Windows.Controls;

namespace FrameShift.Windows.Helpers;

public static class FrameShiftUiFactory
{
    public static FrameShiftFlowRow CreateChoiceRow(params Control[] choices)
    {
        var row = new FrameShiftFlowRow();
        row.Controls.AddRange(choices);
        return row;
    }

    public static TableLayoutPanel CreateVerticalStack(params Control[] controls)
    {
        var stack = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top, ColumnCount = 1, Margin = Padding.Empty, Size = Size.Empty
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var control in controls)
        {
            var row = stack.RowCount++;
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Dock = DockStyle.Top;
            stack.Controls.Add(control, 0, row);
        }
        void Metrics()
        {
            foreach (Control control in stack.Controls)
                control.Margin = new Padding(0, 0, 0, FrameShiftUiMetrics.ToPixels(stack, FrameShiftUiMetrics.BlockGap));
        }
        stack.HandleCreated += (_, _) => Metrics();
        stack.DpiChangedAfterParent += (_, _) => Metrics();
        Metrics();
        return stack;
    }

    public static Label CreateWrappingLabel(string text) => new()
    {
        Text = text, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty,
        ForeColor = FrameShiftTheme.TextSecondary, UseMnemonic = false
    };

    public static FrameShiftHeader CreateHeader(string title, string subtitle, string preferredIconPath,
        string fallbackIconPath, string fallbackGlyph, Point? iconOffset = null)
    {
        var path = FrameShiftUiPainter.ResolveIconPath(preferredIconPath, fallbackIconPath);
        return new FrameShiftHeader(title, subtitle, path, fallbackGlyph,
            ResolveHeaderIconOffset(path, iconOffset ?? Point.Empty));
    }

    public static Button CreateMeasuredActionButton(string text, bool primary, int? logicalMinimumWidth = null)
    {
        var button = new FrameShiftActionButton(logicalMinimumWidth ?? FrameShiftUiMetrics.FooterButtonWidth)
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            BackColor = primary ? FrameShiftTheme.PrimaryButtonBackground : FrameShiftTheme.Surface,
            ForeColor = primary ? Color.White : FrameShiftTheme.AccentText
        };
        button.FlatAppearance.BorderColor = FrameShiftTheme.PrimaryBlue;
        button.FlatAppearance.MouseOverBackColor = primary ? FrameShiftTheme.PrimaryButtonHover : FrameShiftTheme.AccentSoft;
        button.FlatAppearance.MouseDownBackColor = primary ? FrameShiftTheme.PrimaryButtonPressed : FrameShiftTheme.AccentSoftHover;
        return button;
    }

    /// <summary>Content must report its preferred height (e.g. an auto-sized table of fields).</summary>
    public static TableLayoutPanel CreateSection(string title, Control content, bool fill = false)
    {
        var section = new TableLayoutPanel
        {
            AutoSize = !fill, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = fill ? DockStyle.Fill : DockStyle.Top,
            ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, BackColor = FrameShiftTheme.Surface, Size = Size.Empty
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        section.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
        var label = new Label { Text = title, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = FrameShiftTheme.AccentText };
        content.Dock = fill ? DockStyle.Fill : DockStyle.Top;
        section.Controls.Add(label, 0, 0);
        section.Controls.Add(content, 0, 1);
        void Metrics()
        {
            section.Padding = FrameShiftUiMetrics.ToPixels(FrameShiftUiMetrics.StandardSectionPadding, section.DeviceDpi);
            content.Margin = new Padding(0, FrameShiftUiMetrics.ToPixels(section, FrameShiftUiMetrics.SectionContentGap), 0, 0);
        }
        section.HandleCreated += (_, _) => Metrics();
        section.DpiChangedAfterParent += (_, _) => Metrics();
        Metrics();
        FrameShiftUiPainter.AttachRoundedBorder(section, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.PanelCornerRadius);
        return section;
    }

    public static TableLayoutPanel CreateFieldRow(string labelText, Control editor, string? unit = null, int? logicalEditorWidth = null)
    {
        var row = new FrameShiftFieldRow
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
            ColumnCount = unit is null ? 2 : 3, RowCount = 1, Margin = Padding.Empty
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label { Text = labelText, AutoSize = true, Anchor = AnchorStyles.Left, TabIndex = 0 };
        editor.Dock = DockStyle.Fill;
        editor.TabIndex = 1;
        editor.AccessibleName ??= labelText.Replace("&", "");
        row.Controls.Add(label, 0, 0);
        row.Controls.Add(editor, 1, 0);
        if (unit is not null)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.Controls.Add(new Label { Text = unit, AutoSize = true, Anchor = AnchorStyles.Left, TabIndex = 2 }, 2, 0);
        }
        if (logicalEditorWidth.HasValue)
        {
            row.ColumnStyles[1].SizeType = SizeType.Absolute;
            row.ColumnCount++;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        }
        void Metrics()
        {
            if (logicalEditorWidth.HasValue) row.ColumnStyles[1].Width = FrameShiftUiMetrics.ToPixels(row, logicalEditorWidth.Value);
            var gap = FrameShiftUiMetrics.ToPixels(row, FrameShiftUiMetrics.LineGap);
            foreach (Control control in row.Controls) control.Margin = new Padding(0, 0, gap, gap);
        }
        row.HandleCreated += (_, _) => Metrics();
        row.DpiChangedAfterParent += (_, _) => Metrics();
        Metrics();
        return row;
    }

    public static FrameShiftStatusMessage CreateStatusMessage(string text) => new() { Text = text };

    public static TableLayoutPanel CreateFieldWithUnit(string label, Control editor, Control unit, int logicalEditorWidth = 240)
    {
        var input = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty,
            ColumnCount = 2, RowCount = 1, Size = Size.Empty
        };
        input.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        input.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        input.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.Dock = unit.Dock = DockStyle.Fill;
        unit.Margin = Padding.Empty;
        input.Controls.Add(editor, 0, 0);
        input.Controls.Add(unit, 1, 0);
        void Metrics() => editor.Margin = new Padding(0, 0, FrameShiftUiMetrics.ToPixels(input, FrameShiftUiMetrics.LineGap), 0);
        input.HandleCreated += (_, _) => Metrics();
        input.DpiChangedAfterParent += (_, _) => Metrics();
        Metrics();
        return CreateFieldRow(label, input, logicalEditorWidth: logicalEditorWidth);
    }

    public static Panel CreateFramedPanel(Color backgroundColor, Color borderColor, int radius)
    {
        var panel = new Panel
        {
            BackColor = backgroundColor
        };
        FrameShiftUiPainter.AttachRoundedBorder(panel, borderColor, radius);
        return panel;
    }

    private static Point ResolveHeaderIconOffset(string iconPath, Point requestedOffset)
    {
        var normalized = Path.GetFileName(iconPath);
        var standardOffset = normalized.Equals("cut-video-audio-icon.ico", StringComparison.OrdinalIgnoreCase)
            ? new Point(1, 1)
            : Point.Empty;

        return new Point(standardOffset.X + requestedOffset.X, standardOffset.Y + requestedOffset.Y);
    }
}
