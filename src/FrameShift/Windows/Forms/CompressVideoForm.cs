using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;
using FrameShift.Windows.Controls;

namespace FrameShift.Windows.Forms;

public sealed class CompressVideoForm : Form
{
    private readonly CheckBox _checkTarget;
    private readonly TextBox _textTarget;
    private readonly ComboBox _unitSelector;
    private string _selectedProfileId = "high";

    public CompressVideoForm(string sourcePath, string sourceExtension, long sourceBytes, string? resolutionText)
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(600, 500), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, "FrameShift - Compress Video");
        var header = FrameShiftUiFactory.CreateHeader("FrameShift - Compress Video",
            $"{Path.GetFileName(sourcePath)}    {sourceExtension.TrimStart('.').ToUpperInvariant()}    {FormatFileSize(sourceBytes)}    {resolutionText ?? ""}",
            IconPaths.CompressVideoIcon, IconPaths.AppIcon, "▶");
        var high = new FrameShiftChoiceCard("High quality", "Preserves more detail") { Checked = true };
        var balanced = new FrameShiftChoiceCard("Balanced", "Quality and size");
        var small = new FrameShiftChoiceCard("Small file", "Strongest reduction");
        high.CheckedChanged += (_, _) => { if (high.Checked) _selectedProfileId = "high"; };
        balanced.CheckedChanged += (_, _) => { if (balanced.Checked) _selectedProfileId = "balanced"; };
        small.CheckedChanged += (_, _) => { if (small.Checked) _selectedProfileId = "small"; };
        var supported = true;
        _checkTarget = new CheckBox { Name = "useTarget", Text = "Target file size (optional)", AutoSize = true, Enabled = supported };
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
            FrameShiftUiFactory.CreateSection("Compression profile", FrameShiftUiFactory.CreateChoiceRow(high, balanced, small)),
            FrameShiftUiFactory.CreateSection("Output", FrameShiftUiFactory.CreateVerticalStack(_checkTarget,
                FrameShiftUiFactory.CreateFieldWithUnit("&Size", _textTarget, _unitSelector),
                FrameShiftUiFactory.CreateWrappingLabel(supported ? "Best-effort target" : "Target size is not available for this format."))));
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Name = "cancelButton";
        var primary = FrameShiftUiFactory.CreateMeasuredActionButton("Compress", true);
        primary.Name = "primaryButton";
        primary.DialogResult = DialogResult.OK;
        AcceptButton = primary;
        CancelButton = cancel;
        var layout = FrameShiftDialogLayout.Create(header, content,
            FrameShiftDialogLayout.CreateActions(cancel, primary),
            FrameShiftUiFactory.CreateStatusMessage("The compressed video is created next to the original file. The format stays the same."));
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
                        "Target file size must be a positive number.",
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
