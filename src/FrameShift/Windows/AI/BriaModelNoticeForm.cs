using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FrameShift.Core.Logging;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.AI;

public enum BriaModelStatus
{
    Missing,
    Mismatch,
    Valid
}

// Shown when a user-supplied BRIA RMBG-2.0 model is missing or does not match the
// expected official file. FrameShift never downloads BRIA models, so this dialog only
// guides the user to the official BRIA page and the local folder where the file must be
// placed. "Re-check" re-runs verification: if the file is now valid the dialog proceeds
// to the action; otherwise it stays open and reports the current status. On a checksum
// mismatch (strict pin, warn-only fallback) the user may also run the file anyway.
public sealed class BriaModelNoticeForm : Form
{
    private readonly string _infoPageUrl;
    private readonly string _modelFolder;
    private readonly Func<BriaModelStatus> _recheck;

    private readonly Label _messageLabel;
    private readonly Label _statusLabel;
    private readonly Button _openPageButton;
    private readonly Button _openFolderButton;
    private readonly Button _recheckButton;
    private readonly Button _useAnywayButton;
    private readonly Button _cancelButton;
    private readonly FlowLayoutPanel _actions;

    private BriaModelStatus _status;

    // True when the user resolved the dialog by proceeding to the action (a valid
    // re-check, or "Use anyway" on a mismatch).
    public bool Proceed { get; private set; }

    public BriaModelNoticeForm(
        string expectedFileName,
        string modelFolder,
        string infoPageUrl,
        string approxSizeText,
        BriaModelStatus initialStatus,
        Func<BriaModelStatus> recheck)
    {
        _infoPageUrl = infoPageUrl;
        _modelFolder = modelFolder;
        _recheck = recheck;
        _status = initialStatus;

        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(680, 460), new Size(400, 300));
        FrameShiftWindowChrome.Apply(this, L.Get("BriaModelNoticeForm_FrameShift_AI_Remove_Background_BRIA_c3d1fdfe"), IconPaths.FrameShiftAiIcon, IconPaths.AppIcon);
        var header = FrameShiftUiFactory.CreateHeader(L.Get("BriaModelNoticeForm_FrameShift_Remove_Background_BRIA_6b3c8039"),
            L.Get("BriaModelNoticeForm_Manual_model_installation_required_5d2859f9"), IconPaths.RemoveBackgroundAiIcon, IconPaths.FrameShiftAiIcon, "AI");
        _messageLabel = FrameShiftUiFactory.CreateWrappingLabel("");
        var instructions = FrameShiftUiFactory.CreateSection(L.Get("BriaModelNoticeForm_Model_installation_8b4f84a9"), FrameShiftUiFactory.CreateVerticalStack(
            _messageLabel,
            FrameShiftUiFactory.CreateStatusMessage(L.Format("BriaModelNoticeForm_Expected_file_0_approx_1_Target_fold_7abadf24",expectedFileName,approxSizeText,modelFolder))));
        _statusLabel = FrameShiftUiFactory.CreateWrappingLabel("");
        _openPageButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("BriaModelNoticeForm_Open_BRIA_page_b2343cc8"), false);
        _openPageButton.Click += OnOpenPageClick;
        _openFolderButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("BriaModelNoticeForm_Open_folder_20cc8e52"), false);
        _openFolderButton.Click += OnOpenFolderClick;
        var links = FrameShiftUiFactory.CreateChoiceRow(_openPageButton, _openFolderButton);
        _recheckButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("BriaModelNoticeForm_Re_check_1717551f"), true);
        _recheckButton.Click += OnRecheckClick;
        _useAnywayButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("BriaModelNoticeForm_Use_anyway_41651793"), false);
        _useAnywayButton.Click += (_, _) =>
        {
            Proceed = true;
            DialogResult = DialogResult.OK;
            Close();
        };
        _cancelButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("BriaModelNoticeForm_Cancel_998b9c48"), false);
        _cancelButton.DialogResult = DialogResult.Cancel;
        CancelButton = _cancelButton;
        AcceptButton = _recheckButton;
        _actions = FrameShiftDialogLayout.CreateActions(_cancelButton, _useAnywayButton, _recheckButton);
        var root = FrameShiftDialogLayout.Create(header,
            FrameShiftUiFactory.CreateVerticalStack(instructions, links, _statusLabel),
            _actions);
        Controls.Add(root);
        ApplyStatus(_status);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, root);
        ResumeLayout(true);
    }

    private void ApplyStatus(BriaModelStatus status)
    {
        _status = status;

        _messageLabel.Text = status == BriaModelStatus.Mismatch
            ? L.Get("BriaModelNoticeForm_The_installed_file_does_not_match_th_02d9b1d8") +
              L.Get("BriaModelNoticeForm_means_the_wrong_file_the_wrong_varia_6ddb6cc2") +
              L.Get("BriaModelNoticeForm_Replace_it_from_BRIA_s_official_Hugg_00e5d3db") +
              L.Get("BriaModelNoticeForm_current_file_anyway_at_your_own_risk_b0e8c763")
            : L.Get("BriaModelNoticeForm_The_BRIA_RMBG_2_0_model_is_not_insta_770eb681") +
              L.Get("BriaModelNoticeForm_Download_it_manually_from_BRIA_s_off_526fdd5a") +
              L.Get("BriaModelNoticeForm_documentation_and_licensing_place_th_0c434a89");

        // The shared action bar measures its controls; remove an unavailable action
        // rather than reserving an empty button slot or wrapped row for it.
        if (status == BriaModelStatus.Mismatch)
        {
            if (!_actions.Controls.Contains(_useAnywayButton)) _actions.Controls.Add(_useAnywayButton);
            _actions.Controls.SetChildIndex(_useAnywayButton, 1);
        }
        else _actions.Controls.Remove(_useAnywayButton);
    }

    private void OnRecheckClick(object? sender, EventArgs e)
    {
        BriaModelStatus result;
        try
        {
            result = _recheck();
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"BriaModelNoticeForm: re-check failed. {ex.Message}");
            _statusLabel.ForeColor = FrameShiftTheme.TextSecondary;
            _statusLabel.Text = L.Get("BriaModelNoticeForm_Could_not_verify_the_model_file_Plea_babcac83");
            return;
        }

        if (result == BriaModelStatus.Valid)
        {
            Proceed = true;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        ApplyStatus(result);
        _statusLabel.ForeColor = FrameShiftTheme.TextSecondary;
        _statusLabel.Text = result == BriaModelStatus.Mismatch
            ? L.Get("BriaModelNoticeForm_A_file_was_found_but_it_does_not_mat_1238fd3f")
            : L.Get("BriaModelNoticeForm_The_model_is_still_not_present_in_th_0a3c5b69");
    }

    private void OnOpenPageClick(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_infoPageUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"BriaModelNoticeForm: failed to open BRIA page. {ex.Message}");
            MessageBox.Show(
                this,
                L.Get("BriaModelNoticeForm_Could_not_open_the_browser_Please_vi_efb24b4e") + _infoPageUrl,
                "FrameShift",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private void OnOpenFolderClick(object? sender, EventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_modelFolder);
            Process.Start(new ProcessStartInfo(_modelFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"BriaModelNoticeForm: failed to open model folder. {ex.Message}");
            MessageBox.Show(
                this,
                L.Get("BriaModelNoticeForm_Could_not_open_the_folder_20ccd421") + _modelFolder,
                "FrameShift",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    protected override void Dispose(bool disposing)
    {
        // It can be detached from the footer in the Missing state.
        if (disposing) _useAnywayButton.Dispose();
        base.Dispose(disposing);
    }
}
