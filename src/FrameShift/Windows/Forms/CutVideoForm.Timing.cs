using FrameShift.Core.Actions;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed partial class CutVideoForm
{
    private VideoLoopTimingIndex? _timingIndex;
    private VideoLoopSourceStamp? _sourceStamp;
    private System.Windows.Forms.Timer? _sourceTimer;
    private bool _sourceInvalid;
    private bool _nativeBusy;
    private bool _nativeConfirming;
    private Task _nativeTask = Task.CompletedTask;
    private CancellationTokenSource? _nativeCancellation;
    private int _nativeWorkVersion;
    private double? _nativeStartTime;
    private double? _nativeEndTime;
    private VideoLoopFramePosition? _nativeStartPosition;
    private VideoLoopFramePosition? _nativeEndPosition;
    private bool _nativeStartNumberKnown;
    private bool _nativeEndNumberKnown;
    private (int Start, int End) _nativeFrameSnapshot;
    private bool NativeTimingEnabled => _timingIndex is not null && _loopContext != LoopContext.Cut;
    private VideoLoopCandidate? LoopRenderBounds => NativeTimingEnabled && _nativeStartTime is { } start && _nativeEndTime is { } end
        ? new(start, end, _loopCandidate?.Quality ?? 0) : _loopCandidate;

    private void InitializeNativeTiming()
    {
        _nativeFrameSnapshot = (_selection.StartFrame, _selection.EndFrame);
        if (!File.Exists(_inputPath)) return; // Existing injected-preview tests can use a virtual source.
        _sourceStamp = VideoLoopSourceStamp.Read(_inputPath);
        _timingIndex = new(_loopRunner, _loopExecutable, _inputPath, _loopProbe);
        _sourceTimer = new() { Interval = 750 };
        _sourceTimer.Tick += (_, _) => CheckSourceStillValid();
        Shown += (_, _) => _sourceTimer.Start();
    }

    private bool CheckSourceStillValid()
    {
        if (_sourceInvalid) return false;
        if (_sourceStamp is null) return true;
        try { if (VideoLoopSourceStamp.Read(_inputPath) == _sourceStamp) return true; }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        InvalidateChangedSource();
        return false;
    }

    private void InvalidateChangedSource()
    {
        if (_sourceInvalid || _closing) return;
        _sourceInvalid = true; _sourceTimer?.Stop();
        _previewTimer.Stop(); _previewCancellation?.Cancel(); _loopCancellation?.Cancel();
        CancelNativeWork(); StopLoopPlayback(); InvalidateLoopPreviewCache();
        _timingIndex?.Dispose();
        _loopCandidate = null; ClearNativeSelection();
        _updatingLoopResults = true;
        try { _loopResults.Rows.Clear(); }
        finally { _updatingLoopResults = false; }
        StopPreview();
        _labelPreviewState.Text = _loopStatus.Text = "The source changed. Reopen Cut Video to refresh its metadata and selection.";
        UpdateLoopCommands();
    }

    private void ClearNativeSelection()
    {
        _nativeStartTime = _nativeEndTime = null;
        _nativeStartPosition = _nativeEndPosition = null;
        _nativeStartNumberKnown = _nativeEndNumberKnown = false;
        _nativeFrameSnapshot = (_selection.StartFrame, _selection.EndFrame);
    }

    private void SetNativeCandidate(VideoLoopCandidate candidate)
    {
        CancelNativeWork();
        ClearNativeSelection();
        if (!NativeTimingEnabled) return;
        _nativeStartTime = candidate.StartSeconds; _nativeEndTime = candidate.EndSeconds;
        if (_timingIndex!.TryGetIndexedPosition(candidate.StartSeconds, false) is { } start) CommitNativeBoundary("start", start, false);
        if (_timingIndex.TryGetIndexedPosition(candidate.EndSeconds, true) is { } end) CommitNativeBoundary("end", end, false);
    }

    private void InvalidateNativeFrameEdits()
    {
        var current = (_selection.StartFrame, _selection.EndFrame);
        if (_nativeFrameSnapshot == current) return;
        CancelNativeWork();
        if (_nativeFrameSnapshot.Start != current.StartFrame) { _nativeStartTime = null; _nativeStartPosition = null; _nativeStartNumberKnown = true; }
        if (_nativeFrameSnapshot.End != current.EndFrame) { _nativeEndTime = null; _nativeEndPosition = null; _nativeEndNumberKnown = true; }
        _nativeFrameSnapshot = current;
    }

    private void CancelNativeWork()
    {
        _nativeCancellation?.Cancel();
        _nativeCancellation?.Dispose(); _nativeCancellation = null;
        _nativeWorkVersion++;
        _nativeBusy = false; _nativeConfirming = false;
    }

    private Task QueueNativeWork(Func<CancellationToken, Task> work)
    {
        if (_closing || !CheckSourceStillValid()) return Task.CompletedTask;
        if (_nativeCancellation?.IsCancellationRequested == true) { _nativeCancellation.Dispose(); _nativeCancellation = null; }
        _nativeCancellation ??= new CancellationTokenSource();
        var token = _nativeCancellation.Token;
        var version = ++_nativeWorkVersion;
        var previous = _nativeTask;
        _previewTimer.Stop(); _previewCancellation?.Cancel(); StopLoopPlayback();
        _nativeBusy = true; UpdateLoopCommands();
        _nativeTask = RunNativeWorkAsync(previous, work, version, token);
        return _nativeTask;
    }

    private async Task RunNativeWorkAsync(Task previous, Func<CancellationToken, Task> work, int version, CancellationToken token)
    {
        try
        {
            await previous; await _previewTask; await _loopPlaybackTask;
            token.ThrowIfCancellationRequested();
            await work(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (VideoLoopSourceChangedException) { InvalidateChangedSource(); }
        catch (Exception ex)
        {
            if (!_closing && !_sourceInvalid && !token.IsCancellationRequested)
                _labelPreviewState.Text = _loopStatus.Text = $"Native timing unavailable: {ex.Message}";
        }
        finally
        {
            if (version == _nativeWorkVersion)
            {
                _nativeBusy = false; _nativeConfirming = false;
                if (!_closing && !IsDisposed) UpdateLoopCommands();
            }
        }
    }

    private Action<string> IndexingStatus(CancellationToken token)
    {
        var version = _nativeWorkVersion;
        return message =>
        {
            void Apply()
            {
                if (!_closing && !_sourceInvalid && !token.IsCancellationRequested && version == _nativeWorkVersion)
                    _labelPreviewState.Text = _loopStatus.Text = message;
            }
            if (IsDisposed || _closing) return;
            if (!InvokeRequired) Apply();
            else if (IsHandleCreated)
                try { BeginInvoke((Action)Apply); } catch (InvalidOperationException) { }
        };
    }

    private async Task<VideoLoopFramePosition> ResolveNativeBoundaryAsync(string boundary, int frame,
        double? time, CancellationToken token)
    {
        if (time is { } seconds)
            return await _timingIndex!.ResolveTimeAsync(seconds, boundary == "end", token);
        return await _timingIndex!.ResolveFrameAsync(frame, token, IndexingStatus(token));
    }

    private void CommitNativeBoundary(string boundary, VideoLoopFramePosition position, bool fromFrameNumber)
    {
        if (boundary == "start")
        {
            _nativeStartPosition = position; _nativeStartTime = position.Time;
            _nativeStartNumberKnown = position.Number is not null;
            if (position.Number is { } n) _selection.StartFrame = n;
        }
        else
        {
            _nativeEndPosition = position;
            if (fromFrameNumber || _nativeEndTime > position.End) _nativeEndTime = position.End;
            _nativeEndNumberKnown = position.Number is not null;
            if (position.Number is { } n) _selection.EndFrame = n;
        }
        // Once an ordinal prefix exists, replace both approximate labels together.
        if (_nativeStartTime is { } startTime && _timingIndex!.TryGetIndexedPosition(startTime, false) is { } knownStart)
        {
            _nativeStartPosition = knownStart; _nativeStartNumberKnown = true; _selection.StartFrame = knownStart.Number!.Value;
        }
        if (_nativeEndTime is { } endTime && _timingIndex!.TryGetIndexedPosition(endTime, true) is { } knownEnd)
        {
            _nativeEndPosition = knownEnd; _nativeEndNumberKnown = true; _selection.EndFrame = knownEnd.Number!.Value;
        }
        _nativeFrameSnapshot = (_selection.StartFrame, _selection.EndFrame);
        if (_loopCandidate is not null) _candidateFrames = _nativeFrameSnapshot;
    }

    private async Task ResolveNativeSelectionAsync(CancellationToken token)
    {
        if (!NativeTimingEnabled) return;
        if (_nativeStartTime is null)
        {
            var start = await _timingIndex!.ResolveFrameAsync(_selection.StartFrame, token, IndexingStatus(token));
            token.ThrowIfCancellationRequested(); CommitNativeBoundary("start", start, true);
        }
        else if (_nativeStartPosition is null)
        {
            var start = await _timingIndex!.ResolveTimeAsync(_nativeStartTime.Value, false, token);
            token.ThrowIfCancellationRequested(); CommitNativeBoundary("start", start, false);
        }
        if (_nativeEndTime is null)
        {
            var end = await _timingIndex!.ResolveFrameAsync(_selection.EndFrame, token, IndexingStatus(token));
            token.ThrowIfCancellationRequested(); CommitNativeBoundary("end", end, true);
        }
        else if (_nativeEndPosition is null)
        {
            var end = await _timingIndex!.ResolveTimeAsync(_nativeEndTime.Value, true, token);
            token.ThrowIfCancellationRequested(); CommitNativeBoundary("end", end, false);
        }
        if (_nativeStartTime >= _nativeEndTime) throw new InvalidOperationException("The native selection must contain at least one frame.");
        RefreshSelectionUi(false);
    }

    internal Task StepNativeBoundaryAsync(string boundary, int delta) => QueueNativeWork(async token =>
    {
        await ResolveNativeSelectionAsync(token);
        var frame = boundary == "start" ? _selection.StartFrame : _selection.EndFrame;
        var time = boundary == "start" ? _nativeStartTime : _nativeEndTime;
        var current = await ResolveNativeBoundaryAsync(boundary, frame, time, token);
        var neighbor = await _timingIndex!.NeighborAsync(current, delta, token);
        token.ThrowIfCancellationRequested();
        if (neighbor is null || boundary == "start" && neighbor.Time >= _nativeEndTime || boundary == "end" && neighbor.End <= _nativeStartTime) return;
        // Keep the untouched boundary in native time, even if its displayed frame number is approximate.
        MarkCustomLoopSelection();
        if (boundary == "start")
        {
            _selection.StartFrame = neighbor.Number ?? Math.Clamp(_selection.StartFrame + delta, 1, _selection.EndFrame);
            CommitNativeBoundary("start", neighbor, true);
        }
        else
        {
            _selection.EndFrame = neighbor.Number ?? Math.Clamp(_selection.EndFrame + delta, _selection.StartFrame, _totalFrames);
            CommitNativeBoundary("end", neighbor, true); _nativeEndTime = neighbor.End;
        }
        _uiState.ActiveBoundary = boundary;
        RefreshSelectionUi(true);
    });

    private void ApplyNativeTimeEdit(string boundary, double seconds)
    {
        CancelNativeWork(); MarkCustomLoopSelection();
        if (boundary == "start") { _nativeStartTime = seconds; _nativeStartPosition = null; _nativeStartNumberKnown = false; }
        else { _nativeEndTime = seconds; _nativeEndPosition = null; _nativeEndNumberKnown = false; }
        _nativeFrameSnapshot = (_selection.StartFrame, _selection.EndFrame);
    }

    internal Task ConfirmNativeLoopAsync()
    {
        if (_nativeBusy || _loopBusy != LoopBusy.Idle || _closing || !CheckSourceStillValid()) return Task.CompletedTask;
        if (!ApplyBoundaryFromText("start") || !ApplyBoundaryFromText("end")) return Task.CompletedTask;
        _nativeConfirming = true;
        return QueueNativeWork(async token =>
        {
            await ResolveNativeSelectionAsync(token);
            token.ThrowIfCancellationRequested();
            if (!CheckSourceStillValid()) return;
            _nativeBusy = false;
            ConfirmLoop();
        });
    }
}
