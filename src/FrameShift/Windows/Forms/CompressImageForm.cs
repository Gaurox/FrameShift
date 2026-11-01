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
        FrameShiftWindowChrome.Apply(this, L.Get("CompressImageForm_FrameShift_Compress_Image_a80dce39"));
        var header = FrameShiftUiFactory.CreateHeader(L.Get("CompressImageForm_FrameShift_Compress_Image_a80dce39"),
            L.Format("CompressImageForm_0_Source_1_Size_2_9058ea3c",Path.GetFileName(sourcePath),sourceExtension.TrimStart('.').ToUpperInvariant(),FormatFileSize(sourceBytes)),
            IconPaths.CompressVideoIcon, IconPaths.AppIcon, "▶");
        _radioHigh = new FrameShiftChoiceCard(L.Get("CompressImageForm_High_quality_f9552ad2"), L.Get("CompressImageForm_Preserves_more_detail_2874f302")) { Checked = true };
        _radioBalanced = new FrameShiftChoiceCard(L.Get("CompressImageForm_Balanced_b9f90b2b"), L.Get("CompressImageForm_Quality_and_size_6d6829ce")) { Checked = false };
        _radioSmall = new FrameShiftChoiceCard(L.Get("CompressImageForm_Small_file_461660a8"), L.Get("CompressImageForm_Strongest_reduction_d4f68c80"));
        _radioHigh.CheckedChanged += (_, _) => { if (_radioHigh.Checked) _selectedProfileId = CompressImageSettings.ProfileHigh; };
        _radioBalanced.CheckedChanged += (_, _) => { if (_radioBalanced.Checked) _selectedProfileId = CompressImageSettings.ProfileBalanced; };
        _radioSmall.CheckedChanged += (_, _) => { if (_radioSmall.Checked) _selectedProfileId = CompressImageSettings.ProfileSmall; };
        var profiles = FrameShiftUiFactory.CreateSection(L.Get("CompressImageForm_Compression_profile_a73f05f6"), FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateChoiceRow(_radioHigh, _radioBalanced, _radioSmall),
            FrameShiftUiFactory.CreateWrappingLabel(L.Get("CompressImageForm_PNG_stays_lossless_JPG_and_WEBP_usua_10f52a5c"))));
        _formatCombo = new ComboBox { Name = "outputFormat", DropDownStyle = ComboBoxStyle.DropDownList };
        _formatCombo.Items.AddRange(["PNG", "JPG", "WEBP"]);
        _formatCombo.SelectedItem = CompressImageSettings.GetDefaultOutputFormat(sourceExtension).ToUpperInvariant();
        _checkTarget = new CheckBox { Name = "useTarget", Text = L.Get("CompressImageForm_Target_file_size_optional_48bbbfd1"), AutoSize = true };
        _textTarget = new TextBox { Name = "targetSize" };
        _unitSelector = new ComboBox { Name = "targetUnit", DropDownStyle = ComboBoxStyle.DropDownList };
        _unitSelector.Items.AddRange(["KB", "MB"]);
        _unitSelector.SelectedIndex = 0;
        _targetHint = FrameShiftUiFactory.CreateWrappingLabel("");
        var output = FrameShiftUiFactory.CreateSection(L.Get("CompressImageForm_Output_8520bb37"), FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateFieldRow(L.Get("CompressImageForm_Format_d6a6bb9c"), _formatCombo, logicalEditorWidth: 160),
            _checkTarget, FrameShiftUiFactory.CreateFieldWithUnit(L.Get("CompressImageForm_Size_bf30e36d"), _textTarget, _unitSelector), _targetHint));
        var content = FrameShiftUiFactory.CreateVerticalStack(profiles, output);
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("CompressImageForm_Cancel_998b9c48"), false);
        cancel.Name = "cancelButton";
        cancel.DialogResult = DialogResult.Cancel;
        var compress = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("CompressImageForm_Compress_5830ffa3"), true);
        compress.Name = "compressButton";
        compress.DialogResult = DialogResult.OK;
        var layout = FrameShiftDialogLayout.Create(header, content, FrameShiftDialogLayout.CreateActions(cancel, compress),
            FrameShiftUiFactory.CreateStatusMessage(L.Get("CompressImageForm_Creates_a_new_image_next_to_the_sour_322e9d7c")));
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
                        L.Get("CompressImageForm_Target_file_size_must_be_a_positive__028fded9"),
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
        _targetHint.Text = supportsTarget ? L.Get("CompressImageForm_Best_effort_target_ebaf584f") : L.Get("CompressImageForm_Only_available_for_JPG_WEBP_17baa4d3");
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
