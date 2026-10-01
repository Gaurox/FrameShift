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
    private readonly Func<IReadOnlyList<string>, TimeSpan, CancellationToken, Task<FfmpegRunResult>> _runPreview;

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
    private bool _closing;
    private CancellationTokenSource? _previewCts;

    public ChangePitchForm(string inputPath, string ffmpegPath, FfmpegRunner ffmpegRunner)
        : this(inputPath, ffmpegPath, ffmpegRunner, null) { }

    internal ChangePitchForm(string inputPath, string ffmpegPath, FfmpegRunner ffmpegRunner,
        Func<IReadOnlyList<string>, TimeSpan, CancellationToken, Task<FfmpegRunResult>>? runPreview)
    {
        _inputPath = inputPath;
        _runPreview = runPreview ?? ((arguments, duration, token) => ffmpegRunner.RunAsync(
            ffmpegPath, arguments, duration, null, inputPath, "Change Pitch", "Audio", token));
        var title = "Change Pitch";
        var iconPath = IconPaths.ContextMenuIco("change-pitch-audio-icon.ico");
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(640, 600), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, $"FrameShift - {title}");
        var header = FrameShiftUiFactory.CreateHeader($"FrameShift - {title}", $"Source: {Path.GetFileName(inputPath)}",
            iconPath, IconPaths.AppIcon, "♪");
        _infoLabel = FrameShiftUiFactory.CreateWrappingLabel("");
        _trackBar = new TrackBar { Name = "pitchSlider", Minimum = -12, Maximum = 12, Value = 0,
            SmallChange = 1, LargeChange = 3, TickFrequency = 1, AutoSize = true };
        _trackBar.ValueChanged += (_, _) => OnTrackBarChanged();
        _textSemitones = new TextBox { Name = "semitones", Text = "0" };
        _textPercent = new TextBox { Name = "percent", Text = "100" };
        _textSemitones.Leave += (_, _) => ApplySemitonesFromText();
        _textPercent.Leave += (_, _) => ApplyPercentFromText();
        _textSemitones.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplySemitonesFromText(); } };
        _textPercent.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyPercentFromText(); } };
        _checkKeepDuration = new CheckBox { Name = "keepDuration", Text = "Keep original duration", AutoSize = true, Checked = true };
        _checkKeepDuration.CheckedChanged += (_, _) => RefreshInfoLabel();
        var negative = FrameShiftUiFactory.CreateChoiceRow();
        var positive = FrameShiftUiFactory.CreateChoiceRow();
        foreach (var (semitones, label) in NegativePresets.Concat(PositivePresets))
        {
            var button = FrameShiftUiFactory.CreateMeasuredActionButton(label, false, 72);
            button.Click += (_, _) => SetSemitones(semitones);
            (semitones < 0 ? negative : positive).Controls.Add(button);
        }
        var content = FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateSection("Pitch", FrameShiftUiFactory.CreateVerticalStack(
                _trackBar, FrameShiftUiFactory.CreateFieldRow("&Semitones", _textSemitones, null, 120),
                FrameShiftUiFactory.CreateFieldRow("&Pitch", _textPercent, "%", 120),
                FrameShiftUiFactory.CreateWrappingLabel("Slider: −12 to +12 semitones. Manual input: −24 to +24."))),
            FrameShiftUiFactory.CreateSection("Presets", FrameShiftUiFactory.CreateVerticalStack(negative, positive)),
            FrameShiftUiFactory.CreateSection("Options", FrameShiftUiFactory.CreateChoiceRow(_checkKeepDuration)));
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

    public ChangePitchSettings? Selection { get; private set; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            var clamped = Math.Clamp(_semitones, ChangePitchSettings.MinSemitones, ChangePitchSettings.MaxSemitones);
            Selection = new ChangePitchSettings(clamped, _checkKeepDuration.Checked);
        }

        base.OnFormClosing(e);
        if (!e.Cancel) StopPreviewForClose();
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
        if (_previewing || _closing)
        {
            return;
        }

        _previewing = true;
        using var request = new CancellationTokenSource();
        _previewCts = request;
        _buttonPreview.Enabled = false;
        try
        {
            CleanupPreview();

            var settings = new ChangePitchSettings(
                Math.Clamp(_semitones, ChangePitchSettings.MinSemitones, ChangePitchSettings.MaxSemitones),
                _checkKeepDuration.Checked);

            var tempPath = Path.Combine(Path.GetTempPath(), $"fs_pitch_preview_{Guid.NewGuid():N}.wav");
            _previewPath = tempPath;
            var args = BuildPreviewArguments(_inputPath, tempPath, settings);

            var result = await _runPreview(args, TimeSpan.FromSeconds(30), request.Token).ConfigureAwait(true);

            request.Token.ThrowIfCancellationRequested();

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
            if (_closing) CleanupPreview();
            _previewCts = null;
            if (!_closing && !IsDisposed) _buttonPreview.Enabled = true;
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
            if (!_previewing) _previewPath = null;
        }
    }
}
