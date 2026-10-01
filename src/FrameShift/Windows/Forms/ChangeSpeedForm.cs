using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public enum ChangeSpeedMediaKind { Audio, Video }

public sealed class ChangeSpeedForm : Form
{
    private static readonly (int Percent, string Label)[] AudioPresets =
        [(50, "50%"), (75, "75%"), (100, "100%"), (125, "125%"), (150, "150%"), (200, "200%")];

    private static readonly (int Percent, string Label)[] VideoPresets =
        [(25, "25%"), (50, "50%"), (75, "75%"), (100, "100%"), (125, "125%"), (150, "150%"), (200, "200%"), (400, "400%")];

    // Slider visual range used by the UI.
    private const int SliderMin = 25;
    private const int SliderMax = 400;
    private const int PresetButtonWidth = 72;
    private const double UiMinFactor = SliderMin / 100.0;
    private const double UiMaxFactor = SliderMax / 100.0;

    private readonly string _inputPath;
    private readonly Func<IReadOnlyList<string>, TimeSpan, CancellationToken, Task<FfmpegRunResult>> _runPreview;
    private readonly ChangeSpeedMediaKind _mediaKind;
    private readonly double _sourceDurationSeconds;
    private readonly bool _hasAudio;
    private readonly int _sampleRate;

    private readonly TrackBar _trackBar;
    private readonly TextBox _textPercent;
    private readonly TextBox _textDuration;
    private readonly CheckBox _checkKeepPitch;
    private readonly Label _infoLabel;
    private readonly Button _buttonPreview;

    private bool _syncing;
    private double _speedFactor = 1.0;

    private SoundPlayer? _previewPlayer;
    private string? _previewAudioPath;
    private System.Windows.Forms.Timer? _previewTimer;
    private string? _previewVideoPath;
    private bool _previewing;
    private bool _closing;
    private CancellationTokenSource? _previewCts;

    public ChangeSpeedForm(string inputPath, string ffmpegPath, FfmpegRunner ffmpegRunner,
        ChangeSpeedMediaKind mediaKind, double sourceDurationSeconds, bool hasAudio, int sampleRate)
        : this(inputPath, ffmpegPath, ffmpegRunner, mediaKind, sourceDurationSeconds, hasAudio, sampleRate, null) { }

