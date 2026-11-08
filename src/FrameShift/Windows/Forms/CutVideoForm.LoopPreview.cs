using FrameShift.Core.Actions;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Windows.Controls;

namespace FrameShift.Windows.Forms;

public sealed partial class CutVideoForm
{
    private VideoLoopPreview? _loopPreview;
    private VideoLoopPreviewKey? _loopPreviewKey;
    private Task _loopPreviewCleanupTask = Task.CompletedTask;

    private async Task PreviewLoopAsync(VideoLoopPreviewKind kind, CancellationToken token)
    {
        await ResolveNativeSelectionAsync(token);
        var cut = new CutVideoSettings(_selection.StartFrame, _selection.EndFrame, _fps);
        var settings = ReadLoopSettings();
        var bounds = LoopRenderBounds;
        var key = VideoLoopPreviewBuilder.Key(_inputPath, _loopProbe, cut, settings, bounds, kind);
        if (_loopPreviewKey != key || _loopPreview?.Clips.Any(c => !File.Exists(c.Path)) != false)
        {
            InvalidateLoopPreviewCache();
            await _loopPreviewCleanupTask;
            _loopStatus.Text = kind == VideoLoopPreviewKind.Junction
                ? L.Get("Loop_RenderingJunction") : L.Get("Loop_RenderingFull");
            VideoLoopPreview? rendered = null;
            try
            {
                rendered = await new VideoLoopPreviewBuilder(_loopRunner, new FfprobeRunner(new AppLogger())).BuildAsync(
                    _loopExecutable, new ToolLocator().ResolveFfprobePath(), _inputPath, _loopProbe, cut, settings,
                    bounds, kind, new LoopUiProgress(this, token), token);
                token.ThrowIfCancellationRequested();
                if (!CheckSourceStillValid() || key != VideoLoopPreviewBuilder.Key(_inputPath, _loopProbe, cut, settings, LoopRenderBounds, kind))
                    throw new IOException(L.Get("Loop_SourceChangedPreview"));
                if (_closing || IsDisposed) return;
                _loopPreview = rendered;
                _loopPreviewKey = key;
                rendered = null;
            }
            finally { rendered?.Dispose(); }
        }
        token.ThrowIfCancellationRequested();
        var preview = _loopPreview!;
        var player = new LoopVideoPlayer();
        _loopPlayer = player;
        _previewPanel.Controls.Add(player);
        player.BringToFront();
        var playbackCancellation = new CancellationTokenSource();
        _loopPlaybackCancellation = playbackCancellation;
        _loopPlaybackTask = PlayRenderedLoopAsync(player, preview, playbackCancellation);
    }

    private void InvalidateLoopPreviewCache()
    {
        var preview = _loopPreview;
        _loopPreview = null; _loopPreviewKey = null;
        if (preview is null) return;
        _loopPreviewCleanupTask = DeleteLoopPreviewAsync(_loopPreviewCleanupTask, _loopPlaybackTask, preview);
    }

    private static async Task DeleteLoopPreviewAsync(Task previous, Task playback, VideoLoopPreview preview)
    {
        await previous;
        await playback;
        preview.Dispose();
    }

    private async Task PlayRenderedLoopAsync(LoopVideoPlayer player, VideoLoopPreview preview, CancellationTokenSource cts)
    {
        try
        {
            for (var i = 0; i < preview.Clips.Count; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                _loopStatus.Text = L.Format("Loop_ProxyReview", preview.Clips[i].Description);
                if (i > 0) await Task.Delay(350, cts.Token);
                await player.PlayAsync(_loopExecutable, _loopRunner, preview.Clips[i].Path, preview.Width, preview.Height, cts.Token);
            }
            if (!_closing && !IsDisposed && !cts.IsCancellationRequested)
                _loopStatus.Text = L.Get("Loop_PreviewComplete");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closing && !IsDisposed && !cts.IsCancellationRequested) _loopStatus.Text = L.Format("Loop_PlaybackUnavailable", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_loopPlayer, player)) { _loopPlayer = null; _loopPlaybackCancellation = null; }
            if (!IsDisposed) _previewPanel.Controls.Remove(player);
            player.Dispose(); cts.Dispose();
            if (!_closing && !IsDisposed) UpdateLoopCommands();
        }
    }
}
