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
    private bool _updatingStrength;
    private readonly CheckBox _stereoCheckBox;
    private Panel _lightTile   = null!;
    private Panel _normalTile  = null!;
    private Panel _strongTile  = null!;
    private Panel _maximumTile = null!;
    private RadioButton _lightRadio   = null!;
    private RadioButton _normalRadio  = null!;
    private RadioButton _strongRadio  = null!;
    private RadioButton _maximumRadio = null!;
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

        FrameShiftWindowChrome.Apply(this, "FrameShift - Remove Noise (Audio)", IconPaths.FrameShiftAiIcon, IconPaths.AppIcon);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 384);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var header = FrameShiftUiFactory.CreateFixedHeader(
            "FrameShift - Remove Noise (Audio)",
            $"Source: {sourceLabel}",
            IconPaths.RemoveNoiseAiIcon,
            IconPaths.FrameShiftAiIcon,
            "AI");
        Controls.Add(header);

        var strengthSection = FrameShiftUiFactory.CreateFixedSection(
            new Point(12, 82),
            new Size(536, 142),
            "Noise reduction strength");
        Controls.Add(strengthSection);

        _lightTile   = CreateStrengthTile("Light",   "Noticeable noise, lightly reduced", new Point(13,  32), RemoveNoiseAudioSettings.StrengthLight,   false, out _lightRadio);
        _normalTile  = CreateStrengthTile("Normal",  "Balanced noise reduction",           new Point(143, 32), RemoveNoiseAudioSettings.StrengthNormal,  false, out _normalRadio);
        _strongTile  = CreateStrengthTile("Strong",  "Aggressive noise reduction",         new Point(273, 32), RemoveNoiseAudioSettings.StrengthStrong,  false, out _strongRadio);
        _maximumTile = CreateStrengthTile("Maximum", "Maximum cleanup",                    new Point(403, 32), RemoveNoiseAudioSettings.StrengthMaximum, true,  out _maximumRadio);

        strengthSection.Controls.AddRange([_lightTile, _normalTile, _strongTile, _maximumTile]);

        var hintLabel = new Label
        {
            AutoSize = false,
            Location = new Point(18, 102),
            Size = new Size(500, 18),
            ForeColor = FrameShiftTheme.AccentText,
            Text = "Higher strength removes more noise. Maximum may affect natural audio characteristics."
        };
        strengthSection.Controls.Add(hintLabel);

        _lightRadio.CheckedChanged   += (_, _) => SelectStrength(RemoveNoiseAudioSettings.StrengthLight,   _lightTile, _normalTile, _strongTile, _maximumTile);
        _normalRadio.CheckedChanged  += (_, _) => SelectStrength(RemoveNoiseAudioSettings.StrengthNormal,  _lightTile, _normalTile, _strongTile, _maximumTile);
        _strongRadio.CheckedChanged  += (_, _) => SelectStrength(RemoveNoiseAudioSettings.StrengthStrong,  _lightTile, _normalTile, _strongTile, _maximumTile);
        _maximumRadio.CheckedChanged += (_, _) => SelectStrength(RemoveNoiseAudioSettings.StrengthMaximum, _lightTile, _normalTile, _strongTile, _maximumTile);

        _stereoCheckBox = new CheckBox
        {
            AutoSize = true,
            Location = new Point(20, 234),
            Text = "Stereo processing  —  2× longer, processes L and R channels independently",
            ForeColor = sourceIsStereo ? FrameShiftTheme.TextPrimary : FrameShiftTheme.TextSecondary,
            Enabled = sourceIsStereo,
            Checked = false,
            FlatStyle = FlatStyle.Flat
        };
        Controls.Add(_stereoCheckBox);

        var stereoHintLabel = new Label
        {
            AutoSize = false,
            Location = new Point(42, 254),
            Size = new Size(494, 16),
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = sourceIsStereo
                ? "L and R channels are denoised separately, then merged back."
                : "Source audio is mono — stereo processing is not available."
        };
        Controls.Add(stereoHintLabel);

        var infoCard = FrameShiftUiFactory.CreateFixedInfoCard(new Point(12, 278), new Size(536, 44));
        Controls.Add(infoCard);

        var infoLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 11),
            Size = new Size(504, 18),
            ForeColor = FrameShiftTheme.TextSecondary,
            Text = "Audio will be processed at 48 kHz."
        };
        infoCard.Controls.Add(infoLabel);

        _previewButton = FrameShiftUiFactory.CreateFixedActionButton("▶  Preview", new Point(12, 328), new Size(130, 34), primary: false);
        _previewButton.Enabled = _ffmpegRunner != null;
        _previewButton.Click  += async (_, _) => await StartPreviewAsync().ConfigureAwait(true);
        Controls.Add(_previewButton);

        _cancelButton  = FrameShiftUiFactory.CreateFixedActionButton("Cancel",  new Point(278, 328), new Size(120, 34), primary: false);
        _denoiseButton = FrameShiftUiFactory.CreateFixedActionButton("Denoise", new Point(408, 328), new Size(140, 34), primary: true);
        _cancelButton.Click += (_, _) => RequestClose(DialogResult.Cancel);
        _denoiseButton.Click += (_, _) => RequestClose(DialogResult.OK);
        Controls.Add(_cancelButton);
        Controls.Add(_denoiseButton);

        AcceptButton = _denoiseButton;
        CancelButton = _cancelButton;
        FormClosing += OnFormClosing;
        FormClosed += (_, _) => _lifetime.Dispose();

        SelectStrength(RemoveNoiseAudioSettings.StrengthMaximum, _lightTile, _normalTile, _strongTile, _maximumTile);
    }

    public string? SelectedStrength => _selectedStrength;
    public bool ProcessStereo => _stereoCheckBox.Checked;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FrameShiftUiLayout.AutoSizeAndPositionFooterButtons(this, _cancelButton, _denoiseButton);
        _previewButton.SetBounds(12, _cancelButton.Top, _previewButton.Width, _cancelButton.Height);
    }

    public static string BuildSourceLabel(IReadOnlyList<string> inputPaths)
    {
        if (inputPaths.Count == 1)
        {
            var name = Path.GetFileName(inputPaths[0]);
            const int max = 72;
            return name.Length <= max ? name : $"{name[..34]}...{name[^30..]}";
        }

        return $"{inputPaths.Count} selected files";
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
        _previewButton.Text    = "Processing...";

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
                    _previewButton.Text = $"Processing {p.percent}%";
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
                MessageBox.Show($"Preview failed: {ex.Message}", "FrameShift", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!ct.IsCancellationRequested && CanUpdateUi)
            {
                _previewButton.Text    = "▶  Preview";
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

        _allowClose = true;
        DialogResult = _requestedDialogResult ?? DialogResult.Cancel;
        Close();
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
        _previewButton.Text = "Closing...";
        _cancelButton.Enabled = false;
        _denoiseButton.Enabled = false;
        _stereoCheckBox.Enabled = false;
        _lightTile.Enabled = false;
        _normalTile.Enabled = false;
        _strongTile.Enabled = false;
        _maximumTile.Enabled = false;
    }

    private void SelectStrength(string strength, params Panel[] tiles)
    {
        if (_updatingStrength) return;
        _updatingStrength = true;
        try
        {
            _selectedStrength = strength;
            _lightRadio.Checked   = strength.Equals(RemoveNoiseAudioSettings.StrengthLight,   StringComparison.OrdinalIgnoreCase);
            _normalRadio.Checked  = strength.Equals(RemoveNoiseAudioSettings.StrengthNormal,  StringComparison.OrdinalIgnoreCase);
            _strongRadio.Checked  = strength.Equals(RemoveNoiseAudioSettings.StrengthStrong,  StringComparison.OrdinalIgnoreCase);
            _maximumRadio.Checked = strength.Equals(RemoveNoiseAudioSettings.StrengthMaximum, StringComparison.OrdinalIgnoreCase);
            foreach (var tile in tiles)
                tile.Invalidate();
        }
        finally
        {
            _updatingStrength = false;
        }
    }

    private Panel CreateStrengthTile(
        string title,
        string description,
        Point location,
        string strengthId,
        bool selected,
        out RadioButton radio)
    {
        var tile = new Panel
        {
            Location = location,
            Size = new Size(120, 58),
            Cursor = Cursors.Hand
        };

        radio = new RadioButton
        {
            Location = new Point(8, 8),
            Size = new Size(14, 14),
            Checked = selected,
            FlatStyle = FlatStyle.Flat,
            TabStop = true
        };
        radio.FlatAppearance.BorderSize = 0;

        var titleLabel = new Label
        {
            AutoSize = false,
            Location = new Point(26, 6),
            Size = new Size(86, 18),
            Text = title,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = FrameShiftTheme.TextPrimary
        };

        var descLabel = new Label
        {
            AutoSize = false,
            Location = new Point(8, 28),
            Size = new Size(104, 22),
            Text = description,
            ForeColor = FrameShiftTheme.TextSecondary
        };

        tile.Controls.AddRange([radio, titleLabel, descLabel]);

        tile.Paint += (_, e) =>
        {
            var isSelected = _selectedStrength.Equals(strengthId, StringComparison.OrdinalIgnoreCase);
            var borderColor = isSelected ? FrameShiftTheme.SecondaryBlue : FrameShiftTheme.PrimaryBlue;
            var pen = new Pen(borderColor, isSelected ? 2f : 1f);
            var rect = new Rectangle(0, 0, tile.Width - 1, tile.Height - 1);
            const int radius = 4;
            using var path = RoundedRectPath(rect, radius);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
            pen.Dispose();
        };

        var capturedRadio = radio;
        tile.Click       += (_, _) => capturedRadio.Checked = true;
        descLabel.Click  += (_, _) => capturedRadio.Checked = true;
        titleLabel.Click += (_, _) => capturedRadio.Checked = true;

        return tile;
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRectPath(Rectangle rect, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
        path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
        path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void TryDeletePreviewTemp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
