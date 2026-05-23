using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed class ChangePitchForm : Form
{
    private static readonly (int Semitones, string Label)[] NegativePresets =
        [(-12, "-12st"), (-7, "-7st"), (-5, "-5st"), (-3, "-3st"), (-1, "-1st")];

    private static readonly (int Semitones, string Label)[] PositivePresets =
        [(+1, "+1st"), (+3, "+3st"), (+5, "+5st"), (+7, "+7st"), (+12, "+12st")];

    private readonly string _inputPath;
    private readonly string _ffmpegPath;
    private readonly FfmpegRunner _ffmpegRunner;

    private readonly TrackBar _trackBar;
    private readonly TextBox _textSemitones;
    private readonly TextBox _textPercent;
    private readonly CheckBox _checkKeepDuration;
    private readonly Label _infoLabel;
    private readonly Button _buttonPreview;

    private bool _syncing;
    private double _semitones;
    private SoundPlayer? _previewPlayer;
    private string? _previewPath;
    private System.Windows.Forms.Timer? _previewTimer;
    private bool _previewing;

    public ChangePitchForm(string inputPath, string ffmpegPath, FfmpegRunner ffmpegRunner)
    {
        _inputPath = inputPath;
        _ffmpegPath = ffmpegPath;
        _ffmpegRunner = ffmpegRunner;

        var iconPath = IconPaths.ContextMenuIco("change-pitch-audio-icon.ico");
        FrameShiftWindowChrome.Apply(this, "FrameShift - Change Pitch");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 454);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var header = FrameShiftUiFactory.CreateFixedHeader(
            "FrameShift - Change Pitch",
            $"Source: {Path.GetFileName(inputPath)}",
            iconPath,
            IconPaths.AppIcon,
            "♪");
        Controls.Add(header);

        // Pitch amount section: y=82, h=190
        var pitchSection = FrameShiftUiFactory.CreateFixedSection(
            new Point(12, 82), new Size(536, 190), "Pitch amount");
        Controls.Add(pitchSection);

        _trackBar = new TrackBar
        {
            Location = new Point(12, 32),
            Size = new Size(512, 45),
            Minimum = -12,
            Maximum = 12,
            Value = 0,
            SmallChange = 1,
            LargeChange = 3,
            TickFrequency = 1,
            TickStyle = TickStyle.BottomRight,
            AutoSize = false
        };
        _trackBar.ValueChanged += (_, _) => OnTrackBarChanged();
        pitchSection.Controls.Add(_trackBar);

        var semiLabel = new Label
        {
            Location = new Point(18, 87),
            Size = new Size(70, 26),
            Text = "Semitones:",
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        pitchSection.Controls.Add(semiLabel);

        _textSemitones = FrameShiftUiFactory.CreateValueTextBox(textAlign: HorizontalAlignment.Center);
        _textSemitones.Text = "0";
        var semiHost = FrameShiftUiFactory.CreateFixedTextInputHost(_textSemitones, new Point(92, 83), new Size(68, 30));
        pitchSection.Controls.Add(semiHost);
        _textSemitones.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ApplySemitonesFromText();
            }
        };
        _textSemitones.Leave += (_, _) => ApplySemitonesFromText();

        var eqLabel = new Label
        {
            Location = new Point(166, 87),
            Size = new Size(18, 26),
            Text = "=",
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleCenter
        };
        pitchSection.Controls.Add(eqLabel);

        _textPercent = FrameShiftUiFactory.CreateValueTextBox(textAlign: HorizontalAlignment.Center);
        _textPercent.Text = "100.00";
        var pctHost = FrameShiftUiFactory.CreateFixedTextInputHost(_textPercent, new Point(188, 83), new Size(72, 30));
        pitchSection.Controls.Add(pctHost);
        _textPercent.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ApplyPercentFromText();
            }
        };
        _textPercent.Leave += (_, _) => ApplyPercentFromText();

        var pctSuffix = new Label
        {
            Location = new Point(264, 87),
            Size = new Size(16, 26),
            Text = "%",
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        pitchSection.Controls.Add(pctSuffix);

        var rangeHint = new Label
        {
            Location = new Point(288, 87),
            Size = new Size(242, 26),
            Text = "Manual input: −24 to +24 semitones",
            ForeColor = FrameShiftTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        pitchSection.Controls.Add(rangeHint);

        AddPresetRow(pitchSection, NegativePresets, y: 121);
        AddPresetRow(pitchSection, PositivePresets, y: 155);

        // Options section: y=284, h=56
        var optionsSection = FrameShiftUiFactory.CreateFixedSection(
            new Point(12, 284), new Size(536, 56), "Options");
        Controls.Add(optionsSection);

        _checkKeepDuration = new CheckBox
        {
            Text = "Keep original duration (pitch only — uses rubberband, no tempo change)",
            Location = new Point(18, 28),
            Size = new Size(500, 22),
            Checked = true,
            ForeColor = FrameShiftTheme.TextPrimary,
            FlatStyle = FlatStyle.Standard
        };
        _checkKeepDuration.CheckedChanged += (_, _) => RefreshInfoLabel();
        optionsSection.Controls.Add(_checkKeepDuration);

        // Info card: y=352, h=44
        var infoCard = FrameShiftUiFactory.CreateFixedInfoCard(new Point(12, 352), new Size(536, 44));
        Controls.Add(infoCard);

        _infoLabel = new Label
        {
            Location = new Point(12, 13),
            Size = new Size(512, 18),
            ForeColor = FrameShiftTheme.TextSecondary,
            AutoEllipsis = true
        };
        infoCard.Controls.Add(_infoLabel);

        // Footer buttons: y=408
        _buttonPreview = FrameShiftUiFactory.CreateFixedActionButton("Preview 5s", new Point(12, 408), new Size(108, 34), primary: false);
        _buttonPreview.Click += async (_, _) => await PreviewAsync().ConfigureAwait(true);
        Controls.Add(_buttonPreview);

        var cancelButton = FrameShiftUiFactory.CreateFixedActionButton("Cancel", new Point(292, 408), new Size(120, 34), primary: false);
        cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);

        var applyButton = FrameShiftUiFactory.CreateFixedActionButton("Apply", new Point(422, 408), new Size(126, 34), primary: true);
        applyButton.DialogResult = DialogResult.OK;
        Controls.Add(applyButton);

        AcceptButton = applyButton;
        CancelButton = cancelButton;

        FormClosing += (_, _) => CleanupPreview();

        RefreshInfoLabel();
    }

    public ChangePitchSettings? Selection { get; private set; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            var clamped = Math.Clamp(_semitones, ChangePitchSettings.MinSemitones, ChangePitchSettings.MaxSemitones);
            Selection = new ChangePitchSettings(clamped, _checkKeepDuration.Checked);
        }

        base.OnFormClosing(e);
    }

    private void AddPresetRow(Panel parent, (int Semitones, string Label)[] presets, int y)
    {
        const int ButtonWidth = 92;
        const int ButtonGap = 6;
        var x = 18;
        foreach (var (semitones, label) in presets)
        {
            var captured = semitones;
            var btn = FrameShiftUiFactory.CreateFixedActionButton(label, new Point(x, y), new Size(ButtonWidth, 28), primary: false);
            btn.Click += (_, _) => SetSemitones(captured);
            parent.Controls.Add(btn);
            x += ButtonWidth + ButtonGap;
        }
    }

    private void SetSemitones(double semitones)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            _semitones = Math.Clamp(semitones, ChangePitchSettings.MinSemitones, ChangePitchSettings.MaxSemitones);

            var trackValue = (int)Math.Round(Math.Clamp(_semitones, _trackBar.Minimum, _trackBar.Maximum));
            _trackBar.Value = trackValue;

            _textSemitones.Text = _semitones.ToString("0.##", CultureInfo.InvariantCulture);

            var factor = Math.Pow(2.0, _semitones / 12.0);
            _textPercent.Text = (factor * 100d).ToString("0.##", CultureInfo.InvariantCulture);

            RefreshInfoLabel();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnTrackBarChanged()
    {
        if (_syncing)
        {
            return;
        }

        SetSemitones(_trackBar.Value);
    }

    private void ApplySemitonesFromText()
    {
        var raw = _textSemitones.Text.Trim().Replace(',', '.');
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            SetSemitones(parsed);
        }
        else
        {
            _textSemitones.Text = _semitones.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    private void ApplyPercentFromText()
    {
        var raw = _textPercent.Text.Trim().Replace(',', '.');
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) && pct > 0)
        {
            var semitones = 12.0 * Math.Log2(pct / 100.0);
            SetSemitones(semitones);
        }
        else
        {
            var factor = Math.Pow(2.0, _semitones / 12.0);
            _textPercent.Text = (factor * 100d).ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    private void RefreshInfoLabel()
    {
        var factor = Math.Pow(2.0, _semitones / 12.0);
        var pct = factor * 100d;
        var modeText = _checkKeepDuration.Checked
            ? "pitch only, duration preserved"
            : "pitch + tempo change";
        var sign = _semitones >= 0 ? "+" : string.Empty;
        _infoLabel.Text = $"Output: {sign}{_semitones:0.##} semitones ({pct:0.##}%) — {modeText}. Output saved next to source.";
    }

    private async Task PreviewAsync()
    {
        if (_previewing)
        {
            return;
        }

        _previewing = true;
        _buttonPreview.Enabled = false;
        try
        {
            CleanupPreview();

            var settings = new ChangePitchSettings(
                Math.Clamp(_semitones, ChangePitchSettings.MinSemitones, ChangePitchSettings.MaxSemitones),
                _checkKeepDuration.Checked);

            var tempPath = Path.Combine(Path.GetTempPath(), $"fs_pitch_preview_{Guid.NewGuid():N}.wav");
            var args = BuildPreviewArguments(_inputPath, tempPath, settings);

            var result = await _ffmpegRunner.RunAsync(
                _ffmpegPath,
                args,
                TimeSpan.FromSeconds(30),
                null,
                _inputPath,
                "Change Pitch",
                "Audio",
                CancellationToken.None).ConfigureAwait(true);

            if (result.ExitCode != 0 || !File.Exists(tempPath))
            {
                ConversionActionHelper.DeleteIfExists(tempPath);
                return;
            }

            _previewPath = tempPath;
            _previewPlayer = new SoundPlayer(tempPath);
            _previewPlayer.Load();
            _previewPlayer.Play();

            _previewTimer = new System.Windows.Forms.Timer { Interval = 6000 };
            _previewTimer.Tick += (_, _) => CleanupPreview();
            _previewTimer.Start();
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

    private static IReadOnlyList<string> BuildPreviewArguments(string inputPath, string outputPath, ChangePitchSettings settings)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error",
            "-y",
            "-ss", "0", "-t", "5",
            "-i", inputPath,
            "-vn"
        };

        if (settings.KeepDuration)
        {
            args.Add("-af");
            args.Add($"rubberband=pitch={settings.PitchFactor.ToString("0.######", CultureInfo.InvariantCulture)}");
        }
        else
        {
            const int SampleRate = 44100;
            var targetRate = Math.Max(1000, (int)Math.Round(SampleRate * settings.PitchFactor));
            args.Add("-filter:a");
            args.Add($"asetrate={targetRate},aresample={SampleRate}");
        }

        args.Add("-c:a");
        args.Add("pcm_s16le");
        args.Add(outputPath);
        return args;
    }

    private void CleanupPreview()
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

        if (!string.IsNullOrWhiteSpace(_previewPath))
        {
            ConversionActionHelper.DeleteIfExists(_previewPath);
            _previewPath = null;
        }
    }
}
