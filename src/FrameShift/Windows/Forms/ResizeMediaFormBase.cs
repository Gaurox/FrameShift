using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public abstract class ResizeMediaFormBase : Form
{
    private const int DimensionFieldWidth = 128;
    private readonly int _originalWidth;
    private readonly int _originalHeight;
    private readonly double _ratio;

    private bool _updatingFields;
    private string? _activeEditField;

    private readonly TextBox _textWidthPx;
    private readonly TextBox _textHeightPx;
    private readonly TextBox _textWidthPct;
    private readonly TextBox _textHeightPct;
    private readonly CheckBox _checkLockRatio;

    protected ResizeMediaFormBase(string functionName, string sourcePath, int originalWidth, int originalHeight, string fallbackGlyph)
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(640, 500), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, $"FrameShift - {functionName}");
        _originalWidth = originalWidth;
        _originalHeight = originalHeight;
        _ratio = (double)originalWidth / originalHeight;
        var header = FrameShiftUiFactory.CreateHeader($"FrameShift - {functionName}",
            $"{Path.GetFileName(sourcePath)}    Original: {originalWidth} × {originalHeight} px",
            IconPaths.ResizeImageVideoIcon, IconPaths.AppIcon, fallbackGlyph);
        _textWidthPx = CreateValueTextBox("widthPx");
        _textHeightPx = CreateValueTextBox("heightPx");
        _textWidthPct = CreateValueTextBox("widthPct");
        _textHeightPct = CreateValueTextBox("heightPct");
        _checkLockRatio = new CheckBox { Name = "keepRatio", Text = "Keep ratio", Checked = true, AutoSize = true };
        var dimensions = FrameShiftUiFactory.CreateSection("New size", FrameShiftUiFactory.CreateVerticalStack(
            CreateDimensionsGrid(), FrameShiftUiFactory.CreateChoiceRow(_checkLockRatio)));
        var presets = FrameShiftUiFactory.CreateChoiceRow();
        foreach (var scale in new[] { 0.5, 0.75, 1.5, 2.0, 4.0 })
        {
            var button = FrameShiftUiFactory.CreateMeasuredActionButton(FormatScaleButtonText(scale), false, 72);
            button.Click += (_, _) => ApplyScalePreset(scale);
            presets.Controls.Add(button);
        }
        var reset = FrameShiftUiFactory.CreateMeasuredActionButton("Reset", false, 80);
        reset.Click += (_, _) =>
        {
            _checkLockRatio.Checked = true;
            SetAllFields(_originalWidth, _originalHeight);
            _textWidthPx.Focus();
            _textWidthPx.SelectAll();
        };
        presets.Controls.Add(reset);
        var content = FrameShiftUiFactory.CreateVerticalStack(dimensions,
            FrameShiftUiFactory.CreateSection("Quick scale", presets));
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Name = "cancelButton";
        var primary = FrameShiftUiFactory.CreateMeasuredActionButton("OK", true);
        primary.Name = "primaryButton";
        primary.DialogResult = DialogResult.OK;
        AcceptButton = primary;
        CancelButton = cancel;
        var layout = FrameShiftDialogLayout.Create(header, content,
            FrameShiftDialogLayout.CreateActions(cancel, primary),
            FrameShiftUiFactory.CreateStatusMessage("Creates a resized file next to the original with unique naming."));
        Controls.Add(layout);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, layout);
        RegisterFieldHandlers();
        SetAllFields(_originalWidth, _originalHeight);
        Shown += (_, _) => { _textWidthPx.Focus(); _textWidthPx.SelectAll(); };
        ResumeLayout(true);
    }

    public ResizeSettings? Selection { get; private set; }

    private TableLayoutPanel CreateDimensionsGrid()
    {
        var grid = new TableLayoutPanel
        {
            Name = "dimensionsGrid", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top, Margin = Padding.Empty, ColumnCount = 4, RowCount = 3, Size = Size.Empty
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 3; row++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(FrameShiftUiFactory.CreateWrappingLabel("Pixels (px)"), 1, 0);
        grid.Controls.Add(FrameShiftUiFactory.CreateWrappingLabel("Scale (%)"), 2, 0);
        grid.Controls.Add(new Label { Text = "&Width", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        grid.Controls.Add(new Label { Text = "&Height", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        grid.Controls.Add(_textWidthPx, 1, 1);
        grid.Controls.Add(_textWidthPct, 2, 1);
        grid.Controls.Add(_textHeightPx, 1, 2);
        grid.Controls.Add(_textHeightPct, 2, 2);
        foreach (var (box, caption, index) in new[]
        {
            (_textWidthPx, "Width in pixels", 0), (_textWidthPct, "Width scale in percent", 1),
            (_textHeightPx, "Height in pixels", 2), (_textHeightPct, "Height scale in percent", 3)
        })
        {
            box.Dock = DockStyle.Fill;
            box.AccessibleName = caption;
            box.TabIndex = index;
        }
        void Metrics()
        {
            var gap = FrameShiftUiMetrics.ToPixels(grid, FrameShiftUiMetrics.LineGap);
            foreach (Control child in grid.Controls)
                child.Margin = new Padding(0, 0, gap, gap);
            var labelsWidth = grid.Controls.Cast<Control>().Where(c => grid.GetColumn(c) == 0)
                .Max(c => c.GetPreferredSize(Size.Empty).Width + gap);
            // Keep four compact, equal inputs; the empty column absorbs unused space.
            var columnWidth = Math.Min(FrameShiftUiMetrics.ToPixels(grid, DimensionFieldWidth) + gap,
                Math.Max(gap + 1, (grid.ClientSize.Width - labelsWidth) / 2));
            grid.ColumnStyles[1].Width = grid.ColumnStyles[2].Width = columnWidth;
        }
        grid.HandleCreated += (_, _) => Metrics();
        grid.DpiChangedAfterParent += (_, _) => Metrics();
        grid.FontChanged += (_, _) => Metrics();
        grid.SizeChanged += (_, _) => Metrics();
        Metrics();
        return grid;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            var width = TryParsePositiveInt(_textWidthPx.Text);
            var height = TryParsePositiveInt(_textHeightPx.Text);
            if (width is null || height is null)
            {
                MessageBox.Show(
                    "Width and height must be positive values.",
                    "FrameShift",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                e.Cancel = true;
                return;
            }

            Selection = new ResizeSettings(width.Value, height.Value);
        }

        base.OnFormClosing(e);
    }

    private void RegisterFieldHandlers()
    {
        RegisterEditTracking(_textWidthPx);
        RegisterEditTracking(_textHeightPx);
        RegisterEditTracking(_textWidthPct);
        RegisterEditTracking(_textHeightPct);
        RegisterPercentTypingBehavior(_textWidthPct);
        RegisterPercentTypingBehavior(_textHeightPct);

        _textWidthPx.TextChanged += (_, _) =>
        {
            if (_updatingFields || _activeEditField != (string?)_textWidthPx.Tag)
            {
                return;
            }

            var value = TryParsePositiveInt(_textWidthPx.Text);
            if (value is not null)
            {
                SetFromWidthPx(value.Value);
            }
        };

        _textHeightPx.TextChanged += (_, _) =>
        {
            if (_updatingFields || _activeEditField != (string?)_textHeightPx.Tag)
            {
                return;
            }

            var value = TryParsePositiveInt(_textHeightPx.Text);
            if (value is not null)
            {
                SetFromHeightPx(value.Value);
            }
        };

        _textWidthPct.TextChanged += (_, _) =>
        {
            if (_updatingFields || _activeEditField != (string?)_textWidthPct.Tag)
            {
                return;
            }

            var value = TryParsePositiveDouble(_textWidthPct.Text);
            if (value is not null)
            {
                SetFromWidthPct(value.Value);
            }
        };

        _textHeightPct.TextChanged += (_, _) =>
        {
            if (_updatingFields || _activeEditField != (string?)_textHeightPct.Tag)
            {
                return;
            }

            var value = TryParsePositiveDouble(_textHeightPct.Text);
            if (value is not null)
            {
                SetFromHeightPct(value.Value);
            }
        };

        _checkLockRatio.CheckedChanged += (_, _) =>
        {
            if (!_checkLockRatio.Checked)
            {
                return;
            }

            var widthPx = TryParsePositiveInt(_textWidthPx.Text);
            if (widthPx is not null)
            {
                SetFromWidthPx(widthPx.Value);
                return;
            }

            var heightPx = TryParsePositiveInt(_textHeightPx.Text);
            if (heightPx is not null)
            {
                SetFromHeightPx(heightPx.Value);
                return;
            }

            SetAllFields(_originalWidth, _originalHeight);
        };
    }

    private void RegisterEditTracking(TextBox textBox)
    {
        textBox.Enter += (_, _) => _activeEditField = (string?)textBox.Tag;
        textBox.Leave += (_, _) =>
        {
            var fieldName = (string?)textBox.Tag;
            if (_activeEditField != fieldName)
            {
                return;
            }

            _activeEditField = null;
            CommitField(fieldName);
        };
    }

    private void RegisterPercentTypingBehavior(TextBox textBox)
    {
        textBox.KeyPress += (_, e) =>
        {
            if (char.IsControl(e.KeyChar) || char.IsDigit(e.KeyChar))
            {
                return;
            }

            if (e.KeyChar is '.' or ',')
            {
                var decimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
                if (textBox.Text.IndexOf(decimalSeparator, StringComparison.Ordinal) < 0)
                {
                    e.Handled = true;
                    var selectionStart = textBox.SelectionStart;
                    var selectionLength = textBox.SelectionLength;
                    var currentText = textBox.Text;
                    var nextText = currentText.Remove(selectionStart, selectionLength).Insert(selectionStart, decimalSeparator);
                    textBox.Text = nextText;
                    textBox.SelectionStart = selectionStart + decimalSeparator.Length;
                    return;
                }
            }

            e.Handled = true;
        };

        textBox.TextChanged += (_, _) =>
        {
            if (_updatingFields || _activeEditField != (string?)textBox.Tag)
            {
                return;
            }

            var sanitized = System.Text.RegularExpressions.Regex.Replace(textBox.Text, @"[^\d\.,]", string.Empty);
            if (sanitized == textBox.Text)
            {
                return;
            }

            var selectionStart = textBox.SelectionStart;
            _updatingFields = true;
            textBox.Text = sanitized;
            textBox.SelectionStart = Math.Min(selectionStart, textBox.Text.Length);
            _updatingFields = false;
        };
    }

    private void SetAllFields(int widthPx, int heightPx)
    {
        _updatingFields = true;

        if (_activeEditField != "widthPx")
        {
            _textWidthPx.Text = widthPx.ToString(CultureInfo.InvariantCulture);
        }

        if (_activeEditField != "heightPx")
        {
            _textHeightPx.Text = heightPx.ToString(CultureInfo.InvariantCulture);
        }

        if (_activeEditField != "widthPct")
        {
            _textWidthPct.Text = FormatPercentText(((double)widthPx / _originalWidth) * 100.0);
        }

        if (_activeEditField != "heightPct")
        {
            _textHeightPct.Text = FormatPercentText(((double)heightPx / _originalHeight) * 100.0);
        }

        _updatingFields = false;
    }

    private void SetFromWidthPx(int widthPx)
    {
        var heightPx = _checkLockRatio.Checked
            ? Math.Max(1, (int)Math.Round(widthPx / _ratio))
            : TryParsePositiveInt(_textHeightPx.Text) ?? _originalHeight;

        SetAllFields(widthPx, heightPx);
    }

    private void SetFromHeightPx(int heightPx)
    {
        var widthPx = _checkLockRatio.Checked
            ? Math.Max(1, (int)Math.Round(heightPx * _ratio))
            : TryParsePositiveInt(_textWidthPx.Text) ?? _originalWidth;

        SetAllFields(widthPx, heightPx);
    }

    private void SetFromWidthPct(double widthPct)
    {
        var widthPx = Math.Max(1, (int)Math.Round((_originalWidth * widthPct) / 100.0));
        SetFromWidthPx(widthPx);
    }

    private void SetFromHeightPct(double heightPct)
    {
        var heightPx = Math.Max(1, (int)Math.Round((_originalHeight * heightPct) / 100.0));
        SetFromHeightPx(heightPx);
    }

    private void ApplyScalePreset(double scale)
    {
        var widthPx = Math.Max(1, (int)Math.Round(_originalWidth * scale));
        var heightPx = Math.Max(1, (int)Math.Round(_originalHeight * scale));
        SetAllFields(widthPx, heightPx);
    }

    private void CommitField(string? fieldName)
    {
        switch (fieldName)
        {
            case "widthPx":
            {
                var value = TryParsePositiveInt(_textWidthPx.Text);
                if (value is not null)
                {
                    SetFromWidthPx(value.Value);
                }

                break;
            }
            case "heightPx":
            {
                var value = TryParsePositiveInt(_textHeightPx.Text);
                if (value is not null)
                {
                    SetFromHeightPx(value.Value);
                }

                break;
            }
            case "widthPct":
            {
                var value = TryParsePositiveDouble(_textWidthPct.Text);
                if (value is not null)
                {
                    SetFromWidthPct(value.Value);
                }

                break;
            }
            case "heightPct":
            {
                var value = TryParsePositiveDouble(_textHeightPct.Text);
                if (value is not null)
                {
                    SetFromHeightPct(value.Value);
                }

                break;
            }
        }
    }

    private static TextBox CreateValueTextBox(string tag)
    {
        return new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary,
            TextAlign = HorizontalAlignment.Right,
            Name = tag,
            Tag = tag
        };
    }

    private static int? TryParsePositiveInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;
    }

    private static double? TryParsePositiveDouble(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var styles = NumberStyles.Float;
        foreach (var culture in new[] { CultureInfo.CurrentCulture, CultureInfo.InvariantCulture })
        {
            if (double.TryParse(text.Trim(), styles, culture, out var value) && value > 0)
            {
                return value;
            }
        }

        return null;
    }

    private static string FormatPercentText(double value)
    {
        var rounded = Math.Round(value, 2);
        return Math.Abs(rounded - Math.Round(rounded)) < 0.0000001
            ? ((int)Math.Round(rounded)).ToString(CultureInfo.InvariantCulture)
            : rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatScaleButtonText(double scale)
    {
        return "x" + scale.ToString("0.##", CultureInfo.CurrentCulture);
    }
}
