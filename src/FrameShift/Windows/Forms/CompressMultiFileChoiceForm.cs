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
        FrameShiftWindowChrome.Apply(this, L.Get("CompressMultiFileChoiceForm_FrameShift_Compression_57bb2419"));
        var header = FrameShiftUiFactory.CreateHeader(L.Get("CompressMultiFileChoiceForm_FrameShift_Compression_57bb2419"),
            L.Format("CompressMultiFileChoiceForm_Compression_for_0_selected_files_22119489",fileCount), IconPaths.CompressVideoIcon, IconPaths.AppIcon, "▶");
        var same = new RadioButton { Text = L.Get("CompressMultiFileChoiceForm_Use_same_settings_for_all_files_277d6946"), AutoSize = true, Checked = true };
        var each = new RadioButton { Text = L.Get("CompressMultiFileChoiceForm_Configure_each_file_separately_a8f4d916"), AutoSize = true };
        same.CheckedChanged += (_, _) => { if (same.Checked) _choice = CompressMultiFileChoice.SameForAll; };
        each.CheckedChanged += (_, _) => { if (each.Checked) _choice = CompressMultiFileChoice.PerFile; };
        var content = FrameShiftUiFactory.CreateSection(L.Get("CompressMultiFileChoiceForm_Configuration_mode_ab5943dc"), FrameShiftUiFactory.CreateVerticalStack(
            same, FrameShiftUiFactory.CreateWrappingLabel(L.Get("CompressMultiFileChoiceForm_One_compression_window_applied_to_al_b03cb47b")),
            each, FrameShiftUiFactory.CreateWrappingLabel(L.Get("CompressMultiFileChoiceForm_A_compression_window_will_open_for_e_280b947a"))));
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("CompressMultiFileChoiceForm_Cancel_998b9c48"), false);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Name = "cancelButton";
        var primary = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("CompressMultiFileChoiceForm_Continue_fd19155b"), true);
        primary.Name = "primaryButton";
        primary.DialogResult = DialogResult.OK;
        AcceptButton = primary;
        CancelButton = cancel;
        var layout = FrameShiftDialogLayout.Create(header, content,
            FrameShiftDialogLayout.CreateActions(cancel, primary),
            FrameShiftUiFactory.CreateStatusMessage(L.Get("CompressMultiFileChoiceForm_Compressed_files_are_created_next_to_c07fd0e5")));
        Controls.Add(layout);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, layout);
        ResumeLayout(true);
    }

    public CompressMultiFileChoice Choice => _choice;

}
