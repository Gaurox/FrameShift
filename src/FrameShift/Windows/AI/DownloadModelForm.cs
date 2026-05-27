using System;
using System.Drawing;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.AI;
using FrameShift.Core.Logging;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.AI;

public sealed class DownloadModelForm : Form
{
    private readonly Func<IProgress<AiModelDownloadProgress>, CancellationToken, Task> _downloadAction;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly Label _progressTextLabel;
    private readonly Button _downloadButton;
    private readonly Button _cancelButton;
    private CancellationTokenSource? _cancellationSource;
    private bool _downloadInProgress;

    public DownloadModelForm(
        string featureTitle,
        string featureSubtitle,
        string preferredIconPath,
        string modelDisplayName,
        string modelLicense,
        long modelSizeBytes,
        Func<IProgress<AiModelDownloadProgress>, CancellationToken, Task> downloadAction)
    {
        _downloadAction = downloadAction;

        FrameShiftWindowChrome.Apply(this, "FrameShift AI - Download model", IconPaths.FrameShiftAiIcon, IconPaths.AppIcon);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 300);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var header = FrameShiftUiFactory.CreateFixedHeader(
            featureTitle,
            featureSubtitle,
            preferredIconPath,
            IconPaths.FrameShiftAiIcon,
            "AI");
        Controls.Add(header);

        var infoCard = FrameShiftUiFactory.CreateFixedInfoCard(
            new Point(12, 82),
            new Size(536, 52));
        infoCard.Controls.Add(new Label
        {
            Location = new Point(10, 8),
            Size = new Size(516, 18),
            Text = $"Model: {modelDisplayName}  —  {modelSizeBytes / (1024L * 1024L)} MB",
            ForeColor = FrameShiftTheme.TextSecondary
        });
        infoCard.Controls.Add(new Label
        {
            Location = new Point(10, 26),
            Size = new Size(516, 18),
            Text = $"License: {modelLicense}",
            ForeColor = FrameShiftTheme.TextMuted
        });
        Controls.Add(infoCard);

        _statusLabel = new Label
        {
            Location = new Point(12, 150),
            Size = new Size(536, 20),
            Text = $"Ready to download — approximately {modelSizeBytes / (1024L * 1024L)} MB",
            ForeColor = FrameShiftTheme.TextSecondary
        };
        Controls.Add(_statusLabel);

        _progressBar = new ProgressBar
        {
            Location = new Point(12, 178),
            Size = new Size(536, 22),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous
        };
        Controls.Add(_progressBar);

        _progressTextLabel = new Label
        {
            Location = new Point(12, 208),
            Size = new Size(536, 18),
            Text = string.Empty,
            ForeColor = FrameShiftTheme.TextMuted
        };
        Controls.Add(_progressTextLabel);

        _cancelButton = FrameShiftUiFactory.CreateFixedActionButton(
            "Cancel",
            new Point(278, 254),
            new Size(120, 34),
            primary: false);
        Controls.Add(_cancelButton);

        _downloadButton = FrameShiftUiFactory.CreateFixedActionButton(
            "Download",
            new Point(408, 254),
            new Size(140, 34),
            primary: true);
        Controls.Add(_downloadButton);

        _downloadButton.Click += OnDownloadClick;
        _cancelButton.Click += OnCancelClick;
        FormClosing += OnFormClosing;
    }

    private async void OnDownloadClick(object? sender, EventArgs e)
    {
        _downloadButton.Enabled = false;
        _downloadInProgress = true;
        _cancellationSource = new CancellationTokenSource();

        try
        {
            var progress = new Progress<AiModelDownloadProgress>(OnProgressReport);
            await _downloadAction(progress, _cancellationSource.Token);

            if (IsDisposed || Disposing)
                return;

            _progressBar.Value = 100;
            _statusLabel.Text = "Download complete.";
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && !Disposing)
            {
                _progressBar.Value = 0;
                _progressTextLabel.Text = string.Empty;
                _statusLabel.Text = "Download cancelled.";
                _downloadButton.Enabled = true;
                _cancelButton.Enabled = true;
                _cancelButton.Text = "Close";
            }
        }
        catch (InvalidDataException ex)
        {
            if (!IsDisposed && !Disposing)
            {
                _statusLabel.Text = ex.Message;
                _downloadButton.Enabled = true;
                AppLogger.LogStatic("DownloadModelForm: integrity error. " + ex);
            }
        }
        catch (HttpRequestException ex)
        {
            if (!IsDisposed && !Disposing)
            {
                _statusLabel.Text = "Network error — check your connection and try again.";
                _downloadButton.Enabled = true;
                AppLogger.LogStatic("DownloadModelForm: network error. " + ex);
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing)
            {
                _statusLabel.Text = "Download failed — check your connection and try again.";
                _downloadButton.Enabled = true;
                AppLogger.LogStatic("DownloadModelForm: unexpected error. " + ex);
            }
        }
        finally
        {
            _downloadInProgress = false;
            _cancellationSource?.Dispose();
            _cancellationSource = null;
        }
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        if (_downloadInProgress && _cancellationSource != null)
        {
            _cancelButton.Enabled = false;
            _cancellationSource.Cancel();
        }
        else
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _cancellationSource?.Cancel();
    }

    private void OnProgressReport(AiModelDownloadProgress p)
    {
        if (IsDisposed || Disposing)
            return;

        _progressBar.Value = Math.Clamp(p.Percent, 0, 100);
        _progressTextLabel.Text = p.Status;

        if (p.Percent > 0)
            _statusLabel.Text = $"Downloading... {p.Percent}%";
    }
}
