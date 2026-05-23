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
    private const double UiMinFactor = SliderMin / 100.0;
    private const double UiMaxFactor = SliderMax / 100.0;

    private readonly string _inputPath;
    private readonly string _ffmpegPath;
    private readonly FfmpegRunner _ffmpegRunner;
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

    public ChangeSpeedForm(
        string inputPath,
        string ffmpegPath,
        FfmpegRunner ffmpegRunner,
        ChangeSpeedMediaKind mediaKind,
        double sourceDurationSeconds,
        bool hasAudio,
        int sampleRate)
    {
        _inputPath = inputPath;
        _ffmpegPath = ffmpegPath;
        _ffmpegRunner = ffmpegRunner;
        _mediaKind = mediaKind;
        _sourceDurationSeconds = sourceDurationSeconds;
        _hasAudio = hasAudio;
        _sampleRate = sampleRate;

        var isAudio = mediaKind == ChangeSpeedMediaKind.Audio;
        var functionTitle = isAudio ? "Change Audio Speed" : "Change Video Speed";
        var iconFileName = isAudio ? "change-audio-speed-audio-icon.ico" : "change-video-speed-video-icon.ico";
        var iconPath = IconPaths.ContextMenuIco(iconFileName);

        FrameShiftWindowChrome.Apply(this, $"FrameShift - {functionTitle}");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 490);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        // ── Header ─────────────────────────────────────────────────────────────
        var header = FrameShiftUiFactory.CreateFixedHeader(
            $"FrameShift - {functionTitle}",
            $"Source: {Path.GetFileName(inputPath)}",
            iconPath,
            IconPaths.AppIcon,
            isAudio ? "♪" : "▶");
        Controls.Add(header);

        // ── Speed section (y=82, h=225) ─────────────────────────────────────────
        var speedSection = FrameShiftUiFactory.CreateFixedSection(
            new Point(12, 82), new Size(536, 225), "Speed");
        Controls.Add(speedSection);

        // Original duration info
        speedSection.Controls.Add(new Label
        {
            Location = new Point(18, 36),
            Size = new Size(500, 20),
            Text = $"Original duration: {FormatDuration(sourceDurationSeconds)}",
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        });

        // Slider left/right endpoint labels
        speedSection.Controls.Add(new Label
        {
            Location = new Point(18, 74),
            Size = new Size(36, 20),
            Text = "25%",
            ForeColor = FrameShiftTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        });

        _trackBar = new TrackBar
        {
            Location = new Point(56, 62),
            Size = new Size(406, 45),
            Minimum = SliderMin,
            Maximum = SliderMax,
            Value = 100,
            SmallChange = 1,
            LargeChange = 25,
            TickFrequency = 25,
            TickStyle = TickStyle.BottomRight,
            AutoSize = false
        };
        _trackBar.ValueChanged += (_, _) => OnTrackBarChanged();
        speedSection.Controls.Add(_trackBar);

        speedSection.Controls.Add(new Label
        {
            Location = new Point(464, 74),
            Size = new Size(54, 20),
            Text = "400%",
            ForeColor = FrameShiftTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleRight
        });

        // Speed % row (y=115)
        speedSection.Controls.Add(new Label
        {
            Location = new Point(18, 115),
            Size = new Size(52, 28),
            Text = "Speed:",
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        });

        _textPercent = FrameShiftUiFactory.CreateValueTextBox(textAlign: HorizontalAlignment.Right);
        _textPercent.Text = "100";
        var pctHost = FrameShiftUiFactory.CreateFixedTextInputHost(_textPercent, new Point(74, 115), new Size(76, 28));
        speedSection.Controls.Add(pctHost);

        speedSection.Controls.Add(new Label
        {
            Location = new Point(154, 118),
            Size = new Size(18, 22),
            Text = "%",
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        });

        speedSection.Controls.Add(new Label
        {
            Location = new Point(180, 118),
            Size = new Size(240, 22),
            Text = "Range: 25% to 400%",
            ForeColor = FrameShiftTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        });

        // Target duration row (y=151)
        speedSection.Controls.Add(new Label
        {
            Location = new Point(18, 151),
            Size = new Size(52, 28),
            Text = "Target:",
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        });

        _textDuration = FrameShiftUiFactory.CreateValueTextBox(textAlign: HorizontalAlignment.Left);
        _textDuration.Text = FormatDuration(sourceDurationSeconds);
        var durHost = FrameShiftUiFactory.CreateFixedTextInputHost(_textDuration, new Point(74, 151), new Size(142, 28));
        speedSection.Controls.Add(durHost);

        speedSection.Controls.Add(new Label
        {
            Location = new Point(222, 154),
            Size = new Size(170, 22),
            Text = "sec or hh:mm:ss",
            ForeColor = FrameShiftTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        });

        // Preset buttons (y=187)
        var presets = isAudio ? AudioPresets : VideoPresets;
        AddPresetRow(speedSection, presets, y: 187);

        // Attach text events after initial values are set
        _textPercent.TextChanged += (_, _) => OnPercentTextChanged();
        _textPercent.Leave += (_, _) => ReformatPercentOnLeave();
        _textDuration.TextChanged += (_, _) => OnDurationTextChanged();
        _textDuration.Leave += (_, _) => ReformatDurationOnLeave();

        // ── Options section (y=319, h=56) ───────────────────────────────────────
        var optionsSection = FrameShiftUiFactory.CreateFixedSection(
            new Point(12, 319), new Size(536, 56), "Options");
        Controls.Add(optionsSection);

        var keepPitchText = isAudio
            ? "Keep original pitch (uses atempo — tempo changes, pitch preserved)"
            : "Keep original audio pitch (uses atempo — tempo changes, pitch preserved)";

        _checkKeepPitch = new CheckBox
        {
            Text = keepPitchText,
            Location = new Point(18, 28),
            Size = new Size(500, 22),
            Checked = true,
            ForeColor = FrameShiftTheme.TextPrimary,
            FlatStyle = FlatStyle.Standard
        };
        if (!isAudio && !hasAudio)
        {
            _checkKeepPitch.Checked = false;
            _checkKeepPitch.Enabled = false;
        }
        _checkKeepPitch.CheckedChanged += (_, _) => RefreshInfoLabel();
        optionsSection.Controls.Add(_checkKeepPitch);

        // ── Info card (y=387, h=44) ─────────────────────────────────────────────
        var infoCard = FrameShiftUiFactory.CreateFixedInfoCard(new Point(12, 387), new Size(536, 44));
        Controls.Add(infoCard);

        _infoLabel = new Label
        {
            Location = new Point(12, 13),
            Size = new Size(512, 18),
            ForeColor = FrameShiftTheme.TextSecondary,
            AutoEllipsis = true
        };
        infoCard.Controls.Add(_infoLabel);

        // ── Footer buttons (y=443) ──────────────────────────────────────────────
        var previewLabel = isAudio ? "Preview 5s" : "Preview 10s";
        _buttonPreview = FrameShiftUiFactory.CreateFixedActionButton(previewLabel, new Point(12, 443), new Size(116, 34), primary: false);
        _buttonPreview.Click += async (_, _) => await PreviewAsync().ConfigureAwait(true);
        Controls.Add(_buttonPreview);

        var cancelButton = FrameShiftUiFactory.CreateFixedActionButton("Cancel", new Point(298, 443), new Size(120, 34), primary: false);
        cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);

        var applyButton = FrameShiftUiFactory.CreateFixedActionButton("Apply", new Point(428, 443), new Size(120, 34), primary: true);
        applyButton.DialogResult = DialogResult.OK;
        Controls.Add(applyButton);

        AcceptButton = applyButton;
        CancelButton = cancelButton;
        FormClosing += (_, _) => CleanupPreview();

        RefreshInfoLabel();
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
    }

    // ── Preset buttons ──────────────────────────────────────────────────────────

    private void AddPresetRow(Panel parent, (int Percent, string Label)[] presets, int y)
    {
        var count = presets.Length;
        var buttonWidth = count <= 6 ? 76 : 56;
        var gap = count <= 6 ? 8 : 6;
        var x = 18;

        foreach (var (pct, label) in presets)
        {
            var factor = pct / 100.0;
            var btn = FrameShiftUiFactory.CreateFixedActionButton(label, new Point(x, y), new Size(buttonWidth, 26), primary: false);
            btn.Click += (_, _) => SetSpeedFactor(factor);
            parent.Controls.Add(btn);
            x += buttonWidth + gap;
        }
    }

    // ── Speed value management ──────────────────────────────────────────────────

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

    private async Task PreviewAsync()
    {
        if (_previewing) return;
        _previewing = true;
        _buttonPreview.Enabled = false;

        try
        {
            if (_mediaKind == ChangeSpeedMediaKind.Audio)
            {
                await PreviewAudioAsync().ConfigureAwait(true);
            }
            else
            {
                await PreviewVideoAsync().ConfigureAwait(true);
            }
        }
        catch
        {
            CleanupPreview();
        }
        finally
        {
            _previewing = false;
            _buttonPreview.Enabled = true;
        }
    }

    private async Task PreviewAudioAsync()
    {
        CleanupAudioPreview();

        var settings = new ChangeSpeedSettings(
            Math.Clamp(_speedFactor, ChangeSpeedSettings.MinFactor, ChangeSpeedSettings.MaxFactor),
            _checkKeepPitch.Checked);

        var tempPath = Path.Combine(Path.GetTempPath(), $"fs_speed_preview_{Guid.NewGuid():N}.wav");
        var args = BuildAudioPreviewArguments(_inputPath, tempPath, settings, _sampleRate);

        var result = await _ffmpegRunner.RunAsync(
            _ffmpegPath,
            args,
            TimeSpan.FromSeconds(30),
            null,
            _inputPath,
            "Change Audio Speed",
            "Audio",
            CancellationToken.None).ConfigureAwait(true);

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

    private async Task PreviewVideoAsync()
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
        var args = BuildVideoPreviewArguments(_inputPath, tempPath, settings, _hasAudio, _sampleRate);

        var result = await _ffmpegRunner.RunAsync(
            _ffmpegPath,
            args,
            TimeSpan.FromSeconds(60),
            null,
            _inputPath,
            "Change Video Speed",
            "Video",
            CancellationToken.None).ConfigureAwait(true);

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
            _previewAudioPath = null;
        }
    }

    private void CleanupPreview()
    {
        CleanupAudioPreview();

        if (!string.IsNullOrWhiteSpace(_previewVideoPath))
        {
            try { ConversionActionHelper.DeleteIfExists(_previewVideoPath); } catch { }
            _previewVideoPath = null;
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
