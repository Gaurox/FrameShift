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
            ffmpegPath, arguments, duration, null, inputPath, L.Get("ChangePitchForm_Change_Pitch_9a8443c3"), "Audio", token));
        var title = L.Get("ChangePitchForm_Change_Pitch_9a8443c3");
        var iconPath = IconPaths.ContextMenuIco("change-pitch-audio-icon.ico");
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(640, 600), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, $"FrameShift - {title}");
        var header = FrameShiftUiFactory.CreateHeader($"FrameShift - {title}", L.Format("ChangePitchForm_Source_0_653ab450",Path.GetFileName(inputPath)),
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
        _checkKeepDuration = new CheckBox { Name = "keepDuration", Text = L.Get("ChangePitchForm_Keep_original_duration_ed997306"), AutoSize = true, Checked = true };
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
            FrameShiftUiFactory.CreateSection(L.Get("ChangePitchForm_Pitch_1d37f054"), FrameShiftUiFactory.CreateVerticalStack(
                _trackBar, FrameShiftUiFactory.CreateFieldRow(L.Get("ChangePitchForm_Semitones_b193f61e"), _textSemitones, null, 120),
                FrameShiftUiFactory.CreateFieldRow(L.Get("ChangePitchForm_Pitch_ca41a3ff"), _textPercent, "%", 120),
                FrameShiftUiFactory.CreateWrappingLabel(L.Get("ChangePitchForm_Slider_12_to_12_semitones_Manual_inp_de81df65")))),
            FrameShiftUiFactory.CreateSection(L.Get("ChangePitchForm_Presets_66948156"), FrameShiftUiFactory.CreateVerticalStack(negative, positive)),
            FrameShiftUiFactory.CreateSection(L.Get("ChangePitchForm_Options_59a2f992"), FrameShiftUiFactory.CreateChoiceRow(_checkKeepDuration)));
        _buttonPreview = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("ChangePitchForm_Preview_5s_dbda04dc"), false);
        _buttonPreview.Name = "previewButton";
        _buttonPreview.Click += async (_, _) => await PreviewAsync();
        var previewSection = FrameShiftUiFactory.CreateSection(L.Get("ChangePitchForm_Preview_68dd3b5d"), FrameShiftUiFactory.CreateVerticalStack(
            _buttonPreview, _infoLabel));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(previewSection, 0, content.RowCount++);
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("ChangePitchForm_Cancel_998b9c48"), false);
        cancel.DialogResult = DialogResult.Cancel;
        var apply = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("ChangePitchForm_Apply_3d57c12e"), true);
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
            ? L.Get("ChangePitchForm_pitch_only_duration_preserved_2a7f471a")
            : L.Get("ChangePitchForm_pitch_tempo_change_f692bdfc");
        var sign = _semitones >= 0 ? "+" : string.Empty;
        _infoLabel.Text = L.Format("ChangePitchForm_Output_0_1_0_semitones_2_0_3_Output__40a6bede",sign,_semitones,pct,modeText);
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
