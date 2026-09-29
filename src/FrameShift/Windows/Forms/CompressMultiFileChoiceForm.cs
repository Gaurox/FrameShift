using System;
using System.Drawing;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

internal enum CompressMultiFileChoice { SameForAll, PerFile }

internal sealed class CompressMultiFileChoiceForm : Form
{
    private CompressMultiFileChoice _choice = CompressMultiFileChoice.SameForAll;
    public CompressMultiFileChoiceForm(int fileCount)
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(600, 500), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, "FrameShift - Compression");
        var header = FrameShiftUiFactory.CreateHeader("FrameShift - Compression",
            $"Compression for {fileCount} selected files", IconPaths.CompressVideoIcon, IconPaths.AppIcon, "▶");
        var same = new RadioButton { Text = "Use same settings for all files", AutoSize = true, Checked = true };
        var each = new RadioButton { Text = "Configure each file separately", AutoSize = true };
        same.CheckedChanged += (_, _) => { if (same.Checked) _choice = CompressMultiFileChoice.SameForAll; };
        each.CheckedChanged += (_, _) => { if (each.Checked) _choice = CompressMultiFileChoice.PerFile; };
        var content = FrameShiftUiFactory.CreateSection("Configuration mode", FrameShiftUiFactory.CreateVerticalStack(
            same, FrameShiftUiFactory.CreateWrappingLabel("One compression window, applied to all selected files."),
            each, FrameShiftUiFactory.CreateWrappingLabel("A compression window will open for every selected file.")));
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Name = "cancelButton";
        var primary = FrameShiftUiFactory.CreateMeasuredActionButton("Continue", true);
        primary.Name = "primaryButton";
        primary.DialogResult = DialogResult.OK;
        AcceptButton = primary;
        CancelButton = cancel;
        var layout = FrameShiftDialogLayout.Create(header, content,
            FrameShiftDialogLayout.CreateActions(cancel, primary),
            FrameShiftUiFactory.CreateStatusMessage("Compressed files are created next to the originals. The format stays the same."));
        Controls.Add(layout);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, layout);
        ResumeLayout(true);
    }

    public CompressMultiFileChoice Choice => _choice;

}
