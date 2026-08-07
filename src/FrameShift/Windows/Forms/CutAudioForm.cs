using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class CutAudioForm : Form
{
    private const int StandardToolButtonWidth = 130;
    private const int StandardWideToolButtonWidth = 146;

    private readonly string _inputPath;
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly CutAudioEditingService _editingService;
    private readonly CutAudioFormLifetime _lifetime = new();
    private readonly Panel _wavePanel;
    private readonly TextBox _textStart;
    private readonly TextBox _textEnd;
    private readonly Label _labelSelection;
    private readonly Label _labelTimeline;
    private readonly Label _statusLabel;
    private readonly Button _buttonPlay;
    private readonly Button _buttonStop;
    private readonly Button _buttonRemove;
    private readonly Button _buttonSilence;
    private readonly Button _buttonCut;
    private readonly Button _buttonCancel;

    private string _temporaryRootPath;
    private string _workingFilePath;
    private double _currentDurationSeconds;
    private double[] _waveformPoints = Array.Empty<double>();
    private double _startSeconds;
    private double _endSeconds;
    private bool _dragActive;
    private string _dragTarget = string.Empty;
    private int _removeCount;
    private double _totalRemovedSeconds;
    private bool _busy;
    private bool _workspaceReady;
    private bool _initializationStarted;
    private bool _closingRequested;
    private bool _allowClose;
    private bool _temporaryRootCleanupCompleted;
    private bool _temporaryRootCleanupBlocked;
    private bool _preserveWorkingFilesForExecution;
    private Task? _closingTask;
    private DialogResult? _requestedDialogResult;
    private SoundPlayer? _previewPlayer;
    private string? _previewPath;

    public CutAudioForm(
        string inputPath,
        string ffmpegPath,
        string ffprobePath,
        double durationSeconds,
        FfmpegRunner ffmpegRunner,
        FfprobeRunner ffprobeRunner)
    {
        _inputPath = inputPath;
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath;
        _ffprobeRunner = ffprobeRunner;
        _editingService = new CutAudioEditingService(ffmpegRunner);
        _currentDurationSeconds = durationSeconds;
        _temporaryRootPath = CutAudioEditingService.CreateTemporaryRoot();
        _workingFilePath = string.Empty;

        SuspendLayout();

        FrameShiftWindowChrome.Apply(this, "FrameShift - Cut Audio");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(960, 540);
        MinimumSize = new Size(960, 540);
        MaximumSize = new Size(960, 540);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        ControlHelper.SetDoubleBuffered(this);

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(FrameShiftUiMetrics.OuterPadding),
            ColumnCount = 1,
            RowCount = 9,
            Margin = Padding.Empty
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.HeaderHeight));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.FooterButtonHeight));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.FooterButtonHeight));

        var headerPanel = CreateHeaderPanel();

        var editorSection = CreateSectionPanel("Selection", out var editorContentHost);
        var editorLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 3
        };
        editorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 214F));
        editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.LineGap));
        editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));

        _wavePanel = CreateFramedPanel(FrameShiftTheme.Surface, FrameShiftTheme.PrimaryBlue, 8);
        _wavePanel.Dock = DockStyle.Fill;
        _wavePanel.Margin = Padding.Empty;
        ControlHelper.SetDoubleBuffered(_wavePanel);
        _wavePanel.Paint += WavePanelOnPaint;
        _wavePanel.MouseDown += WavePanelOnMouseDown;
        _wavePanel.MouseMove += WavePanelOnMouseMove;
        _wavePanel.MouseUp += WavePanelOnMouseUp;

        var fieldsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 7,
            RowCount = 2
        };
        fieldsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
        fieldsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152F));
        fieldsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        fieldsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
        fieldsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152F));
        fieldsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FrameShiftUiMetrics.OuterPadding));
        fieldsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        fieldsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        fieldsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));

        var startLabel = FrameShiftUiFactory.CreateFieldLabel("Start");
        var endLabel = FrameShiftUiFactory.CreateFieldLabel("End");
        _labelSelection = FrameShiftUiFactory.CreateInfoValueLabel(ContentAlignment.MiddleLeft);
        _labelSelection.ForeColor = FrameShiftTheme.TextPrimary;
        _labelTimeline = FrameShiftUiFactory.CreateInfoValueLabel(ContentAlignment.MiddleLeft);
        _labelTimeline.ForeColor = FrameShiftTheme.TextPrimary;

        _textStart = CreateValueTextBox();
        _textStart.Leave += (_, _) => ApplyBoundaryFromText("start");

        _textEnd = CreateValueTextBox();
        _textEnd.Leave += (_, _) => ApplyBoundaryFromText("end");

        var startHost = FrameShiftUiFactory.CreateTextInputHost(_textStart);
        var endHost = FrameShiftUiFactory.CreateTextInputHost(_textEnd);

        fieldsLayout.Controls.Add(startLabel, 0, 0);
        fieldsLayout.Controls.Add(startHost, 1, 0);
        fieldsLayout.Controls.Add(endLabel, 3, 0);
        fieldsLayout.Controls.Add(endHost, 4, 0);
        fieldsLayout.Controls.Add(_labelSelection, 6, 0);
        fieldsLayout.Controls.Add(_labelTimeline, 0, 1);
        fieldsLayout.SetColumnSpan(_labelTimeline, 7);

        var toolsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        _buttonPlay = CreateActionButton("Play selection", primary: false, StandardToolButtonWidth);
        _buttonPlay.Click += async (_, _) => await StartOperationAsync(PlaySelectionAsync).ConfigureAwait(true);

        _buttonStop = CreateActionButton("Stop", primary: false, 92);
        _buttonStop.Click += (_, _) => StopPreviewPlayer();

        _buttonRemove = CreateActionButton("Remove selection", primary: false, StandardWideToolButtonWidth);
        _buttonRemove.Click += async (_, _) => await StartOperationAsync(RemoveSelectionAsync).ConfigureAwait(true);

        _buttonSilence = CreateActionButton("Silence selection", primary: false, StandardWideToolButtonWidth);
        _buttonSilence.Click += async (_, _) => await StartOperationAsync(SilenceSelectionAsync).ConfigureAwait(true);

        toolsPanel.Controls.AddRange([_buttonPlay, _buttonStop, _buttonRemove, _buttonSilence]);
        toolsPanel.Resize += (_, _) => LayoutToolButtons(toolsPanel, _buttonPlay, _buttonStop, _buttonRemove, _buttonSilence);

        editorLayout.Controls.Add(_wavePanel, 0, 0);
        editorLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
        editorLayout.Controls.Add(fieldsLayout, 0, 2);
        editorContentHost.Controls.Add(editorLayout);

        var helpCard = CreateInfoCardPanel();
        helpCard.Margin = Padding.Empty;
        helpCard.Padding = new Padding(FrameShiftUiMetrics.OuterPadding, FrameShiftUiMetrics.LineGap, FrameShiftUiMetrics.OuterPadding, FrameShiftUiMetrics.LineGap);

        var helpLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 2
        };
        helpLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        helpLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        helpLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ForeColor = FrameShiftTheme.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Text = "Preparing waveform..."
        };

        var hintLabel = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Text = "Drag the start and end cursors or type exact times. Output stays next to the source with unique naming."
        };

        helpLayout.Controls.Add(_statusLabel, 0, 0);
        helpLayout.Controls.Add(hintLabel, 0, 1);
        helpCard.Controls.Add(helpLayout);

        _buttonCut = CreateActionButton("Cut", primary: true, FrameShiftUiMetrics.PrimaryButtonWidth);
        _buttonCut.Click += (_, _) => ConfirmCut();

        _buttonCancel = CreateActionButton("Cancel", primary: false, FrameShiftUiMetrics.SecondaryButtonWidth);
        _buttonCancel.Click += (_, _) => RequestClose(DialogResult.Cancel);

        var footerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        footerPanel.Controls.Add(_buttonCancel);
        footerPanel.Controls.Add(_buttonCut);
        footerPanel.Resize += (_, _) => UpdateFooterButtonLayout(footerPanel, _buttonCancel, _buttonCut);

        rootLayout.Controls.Add(headerPanel, 0, 0);
        rootLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
        rootLayout.Controls.Add(editorSection, 0, 2);
        rootLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 3);
        rootLayout.Controls.Add(toolsPanel, 0, 4);
        rootLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 5);
        rootLayout.Controls.Add(helpCard, 0, 6);
        rootLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 7);
        rootLayout.Controls.Add(footerPanel, 0, 8);

        Controls.Add(rootLayout);

        AcceptButton = _buttonCut;
        CancelButton = _buttonCancel;

        FormClosing += CutAudioFormOnFormClosing;
        FormClosed += (_, _) => _lifetime.Dispose();

        Shown += async (_, _) =>
        {
            LayoutToolButtons(toolsPanel, _buttonPlay, _buttonStop, _buttonRemove, _buttonSilence);
            UpdateFooterButtonLayout(footerPanel, _buttonCancel, _buttonCut);
            _initializationStarted = true;
            await StartOperationAsync(InitializeWorkspaceAsync).ConfigureAwait(true);
        };

        SetBusyState(true, "Preparing audio workspace...");
        ResumeLayout(true);
    }

    public CutAudioSettings? Selection { get; private set; }

    public string? WorkingFilePath => _preserveWorkingFilesForExecution ? _workingFilePath : null;

    public string? TemporaryRootPath => _preserveWorkingFilesForExecution ? _temporaryRootPath : null;

    internal bool IsWorkspaceReadyForTesting => _workspaceReady;

    internal bool IsInitializationStartedForTesting => _initializationStarted;

    internal string TemporaryRootPathForTesting => _temporaryRootPath;

    private bool CanUpdateUi => !_closingRequested && !IsDisposed && !Disposing;

    private async Task StartOperationAsync(Func<CancellationToken, Task> operation)
    {
        try
        {
            await _lifetime.RunAsync(operation).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsClosing || _lifetime.Token.IsCancellationRequested)
        {
        }
    }

    private void RequestClose(DialogResult requestedDialogResult)
    {
        _requestedDialogResult = requestedDialogResult;
        Close();
    }

    private void CutAudioFormOnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_closingTask is not null)
        {
            return;
        }

        _closingRequested = true;
        SetBusyState(true, "Closing: stopping audio processing...");
        StopPreviewPlayer();
        _closingTask = CompleteCloseAsync();
    }

    private async Task CompleteCloseAsync()
    {
        await _lifetime.BeginClosingAsync(
            CleanupTemporaryRootAfterWorkAsync,
            ex => Core.Logging.AppLogger.LogStatic($"CutAudioForm: close cleanup failed. {ex}")).ConfigureAwait(true);

        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        _allowClose = true;
        if (_requestedDialogResult is not null)
        {
            DialogResult = _requestedDialogResult.Value;
        }

        Close();
    }

    private Task CleanupTemporaryRootAfterWorkAsync()
    {
        if (_preserveWorkingFilesForExecution || _temporaryRootCleanupCompleted || _temporaryRootCleanupBlocked)
        {
            return Task.CompletedTask;
        }

        _temporaryRootCleanupCompleted = true;
        try
        {
            if (Directory.Exists(_temporaryRootPath))
            {
                Directory.Delete(_temporaryRootPath, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Core.Logging.AppLogger.LogStatic($"CutAudioForm: temporary root cleanup deferred because processing may not have stopped cleanly. path={_temporaryRootPath}, error={ex}");
        }

        return Task.CompletedTask;
    }

    private void PreserveTemporaryRootBecauseTerminationWasNotConfirmed(Exception exception)
    {
        _temporaryRootCleanupBlocked = true;
        Core.Logging.AppLogger.LogStatic(
            $"CutAudioForm: temporary root retained because FFmpeg or FFprobe termination was not confirmed. path={_temporaryRootPath}, error={exception}");
    }

    private async Task InitializeWorkspaceAsync(CancellationToken cancellationToken)
    {
        string? workingFilePath = null;
        try
        {
            SetBusyState(true, "Preparing editable audio workspace...");
            workingFilePath = await _editingService.CreateEditableWorkingCopyAsync(
                _ffmpegPath,
                _inputPath,
                _temporaryRootPath,
                cancellationToken).ConfigureAwait(true);

            var waveformPoints = await _editingService.GenerateWaveformPointsAsync(
                _ffmpegPath,
                workingFilePath,
                _temporaryRootPath,
                Math.Max(1, _wavePanel.ClientSize.Width),
                cancellationToken).ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();
            if (!CanUpdateUi)
            {
                return;
            }

            _workingFilePath = workingFilePath;
            _waveformPoints = waveformPoints;
            SetSelectionToFullRange();
            _workspaceReady = true;
            RefreshSelectionUi(true);
            _statusLabel.Text = "Waveform ready. Select the area to keep, preview, remove, or silence.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _closingRequested)
        {
        }
        catch (TimeoutException ex) when (_closingRequested)
        {
            PreserveTemporaryRootBecauseTerminationWasNotConfirmed(ex);
        }
        catch (Exception ex)
        {
            if (CanUpdateUi)
            {
                _statusLabel.Text = "Audio preparation failed. You can close this window.";
                ShowActionError(ex.Message);
            }
        }
        finally
        {
            if (CanUpdateUi)
            {
                SetBusyState(false, _workspaceReady
                    ? "Waveform ready. Select the area to keep, preview, remove, or silence."
                    : "Audio preparation failed. You can close this window.");
            }
        }
    }

    private async Task PlaySelectionAsync(CancellationToken cancellationToken)
    {
        if (_busy || !_workspaceReady || _closingRequested)
        {
            return;
        }

        string? previewPath = null;
        try
        {
            ApplyBoundaryFromText("start");
            ApplyBoundaryFromText("end");
            SetBusyState(true, "Preparing preview...");

            previewPath = await _editingService.CreatePreviewAsync(
                _ffmpegPath,
                _workingFilePath,
                _temporaryRootPath,
                _startSeconds,
                _endSeconds,
                cancellationToken).ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();
            if (!CanUpdateUi)
            {
                ConversionActionHelper.DeleteIfExists(previewPath);
                return;
            }

            StopPreviewPlayer();
            _previewPath = previewPath;
            previewPath = null;
            _previewPlayer = new SoundPlayer(_previewPath);
            _previewPlayer.LoadAsync();
            _previewPlayer.Play();
            _statusLabel.Text = "Playing selection preview...";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _closingRequested)
        {
            ConversionActionHelper.DeleteIfExists(previewPath ?? string.Empty);
        }
        catch (TimeoutException ex) when (_closingRequested)
        {
            PreserveTemporaryRootBecauseTerminationWasNotConfirmed(ex);
        }
        catch (Exception ex)
        {
            ConversionActionHelper.DeleteIfExists(previewPath ?? string.Empty);
            if (CanUpdateUi)
            {
                ShowActionError(ex.Message);
            }
        }
        finally
        {
            if (CanUpdateUi)
            {
                SetBusyState(false, "Waveform ready. Select the area to keep, preview, remove, or silence.");
            }
        }
    }

    private async Task RemoveSelectionAsync(CancellationToken cancellationToken)
    {
        if (_busy || !_workspaceReady || _closingRequested)
        {
            return;
        }

        string? newWorkingFile = null;
        try
        {
            ApplyBoundaryFromText("start");
            ApplyBoundaryFromText("end");

            var removeDuration = _endSeconds - _startSeconds;
            if (removeDuration <= 0.05d)
            {
                throw new InvalidOperationException("Selection is too short to remove.");
            }

            if ((_currentDurationSeconds - removeDuration) <= 0.05d)
            {
                throw new InvalidOperationException("Remove selection cannot delete the entire audio.");
            }

            SetBusyState(true, "Removing selection...");
            StopPreviewPlayer();

            var oldWorkingFile = _workingFilePath;
            newWorkingFile = await _editingService.RemoveSelectionAsync(
                _ffmpegPath,
                oldWorkingFile,
                _temporaryRootPath,
                _startSeconds,
                _endSeconds,
                _currentDurationSeconds,
                cancellationToken).ConfigureAwait(true);

            var newDuration = await ProbeDurationSecondsAsync(newWorkingFile, cancellationToken).ConfigureAwait(true);
            var newWaveform = await _editingService.GenerateWaveformPointsAsync(
                _ffmpegPath,
                newWorkingFile,
                _temporaryRootPath,
                Math.Max(1, _wavePanel.ClientSize.Width),
                cancellationToken).ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();
            if (!CanUpdateUi)
            {
                ConversionActionHelper.DeleteIfExists(newWorkingFile);
                return;
            }

            _workingFilePath = newWorkingFile;
            newWorkingFile = null;
            _currentDurationSeconds = newDuration;
            _waveformPoints = newWaveform;
            _removeCount++;
            _totalRemovedSeconds += removeDuration;

            ConversionActionHelper.DeleteIfExists(oldWorkingFile);
            SetSelectionToFullRange();
            RefreshSelectionUi(true);
            _statusLabel.Text = "Selection removed.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _closingRequested)
        {
            ConversionActionHelper.DeleteIfExists(newWorkingFile ?? string.Empty);
        }
        catch (TimeoutException ex) when (_closingRequested)
        {
            PreserveTemporaryRootBecauseTerminationWasNotConfirmed(ex);
        }
        catch (Exception ex)
        {
            ConversionActionHelper.DeleteIfExists(newWorkingFile ?? string.Empty);
            if (CanUpdateUi)
            {
                ShowActionError(ex.Message);
            }
        }
        finally
        {
            if (CanUpdateUi)
            {
                SetBusyState(false, "Waveform ready. Select the area to keep, preview, remove, or silence.");
            }
        }
    }

    private async Task SilenceSelectionAsync(CancellationToken cancellationToken)
    {
        if (_busy || !_workspaceReady || _closingRequested)
        {
            return;
        }

        string? newWorkingFile = null;
        try
        {
            ApplyBoundaryFromText("start");
            ApplyBoundaryFromText("end");

            var silenceDuration = _endSeconds - _startSeconds;
            if (silenceDuration <= 0.05d)
            {
                throw new InvalidOperationException("Selection is too short to silence.");
            }

            SetBusyState(true, "Silencing selection...");
            StopPreviewPlayer();

            var oldWorkingFile = _workingFilePath;
            newWorkingFile = await _editingService.SilenceSelectionAsync(
                _ffmpegPath,
                oldWorkingFile,
                _temporaryRootPath,
                _startSeconds,
                _endSeconds,
                _currentDurationSeconds,
                cancellationToken).ConfigureAwait(true);

            var newDuration = await ProbeDurationSecondsAsync(newWorkingFile, cancellationToken).ConfigureAwait(true);
            var newWaveform = await _editingService.GenerateWaveformPointsAsync(
                _ffmpegPath,
                newWorkingFile,
                _temporaryRootPath,
                Math.Max(1, _wavePanel.ClientSize.Width),
                cancellationToken).ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();
            if (!CanUpdateUi)
            {
                ConversionActionHelper.DeleteIfExists(newWorkingFile);
                return;
            }

            _workingFilePath = newWorkingFile;
            newWorkingFile = null;
            _currentDurationSeconds = newDuration;
            _waveformPoints = newWaveform;

            ConversionActionHelper.DeleteIfExists(oldWorkingFile);
            RefreshSelectionUi(true);
            _statusLabel.Text = "Selection silenced.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _closingRequested)
        {
            ConversionActionHelper.DeleteIfExists(newWorkingFile ?? string.Empty);
        }
        catch (TimeoutException ex) when (_closingRequested)
        {
            PreserveTemporaryRootBecauseTerminationWasNotConfirmed(ex);
        }
        catch (Exception ex)
        {
            ConversionActionHelper.DeleteIfExists(newWorkingFile ?? string.Empty);
            if (CanUpdateUi)
            {
                ShowActionError(ex.Message);
            }
        }
        finally
        {
            if (CanUpdateUi)
            {
                SetBusyState(false, "Waveform ready. Select the area to keep, preview, remove, or silence.");
            }
        }
    }

    private void ConfirmCut()
    {
        if (_busy || !_workspaceReady || _closingRequested)
        {
            return;
        }

        try
        {
            ApplyBoundaryFromText("start");
            ApplyBoundaryFromText("end");

            var duration = _endSeconds - _startSeconds;
            if (duration <= 0.05d)
            {
                throw new InvalidOperationException("Selection is too short.");
            }

            StopPreviewPlayer();
            Selection = new CutAudioSettings(_startSeconds, _endSeconds);
            _preserveWorkingFilesForExecution = true;
            RequestClose(DialogResult.OK);
        }
        catch (Exception ex)
        {
            ShowActionError(ex.Message);
        }
    }

    private async Task<double> ProbeDurationSecondsAsync(string filePath, CancellationToken cancellationToken)
    {
        var probeAttempt = await _ffprobeRunner.TryProbeMediaAsync(_ffprobePath, filePath, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (probeAttempt.Probe?.Duration is null || probeAttempt.Probe.Duration.Value.TotalSeconds <= 0)
        {
            throw new InvalidOperationException(probeAttempt.ErrorMessage ?? MediaActionMessages.DurationUnavailable());
        }

        return probeAttempt.Probe.Duration.Value.TotalSeconds;
    }

    private void ApplyBoundaryFromText(string target)
    {
        if (_busy || _closingRequested)
        {
            return;
        }

        try
        {
            if (string.Equals(target, "start", StringComparison.Ordinal))
            {
                if (!CutAudioSettings.TryParseTimeText(_textStart.Text, out var newStart))
                {
                    throw new InvalidOperationException("Start time must use seconds, MM:SS, or HH:MM:SS.");
                }

                if (newStart < 0)
                {
                    newStart = 0;
                }

                if (newStart >= _endSeconds)
                {
                    throw new InvalidOperationException("Start must be before end.");
                }

                if (newStart > _currentDurationSeconds)
                {
                    newStart = _currentDurationSeconds;
                }

                _startSeconds = newStart;
            }
            else
            {
                if (!CutAudioSettings.TryParseTimeText(_textEnd.Text, out var newEnd))
                {
                    throw new InvalidOperationException("End time must use seconds, MM:SS, or HH:MM:SS.");
                }

                if (newEnd > _currentDurationSeconds)
                {
                    newEnd = _currentDurationSeconds;
                }

                if (newEnd <= _startSeconds)
                {
                    throw new InvalidOperationException("End must be after start.");
                }

                _endSeconds = newEnd;
            }

            RefreshSelectionUi(true);
        }
        catch (Exception ex)
        {
            ShowActionError(ex.Message);
            RefreshSelectionUi(true);
        }
    }

    private void SetSelectionToFullRange()
    {
        _startSeconds = 0d;
        _endSeconds = Math.Max(0.01d, _currentDurationSeconds);
        if (_endSeconds > _currentDurationSeconds)
        {
            _endSeconds = _currentDurationSeconds;
        }

        if (_endSeconds <= _startSeconds)
        {
            _endSeconds = Math.Min(_currentDurationSeconds, _startSeconds + 0.01d);
        }
    }

    private void RefreshSelectionUi(bool forceText)
    {
        if (forceText)
        {
            _textStart.Text = CutAudioSettings.FormatDisplayTime(_startSeconds);
            _textEnd.Text = CutAudioSettings.FormatDisplayTime(_endSeconds);
        }

        var selectionDuration = _endSeconds - _startSeconds;
        if (selectionDuration < 0)
        {
            selectionDuration = 0;
        }

        _labelSelection.Text = "Selection: " + CutAudioSettings.FormatDisplayTime(selectionDuration);

        var timelineText = "Current audio: " + CutAudioSettings.FormatDisplayTime(_currentDurationSeconds);
        if (_removeCount > 0)
        {
            timelineText += $"    Removed: {_removeCount} ({CutAudioSettings.FormatDisplayTime(_totalRemovedSeconds)})";
        }

        _labelTimeline.Text = timelineText;
        _wavePanel.Invalidate();
    }

    private void WavePanelOnPaint(object? sender, PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var width = _wavePanel.ClientSize.Width;
        var height = _wavePanel.ClientSize.Height;
        var midY = height / 2;

        graphics.SmoothingMode = SmoothingMode.None;
        graphics.Clear(FrameShiftTheme.Surface);

        using var wavePen = new Pen(FrameShiftTheme.SecondaryBlue, 1);
        using var selectionBrush = new SolidBrush(Color.FromArgb(42, FrameShiftTheme.PrimaryBlue));
        using var cursorStartPen = new Pen(FrameShiftTheme.SecondaryBlue, 2);
        using var cursorEndPen = new Pen(FrameShiftTheme.SecondaryBlue, 2);

        if (_waveformPoints.Length > 0)
        {
            var maxIndex = Math.Min(width, _waveformPoints.Length);
            for (var x = 0; x < maxIndex; x++)
            {
                var amplitude = _waveformPoints[Math.Min(_waveformPoints.Length - 1, x)];
                amplitude = Math.Clamp(amplitude, 0d, 1d);
                var lineHalf = (int)Math.Round(amplitude * ((height - 14) / 2d));
                if (lineHalf < 1)
                {
                    lineHalf = 1;
                }

                graphics.DrawLine(wavePen, x, midY - lineHalf, x, midY + lineHalf);
            }
        }

        var startX = TimeToX(_startSeconds);
        var endX = TimeToX(_endSeconds);
        if (endX < startX)
        {
            (startX, endX) = (endX, startX);
        }

        graphics.FillRectangle(selectionBrush, startX, 1, Math.Max(1, endX - startX), Math.Max(0, height - 2));
        graphics.DrawLine(cursorStartPen, startX, 1, startX, Math.Max(1, height - 2));
        graphics.DrawLine(cursorEndPen, endX, 1, endX, Math.Max(1, height - 2));
    }

    private void WavePanelOnMouseDown(object? sender, MouseEventArgs e)
    {
        if (_busy || e.Button != MouseButtons.Left)
        {
            return;
        }

        var startX = TimeToX(_startSeconds);
        var endX = TimeToX(_endSeconds);
        if (Math.Abs(e.X - startX) <= 8)
        {
            _dragActive = true;
            _dragTarget = "start";
        }
        else if (Math.Abs(e.X - endX) <= 8)
        {
            _dragActive = true;
            _dragTarget = "end";
        }
        else
        {
            var time = XToTime(e.X);
            _dragActive = true;
            _dragTarget = Math.Abs(time - _startSeconds) <= Math.Abs(time - _endSeconds) ? "start" : "end";
        }

        _wavePanel.Capture = true;
    }

    private void WavePanelOnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_busy)
        {
            _wavePanel.Cursor = Cursors.Default;
            return;
        }

        var startX = TimeToX(_startSeconds);
        var endX = TimeToX(_endSeconds);
        if (Math.Abs(e.X - startX) <= 8 || Math.Abs(e.X - endX) <= 8 || _dragActive)
        {
            _wavePanel.Cursor = Cursors.SizeWE;
        }
        else
        {
            _wavePanel.Cursor = Cursors.Default;
        }

        if (!_dragActive)
        {
            return;
        }

        var time = XToTime(e.X);
        if (string.Equals(_dragTarget, "start", StringComparison.Ordinal))
        {
            if (time < 0)
            {
                time = 0;
            }

            if (time > _endSeconds - 0.01d)
            {
                time = _endSeconds - 0.01d;
            }

            _startSeconds = Math.Max(0d, time);
        }
        else if (string.Equals(_dragTarget, "end", StringComparison.Ordinal))
        {
            if (time < _startSeconds + 0.01d)
            {
                time = _startSeconds + 0.01d;
            }

            if (time > _currentDurationSeconds)
            {
                time = _currentDurationSeconds;
            }

            _endSeconds = Math.Min(_currentDurationSeconds, time);
        }

        RefreshSelectionUi(true);
    }

    private void WavePanelOnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragActive = false;
        _dragTarget = string.Empty;
        _wavePanel.Capture = false;
    }

    private int TimeToX(double seconds)
    {
        var usableWidth = Math.Max(1, _wavePanel.ClientSize.Width - 1);
        if (_currentDurationSeconds <= 0)
        {
            return 0;
        }

        return (int)Math.Round((seconds / _currentDurationSeconds) * usableWidth);
    }

    private double XToTime(int x)
    {
        var usableWidth = Math.Max(1, _wavePanel.ClientSize.Width - 1);
        var clampedX = Math.Max(0, Math.Min(usableWidth, x));
        if (_currentDurationSeconds <= 0)
        {
            return 0d;
        }

        return (clampedX / (double)usableWidth) * _currentDurationSeconds;
    }

    private void StopPreviewPlayer()
    {
        if (_previewPlayer is not null)
        {
            try
            {
                _previewPlayer.Stop();
            }
            catch
            {
            }

            _previewPlayer.Dispose();
            _previewPlayer = null;
        }

        if (!string.IsNullOrWhiteSpace(_previewPath))
        {
            ConversionActionHelper.DeleteIfExists(_previewPath);
            _previewPath = null;
        }
    }

    private void SetBusyState(bool busy, string statusText)
    {
        _busy = busy;
        UseWaitCursor = busy;
        _statusLabel.Text = statusText;
        var allowEditing = _workspaceReady && !busy && !_closingRequested;
        _buttonPlay.Enabled = allowEditing;
        _buttonStop.Enabled = allowEditing;
        _buttonRemove.Enabled = allowEditing;
        _buttonSilence.Enabled = allowEditing;
        _buttonCut.Enabled = allowEditing;
        _buttonCancel.Enabled = !_closingRequested;
        _textStart.Enabled = allowEditing;
        _textEnd.Enabled = allowEditing;
    }

    private void ShowActionError(string message)
    {
        if (!CanUpdateUi)
        {
            return;
        }

        MessageBox.Show(
            this,
            message,
            "FrameShift",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private Panel CreateHeaderPanel()
    {
        return FrameShiftUiFactory.CreateFillHeader(
            "FrameShift - Cut Audio",
            $"Source: {Path.GetFileName(_inputPath)}    Duration: {CutAudioSettings.FormatDisplayTime(_currentDurationSeconds)}",
            IconPaths.ContextMenuIco("cut-video-audio-icon.ico"),
            IconPaths.AppIcon,
            "A",
            860);
    }

    private static Panel CreateSectionPanel(string title, out Panel contentHost)
    {
        return FrameShiftUiFactory.CreateFillSection(title, out contentHost);
    }

    private static TextBox CreateValueTextBox()
    {
        var textBox = FrameShiftUiFactory.CreateValueTextBox();
        textBox.Margin = Padding.Empty;
        textBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        textBox.Location = new Point(10, 6);
        textBox.Width = 120;
        return textBox;
    }

    private static Panel CreateInfoCardPanel()
    {
        return FrameShiftUiFactory.CreateFillInfoCard();
    }

    private static Button CreateActionButton(string text, bool primary, int width)
    {
        return FrameShiftUiFactory.CreateActionButton(text, primary, width);
    }

    private static void LayoutToolButtons(Control host, params Button[] buttons)
    {
        if (host.ClientSize.Width <= 0 || host.ClientSize.Height <= 0)
        {
            return;
        }

        var x = 0;
        var y = Math.Max(0, (host.ClientSize.Height - FrameShiftUiMetrics.FooterButtonHeight) / 2);
        foreach (var button in buttons)
        {
            button.SetBounds(x, y, button.Width, FrameShiftUiMetrics.FooterButtonHeight);
            x += button.Width + FrameShiftUiMetrics.LineGap;
        }
    }

    private static void UpdateFooterButtonLayout(Control footerPanel, Button cancelButton, Button okButton)
    {
        if (footerPanel.ClientSize.Width <= 0 || footerPanel.ClientSize.Height <= 0)
        {
            return;
        }

        FrameShiftUiLayout.LayoutFooterButtons(footerPanel, cancelButton, okButton, FrameShiftUiMetrics.LineGap);
    }

    private static Panel CreateFramedPanel(Color backgroundColor, Color borderColor, int radius)
    {
        return FrameShiftUiFactory.CreateFramedPanel(backgroundColor, borderColor, radius);
    }
}
