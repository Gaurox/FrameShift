using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;
using FrameShift.Windows.Controls;
using FrameShift.Core.Actions;

namespace FrameShift.Windows.Forms;

public sealed class CompressAudioForm : Form
{
    private readonly CheckBox _checkTarget;
    private readonly TextBox _textTarget;
    private readonly ComboBox _unitSelector;
    private string _selectedProfileId = "high";

    public CompressAudioForm(string sourcePath, string sourceExtension, long sourceBytes, int sampleRate, int channels)
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(600, 500), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, L.Get("CompressAudioForm_FrameShift_Compress_Audio_c5afc06c"));
        var header = FrameShiftUiFactory.CreateHeader(L.Get("CompressAudioForm_FrameShift_Compress_Audio_c5afc06c"),
            $"{Path.GetFileName(sourcePath)}    {sourceExtension.TrimStart('.').ToUpperInvariant()}    {FormatFileSize(sourceBytes)}    {FormatAudioInfo(sampleRate, channels)}",
            IconPaths.CompressVideoIcon, IconPaths.AppIcon, "▶");
        var high = new FrameShiftChoiceCard(L.Get("CompressAudioForm_High_quality_f9552ad2"), L.Get("CompressAudioForm_Preserves_more_detail_2874f302")) { Checked = true };
        var balanced = new FrameShiftChoiceCard(L.Get("CompressAudioForm_Balanced_b9f90b2b"), L.Get("CompressAudioForm_Quality_and_size_6d6829ce"));
        var small = new FrameShiftChoiceCard(L.Get("CompressAudioForm_Small_file_461660a8"), L.Get("CompressAudioForm_Strongest_reduction_d4f68c80"));
        high.CheckedChanged += (_, _) => { if (high.Checked) _selectedProfileId = "high"; };
        balanced.CheckedChanged += (_, _) => { if (balanced.Checked) _selectedProfileId = "balanced"; };
        small.CheckedChanged += (_, _) => { if (small.Checked) _selectedProfileId = "small"; };
        var supported = CompressAudioSettings.IsTargetSizeSupportedForExtension(sourceExtension);
        _checkTarget = new CheckBox { Name = "useTarget", Text = L.Get("CompressAudioForm_Target_file_size_optional_48bbbfd1"), AutoSize = true, Enabled = supported };
        _textTarget = new TextBox { Name = "targetSize", ReadOnly = true, Enabled = supported };
        _unitSelector = new ComboBox { Name = "targetUnit", DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
        _unitSelector.Items.AddRange(["KB", "MB"]);
        _unitSelector.SelectedItem = "MB";
        _checkTarget.CheckedChanged += (_, _) =>
        {
            _textTarget.ReadOnly = !_checkTarget.Checked;
            _unitSelector.Enabled = supported && _checkTarget.Checked;
        };
        var content = FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateSection(L.Get("CompressAudioForm_Compression_profile_a73f05f6"), FrameShiftUiFactory.CreateChoiceRow(high, balanced, small)),
            FrameShiftUiFactory.CreateSection(L.Get("CompressAudioForm_Output_8520bb37"), FrameShiftUiFactory.CreateVerticalStack(FrameShiftUiFactory.CreateChoiceRow(_checkTarget),
                FrameShiftUiFactory.CreateFieldWithUnit(L.Get("CompressAudioForm_Size_bf30e36d"), _textTarget, _unitSelector),
                FrameShiftUiFactory.CreateWrappingLabel(supported ? L.Get("CompressAudioForm_Approximate_target_size_the_original_b91530bf")
                    : L.Format("CompressAudioForm_Target_size_is_available_for_MP3_M4A_cc7101be",sourceExtension.TrimStart('.').ToUpperInvariant())))));
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("CompressAudioForm_Cancel_998b9c48"), false);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Name = "cancelButton";
        var primary = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("CompressAudioForm_Compress_5830ffa3"), true);
        primary.Name = "primaryButton";
        primary.DialogResult = DialogResult.OK;
        AcceptButton = primary;
        CancelButton = cancel;
        var layout = FrameShiftDialogLayout.Create(header, content,
            FrameShiftDialogLayout.CreateActions(cancel, primary),
            FrameShiftUiFactory.CreateStatusMessage(L.Get("CompressAudioForm_The_compressed_audio_is_created_next_ab489077")));
        Controls.Add(layout);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, layout);
        ResumeLayout(true);
    }

    public string SelectedProfileId => _selectedProfileId;
    public long? TargetBytes { get; private set; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            if (_checkTarget.Checked)
            {
                if (!TryParseTargetBytes(out var targetBytes))
                {
                    MessageBox.Show(
                        L.Get("CompressAudioForm_Target_file_size_must_be_a_positive__028fded9"),
                        "FrameShift",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    e.Cancel = true;
                    return;
                }

                TargetBytes = targetBytes;
            }
            else
            {
                TargetBytes = null;
            }
        }

        base.OnFormClosing(e);
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

    private static string FormatAudioInfo(int sampleRate, int channels)
    {
        var rateText = sampleRate > 0
            ? $"{sampleRate / 1000.0:0.#} kHz"
            : L.Get("CompressAudioForm_unknown_Hz_bdcb7b7e");
        var chText = channels switch
        {
            1 => L.Get("CompressAudioForm_mono_48caf5cb"),
            2 => L.Get("CompressAudioForm_stereo_af4c74f2"),
            _ when channels > 0 => L.Format("CompressAudioForm_0_ch_f7da5f15",channels),
            _ => L.Get("CompressAudioForm_unknown_ch_d9bb9351")
        };
        return $"{rateText} / {chText}";
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
