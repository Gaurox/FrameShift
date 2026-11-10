using System.Drawing;
using FrameShift.Core.Actions;

namespace FrameShift.Windows.Forms;

public sealed partial class CutVideoForm
{
    private readonly Func<double, CancellationToken, Task<Bitmap>> _capturePreview;
    private bool _hasInjectedCapture;
    private CancellationTokenSource? _previewCancellation;
    private Task _previewTask = Task.CompletedTask;
    private bool _closing;
    private bool _allowClose;

    internal Task RequestPreviewAsync(int frame)
    {
        if (_closing || IsDisposed || !CheckSourceStillValid()) return Task.CompletedTask;
        _previewTimer.Stop();
        _previewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        var boundary = _uiState.ActiveBoundary;
        _previewTask = LoadPreviewAsync(_previewTask, frame, boundary, NativeTimingEnabled,
            boundary == "end" ? _nativeEndTime : _nativeStartTime, cancellation);
        return _previewTask;
    }

    private async Task LoadPreviewAsync(Task previous, int frame, string boundary, bool native,
        double? nativeTime, CancellationTokenSource cancellation)
    {
        Bitmap? bitmap = null;
        try
        {
            // Serialize decoding so canceled requests have reaped their process/temp file first.
            await previous;
            await _loopPlaybackTask;
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closing || IsDisposed) return;
            _labelPreviewState.Text = L.Get("CutVideoForm_Preview_Loading_preview_7e19fdfb");
            VideoLoopFramePosition? position = null;
            var seconds = Math.Max(0d, (frame - 1) / _fps);
            if (native)
            {
                position = await ResolveNativeBoundaryAsync(boundary, frame, nativeTime, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                seconds = position.Time;
                bitmap = _hasInjectedCapture ? await _capturePreview(seconds, cancellation.Token)
                    : await FrameShift.Windows.Helpers.PreviewFrameHelper.CaptureNativeFrameAsync(_loopExecutable,
                        _loopRunner, _inputPath, _loopProbe, position.Timestamp, cancellation.Token);
            }
            else bitmap = await _capturePreview(seconds, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closing || IsDisposed || !CheckSourceStillValid()) return;
            if (position is not null)
            {
                CommitNativeBoundary(boundary, position, nativeTime is null);
                RefreshSelectionUi(false);
            }
            var previousBitmap = _currentPreviewBitmap;
            _currentPreviewBitmap = bitmap;
            _previewBox.Image = bitmap;
            bitmap = null; // The form now owns the image.
            previousBitmap?.Dispose();
            _labelPreviewState.Text = position is null
                ? L.Format("CutVideoForm_Preview_Previewing_0_frame_1_2_439822df",Capitalize(boundary),frame,CutVideoMath.FormatPreciseTime(seconds))
                : L.Format("Loop_NativePreview", Capitalize(boundary), CutVideoMath.FormatPreciseTime(seconds)) +
                    (boundary == "end" ? L.Get("Loop_LastIncluded") : "");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (VideoLoopSourceChangedException) { InvalidateChangedSource(); }
        catch (Exception ex)
        {
            if (!_closing && !IsDisposed && !cancellation.IsCancellationRequested)
                _labelPreviewState.Text = L.Format("CutVideoForm_Preview_Preview_unavailable_0_e54ee09c",ex.Message);
        }
        finally
        {
            bitmap?.Dispose();
            if (ReferenceEquals(_previewCancellation, cancellation)) _previewCancellation = null;
            cancellation.Dispose();
        }
    }

    private async void CloseAfterPreviewAsync(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        var result = DialogResult;
        _previewTimer.Stop();
        _previewCancellation?.Cancel();
        _loopCancellation?.Cancel();
        CancelNativeWork(); _sourceTimer?.Stop();
        StopLoopPlayback();
        InvalidateLoopPreviewCache();
        Enabled = false;
        await Task.WhenAll(_previewTask, _loopTask, _loopPlaybackTask, _loopPreviewCleanupTask, _nativeTask);
        _timingIndex?.Dispose();
        if (IsDisposed) return;
        // Also defer completed cleanup, and keep repeated close requests cancelled.
        BeginInvoke(new Action(() =>
        {
            if (IsDisposed) return;
            _allowClose = true;
            DialogResult = result;
            Close();
        }));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closing = true;
            _previewCancellation?.Cancel();
            _loopCancellation?.Cancel();
            CancelNativeWork(); _sourceTimer?.Dispose(); _timingIndex?.Dispose();
            StopLoopPlayback();
            InvalidateLoopPreviewCache();
            if (_loopOptions is not null && _loopOptions.Parent is null) _loopOptions.Dispose();
            if (_loopAdvanced is not null && _loopAdvanced.Parent is null) _loopAdvanced.Dispose();
            if (_exportAllLoops is not null && _exportAllLoops.Parent is null) _exportAllLoops.Dispose();
            _previewTimer?.Dispose();
            DisposePreviewBitmap();
        }
        base.Dispose(disposing);
    }
}
