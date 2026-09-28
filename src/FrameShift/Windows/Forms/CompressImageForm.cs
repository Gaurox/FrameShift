using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Windows.Helpers;
using FrameShift.Windows.Controls;

namespace FrameShift.Windows.Forms;

public sealed class CompressImageForm : Form
{
    private readonly RadioButton _radioHigh;
    private readonly RadioButton _radioBalanced;
    private readonly RadioButton _radioSmall;
    private readonly ComboBox _formatCombo;
    private readonly CheckBox _checkTarget;
    private readonly TextBox _textTarget;
    private readonly Label _targetHint;
    private readonly ComboBox _unitSelector;
    private string _selectedProfileId = CompressImageSettings.ProfileHigh;

    public CompressImageForm(string sourcePath, string sourceExtension, long sourceBytes)
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(600, 600), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, "FrameShift - Compress Image");
        var header = FrameShiftUiFactory.CreateHeader("FrameShift - Compress Image",
            $"{Path.GetFileName(sourcePath)}    Source: {sourceExtension.TrimStart('.').ToUpperInvariant()}    Size: {FormatFileSize(sourceBytes)}",
            IconPaths.CompressVideoIcon, IconPaths.AppIcon, "▶");
        _radioHigh = new FrameShiftChoiceCard("High quality", "Preserves more detail") { Checked = true };
        _radioBalanced = new FrameShiftChoiceCard("Balanced", "Quality and size") { Checked = false };
        _radioSmall = new FrameShiftChoiceCard("Small file", "Strongest reduction");
        _radioHigh.CheckedChanged += (_, _) => { if (_radioHigh.Checked) _selectedProfileId = CompressImageSettings.ProfileHigh; };
        _radioBalanced.CheckedChanged += (_, _) => { if (_radioBalanced.Checked) _selectedProfileId = CompressImageSettings.ProfileBalanced; };
        _radioSmall.CheckedChanged += (_, _) => { if (_radioSmall.Checked) _selectedProfileId = CompressImageSettings.ProfileSmall; };
        var profiles = FrameShiftUiFactory.CreateSection("Compression profile", FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateChoiceRow(_radioHigh, _radioBalanced, _radioSmall),
            FrameShiftUiFactory.CreateWrappingLabel("PNG stays lossless. JPG and WEBP usually give the strongest file size reduction.")));
        _formatCombo = new ComboBox { Name = "outputFormat", DropDownStyle = ComboBoxStyle.DropDownList };
        _formatCombo.Items.AddRange(["PNG", "JPG", "WEBP"]);
        _formatCombo.SelectedItem = CompressImageSettings.GetDefaultOutputFormat(sourceExtension).ToUpperInvariant();
        _checkTarget = new CheckBox { Name = "useTarget", Text = "Target file size (optional)", AutoSize = true };
        _textTarget = new TextBox { Name = "targetSize" };
        _unitSelector = new ComboBox { Name = "targetUnit", DropDownStyle = ComboBoxStyle.DropDownList };
        _unitSelector.Items.AddRange(["KB", "MB"]);
        _unitSelector.SelectedIndex = 0;
        _targetHint = FrameShiftUiFactory.CreateWrappingLabel("");
        var output = FrameShiftUiFactory.CreateSection("Output", FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateFieldRow("&Format", _formatCombo, logicalEditorWidth: 160),
            _checkTarget, FrameShiftUiFactory.CreateFieldWithUnit("&Size", _textTarget, _unitSelector), _targetHint));
        var content = FrameShiftUiFactory.CreateVerticalStack(profiles, output);
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        cancel.Name = "cancelButton";
        cancel.DialogResult = DialogResult.Cancel;
        var compress = FrameShiftUiFactory.CreateMeasuredActionButton("Compress", true);
        compress.Name = "compressButton";
        compress.DialogResult = DialogResult.OK;
        var layout = FrameShiftDialogLayout.Create(header, content, FrameShiftDialogLayout.CreateActions(cancel, compress),
            FrameShiftUiFactory.CreateStatusMessage("Creates a new image next to the source. The original is preserved."));
        Controls.Add(layout);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, layout);
        AcceptButton = compress;
        CancelButton = cancel;
        _formatCombo.SelectedIndexChanged += (_, _) => UpdateTargetAvailability();
        _checkTarget.CheckedChanged += (_, _) => UpdateTargetAvailability();
        UpdateTargetAvailability();
        ResumeLayout(true);
    }

    public CompressImageSettings? Selection { get; private set; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            var format = ((_formatCombo.SelectedItem as string) ?? "PNG").Trim().ToLowerInvariant();
            long? targetBytes = null;

            if (_checkTarget.Checked)
            {
                if (!TryParseTargetBytes(out var parsedTargetBytes))
                {
                    MessageBox.Show(
                        "Target file size must be a positive number.",
                        "FrameShift",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    e.Cancel = true;
                    return;
                }

                targetBytes = parsedTargetBytes;
            }

            Selection = new CompressImageSettings(_selectedProfileId, format, targetBytes);
        }

        base.OnFormClosing(e);
    }

    private void UpdateTargetAvailability()
    {
        var format = ((_formatCombo.SelectedItem as string) ?? "PNG").Trim().ToLowerInvariant();
        var supportsTarget = CompressImageSettings.IsTargetSizeSupportedForFormat(format);
        if (!supportsTarget) _checkTarget.Checked = false;
        _checkTarget.Enabled = supportsTarget;
        _textTarget.ReadOnly = !_checkTarget.Checked;
        _unitSelector.Enabled = supportsTarget && _checkTarget.Checked;
        _targetHint.Text = supportsTarget ? "Best-effort target" : "Only available for JPG/WEBP";
    }

    private bool TryParseTargetBytes(out long targetBytes)
    {
        targetBytes = 0;
        var raw = _textTarget.Text.Trim().Replace(',', '.');
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return false;
        }

        targetBytes = (_unitSelector.SelectedItem as string) == "MB"
            ? (long)Math.Round(value * 1_048_576d)
            : (long)Math.Round(value * 1024d);
        return targetBytes > 0;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1L << 30)
        {
            return $"{Math.Round(bytes / (double)(1L << 30), 2):0.##} GB";
        }

        if (bytes >= 1L << 20)
        {
            return $"{Math.Round(bytes / (double)(1L << 20), 2):0.##} MB";
        }

        return $"{Math.Round(bytes / 1024d):0} KB";
    }
}
