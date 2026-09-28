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
            BackColor = primary ? FrameShiftTheme.SecondaryBlue : FrameShiftTheme.Surface,
            ForeColor = primary ? Color.White : FrameShiftTheme.AccentText
        };
        button.FlatAppearance.BorderColor = FrameShiftTheme.PrimaryBlue;
        button.FlatAppearance.MouseOverBackColor = primary ? FrameShiftTheme.PrimaryBlue : FrameShiftTheme.AccentSoft;
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

    // Compatibility factories below serve forms awaiting migration in C–E; remove unused paths in F.
    public static Panel CreateFramedPanel(Point location, Size size, Color backgroundColor, Color borderColor, int radius)
    {
        var panel = new Panel
        {
            Location = location,
            Size = size,
            BackColor = backgroundColor
        };
        FrameShiftUiPainter.AttachRoundedBorder(panel, borderColor, radius);
        return panel;
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

    public static Panel CreateFixedHeader(string title, string subtitle, string preferredIconPath, string fallbackIconPath, string fallbackGlyph, Point? iconOffset = null)
    {
        var panel = CreateFramedPanel(
            new Point(FrameShiftUiMetrics.OuterPadding, FrameShiftUiMetrics.OuterPadding),
            new Size(536, FrameShiftUiMetrics.HeaderHeight),
            FrameShiftTheme.Surface,
            FrameShiftTheme.PrimaryBlue,
            FrameShiftUiMetrics.PanelCornerRadius);

        PopulateHeader(panel, title, subtitle, preferredIconPath, fallbackIconPath, fallbackGlyph, iconOffset ?? Point.Empty, 460);
        return panel;
    }

    public static Panel CreateFillHeader(string title, string subtitle, string preferredIconPath, string fallbackIconPath, string fallbackGlyph, int subtitleWidth, Point? iconOffset = null)
    {
        var panel = CreateFramedPanel(FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.PanelCornerRadius);
        panel.Dock = DockStyle.Fill;
        panel.Padding = Padding.Empty;

        PopulateHeader(panel, title, subtitle, preferredIconPath, fallbackIconPath, fallbackGlyph, iconOffset ?? Point.Empty, subtitleWidth);
        return panel;
    }

    public static Panel CreateFixedSection(Point location, Size size, string title)
    {
        var panel = CreateFramedPanel(location, size, FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.PanelCornerRadius);
        panel.Controls.Add(CreateSectionTitleLabel(title));
        return panel;
    }

    public static Panel CreateFillSection(string title, out Panel contentHost)
    {
        var panel = CreateFramedPanel(FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.PanelCornerRadius);
        panel.Dock = DockStyle.Fill;
        panel.Padding = FrameShiftUiMetrics.StandardSectionPadding;
        panel.Margin = Padding.Empty;

        var titleLabel = CreateSectionTitleLabel(title);
        titleLabel.AutoSize = false;
        titleLabel.Margin = Padding.Empty;
        panel.Controls.Add(titleLabel);

        contentHost = new Panel
        {
            Margin = Padding.Empty,
            Padding = new Padding(0, FrameShiftUiMetrics.SectionContentGap, 0, 0)
        };
        panel.Controls.Add(contentHost);
        var host = contentHost;
        panel.Resize += (_, _) => FrameShiftUiLayout.LayoutTitledSection(panel, titleLabel, host);
        FrameShiftUiLayout.LayoutTitledSection(panel, titleLabel, contentHost);

        return panel;
    }

    public static Panel CreateFixedInfoCard(Point location, Size size)
    {
        return CreateFramedPanel(location, size, FrameShiftTheme.AccentSoft, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.PanelCornerRadius);
    }

    public static Panel CreateFillInfoCard()
    {
        var panel = CreateFramedPanel(FrameShiftTheme.AccentSoft, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.PanelCornerRadius);
        panel.Dock = DockStyle.Fill;
        panel.Padding = FrameShiftUiMetrics.StandardInfoCardPadding;
        return panel;
    }

    public static Panel CreateFillInfoCardWithMargin(int topMargin = 0, int bottomMargin = 0)
    {
        var panel = CreateFillInfoCard();
        panel.Margin = new Padding(0, topMargin, 0, bottomMargin);
        return panel;
    }

    public static Button CreateFixedActionButton(string text, Point location, Size size, bool primary)
    {
        var button = CreateActionButton(text, primary, size.Width);
        button.Size = size;
        button.Location = location;
        return button;
    }

    public static Button CreateActionButton(string text, bool primary, int width)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.None,
            Margin = Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point),
            UseVisualStyleBackColor = false,
            Height = FrameShiftUiMetrics.FooterButtonHeight,
            Width = width
        };

        if (primary)
        {
            button.BackColor = FrameShiftTheme.SecondaryBlue;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderColor = FrameShiftTheme.SecondaryBlue;
            button.FlatAppearance.MouseOverBackColor = FrameShiftTheme.PrimaryBlue;
            button.FlatAppearance.MouseDownBackColor = FrameShiftTheme.SecondaryBlue;
        }
        else
        {
            button.BackColor = FrameShiftTheme.Surface;
            button.ForeColor = FrameShiftTheme.AccentText;
            button.FlatAppearance.BorderColor = FrameShiftTheme.PrimaryBlue;
            button.FlatAppearance.MouseOverBackColor = FrameShiftTheme.AccentSoft;
            button.FlatAppearance.MouseDownBackColor = FrameShiftTheme.AccentSoftHover;
        }

        return button;
    }

    public static Label CreateFieldLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = FrameShiftTheme.TextPrimary
        };
    }

    public static Label CreateInfoValueLabel(ContentAlignment textAlign)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = textAlign,
            AutoEllipsis = true
        };
    }

    public static TextBox CreateValueTextBox(bool readOnly = false, HorizontalAlignment textAlign = HorizontalAlignment.Left, string? tag = null)
    {
        return new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary,
            ReadOnly = readOnly,
            TextAlign = textAlign,
            Tag = tag
        };
    }

    public static Panel CreateTextInputHost(TextBox textBox)
    {
        var host = CreateFramedPanel(FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.InputCornerRadius);
        host.Dock = DockStyle.Fill;
        host.Margin = Padding.Empty;
        host.Padding = FrameShiftUiMetrics.StandardTextInputPadding;
        host.Controls.Add(textBox);
        host.Resize += (_, _) =>
        {
            var height = Math.Max(20, textBox.PreferredHeight);
            var y = Math.Max(0, (host.ClientSize.Height - height) / 2);
            textBox.SetBounds(host.Padding.Left, y, Math.Max(0, host.ClientSize.Width - host.Padding.Horizontal), height);
        };
        return host;
    }

    public static Panel CreateFixedTextInputHost(TextBox textBox, Point location, Size size)
    {
        var host = CreateFramedPanel(location, size, FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, FrameShiftUiMetrics.InputCornerRadius);
        host.Padding = new Padding(8, 5, 8, 5);
        host.Controls.Add(textBox);
        var height = Math.Max(16, textBox.PreferredHeight);
        textBox.SetBounds(host.Padding.Left, 7, Math.Max(0, size.Width - host.Padding.Horizontal), height);
        return host;
    }

    public static ComboBox CreateFixedComboBox(Point location, Size size)
    {
        return new ComboBox
        {
            Location = location,
            Size = size,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Standard,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary
        };
    }

    public static Label CreateInnerPanelTitle(string text)
    {
        return new Label
        {
            AutoSize = true,
            Location = new Point(0, 0),
            Text = text,
            ForeColor = FrameShiftTheme.AccentText,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
    }

    public static int GetTitledSectionHeight(int contentHeight)
    {
        return FrameShiftUiMetrics.StandardSectionPadding.Top +
               FrameShiftUiMetrics.SectionTitleHeight +
               FrameShiftUiMetrics.SectionContentGap +
               contentHeight +
               FrameShiftUiMetrics.StandardSectionPadding.Bottom;
    }

    public static int GetInfoCardHeight(int lineCount)
    {
        return FrameShiftUiMetrics.StandardInfoCardPadding.Top +
               (lineCount * FrameShiftUiMetrics.InfoLineHeight) +
               FrameShiftUiMetrics.StandardInfoCardPadding.Bottom;
    }

    private static void PopulateHeader(
        Panel panel,
        string title,
        string subtitle,
        string preferredIconPath,
        string fallbackIconPath,
        string fallbackGlyph,
        Point iconOffset,
        int subtitleWidth)
    {
        var iconPanel = CreateFramedPanel(
            FrameShiftTheme.AccentSoft,
            FrameShiftTheme.PrimaryBlue,
            FrameShiftUiMetrics.PanelCornerRadius);
        iconPanel.Size = new Size(FrameShiftUiMetrics.HeaderIconSize, FrameShiftUiMetrics.HeaderIconSize);
        iconPanel.Location = new Point(12, 10);
        iconPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        panel.Controls.Add(iconPanel);

        var iconPath = FrameShiftUiPainter.ResolveIconPath(preferredIconPath, fallbackIconPath);
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            var resolvedIconOffset = ResolveHeaderIconOffset(iconPath, iconOffset);
            iconPanel.BackgroundImage = FrameShiftUiPainter.CreateCenteredIconBackground(
                iconPath,
                new Size(FrameShiftUiMetrics.HeaderIconSize, FrameShiftUiMetrics.HeaderIconSize),
                resolvedIconOffset);
            iconPanel.BackgroundImageLayout = ImageLayout.None;
        }
        else
        {
            iconPanel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = fallbackGlyph,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 14F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = FrameShiftTheme.AccentText
            });
        }

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(64, 8),
            Text = title,
            ForeColor = FrameShiftTheme.TextPrimary,
            Font = new Font("Segoe UI Semibold", 14F, FontStyle.Regular, GraphicsUnit.Point)
        });

        panel.Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(64, 30),
            Size = new Size(subtitleWidth, 18),
            Text = subtitle,
            ForeColor = FrameShiftTheme.TextSecondary,
            AutoEllipsis = true
        });
    }

    private static Label CreateSectionTitleLabel(string title)
    {
        return new Label
        {
            AutoSize = true,
            Location = new Point(12, 10),
            Text = title,
            ForeColor = FrameShiftTheme.AccentText,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
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
