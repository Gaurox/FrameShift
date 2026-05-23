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
    private readonly int _originalWidth;
    private readonly int _originalHeight;
    private readonly double _ratio;
    private readonly string _mediaKindLabel;

    private bool _updatingFields;
    private string? _activeEditField;

    private readonly TextBox _textWidthPx;
    private readonly TextBox _textHeightPx;
    private readonly TextBox _textWidthPct;
    private readonly TextBox _textHeightPct;
    private readonly CheckBox _checkLockRatio;

    protected ResizeMediaFormBase(string functionName, string sourcePath, int originalWidth, int originalHeight, string fallbackGlyph)
    {
        _originalWidth = originalWidth;
        _originalHeight = originalHeight;
        _ratio = (double)originalWidth / originalHeight;
        _mediaKindLabel = functionName;

        var isImageResize = functionName.Equals("Resize Image", StringComparison.OrdinalIgnoreCase);
        var windowTitle = isImageResize
            ? "FrameShift - Resize image"
            : $"FrameShift - {functionName}";
        FrameShiftWindowChrome.Apply(this, windowTitle);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 472);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        Controls.Add(FrameShiftUiFactory.CreateFixedHeader(
            $"FrameShift - {functionName}",
            $"Source: {Path.GetFileName(sourcePath)}",
            IconPaths.ResizeImageVideoIcon,
            IconPaths.AppIcon,
            fallbackGlyph));

        var originalSection = FrameShiftUiFactory.CreateFixedSection(new Point(12, 82), new Size(536, 62), "Original size");
        Controls.Add(originalSection);
        originalSection.Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(18, 30),
            Size = new Size(260, 20),
            Text = $"{originalWidth} x {originalHeight} px",
            ForeColor = FrameShiftTheme.TextPrimary,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point)
        });

        var sizeSection = FrameShiftUiFactory.CreateFixedSection(new Point(12, 156), new Size(536, 112), "New size");
        Controls.Add(sizeSection);

        sizeSection.Controls.Add(CreateFieldLabel("Width", 18, 33));
        _textWidthPx = CreateValueTextBox("widthPx");
        sizeSection.Controls.Add(FrameShiftUiFactory.CreateFixedTextInputHost(_textWidthPx, new Point(74, 28), new Size(92, 30)));
        sizeSection.Controls.Add(CreateUnitLabel("px", 174, 33));

        _textWidthPct = CreateValueTextBox("widthPct");
        sizeSection.Controls.Add(FrameShiftUiFactory.CreateFixedTextInputHost(_textWidthPct, new Point(240, 28), new Size(72, 30)));
        sizeSection.Controls.Add(CreateUnitLabel("%", 320, 33));

        sizeSection.Controls.Add(CreateFieldLabel("Height", 18, 69));
        _textHeightPx = CreateValueTextBox("heightPx");
        sizeSection.Controls.Add(FrameShiftUiFactory.CreateFixedTextInputHost(_textHeightPx, new Point(74, 64), new Size(92, 30)));
        sizeSection.Controls.Add(CreateUnitLabel("px", 174, 69));

        _textHeightPct = CreateValueTextBox("heightPct");
        sizeSection.Controls.Add(FrameShiftUiFactory.CreateFixedTextInputHost(_textHeightPct, new Point(240, 64), new Size(72, 30)));
        sizeSection.Controls.Add(CreateUnitLabel("%", 320, 69));

        _checkLockRatio = new CheckBox
        {
            Text = "Keep ratio",
            Checked = true,
            Location = new Point(376, 46),
            Size = new Size(120, 24),
            ForeColor = FrameShiftTheme.TextPrimary
        };
        sizeSection.Controls.Add(_checkLockRatio);

        var presetsSection = FrameShiftUiFactory.CreateFixedSection(new Point(12, 280), new Size(536, 76), "Quick scale");
        Controls.Add(presetsSection);

        var presetScales = new[] { 0.5, 0.75, 1.5, 2.0, 4.0 };
        var presetX = 18;
        for (var i = 0; i < presetScales.Length; i++)
        {
            var scale = presetScales[i];
            var button = FrameShiftUiFactory.CreateFixedActionButton(
                FormatScaleButtonText(scale),
                new Point(presetX + (i * 84), 30),
                new Size(72, 30),
                primary: false);
            button.Click += (_, _) => ApplyScalePreset(scale);
            presetsSection.Controls.Add(button);
        }

        var resetButton = FrameShiftUiFactory.CreateFixedActionButton("Reset", new Point(438, 30), new Size(80, 30), primary: false);
        resetButton.Click += (_, _) =>
        {
            _checkLockRatio.Checked = true;
            SetAllFields(_originalWidth, _originalHeight);
            _textWidthPx.Focus();
            _textWidthPx.SelectAll();
        };
        presetsSection.Controls.Add(resetButton);

        var infoCard = FrameShiftUiFactory.CreateFixedInfoCard(new Point(12, 368), new Size(536, 44));
        infoCard.Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(16, 12),
            Size = new Size(504, 18),
            Text = $"The resized {_mediaKindLabel.ToLowerInvariant()} is created next to the original file with unique naming.",
            ForeColor = FrameShiftTheme.TextSecondary
        });
        Controls.Add(infoCard);

        var cancelButton = FrameShiftUiFactory.CreateFixedActionButton("Cancel", new Point(278, 426), new Size(120, 34), primary: false);
        cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);

        var okButton = FrameShiftUiFactory.CreateFixedActionButton("OK", new Point(408, 426), new Size(140, 34), primary: true);
        okButton.DialogResult = DialogResult.OK;
        Controls.Add(okButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        RegisterFieldHandlers();
        SetAllFields(_originalWidth, _originalHeight);

        Shown += (_, _) =>
        {
            _textWidthPx.Focus();
            _textWidthPx.SelectAll();
        };
    }

    public ResizeSettings? Selection { get; private set; }

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

    private static Label CreateFieldLabel(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(50, 23),
            ForeColor = FrameShiftTheme.TextPrimary
        };
    }

    private static Label CreateUnitLabel(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(24, 23),
            ForeColor = FrameShiftTheme.TextSecondary
        };
    }

    private static TextBox CreateValueTextBox(string tag)
    {
        return new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary,
            TextAlign = HorizontalAlignment.Right,
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