    internal ChangeSpeedForm(string inputPath, string ffmpegPath, FfmpegRunner ffmpegRunner,
        ChangeSpeedMediaKind mediaKind, double sourceDurationSeconds, bool hasAudio, int sampleRate,
        Func<IReadOnlyList<string>, TimeSpan, CancellationToken, Task<FfmpegRunResult>>? runPreview)
    {
        _inputPath = inputPath;
        _runPreview = runPreview ?? ((arguments, duration, token) => ffmpegRunner.RunAsync(
            ffmpegPath, arguments, duration, null, inputPath, mediaKind == ChangeSpeedMediaKind.Audio ? "Change Audio Speed" : "Change Video Speed",
                mediaKind == ChangeSpeedMediaKind.Audio ? "Audio" : "Video", token));
        _mediaKind = mediaKind;
        _sourceDurationSeconds = sourceDurationSeconds;
        _hasAudio = hasAudio;
        _sampleRate = sampleRate;
        var isAudio = mediaKind == ChangeSpeedMediaKind.Audio;
        var title = isAudio ? "Change Audio Speed" : "Change Video Speed";
        var iconPath = IconPaths.ContextMenuIco(isAudio ? "change-audio-speed-audio-icon.ico" : "change-video-speed-video-icon.ico");
        var initialWidth = isAudio ? 640 : Math.Max(640,
            VideoPresets.Length * (PresetButtonWidth + FrameShiftUiMetrics.LineGap)
            + 2 * FrameShiftUiMetrics.OuterPadding + FrameShiftUiMetrics.StandardSectionPadding.Horizontal);
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(initialWidth, 600), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, $"FrameShift - {title}");
        var header = FrameShiftUiFactory.CreateHeader($"FrameShift - {title}", $"Source: {Path.GetFileName(inputPath)}",
            iconPath, IconPaths.AppIcon, "♪");
        _infoLabel = FrameShiftUiFactory.CreateWrappingLabel("");
        _trackBar = new TrackBar { Name = "speedSlider", Minimum = SliderMin, Maximum = SliderMax, Value = 100,
            SmallChange = 1, LargeChange = 25, TickFrequency = 25, AutoSize = true };
        _textPercent = new TextBox { Name = "percent", Text = "100" };
        _textDuration = new TextBox { Name = "duration", Text = FormatDuration(sourceDurationSeconds) };
        _checkKeepPitch = new CheckBox { Name = "keepPitch", Text = "Keep original audio pitch", AutoSize = true,
            Checked = isAudio || hasAudio, Enabled = isAudio || hasAudio };
        _trackBar.ValueChanged += (_, _) => OnTrackBarChanged();
        _textPercent.TextChanged += (_, _) => OnPercentTextChanged();
        _textPercent.Leave += (_, _) => ReformatPercentOnLeave();
        _textDuration.TextChanged += (_, _) => OnDurationTextChanged();
        _textDuration.Leave += (_, _) => ReformatDurationOnLeave();
        _checkKeepPitch.CheckedChanged += (_, _) => RefreshInfoLabel();
        var presets = FrameShiftUiFactory.CreateChoiceRow();
        presets.Name = "speedPresets";
        foreach (var (percent, label) in isAudio ? AudioPresets : VideoPresets)
        {
            var button = FrameShiftUiFactory.CreateMeasuredActionButton(label, false, PresetButtonWidth);
            button.Click += (_, _) => SetSpeedFactor(percent / 100.0);
            presets.Controls.Add(button);
        }
        var content = FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateSection("Speed", FrameShiftUiFactory.CreateVerticalStack(
                FrameShiftUiFactory.CreateWrappingLabel($"Original duration: {FormatDuration(sourceDurationSeconds)}"),
                _trackBar, FrameShiftUiFactory.CreateFieldRow("&Speed", _textPercent, "%", 120),
                FrameShiftUiFactory.CreateFieldRow("&Target duration", _textDuration, null, 180),
                FrameShiftUiFactory.CreateWrappingLabel("Speed: 25% to 400%. Duration: seconds or hh:mm:ss."))),
            FrameShiftUiFactory.CreateSection("Presets", presets),
            FrameShiftUiFactory.CreateSection("Options", FrameShiftUiFactory.CreateChoiceRow(_checkKeepPitch)));
        _buttonPreview = FrameShiftUiFactory.CreateMeasuredActionButton("Preview 5s", false);
        _buttonPreview.Name = "previewButton";
        _buttonPreview.Click += async (_, _) => await PreviewAsync();
        var previewSection = FrameShiftUiFactory.CreateSection("Preview", FrameShiftUiFactory.CreateVerticalStack(
            _buttonPreview, _infoLabel));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(previewSection, 0, content.RowCount++);
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        cancel.DialogResult = DialogResult.Cancel;
        var apply = FrameShiftUiFactory.CreateMeasuredActionButton("Apply", true);
        apply.DialogResult = DialogResult.OK;
        AcceptButton = apply;
        CancelButton = cancel;
        var layout = FrameShiftDialogLayout.Create(header, content, FrameShiftDialogLayout.CreateActions(cancel, apply));
        Controls.Add(layout);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, layout);
        RefreshInfoLabel();
        ResumeLayout(true);
    }

    public ChangeSpeedSettings? Selection { get; private set; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            Selection = new ChangeSpeedSettings(
                Math.Clamp(_speedFactor, ChangeSpeedSettings.MinFactor, ChangeSpeedSettings.MaxFactor),
                _checkKeepPitch.Checked);
        }
        base.OnFormClosing(e);
        if (!e.Cancel) StopPreviewForClose();
    }

    // ── Preset buttons ──────────────────────────────────────────────────────────

    private void SetSpeedFactor(double factor)
    {
        if (_syncing) return;

        _syncing = true;
        try
        {
            _speedFactor = Math.Clamp(factor, ChangeSpeedSettings.MinFactor, ChangeSpeedSettings.MaxFactor);
            SyncAllControlsFromFactor();
        }
        finally
        {
            _syncing = false;
        }

        RefreshInfoLabel();
    }

    private void SyncAllControlsFromFactor()
    {
        var pct = _speedFactor * 100.0;
        _textPercent.Text = pct.ToString("0.##", CultureInfo.InvariantCulture);
        _textDuration.Text = FormatDuration(_sourceDurationSeconds / _speedFactor);

        var sliderValue = (int)Math.Round(Math.Clamp(pct, SliderMin, SliderMax));
        if (_trackBar.Value != sliderValue)
        {
            _trackBar.Value = sliderValue;
        }
    }

    private void OnTrackBarChanged()
    {
        if (_syncing) return;
        StopAudioPreview();

        _syncing = true;
        try
        {
            _speedFactor = _trackBar.Value / 100.0;
            var pct = _speedFactor * 100.0;
            _textPercent.Text = pct.ToString("0.##", CultureInfo.InvariantCulture);
            _textDuration.Text = FormatDuration(_sourceDurationSeconds / _speedFactor);
        }
        finally
        {
            _syncing = false;
        }

        RefreshInfoLabel();
    }

    private void OnPercentTextChanged()
    {
        if (_syncing) return;
        StopAudioPreview();

        var raw = _textPercent.Text.Trim().Replace(',', '.');
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) || pct <= 0)
        {
            _infoLabel.Text = "Enter a speed percentage greater than 0.";
            return;
        }

        var factor = pct / 100.0;
        if (factor < UiMinFactor || factor > UiMaxFactor)
        {
            _infoLabel.Text = $"Speed must stay between {SliderMin}% and {SliderMax}%.";
            return;
        }

        _syncing = true;
        try
        {
            _speedFactor = factor;
            _textDuration.Text = FormatDuration(_sourceDurationSeconds / _speedFactor);

            var sliderValue = (int)Math.Round(Math.Clamp(pct, SliderMin, SliderMax));
            if (_trackBar.Value != sliderValue) _trackBar.Value = sliderValue;
        }
        finally
        {
            _syncing = false;
        }

        RefreshInfoLabel();
    }

    private void OnDurationTextChanged()
    {
        if (_syncing) return;
        StopAudioPreview();

        if (!TryParseDuration(_textDuration.Text, out var targetSeconds) || targetSeconds <= 0)
        {
            _infoLabel.Text = "Enter a valid target duration greater than 0.";
            return;
        }

        if (_sourceDurationSeconds <= 0)
        {
            return;
        }

        var factor = _sourceDurationSeconds / targetSeconds;
        if (factor < UiMinFactor || factor > UiMaxFactor)
        {
            _infoLabel.Text = $"Speed must stay between {SliderMin}% and {SliderMax}%.";
            return;
        }

        _syncing = true;
        try
        {
            _speedFactor = factor;
            var pct = factor * 100.0;
            _textPercent.Text = pct.ToString("0.##", CultureInfo.InvariantCulture);

            var sliderValue = (int)Math.Round(Math.Clamp(pct, SliderMin, SliderMax));
            if (_trackBar.Value != sliderValue) _trackBar.Value = sliderValue;
        }
        finally
        {
            _syncing = false;
        }

        RefreshInfoLabel();
    }

    private void ReformatPercentOnLeave()
    {
        var raw = _textPercent.Text.Trim().Replace(',', '.');
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) && pct > 0)
        {
            var factor = pct / 100.0;
            if (factor >= UiMinFactor && factor <= UiMaxFactor)
            {
                _syncing = true;
                _textPercent.Text = pct.ToString("0.##", CultureInfo.InvariantCulture);
                _syncing = false;
                return;
            }
        }

        // Restore from last valid internal state
        _syncing = true;
        _textPercent.Text = (_speedFactor * 100.0).ToString("0.##", CultureInfo.InvariantCulture);
        _syncing = false;
        RefreshInfoLabel();
    }

    private void ReformatDurationOnLeave()
    {
        if (TryParseDuration(_textDuration.Text, out var seconds) && seconds > 0)
        {
            var factor = _sourceDurationSeconds > 0 ? _sourceDurationSeconds / seconds : 1.0;
            if (factor >= UiMinFactor && factor <= UiMaxFactor)
            {
                _syncing = true;
                _textDuration.Text = FormatDuration(seconds);
                _syncing = false;
                return;
            }
        }

        // Restore from last valid internal state
        _syncing = true;
        _textDuration.Text = FormatDuration(_sourceDurationSeconds / _speedFactor);
        _syncing = false;
        RefreshInfoLabel();
    }

    private void RefreshInfoLabel()
    {
        var newDuration = _sourceDurationSeconds / _speedFactor;
        var verb = _speedFactor > 1.0 ? "faster" : _speedFactor < 1.0 ? "slower" : "same speed";
        _infoLabel.Text = $"New duration: {FormatDuration(newDuration)} — Speed: x{_speedFactor:0.###} ({verb}) — Output saved next to source.";
    }

    // ── Preview ─────────────────────────────────────────────────────────────────

    private void StopPreviewForClose()
    {
        _closing = true;
        _previewCts?.Cancel();
        CleanupPreview();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopPreviewForClose();
        base.Dispose(disposing);
    }

    private async Task PreviewAsync()
    {
        if (_previewing || _closing) return;
        _previewing = true;
        using var request = new CancellationTokenSource();
        _previewCts = request;
        _buttonPreview.Enabled = false;

        try
        {
            if (_mediaKind == ChangeSpeedMediaKind.Audio)
            {
                await PreviewAudioAsync(request.Token).ConfigureAwait(true);
            }
            else
            {
                await PreviewVideoAsync(request.Token).ConfigureAwait(true);
            }
        }
        catch
        {
            CleanupPreview();
        }
        finally
        {
            _previewing = false;
            if (_closing) CleanupPreview();
            _previewCts = null;
            if (!_closing && !IsDisposed) _buttonPreview.Enabled = true;
        }
    }

    private async Task PreviewAudioAsync(CancellationToken token)
    {
        CleanupAudioPreview();

        var settings = new ChangeSpeedSettings(
            Math.Clamp(_speedFactor, ChangeSpeedSettings.MinFactor, ChangeSpeedSettings.MaxFactor),
            _checkKeepPitch.Checked);

        var tempPath = Path.Combine(Path.GetTempPath(), $"fs_speed_preview_{Guid.NewGuid():N}.wav");
        _previewAudioPath = tempPath;
        var args = BuildAudioPreviewArguments(_inputPath, tempPath, settings, _sampleRate);

        var result = await _runPreview(args, TimeSpan.FromSeconds(30), token).ConfigureAwait(true);

        token.ThrowIfCancellationRequested();

        if (result.ExitCode != 0 || !File.Exists(tempPath))
        {
            ConversionActionHelper.DeleteIfExists(tempPath);
            return;
        }

        _previewAudioPath = tempPath;
        _previewPlayer = new SoundPlayer(tempPath);
        _previewPlayer.Load();
        _previewPlayer.Play();

        _previewTimer = new System.Windows.Forms.Timer { Interval = 6000 };
        _previewTimer.Tick += (_, _) => CleanupAudioPreview();
        _previewTimer.Start();
    }

    private async Task PreviewVideoAsync(CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(_previewVideoPath))
        {
            try { ConversionActionHelper.DeleteIfExists(_previewVideoPath); } catch { }
            _previewVideoPath = null;
        }

        var settings = new ChangeSpeedSettings(
            Math.Clamp(_speedFactor, ChangeSpeedSettings.MinFactor, ChangeSpeedSettings.MaxFactor),
            _checkKeepPitch.Checked);

        var tempPath = Path.Combine(Path.GetTempPath(), $"fs_speed_preview_{Guid.NewGuid():N}.mp4");
        _previewVideoPath = tempPath;
        var args = BuildVideoPreviewArguments(_inputPath, tempPath, settings, _hasAudio, _sampleRate);

        var result = await _runPreview(args, TimeSpan.FromSeconds(60), token).ConfigureAwait(true);

        token.ThrowIfCancellationRequested();

        if (result.ExitCode != 0 || !File.Exists(tempPath))
        {
            ConversionActionHelper.DeleteIfExists(tempPath);
            return;
        }

        _previewVideoPath = tempPath;
        try { Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true }); }
        catch { }
    }

    private void StopAudioPreview()
    {
        if (_previewPlayer is null) return;
        try { _previewPlayer.Stop(); } catch { }
    }

    private void CleanupAudioPreview()
    {
        _previewTimer?.Stop();
        _previewTimer?.Dispose();
        _previewTimer = null;

        if (_previewPlayer is not null)
        {
            try { _previewPlayer.Stop(); } catch { }
            _previewPlayer.Dispose();
            _previewPlayer = null;
        }

        if (!string.IsNullOrWhiteSpace(_previewAudioPath))
        {
            ConversionActionHelper.DeleteIfExists(_previewAudioPath);
            if (!_previewing) _previewAudioPath = null;
        }
    }

    private void CleanupPreview()
    {
        CleanupAudioPreview();

        if (!string.IsNullOrWhiteSpace(_previewVideoPath))
        {
            try { ConversionActionHelper.DeleteIfExists(_previewVideoPath); } catch { }
            if (!_previewing) _previewVideoPath = null;
        }
    }

    // ── FFmpeg argument builders ────────────────────────────────────────────────

    private static IReadOnlyList<string> BuildAudioPreviewArguments(
        string inputPath, string outputPath, ChangeSpeedSettings settings, int sampleRate)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error",
            "-y",
            "-t", "5",
            "-i", inputPath,
            "-vn"
        };

        string audioFilter;
        if (settings.KeepPitch)
        {
            audioFilter = ChangeAudioSpeedAction.BuildAtempoChain(settings.SpeedFactor);
        }
        else
        {
            var targetRate = Math.Max(1000, (int)Math.Round(sampleRate * settings.SpeedFactor));
            audioFilter = $"asetrate={targetRate},aresample={sampleRate}";
        }

        args.Add("-filter:a"); args.Add(audioFilter);
        args.Add("-c:a"); args.Add("pcm_s16le");
        args.Add(outputPath);
        return args;
    }

    private static IReadOnlyList<string> BuildVideoPreviewArguments(
        string inputPath, string outputPath, ChangeSpeedSettings settings, bool hasAudio, int sampleRate)
    {
        var setPtsFactor = (1.0 / settings.SpeedFactor).ToString("0.######", CultureInfo.InvariantCulture);
        var videoFilter = $"setpts={setPtsFactor}*PTS";

        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error",
            "-y",
            "-t", "10",
            "-i", inputPath
        };

        if (hasAudio)
        {
            string audioFilter;
            if (settings.KeepPitch)
            {
                audioFilter = ChangeAudioSpeedAction.BuildAtempoChain(settings.SpeedFactor);
            }
            else
            {
                var targetRate = Math.Max(1000, (int)Math.Round(sampleRate * settings.SpeedFactor));
                audioFilter = $"asetrate={targetRate},aresample={sampleRate}";
            }

            args.Add("-filter_complex");
            args.Add($"[0:v]{videoFilter}[v];[0:a]{audioFilter}[a]");
            args.Add("-map"); args.Add("[v]");
            args.Add("-map"); args.Add("[a]");
            args.Add("-c:a"); args.Add("aac"); args.Add("-b:a"); args.Add("128k");
        }
        else
        {
            args.Add("-filter:v"); args.Add(videoFilter);
            args.Add("-an");
        }

        args.AddRange(["-c:v", "libx264", "-preset", "ultrafast", "-crf", "23", "-pix_fmt", "yuv420p"]);
        args.Add(outputPath);
        return args;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static string FormatDuration(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var totalMs = (long)Math.Round(seconds * 1000.0);
        var h = totalMs / 3600000;
        var m = totalMs % 3600000 / 60000;
        var s = totalMs % 60000 / 1000;
        var ms = totalMs % 1000;
        return $"{h:00}:{m:00}:{s:00}.{ms:000}";
    }

    private static bool TryParseDuration(string text, out double seconds)
    {
        seconds = 0;
        text = text.Trim().Replace(',', '.');
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Plain seconds
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain) && plain >= 0)
        {
            seconds = plain;
            return true;
        }

        // hh:mm:ss[.ms] or mm:ss[.ms]
        var match = Regex.Match(text, @"^(?:(\d+):)?(\d+):(\d+(?:\.\d+)?)$");
        if (!match.Success) return false;

        var culture = CultureInfo.InvariantCulture;
        var hh = match.Groups[1].Success ? double.Parse(match.Groups[1].Value, culture) : 0;
        var mm = double.Parse(match.Groups[2].Value, culture);
        var ss = double.Parse(match.Groups[3].Value, culture);
        seconds = hh * 3600 + mm * 60 + ss;
        return seconds >= 0;
    }
}
