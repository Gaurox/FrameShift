using System.Globalization;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Core.Progress;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed partial class CutVideoForm
{
    internal enum LoopContext { Cut, Natural, ForwardBackward }
    internal enum LoopBusy { Idle, Searching, Rendering, Switching }
    private LoopContext _loopContext;
    private LoopBusy _loopBusy;
    private FfmpegRunner _loopRunner = null!;
    private MediaProbeResult _loopProbe = null!;
    private VideoLoopColorPolicy _loopColorPolicy = null!;
    private Label _loopColorStatus = null!;
    private string _loopExecutable = "";
    private Task _loopTask = Task.CompletedTask;
    private CancellationTokenSource? _loopCancellation;
    private LoopVideoPlayer? _loopPlayer;
    private CancellationTokenSource? _loopPlaybackCancellation;
    private Task _loopPlaybackTask = Task.CompletedTask;
    private VideoLoopCandidate? _loopCandidate;
    private (int Start, int End) _candidateFrames;
    private bool _loopCustomSelection;
    private Control _loopOptions = null!;
    private Panel _loopOptionsHost = null!;
    private Control _loopAdvanced = null!;
    private Panel _loopAdvancedHost = null!;
    private Control _loopSpeedRow = null!;
    private TextBox _loopMinimum = null!;
    private NumericUpDown _loopRepeat = null!;
    private ComboBox _loopSensitivity = null!;
    private DataGridView _loopResults = null!;
    private ComboBox _loopSpeed = null!;
    private CheckBox _loopAudio = null!;
    private Label _loopStatus = null!;
    private ProgressBar _loopProgress = null!;
    private Button _findLoop = null!;
    private Button _previewLoop = null!;
    private Button _fullPreviewLoop = null!;
    private Button _pingPongLoop = null!;
    private Button _stopSearch = null!;
    private Button _stopPreview = null!;
    private Button _advancedLoop = null!;
    private Button _loopBackToSelection = null!;
    private Button _cutOk = null!;
    private Button _exportAllLoops = null!;
    private FlowLayoutPanel _cutActions = null!;
    private Control _cutSelectionControls = null!;

    public IReadOnlyDictionary<string, string>? SelectedLoopOptions { get; private set; }
    public IReadOnlyList<VideoLoopExportSelection>? SelectedLoopExports { get; private set; }

    private Control CreateLoopControls(string executable, MediaProbeResult probe, FfmpegRunner runner)
    {
        _loopExecutable = executable; _loopProbe = probe; _loopRunner = runner;
        _loopColorPolicy = VideoLoopColorPolicy.Evaluate(probe);
        _exportAllLoops = FrameShiftUiFactory.CreateMeasuredActionButton("Export All Candidates (0)", false);
        _exportAllLoops.Name = "exportAllLoopCandidates";
        _exportAllLoops.Visible = false;
        _exportAllLoops.Click += (_, _) => ConfirmAllLoopCandidates();
        Shown += async (_, _) =>
        {
            if (_loopColorPolicy.CanRender && _loopColorPolicy.ConvertsToSdr)
                await StartLoopWorkAsync(PreflightLoopColorAsync, LoopBusy.Switching, null);
        };
        _loopMinimum = new TextBox { Name = "loopMinimum", PlaceholderText = "Auto" };
        _loopRepeat = new NumericUpDown { Name = "loopRepeat", Minimum = 1, Maximum = 10, Value = 1 };
        _loopSensitivity = Choice("loopSensitivity", "Strict", "Normal", "Permissive");
        _loopSensitivity.SelectedIndex = 1;
        _loopResults = CreateLoopCandidateGrid();
        _loopSpeed = Choice("loopSpeed", "1x", "0.75x", "0.5x");
        _loopAudio = new CheckBox { Name = "loopAudio", Text = "Keep source audio (junction not analyzed)", Checked = probe.HasAudio, AutoSize = true };
        _loopSpeed.SelectedIndexChanged += (_, _) => InvalidateLoopPreview();
        _loopStatus = FrameShiftUiFactory.CreateWrappingLabel(_loopColorPolicy.CanRender
            ? $"{_loopColorPolicy.Message} Junctions need visual review. Preview before exporting."
            : _loopColorPolicy.Message);
        _loopStatus.Name = "loopStatus";
        _loopProgress = new ProgressBar { Name = "loopProgress", Dock = DockStyle.Top, Minimum = 0, Maximum = 100, Visible = false };
        _findLoop = FrameShiftUiFactory.CreateMeasuredActionButton("Find Loop", false);
        _previewLoop = FrameShiftUiFactory.CreateMeasuredActionButton("Preview junction", false);
        _fullPreviewLoop = FrameShiftUiFactory.CreateMeasuredActionButton("Full loop preview", false);
        _pingPongLoop = FrameShiftUiFactory.CreateMeasuredActionButton("Create forward/backward loop", false);
        _stopSearch = FrameShiftUiFactory.CreateMeasuredActionButton("Stop search", false);
        _stopPreview = FrameShiftUiFactory.CreateMeasuredActionButton("Stop preview", false);
        _loopBackToSelection = FrameShiftUiFactory.CreateMeasuredActionButton("Back to Cut", false);
        _advancedLoop = FrameShiftUiFactory.CreateMeasuredActionButton("Advanced options", false);
        _loopBackToSelection.Click += async (_, _) => await SetLoopContextAsync(LoopContext.Cut);
        _advancedLoop.Click += (_, _) => ToggleLoopAdvancedOptions();
        _loopSpeedRow = FrameShiftUiFactory.CreateFieldRow("Playback speed", _loopSpeed, logicalEditorWidth: 100);
        _loopAdvanced = FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateFieldRow("Minimum duration", _loopMinimum, "seconds (empty = Auto)", logicalEditorWidth: 120),
            FrameShiftUiFactory.CreateFieldRow("Sensitivity", _loopSensitivity, logicalEditorWidth: 180),
            FrameShiftUiFactory.CreateFieldRow("Export repeats", _loopRepeat, logicalEditorWidth: 80), _loopAudio,
            FrameShiftUiFactory.CreateChoiceRow(_fullPreviewLoop),
            FrameShiftUiFactory.CreateWrappingLabel("Full preview: three continuous proxy cycles. Long selections may exceed the temporary-work budget; use Preview junction instead."));
        _loopAdvancedHost = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
        _loopOptions = FrameShiftUiFactory.CreateVerticalStack(
            _loopProgress, _loopStatus, _loopResults,
            FrameShiftUiFactory.CreateChoiceRow(_previewLoop, _stopPreview, _loopBackToSelection),
            _loopSpeedRow, _advancedLoop, _loopAdvancedHost,
            FrameShiftUiFactory.CreateWrappingLabel("Export: unique MP4 next to the source. Preview is silent."));
        _loopOptions.Visible = false;
        // Detached options cost no space or layout work in ordinary Cut Video.
        _loopOptionsHost = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
        _findLoop.Name = "findLoop"; _previewLoop.Name = "previewLoop";
        _fullPreviewLoop.Name = "fullLoopPreview";
        _pingPongLoop.Name = "forwardBackwardLoop";
        _stopSearch.Name = "stopLoopSearch"; _stopPreview.Name = "stopLoopPreview";
        _findLoop.Click += async (_, _) => await StartLoopWorkAsync(AnalyzeLoopAsync, LoopBusy.Searching, LoopContext.Natural);
        _previewLoop.Click += async (_, _) => await StartLoopWorkAsync(t => PreviewLoopAsync(VideoLoopPreviewKind.Junction, t), LoopBusy.Rendering, null);
        _fullPreviewLoop.Click += async (_, _) => await StartLoopWorkAsync(t => PreviewLoopAsync(VideoLoopPreviewKind.FullLoop, t), LoopBusy.Rendering, null);
        _pingPongLoop.Click += async (_, _) => await SetLoopContextAsync(LoopContext.ForwardBackward);
        _stopSearch.Click += (_, _) => _loopCancellation?.Cancel();
        _stopPreview.Click += (_, _) => StopLoopPreview();
        var controls = new List<Control> { FrameShiftUiFactory.CreateChoiceRow(_findLoop, _stopSearch, _pingPongLoop) };
        _loopColorStatus = FrameShiftUiFactory.CreateWrappingLabel(_loopColorPolicy.Message);
        _loopColorStatus.Name = "loopColorStatus";
        controls.Add(_loopColorStatus);
        controls.Add(_loopOptionsHost);
        return FrameShiftUiFactory.CreateSection("Loop tools", FrameShiftUiFactory.CreateVerticalStack(controls.ToArray()));
    }

    private void ToggleLoopAdvancedOptions()
    {
        var expand = _loopAdvanced.Parent is null;
        if (expand) _loopAdvancedHost.Controls.Add(_loopAdvanced);
        else _loopAdvancedHost.Controls.Remove(_loopAdvanced);
        _advancedLoop.Text = expand ? "Hide advanced options" : "Advanced options";
    }

    internal Task SetLoopContextAsync(LoopContext context) =>
        StartLoopWorkAsync(_ => Task.CompletedTask, LoopBusy.Switching, context);

    private void ApplyLoopContext(LoopContext context)
    {
        var changed = _loopContext != context;
        if (changed) InvalidateLoopPreviewCache();
        _loopContext = context;
        if (context == LoopContext.Cut)
        {
            CancelNativeWork(); ClearNativeSelection();
            _loopOptionsHost.Controls.Remove(_loopOptions);
            _loopOptions.Visible = false;
            _loopCandidate = null;
            _loopCustomSelection = false;
            SelectedLoopOptions = null;
            RefreshSelectionUi(false);
            RevealLoopControl(_textStart);
        }
        else
        {
            ShowLoopOptions();
            if (changed)
            {
                var natural = context == LoopContext.Natural;
                _loopAudio.Checked = natural && _loopProbe.HasAudio;
                if (natural) _loopSpeed.SelectedIndex = 0;
                _loopStatus.Text = natural ? "Natural loop: junction to review. Audio continuity is not analyzed."
                    : "Forward/backward: the selection plays forwards, then backwards, without audio. VFR uses the source average FPS.";
            }
            RefreshSelectionUi(false);
        }
        UpdateLoopCommands();
    }

    private void ShowLoopOptions()
    {
        if (_loopOptions.Parent is null) _loopOptionsHost.Controls.Add(_loopOptions);
        _loopOptions.Visible = true;
        RevealLoopControl(_previewLoop);
    }

    private void RevealLoopControl(Control target)
    {
        if (!IsHandleCreated || _closing || IsDisposed) return;
        BeginInvoke(new Action(() =>
        {
            if (_closing || IsDisposed || target.IsDisposed) return;
            for (var parent = target.Parent; parent is not null; parent = parent.Parent)
                if (parent is Panel { AutoScroll: true } viewport) { viewport.ScrollControlIntoView(target); break; }
        }));
    }

    private static ComboBox Choice(string name, params string[] items)
    {
        var combo = new ComboBox { Name = name, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(items);
        if (items.Length > 0) combo.SelectedIndex = 0;
        return combo;
    }

    internal Task StartLoopWorkAsync(Func<CancellationToken, Task> work,
        LoopBusy busy = LoopBusy.Searching, LoopContext? context = LoopContext.Natural)
    {
        if (_closing || IsDisposed || !CheckSourceStillValid() || (!_loopColorPolicy.CanRender && context != LoopContext.Cut)) return Task.CompletedTask;
        var previous = _loopTask;
        _loopCancellation?.Cancel();
        StopLoopPlayback();
        _previewTimer.Stop();
        _previewCancellation?.Cancel();
        CancelNativeWork();
        var cts = new CancellationTokenSource();
        _loopCancellation = cts;
        _loopBusy = busy;
        UpdateLoopCommands();
        _loopTask = RunLoopWorkAsync(previous, work, cts, context);
        return _loopTask;
    }

    private async Task RunLoopWorkAsync(Task previous, Func<CancellationToken, Task> work,
        CancellationTokenSource cts, LoopContext? context)
    {
        try
        {
            await previous;
            await _loopPlaybackTask;
            await _loopPreviewCleanupTask;
            await _previewTask;
            await _nativeTask;
            cts.Token.ThrowIfCancellationRequested();
            if (context is not null && context != LoopContext.Cut)
                await _loopColorPolicy.RequireAvailableAsync(_loopRunner, _loopExecutable, cts.Token);
            if (context is { } target) ApplyLoopContext(target);
            await work(cts.Token);
            if (!_closing) CheckSourceStillValid();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (!_closing && !_sourceInvalid && ReferenceEquals(_loopCancellation, cts))
                _loopStatus.Text = _loopBusy == LoopBusy.Searching ? "Search stopped. Current selection retained." : "Preview stopped.";
        }
        catch (VideoLoopSourceChangedException) { InvalidateChangedSource(); }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"Cut Video loop failed: {ex}");
            if (!_closing && !_sourceInvalid && !IsDisposed && ReferenceEquals(_loopCancellation, cts)) _loopStatus.Text = $"Loop unavailable: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_loopCancellation, cts))
            {
                _loopCancellation = null;
                _loopBusy = LoopBusy.Idle;
                if (!_closing && !IsDisposed) UpdateLoopCommands();
            }
            cts.Dispose();
        }
    }

    private void UpdateLoopCommands()
    {
        var busy = _loopBusy != LoopBusy.Idle || _nativeBusy;
        var loop = _loopContext != LoopContext.Cut;
        var natural = _loopContext == LoopContext.Natural;
        _cutOk.Text = loop ? (_loopColorPolicy.ConvertsToSdr ? L.Get("Loop_ExportSdr") : L.Get("Loop_Export")) : L.Get("Loop_ExportSelection");
        _cutOk.Name = loop ? "exportLoop" : "exportSelection";
        _cutOk.Enabled = !busy && !_sourceInvalid && (!loop || _loopColorPolicy.CanRender);
        var batchCount = BatchLoopCandidates().Count;
        _exportAllLoops.Text = $"Export All Candidates ({batchCount})";
        _exportAllLoops.Visible = natural && batchCount > 0;
        // Detach the optional command so the shared action bar measures ordinary
        // Cut with its original two buttons, including on a narrow window.
        if (natural && batchCount > 0)
        {
            if (_exportAllLoops.Parent is null)
            {
                _cutActions.Controls.Add(_exportAllLoops);
                _cutActions.Controls.SetChildIndex(_exportAllLoops, 1);
            }
        }
        else _cutActions.Controls.Remove(_exportAllLoops);
        _exportAllLoops.Enabled = !busy && !_sourceInvalid && natural && batchCount > 0 && _loopColorPolicy.CanRender;
        _findLoop.Enabled = _pingPongLoop.Enabled = !_sourceInvalid && _loopColorPolicy.CanRender && _loopBusy != LoopBusy.Switching;
        _loopBackToSelection.Enabled = !_sourceInvalid && _loopBusy != LoopBusy.Switching;
        _previewLoop.Enabled = _fullPreviewLoop.Enabled = !busy && !_sourceInvalid && loop && _loopColorPolicy.CanRender;
        _stopSearch.Visible = _loopBusy == LoopBusy.Searching;
        _stopSearch.Enabled = _loopBusy == LoopBusy.Searching;
        _stopPreview.Visible = _loopBusy == LoopBusy.Rendering || _loopPlaybackCancellation is not null || _nativeBusy;
        _stopPreview.Enabled = _stopPreview.Visible;
        _cutSelectionControls.Enabled = !busy && !_sourceInvalid;
        foreach (var control in new Control[] { _textStart, _textEnd, _textStartTime, _textEndTime,
            _buttonStartPrev, _buttonStartNext, _buttonEndPrev, _buttonEndNext,
            _loopMinimum, _loopSensitivity, _loopRepeat }) control.Enabled = !busy && !_sourceInvalid;
        // Native steps are queued, so repeated arrow clicks are retained while resolving.
        foreach (var button in new[] { _buttonStartPrev, _buttonStartNext, _buttonEndPrev, _buttonEndNext })
            button.Enabled = !_sourceInvalid && _loopBusy == LoopBusy.Idle && !_nativeConfirming;
        _loopResults.Visible = natural && _loopResults.Rows.Count > 0;
        _loopResults.Enabled = !_nativeBusy && !_sourceInvalid && _loopBusy is LoopBusy.Idle or LoopBusy.Searching && _loopResults.Rows.Count > 0;
        _loopSpeedRow.Visible = _loopContext == LoopContext.ForwardBackward;
        _loopSpeed.Enabled = !busy && !natural;
        _loopAudio.Enabled = !busy && natural && _loopProbe.HasAudio;
        _loopProgress.Style = _nativeBusy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        _loopProgress.Visible = _nativeBusy || _loopBusy is LoopBusy.Searching or LoopBusy.Rendering;
    }

    internal double ReadLoopMinimum()
    {
        if (string.IsNullOrWhiteSpace(_loopMinimum.Text))
            return VideoLoopDetector.AutoMinimum(_loopProbe.Duration?.TotalSeconds ?? 0, _fps);
        if (!double.TryParse(_loopMinimum.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var seconds) &&
            !double.TryParse(_loopMinimum.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
            throw new ArgumentException("Minimum duration must be empty (Auto) or a number between 0.1 and 600 seconds.");
        if (!double.IsFinite(seconds) || seconds < 0.1 || seconds > 600)
            throw new ArgumentException("Minimum duration must be between 0.1 and 600 seconds. This does not limit source or candidate duration.");
        return seconds;
    }

    private async Task PreflightLoopColorAsync(CancellationToken token)
    {
        try { await _loopColorPolicy.RequireAvailableAsync(_loopRunner, _loopExecutable, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _loopColorPolicy = _loopColorPolicy with { CanRender = false, Message = ex.Message };
            if (!_closing && !IsDisposed) _loopColorStatus.Text = _loopStatus.Text = ex.Message;
        }
    }

    private async Task AnalyzeLoopAsync(CancellationToken token)
    {
        var progress = new Progress<VideoLoopProgress>(p =>
        {
            if (_closing || IsDisposed || token.IsCancellationRequested || _loopBusy != LoopBusy.Searching ||
                _loopCancellation?.Token != token) return;
            _loopProgress.Value = Math.Clamp(p.Percent, 0, 100);
            _loopStatus.Text = p.Message;
            if (p.Results is { } batch) ApplyLoopAnalysis(batch);
        });
        var result = await new VideoLoopDetector(_loopRunner).AnalyzeAsync(_loopExecutable, _inputPath, _loopProbe,
            string.IsNullOrWhiteSpace(_loopMinimum.Text) ? null : ReadLoopMinimum(),
            (LoopSensitivity)_loopSensitivity.SelectedIndex, progress, token);
        token.ThrowIfCancellationRequested();
        if (_closing || IsDisposed || !CheckSourceStillValid()) return;
        ApplyLoopAnalysis(result);
    }

    private VideoLoopSettings ReadLoopSettings(int? repetitions = null) => new(
        _loopContext == LoopContext.ForwardBackward ? VideoLoopMode.PingPong : VideoLoopMode.Natural,
        _loopContext == LoopContext.ForwardBackward ? _loopSpeed.SelectedIndex switch { 1 => 0.75, 2 => 0.5, _ => 1 } : 1,
        repetitions ?? (int)_loopRepeat.Value, _loopContext == LoopContext.Natural && _loopAudio.Checked);

    private void ConfirmSelection()
    {
        if (_loopBusy != LoopBusy.Idle || _nativeBusy || _closing || !CheckSourceStillValid()) return;
        if (NativeTimingEnabled) { _ = ConfirmNativeLoopAsync(); return; }
        if (_loopContext != LoopContext.Cut) { ConfirmLoop(); return; }
        SelectedLoopOptions = null;
        SelectedLoopExports = null;
        ConfirmCut();
    }

    private void ConfirmLoop()
    {
        if (_loopBusy != LoopBusy.Idle || _nativeBusy || _closing || !CheckSourceStillValid()) return;
        try
        {
            _loopColorPolicy.RequireSupported();
            if (!ApplyBoundaryFromText("start") || !ApplyBoundaryFromText("end")) return;
            var settings = ReadLoopSettings(); settings.Validate();
            SelectedLoopExports = null;
            SelectedLoopOptions = LoopRenderBounds is { } c
                ? VideoLoopExportSelection.FromCandidate(c, settings).ToOptions() : settings.ToOptions();
            ConfirmCut();
        }
        catch (Exception ex) { ShowActionError(ex.Message); }
    }

    private IReadOnlyList<VideoLoopCandidate> BatchLoopCandidates() => _loopResults.Rows.Cast<DataGridViewRow>()
        .Select(row => row.Tag).OfType<VideoLoopCandidate>()
        .Where(c => double.IsFinite(c.StartSeconds) && double.IsFinite(c.EndSeconds) && c.StartSeconds >= 0 &&
            c.DurationSeconds > 0 && c.EndSeconds <= (_loopProbe.Duration?.TotalSeconds ?? 0) + 1 / _fps)
        .DistinctBy(c => (c.StartSeconds, c.EndSeconds)).ToArray();

    private void ConfirmAllLoopCandidates()
    {
        if (_loopContext != LoopContext.Natural || _loopBusy != LoopBusy.Idle || _nativeBusy || _closing || !CheckSourceStillValid()) return;
        try
        {
            _loopColorPolicy.RequireSupported();
            var settings = ReadLoopSettings(); settings.Validate();
            var snapshot = BatchLoopCandidates().Select(c => VideoLoopExportSelection.FromCandidate(c, settings) with
                { SourceStamp = _sourceStamp }).ToArray();
            if (snapshot.Length == 0) return;
            SelectedLoopExports = Array.AsReadOnly(snapshot);
            SelectedLoopOptions = null;
            // Native bounds in each snapshot remain authoritative; these ordinary
            // cut options only preserve the existing action validation contract.
            Selection = new(1, _totalFrames, _fps);
            StopPreview();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { ShowActionError(ex.Message); }
    }

    private void InvalidateLoopPreview()
    {
        if (_loopBusy == LoopBusy.Rendering) _loopCancellation?.Cancel();
        StopLoopPlayback();
        InvalidateLoopPreviewCache();
    }

    private void StopLoopPreview()
    {
        if (_nativeBusy) _nativeCancellation?.Cancel();
        if (_loopBusy == LoopBusy.Rendering) _loopCancellation?.Cancel();
        StopLoopPlayback();
    }

    private void StopLoopPlayback() => _loopPlaybackCancellation?.Cancel();

    private sealed class LoopUiProgress(CutVideoForm owner, CancellationToken token) : IProgressReporter
    {
        private readonly Progress<VideoLoopProgress> _progress = new(p =>
        {
            if (owner._closing || owner.IsDisposed || token.IsCancellationRequested || owner._loopBusy != LoopBusy.Rendering ||
                owner._loopCancellation?.Token != token) return;
            owner._loopProgress.Value = Math.Clamp(p.Percent, 0, 100);
            owner._loopStatus.Text = p.Message;
        });
        public bool IsCancellationRequested => token.IsCancellationRequested;
        public bool IsQueueItemRemovalRequested(string inputPath) => false;
        public bool IsQueueItemCancellationRequested(string inputPath) => false;
        public void ReportQueue(IReadOnlyList<string> items) { }
        public void ReportQueueItem(string currentFile, string state, string? message = null) { }
        public void ReportProgress(int value, string? file, string? action, string? message, string? etaText = null) =>
            ((IProgress<VideoLoopProgress>)_progress).Report(new(value / 10, message ?? "Rendering loop..."));
        public void ReportState(string state, string? message = null) =>
            ((IProgress<VideoLoopProgress>)_progress).Report(new(0, message ?? state));
    }
}
