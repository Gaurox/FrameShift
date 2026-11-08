using System.Drawing;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Windows.Forms;
using Xunit;

namespace FrameShift.Tests;

public sealed partial class VideoLoopUiTests
{
    [Fact]
    public void PreviewCache_ReplaysWithoutRendering_AndInvalidationWaitsForDecoder()
    {
        StaTest.Run(() =>
        {
            using var corpus = new VideoLoopTestCorpus();
            Pump(corpus.GenerateAsync("periodic", 2, CancellationToken.None));
            using var form = new CutVideoForm(corpus.Source, corpus.Tools.ResolveFfmpegPath(), corpus.Probe, corpus.Runner,
                (_, _) => Task.FromResult(new Bitmap(8, 8)));
            Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([new VideoLoopCandidate(0, 1, 99)], 8));
            Task Preview() => form.StartLoopWorkAsync(t => (Task)Call(form, "PreviewLoopAsync", VideoLoopPreviewKind.Junction, t)!,
                CutVideoForm.LoopBusy.Rendering, null);
            Pump(Preview());
            var first = Field<VideoLoopPreview>(form, "_loopPreview");
            Assert.True(Directory.Exists(first.Directory));
            Call(form, "StopLoopPreview"); Pump(Field<Task>(form, "_loopPlaybackTask"));
            Assert.True(Directory.Exists(first.Directory)); // Stop keeps the bounded session cache.
            Field<CheckBox>(form, "_loopAudio").Checked = false;
            Field<NumericUpDown>(form, "_loopRepeat").Value = 4;
            Pump(Preview());
            Assert.Same(first, Field<VideoLoopPreview>(form, "_loopPreview"));
            // Editing while playing cancels its decoder before the obsolete file is deleted.
            Field<TextBox>(form, "_textStart").Text = "2";
            Call(form, "ApplyBoundaryFromText", "start");
            Pump(Field<Task>(form, "_loopPreviewCleanupTask"));
            Assert.False(Directory.Exists(first.Directory));
            Assert.Null(Field<VideoLoopPreview?>(form, "_loopPreview"));
            Assert.Null(Field<VideoLoopCandidate?>(form, "_loopCandidate"));
            Assert.Equal("Preview junction", Field<Button>(form, "_previewLoop").Text);
            Assert.NotNull(Field<Button>(form, "_fullPreviewLoop").Parent);
        });
    }

    [Fact]
    public void ClosingPreview_CleansCachedFiles_AfterRenderingAndPlayback()
    {
        StaTest.Run(() =>
        {
            using var corpus = new VideoLoopTestCorpus();
            Pump(corpus.GenerateAsync("periodic", 2, CancellationToken.None));
            using var form = new CutVideoForm(corpus.Source, corpus.Tools.ResolveFfmpegPath(), corpus.Probe, corpus.Runner,
                (_, _) => Task.FromResult(new Bitmap(8, 8)));
            VideoLoopPreview? preview = null;
            Exception? failure = null;
            using var timeout = new System.Windows.Forms.Timer { Interval = 10000 };
            var timedOut = false;
            timeout.Tick += (_, _) => { timedOut = true; timeout.Stop(); form.Dispose(); };
            form.Shown += (_, _) => form.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural);
                    form.ApplyLoopAnalysis(new([new VideoLoopCandidate(0, 1, 99)], 8));
                    await form.StartLoopWorkAsync(t => (Task)Call(form, "PreviewLoopAsync", VideoLoopPreviewKind.Junction, t)!,
                        CutVideoForm.LoopBusy.Rendering, null);
                    preview = Field<VideoLoopPreview>(form, "_loopPreview");
                    form.DialogResult = DialogResult.Cancel;
                    form.Close(); form.Close();
                }
                catch (Exception ex) { failure = ex; form.Dispose(); }
            }));
            timeout.Start(); var result = form.ShowDialog(); timeout.Stop();
            Assert.Null(failure); Assert.False(timedOut);
            Assert.Equal(DialogResult.Cancel, result);
            Assert.NotNull(preview);
            Assert.False(Directory.Exists(preview.Directory));
            Assert.True(Field<Task>(form, "_loopPlaybackTask").IsCompleted);
            Assert.True(Field<Task>(form, "_loopPreviewCleanupTask").IsCompleted);
        });
    }

    [Fact]
    public void RenderFailure_PreservesSelection_AndLeavesRetryAvailable()
    {
        StaTest.Run(() =>
        {
            using var corpus = new VideoLoopTestCorpus();
            Pump(corpus.GenerateAsync("periodic", 2, CancellationToken.None));
            using var form = new CutVideoForm(corpus.Source, "absent.exe", corpus.Probe, corpus.Runner,
                (_, _) => Task.FromResult(new Bitmap(8, 8)));
            Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            var candidate = new VideoLoopCandidate(0, 1, 99);
            form.ApplyLoopAnalysis(new([candidate], 8));
            Pump(form.StartLoopWorkAsync(t => (Task)Call(form, "PreviewLoopAsync", VideoLoopPreviewKind.Junction, t)!,
                CutVideoForm.LoopBusy.Rendering, null));
            Assert.Same(candidate, Field<VideoLoopCandidate>(form, "_loopCandidate"));
            Assert.Null(Field<VideoLoopPreview?>(form, "_loopPreview"));
            Assert.True(Field<Button>(form, "_previewLoop").Enabled);
            Assert.True(Field<Button>(form, "_cutOk").Enabled);
            Assert.Contains("Loop unavailable", Field<Label>(form, "_loopStatus").Text);
        });
    }
}
