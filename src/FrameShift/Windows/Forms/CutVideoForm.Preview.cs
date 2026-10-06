using System.Drawing;
using FrameShift.Core.Actions;

namespace FrameShift.Windows.Forms;

public sealed partial class CutVideoForm
{
    private readonly Func<double, CancellationToken, Task<Bitmap>> _capturePreview;
    private CancellationTokenSource? _previewCancellation;
    private Task _previewTask = Task.CompletedTask;
    private bool _closing;
    private bool _allowClose;

    internal Task RequestPreviewAsync(int frame)
    {
        if (_closing || IsDisposed) return Task.CompletedTask;
        _previewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        _previewTask = LoadPreviewAsync(_previewTask, frame, _uiState.ActiveBoundary, cancellation);
        return _previewTask;
    }

    private async Task LoadPreviewAsync(Task previous, int frame, string boundary, CancellationTokenSource cancellation)
    {
        Bitmap? bitmap = null;
        try
        {
            // Serialize decoding so canceled requests have reaped their process/temp file first.
            await previous;
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closing || IsDisposed) return;
            _labelPreviewState.Text = "Loading preview...";
            var seconds = Math.Max(0d, (frame - 1) / _fps);
            bitmap = await _capturePreview(seconds, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closing || IsDisposed) return;
            DisposePreviewBitmap();
            _currentPreviewBitmap = bitmap;
            _previewBox.Image = bitmap;
            bitmap = null; // The form now owns the image.
            _labelPreviewState.Text = $"Previewing {Capitalize(boundary)} frame: {frame} ({CutVideoMath.FormatPreciseTime(seconds)})";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closing && !IsDisposed && !cancellation.IsCancellationRequested)
                _labelPreviewState.Text = $"Preview unavailable: {ex.Message}";
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
        Enabled = false;
        await _previewTask;
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
            _previewTimer?.Dispose();
            DisposePreviewBitmap();
        }
        base.Dispose(disposing);
    }
}
