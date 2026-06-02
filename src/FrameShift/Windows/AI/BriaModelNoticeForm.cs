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
    private const int ButtonWidth = 116;
    private const int ButtonHeight = 34;
    private const int ButtonGap = 10;
    private const int ButtonBottomMargin = 18;

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

        FrameShiftWindowChrome.Apply(
            this,
            "FrameShift AI - Remove Background (BRIA)",
            IconPaths.RemoveBackgroundAiIcon,
            IconPaths.AppIcon);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(652, 392);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var usableWidth = ClientSize.Width - (2 * FrameShiftUiMetrics.OuterPadding);

        var header = FrameShiftUiFactory.CreateFixedHeader(
            "Remove Background (BRIA)",
            "Manual model installation required — not included with FrameShift",
            IconPaths.RemoveBackgroundAiIcon,
            IconPaths.FrameShiftAiIcon,
            "AI");
        header.Width = usableWidth;
        Controls.Add(header);

        _messageLabel = new Label
        {
            Location = new Point(FrameShiftUiMetrics.OuterPadding, 82),
            Size = new Size(usableWidth, 96),
            ForeColor = FrameShiftTheme.TextSecondary
        };
        Controls.Add(_messageLabel);

        var infoCard = FrameShiftUiFactory.CreateFixedInfoCard(
            new Point(FrameShiftUiMetrics.OuterPadding, 184),
            new Size(usableWidth, 70));
        infoCard.Controls.Add(new Label
        {
            Location = new Point(10, 8),
            Size = new Size(usableWidth - 20, 18),
            Text = $"Expected file: {expectedFileName}   (approx. {approxSizeText})",
            ForeColor = FrameShiftTheme.TextSecondary
        });
        infoCard.Controls.Add(new Label
        {
            Location = new Point(10, 28),
            Size = new Size(usableWidth - 20, 34),
            Text = $"Target folder: {modelFolder}",
            ForeColor = FrameShiftTheme.TextMuted
        });
        Controls.Add(infoCard);

        _statusLabel = new Label
        {
            Location = new Point(FrameShiftUiMetrics.OuterPadding, 262),
            Size = new Size(usableWidth, 36),
            Text = string.Empty,
            ForeColor = FrameShiftTheme.TextSecondary
        };
        Controls.Add(_statusLabel);

        _openPageButton = FrameShiftUiFactory.CreateFixedActionButton(
            "Open BRIA page", Point.Empty, new Size(ButtonWidth, ButtonHeight), primary: true);
        _openPageButton.Click += OnOpenPageClick;
        Controls.Add(_openPageButton);

        _openFolderButton = FrameShiftUiFactory.CreateFixedActionButton(
            "Open folder", Point.Empty, new Size(ButtonWidth, ButtonHeight), primary: false);
        _openFolderButton.Click += OnOpenFolderClick;
        Controls.Add(_openFolderButton);

        _recheckButton = FrameShiftUiFactory.CreateFixedActionButton(
            "Re-check", Point.Empty, new Size(ButtonWidth, ButtonHeight), primary: false);
        _recheckButton.Click += OnRecheckClick;
        Controls.Add(_recheckButton);

        _useAnywayButton = FrameShiftUiFactory.CreateFixedActionButton(
            "Use anyway", Point.Empty, new Size(ButtonWidth, ButtonHeight), primary: false);
        _useAnywayButton.Click += (_, _) =>
        {
            Proceed = true;
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(_useAnywayButton);

        _cancelButton = FrameShiftUiFactory.CreateFixedActionButton(
            "Cancel", Point.Empty, new Size(ButtonWidth, ButtonHeight), primary: false);
        _cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        Controls.Add(_cancelButton);
        CancelButton = _cancelButton;

        ApplyStatus(_status);
    }

    private void ApplyStatus(BriaModelStatus status)
    {
        _status = status;

        _messageLabel.Text = status == BriaModelStatus.Mismatch
            ? "The installed file does not match the expected BRIA RMBG-2.0 model. This usually " +
              "means the wrong file, the wrong variant, an older version, or a corrupted download. " +
              "Replace it from BRIA's official Hugging Face page, then click Re-check — or use the " +
              "current file anyway at your own risk."
            : "The BRIA RMBG-2.0 model is not installed. FrameShift does not distribute this model. " +
              "Download it manually from BRIA's official Hugging Face page, review BRIA's " +
              "documentation and licensing, place the file in the folder below, then click Re-check.";

        _useAnywayButton.Visible = status == BriaModelStatus.Mismatch;
        LayoutButtons();
    }

    private void LayoutButtons()
    {
        var visible = new List<Button> { _openPageButton, _openFolderButton, _recheckButton };
        if (_status == BriaModelStatus.Mismatch)
        {
            visible.Add(_useAnywayButton);
        }
        visible.Add(_cancelButton);

        var count = visible.Count;
        var totalWidth = (count * ButtonWidth) + ((count - 1) * ButtonGap);
        var startX = (ClientSize.Width - totalWidth) / 2;
        var y = ClientSize.Height - ButtonHeight - ButtonBottomMargin;

        for (var i = 0; i < count; i++)
        {
            visible[i].SetBounds(startX + (i * (ButtonWidth + ButtonGap)), y, ButtonWidth, ButtonHeight);
        }
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
            _statusLabel.Text = "Could not verify the model file. Please try again.";
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
            ? "A file was found, but it does not match the expected BRIA model."
            : "The model is still not present in the folder above. Place the file, then re-check.";
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
                "Could not open the browser. Please visit:\r\n" + _infoPageUrl,
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
                "Could not open the folder:\r\n" + _modelFolder,
                "FrameShift",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
