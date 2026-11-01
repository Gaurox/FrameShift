using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FrameShift.Core.AI.RemoveNoise;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.Logging;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.AI;

public sealed class RemoveNoiseAudioPickerForm : Form
{
    private string _selectedStrength = RemoveNoiseAudioSettings.StrengthMaximum;
    private readonly CheckBox _stereoCheckBox;
    private readonly RadioButton[] _strengthChoices;
    private readonly FrameShift.Windows.Controls.FrameShiftStatusMessage _previewStatus;
    private readonly Button _cancelButton;
    private readonly Button _denoiseButton;
    private readonly Button _previewButton;

    private readonly string? _previewSourcePath;
    private readonly string? _ffmpegPath;
    private readonly FfmpegRunner? _ffmpegRunner;
    private RemoveNoiseEngine? _previewEngine;
    private readonly OnnxFormLifetime _lifetime = new();
    private Task? _closingTask;
    private string? _previewTempExtract;
    private string? _previewTempClean;
    private SoundPlayer? _soundPlayer;
    private bool _closingRequested;
    private bool _allowClose;
    private bool _resourcesCleaned;
    private DialogResult? _requestedDialogResult;

    public RemoveNoiseAudioPickerForm(
        string sourceLabel,
        bool sourceIsStereo,
        string? previewSourcePath = null,
        string? ffmpegPath = null)
    {
        _previewSourcePath = previewSourcePath;
        _ffmpegPath        = ffmpegPath;
        if (previewSourcePath != null && ffmpegPath != null)
            _ffmpegRunner = new FfmpegRunner(new AppLogger());

        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(640, 440), new Size(400, 300));
        FrameShiftWindowChrome.Apply(this, L.Get("RemoveNoiseAudioPickerForm_FrameShift_Remove_Noise_Audio_bc28079f"), IconPaths.FrameShiftAiIcon, IconPaths.AppIcon);
        var header = FrameShiftUiFactory.CreateHeader(
            L.Get("RemoveNoiseAudioPickerForm_FrameShift_Remove_Noise_Audio_bc28079f"), L.Format("RemoveNoiseAudioPickerForm_Source_0_653ab450",sourceLabel),
            IconPaths.RemoveNoiseAiIcon, IconPaths.FrameShiftAiIcon, "AI");
        var strength = RemoveNoisePickerUi.CreateStrengthSection(value => _selectedStrength = value, out _strengthChoices);
        var channels = RemoveNoisePickerUi.CreateStereoSection(sourceIsStereo, out _stereoCheckBox);
        _previewStatus = FrameShiftUiFactory.CreateStatusMessage(L.Get("RemoveNoiseAudioPickerForm_Audio_will_be_processed_at_48_kHz_406f2a5e"));
        _previewButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("RemoveNoiseAudioPickerForm_Preview_4639753c"), false);
        _previewButton.Enabled = _ffmpegRunner != null;
        _previewButton.Click += async (_, _) => await StartPreviewAsync().ConfigureAwait(true);
        _cancelButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("RemoveNoiseAudioPickerForm_Cancel_998b9c48"), false);
        _denoiseButton = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("RemoveNoiseAudioPickerForm_Denoise_38e80a90"), true);
        _cancelButton.Click += (_, _) => RequestClose(DialogResult.Cancel);
        _denoiseButton.Click += (_, _) => RequestClose(DialogResult.OK);
        AcceptButton = _denoiseButton;
        CancelButton = _cancelButton;
        var root = FrameShiftDialogLayout.Create(header,
            FrameShiftUiFactory.CreateVerticalStack(strength, channels),
            FrameShiftDialogLayout.CreateActions(_previewButton, _cancelButton, _denoiseButton), _previewStatus);
        Controls.Add(root);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, root);
        FormClosing += OnFormClosing;
        FormClosed += (_, _) => _lifetime.Dispose();

        ResumeLayout(true);
    }

    public string? SelectedStrength => _selectedStrength;
    public bool ProcessStereo => _stereoCheckBox.Checked;

    public static string BuildSourceLabel(IReadOnlyList<string> inputPaths)
    {
        if (inputPaths.Count == 1)
        {
            var name = Path.GetFileName(inputPaths[0]);
            return name;
        }

        return L.Format("RemoveNoiseAudioPickerForm_0_selected_files_dd068a4b",inputPaths.Count);
    }

    private bool CanUpdateUi => !_closingRequested && !IsDisposed && !Disposing;

    private async Task StartPreviewAsync()
    {
        if (_closingRequested)
        {
            return;
        }

        try
        {
            await _lifetime.RunAsync(RunPreviewCoreAsync).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsClosing || _lifetime.Token.IsCancellationRequested)
        {
        }
    }

    private async Task RunPreviewCoreAsync(CancellationToken ct)
    {
        if (_closingRequested)
        {
            return;
        }

        _soundPlayer?.Stop();

        _previewButton.Enabled = false;
        _previewButton.Text    = L.Get("RemoveNoiseAudioPickerForm_Processing_44c765f2");

        TryDeletePreviewTemp(_previewTempExtract);
        TryDeletePreviewTemp(_previewTempClean);
        _previewTempExtract = null;
        _previewTempClean   = null;

        var tempId = Guid.NewGuid().ToString("N")[..8];
        _previewTempExtract = Path.Combine(Path.GetTempPath(), $"fs_rnprev_{tempId}.wav");

        try
        {
            var extractArgs = new[]
            {
                "-hide_banner", "-loglevel", "error",
                "-i", _previewSourcePath!,
                "-t", "8",
                "-vn", "-c:a", "pcm_s16le",
                _previewTempExtract
            };

            var extractResult = await _ffmpegRunner!
                .RunAsync(_ffmpegPath!, extractArgs, null, null, _previewSourcePath, "Preview", "CPU", ct)
                .ConfigureAwait(true);

            if (ct.IsCancellationRequested || extractResult.Canceled || extractResult.ExitCode != 0)
                return;

            _previewEngine ??= new RemoveNoiseEngine();

            var minGain = RemoveNoiseAudioSettings.ToMinGain(_selectedStrength);
            var aiProgress = new Progress<(int percent, string status)>(p =>
            {
                if (!ct.IsCancellationRequested && CanUpdateUi)
                    _previewButton.Text = L.Format("RemoveNoiseAudioPickerForm_Processing_0_7f7b7bab",p.percent);
            });

            _previewTempClean = await _previewEngine
                .RemoveNoiseAsync(_previewTempExtract, aiProgress, ct, minGain)
                .ConfigureAwait(true);

            if (ct.IsCancellationRequested || !CanUpdateUi)
                return;

            _soundPlayer?.Dispose();
            _soundPlayer = new SoundPlayer(_previewTempClean);
            _soundPlayer.Play();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested && CanUpdateUi)
                _previewStatus.Text = L.Format("RemoveNoiseAudioPickerForm_Preview_failed_0_44cd1b41",ex.Message);
        }
        finally
        {
            if (!ct.IsCancellationRequested && CanUpdateUi)
            {
                _previewButton.Text    = L.Get("RemoveNoiseAudioPickerForm_Preview_4639753c");
                _previewButton.Enabled = true;
            }
        }
    }

    private void RequestClose(DialogResult requestedDialogResult)
    {
        _requestedDialogResult = requestedDialogResult;
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
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
        _requestedDialogResult ??= DialogResult.Cancel;
        SetClosingUiState();
        _closingTask = CompleteCloseAsync();
    }

    private async Task CompleteCloseAsync()
    {
        await _lifetime.BeginClosingAsync(
            CleanupPreviewResourcesAsync,
            ex => AppLogger.LogStatic($"RemoveNoiseAudioPickerForm: close cleanup failed. {ex}")).ConfigureAwait(true);

        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        // Leave the cancelled FormClosing before setting the modal result.
        BeginInvoke(new Action(() =>
        {
            if (IsDisposed || Disposing) return;
            _allowClose = true;
            DialogResult = _requestedDialogResult ?? DialogResult.Cancel;
            Close();
        }));
    }

    private Task CleanupPreviewResourcesAsync()
    {
        if (_resourcesCleaned)
        {
            return Task.CompletedTask;
        }

        _resourcesCleaned = true;
        _soundPlayer?.Stop();
        _soundPlayer?.Dispose();
        _soundPlayer = null;
        _previewEngine?.Dispose();
        _previewEngine = null;
        TryDeletePreviewTemp(_previewTempExtract);
        TryDeletePreviewTemp(_previewTempClean);
        _previewTempExtract = null;
        _previewTempClean = null;
        return Task.CompletedTask;
    }

    private void SetClosingUiState()
    {
        _previewButton.Enabled = false;
        _previewButton.Text = L.Get("RemoveNoiseAudioPickerForm_Closing_dd28ceb7");
        _cancelButton.Enabled = false;
        _denoiseButton.Enabled = false;
        _stereoCheckBox.Enabled = false;
        foreach (var choice in _strengthChoices) choice.Enabled = false;
    }
    private static void TryDeletePreviewTemp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
