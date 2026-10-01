using System.Net.Http;
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
    private readonly TextBox _errorDetails;
    private readonly TableLayoutPanel _errorSection;
    private readonly TableLayoutPanel _root;
    private readonly Button _downloadButton;
    private readonly Button _cancelButton;
    private CancellationTokenSource? _cancellationSource;
    private Task? _downloadTask;
    private bool _downloadInProgress;
    private bool _closingRequested;

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
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(640, 380), new Size(400, 300));
        FrameShiftWindowChrome.Apply(this, "FrameShift AI - Download model", IconPaths.FrameShiftAiIcon, IconPaths.AppIcon);
        var header = FrameShiftUiFactory.CreateHeader(featureTitle, featureSubtitle,
            preferredIconPath, IconPaths.FrameShiftAiIcon, "AI");
        var info = FrameShiftUiFactory.CreateSection("Model", FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateWrappingLabel($"{modelDisplayName} — {modelSizeBytes / (1024L * 1024L)} MB"),
            FrameShiftUiFactory.CreateWrappingLabel($"License: {modelLicense}")));
        _statusLabel = FrameShiftUiFactory.CreateWrappingLabel($"Ready to download — approximately {modelSizeBytes / (1024L * 1024L)} MB");
        _progressBar = new ProgressBar { Name = "downloadProgress", Dock = DockStyle.Top, Maximum = 100, Style = ProgressBarStyle.Continuous };
        _progressTextLabel = FrameShiftUiFactory.CreateWrappingLabel("");
        var activity = FrameShiftUiFactory.CreateSection("Download",
            FrameShiftUiFactory.CreateVerticalStack(_statusLabel, _progressBar, _progressTextLabel));
        _errorDetails = new TextBox
        {
            Name = "errorDetails", Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None, BackColor = FrameShiftTheme.Surface, ForeColor = FrameShiftTheme.TextPrimary,
            AccessibleName = "Download error details", Dock = DockStyle.Top
        };
        void Metrics()
        {
            _progressBar.Height = FrameShiftUiMetrics.ToPixels(_progressBar, 20);
            _errorDetails.MinimumSize = new Size(0, Math.Max(FrameShiftUiMetrics.ToPixels(_errorDetails, 140), _errorDetails.Font.Height * 7));
            _errorDetails.Height = _errorDetails.MinimumSize.Height;
        }
        _errorDetails.HandleCreated += (_, _) => Metrics();
        _errorDetails.FontChanged += (_, _) => Metrics();
        _errorDetails.DpiChangedAfterParent += (_, _) => Metrics();
        Metrics();
        _errorSection = FrameShiftUiFactory.CreateSection("Error details — select text to copy", _errorDetails);
        _errorSection.Visible = false;
        _cancelButton = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        _downloadButton = FrameShiftUiFactory.CreateMeasuredActionButton("Download", true);
        _downloadButton.Click += async (_, _) => await StartDownloadAsync();
        _cancelButton.Click += OnCancelClick;
        AcceptButton = _downloadButton;
        CancelButton = _cancelButton;
        _root = FrameShiftDialogLayout.Create(header, FrameShiftUiFactory.CreateVerticalStack(info, activity, _errorSection),
            FrameShiftDialogLayout.CreateActions(_cancelButton, _downloadButton));
        Controls.Add(_root);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, _root);
        FormClosing += OnFormClosing;
        ResumeLayout(true);
    }

    // Also used by hidden tests with an injected download action; never starts on opening.
    internal Task StartDownloadAsync()
    {
        if (_downloadInProgress || _closingRequested || IsDisposed) return _downloadTask ?? Task.CompletedTask;
        _downloadInProgress = true;
        _downloadButton.Enabled = false;
        _cancelButton.Enabled = true;
        _cancelButton.Text = "Cancel";
        _errorSection.Visible = false;
        _errorDetails.Clear();
        _progressBar.Value = 0;
        _statusLabel.Text = "Starting download…";
        _cancellationSource = new CancellationTokenSource();
        _downloadTask = RunDownloadAsync(_cancellationSource);
        return _downloadTask;
    }

    private async Task RunDownloadAsync(CancellationTokenSource source)
    {
        try
        {
            var progress = new Progress<AiModelDownloadProgress>(p =>
            {
                if (ReferenceEquals(source, _cancellationSource) && !source.IsCancellationRequested
                    && _downloadInProgress && !_closingRequested && !IsDisposed && !Disposing)
                    OnProgressReport(p);
            });
            await _downloadAction(progress, source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (IsDisposed || Disposing || _closingRequested) return;
            _progressBar.Value = 100;
            _statusLabel.Text = "Download complete.";
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && !Disposing && !_closingRequested)
            {
                _progressBar.Value = 0;
                _progressTextLabel.Text = "";
                _statusLabel.Text = "Download cancelled.";
                _cancelButton.Text = "Close";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic("DownloadModelForm: download failed. " + ex);
            if (!IsDisposed && !Disposing && !_closingRequested)
            {
                _statusLabel.Text = ex is InvalidDataException ? "Model verification failed."
                    : ex is HttpRequestException ? "Network error — check your connection and try again."
                    : "Download failed — see details below.";
                _errorDetails.Text = ex.ToString();
                _errorDetails.SelectionStart = 0;
                _errorDetails.SelectionLength = 0;
                _errorSection.Visible = true;
                _cancelButton.Text = "Close";
                if (WindowState == FormWindowState.Normal) FrameShiftDialogLayout.FitInitialHeight(this, _root);
            }
        }
        finally
        {
            _downloadInProgress = false;
            source.Dispose();
            if (ReferenceEquals(source, _cancellationSource)) _cancellationSource = null;
            if (!IsDisposed && !Disposing)
            {
                _downloadButton.Enabled = !_closingRequested;
                _cancelButton.Enabled = !_closingRequested;
                if (_closingRequested)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            }
        }
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        if (_downloadInProgress) RequestCancellation();
        else { DialogResult = DialogResult.Cancel; Close(); }
    }

    private void RequestCancellation()
    {
        _cancelButton.Enabled = false;
        _statusLabel.Text = _closingRequested ? "Closing — waiting for download cleanup…" : "Cancelling…";
        _cancellationSource?.Cancel();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_downloadInProgress) return;
        // A completed successful download is allowed to close. All other routes wait for cleanup.
        if (DialogResult == DialogResult.OK && !_closingRequested) return;
        e.Cancel = true;
        _closingRequested = true;
        RequestCancellation();
    }

    private void OnProgressReport(AiModelDownloadProgress progress)
    {
        _progressBar.Value = Math.Clamp(progress.Percent, 0, 100);
        _progressTextLabel.Text = progress.Status;
        if (progress.Percent > 0) _statusLabel.Text = $"Downloading… {progress.Percent}%";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _closingRequested = true; _cancellationSource?.Cancel(); }
        base.Dispose(disposing);
    }
}