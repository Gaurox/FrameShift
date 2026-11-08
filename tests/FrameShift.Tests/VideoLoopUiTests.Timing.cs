using System.Drawing;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFprobe;
using FrameShift.Windows.Forms;
using Xunit;

namespace FrameShift.Tests;

public sealed partial class VideoLoopUiTests
{
    private static async Task<MediaProbeResult> IrregularSource(VideoLoopTestCorpus corpus)
    {
        Directory.CreateDirectory(corpus.Root);
        var result = await corpus.Runner.RunAsync(corpus.Tools.ResolveFfmpegPath(),
            ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i",
                "nullsrc=s=160x96:r=15:d=6,geq=lum='80+N':cb=128:cr=128", "-vf",
                "select='eq(n,0)+eq(n,1)+eq(n,5)+eq(n,12)+eq(n,30)+eq(n,60)+eq(n,89)'",
                "-fps_mode", "passthrough", "-c:v", "ffv1", corpus.Source],
            null, null, corpus.Source, "UI native timing fixture", "CPU", CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        return (await corpus.Prober.TryProbeMediaAsync(corpus.Tools.ResolveFfprobePath(), corpus.Source, CancellationToken.None)).Probe!;
    }

    [Fact]
    public void CandidateBoundaryCaptures_UseNativeStartAndLastIncludedImage()
    {
        StaTest.Run(() =>
        {
            using var corpus = new VideoLoopTestCorpus();
            var probeTask = IrregularSource(corpus); Pump(probeTask);
            var captured = new List<double>();
            using var form = new CutVideoForm(corpus.Source, corpus.Tools.ResolveFfmpegPath(), probeTask.Result, corpus.Runner,
                (seconds, _) => { captured.Add(seconds); return Task.FromResult(new Bitmap(8, 8)); });
            Handles(form); Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([new VideoLoopCandidate(0.333, 4, 99)], 8));
            Pump(form.RequestPreviewAsync(1));
            Assert.True(captured.Count > 0, Field<Label>(form, "_labelPreviewState").Text);
            Assert.Equal(0.333, captured[^1], 6);
            Field<TextBox>(form, "_textEnd").Text = Field<TextBox>(form, "_textEnd").Text;
            Call(form, "ApplyBoundaryFromText", "end");
            Pump(form.RequestPreviewAsync(60));
            Assert.Equal(2, captured[^1], 6);
            Assert.Equal("00:00:04.000", Field<TextBox>(form, "_textEndTime").Text);
            Assert.Contains("last included image", Field<Label>(form, "_labelPreviewState").Text);
            Assert.Equal(0, Field<VideoLoopTimingIndex>(form, "_timingIndex").IndexedFrameCount);
        });
    }

    [Fact]
    public void NativeArrowSteps_KeepEveryClick_AndPreserveTheUntouchedBoundary()
    {
        StaTest.Run(() =>
        {
            using var corpus = new VideoLoopTestCorpus();
            var probe = IrregularSource(corpus); Pump(probe);
            using var form = new CutVideoForm(corpus.Source, corpus.Tools.ResolveFfmpegPath(), probe.Result, corpus.Runner,
                (_, _) => Task.FromResult(new Bitmap(8, 8)));
            Handles(form); Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            var candidate = new VideoLoopCandidate(0.333, 4, 99);
            form.ApplyLoopAnalysis(new([candidate], 8));
            var first = form.StepNativeBoundaryAsync("start", 1);
            var second = form.StepNativeBoundaryAsync("start", 1);
            Pump(second); Pump(first);
            Assert.Equal("00:00:02.000", Field<TextBox>(form, "_textStartTime").Text);
            Assert.Equal("00:00:04.000", Field<TextBox>(form, "_textEndTime").Text);
            Assert.Null(Field<VideoLoopCandidate?>(form, "_loopCandidate"));
            Assert.Same(candidate, Field<DataGridView>(form, "_loopResults").Rows[0].Tag);
            Pump(form.StepNativeBoundaryAsync("start", -1));
            Assert.Equal("00:00:00.800", Field<TextBox>(form, "_textStartTime").Text);
            Pump(form.StepNativeBoundaryAsync("end", -1));
            Assert.Equal("00:00:02.000", Field<TextBox>(form, "_textEndTime").Text);
            Pump(form.ConfirmNativeLoopAsync());
            Assert.Equal("0.8", form.SelectedLoopOptions!["loop-start"]);
            Assert.Equal("2", form.SelectedLoopOptions["loop-end"]);
        });
    }

    [Fact]
    public void NumericFrameEdit_ResolvesTheTrueOrdinal_WithoutRoundingTheOtherBound()
    {
        StaTest.Run(() =>
        {
            using var corpus = new VideoLoopTestCorpus();
            var probe = IrregularSource(corpus); Pump(probe);
            using var form = new CutVideoForm(corpus.Source, corpus.Tools.ResolveFfmpegPath(), probe.Result, corpus.Runner,
                (_, _) => Task.FromResult(new Bitmap(8, 8)));
            Handles(form); Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([new VideoLoopCandidate(0.333, 4, 99)], 8));
            Field<TextBox>(form, "_textStart").Text = "4";
            Call(form, "ApplyBoundaryFromText", "start");
            Pump(form.RequestPreviewAsync(4));
            Assert.Equal("00:00:00.800", Field<TextBox>(form, "_textStartTime").Text);
            Assert.Equal("00:00:04.000", Field<TextBox>(form, "_textEndTime").Text);
            var index = Field<VideoLoopTimingIndex>(form, "_timingIndex");
            var passes = index.DecodePasses;
            Pump(form.RequestPreviewAsync(4));
            Assert.Equal(passes, index.DecodePasses);
            Pump(form.ConfirmNativeLoopAsync());
            Assert.Equal("0.8", form.SelectedLoopOptions!["loop-start"]);
            Assert.Equal("4", form.SelectedLoopOptions["loop-end"]);
        });
    }

    [Fact]
    public void TimeEdit_SnapsTheStartToTheFirstIncludedNativeFrame()
    {
        StaTest.Run(() =>
        {
            using var corpus = new VideoLoopTestCorpus();
            var probe = IrregularSource(corpus); Pump(probe);
            using var form = new CutVideoForm(corpus.Source, corpus.Tools.ResolveFfmpegPath(), probe.Result, corpus.Runner,
                (_, _) => Task.FromResult(new Bitmap(8, 8)));
            Handles(form); Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([new VideoLoopCandidate(0.333, 4, 99)], 8));
            Field<TextBox>(form, "_textStartTime").Text = "00:00:00.500";
            Call(form, "ApplyBoundaryFromTimeText", "start");
            Pump(form.ConfirmNativeLoopAsync());
            Assert.Equal("0.8", form.SelectedLoopOptions!["loop-start"]);
            Assert.Equal("4", form.SelectedLoopOptions["loop-end"]);
        });
    }

    [Fact]
    public void SourceChange_ClearsCandidatesAndIndex_AndBlocksStaleExport()
    {
        StaTest.Run(() =>
        {
            using var corpus = new VideoLoopTestCorpus();
            var probe = IrregularSource(corpus); Pump(probe);
            using var form = new CutVideoForm(corpus.Source, corpus.Tools.ResolveFfmpegPath(), probe.Result, corpus.Runner,
                (_, _) => Task.FromResult(new Bitmap(8, 8)));
            Handles(form); Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([new VideoLoopCandidate(0.333, 4, 99)], 8));
            var index = Field<VideoLoopTimingIndex>(form, "_timingIndex");
            Pump(index.ResolveFrameAsync(2, CancellationToken.None));
            Pump(form.StartLoopWorkAsync(t => (Task)Call(form, "PreviewLoopAsync", VideoLoopPreviewKind.Junction, t)!,
                CutVideoForm.LoopBusy.Rendering, null));
            var cache = Field<VideoLoopPreview>(form, "_loopPreview");
            Assert.True(Directory.Exists(cache.Directory));
            File.SetLastWriteTimeUtc(corpus.Source, File.GetLastWriteTimeUtc(corpus.Source).AddSeconds(3));
            Assert.False((bool)Call(form, "CheckSourceStillValid")!);
            Pump(Field<Task>(form, "_loopPreviewCleanupTask"));
            Assert.False(Directory.Exists(cache.Directory));
            Assert.Empty(Field<DataGridView>(form, "_loopResults").Rows.Cast<DataGridViewRow>());
            Assert.False(File.Exists(index.IndexPath));
            Assert.Null(Field<VideoLoopCandidate?>(form, "_loopCandidate"));
            Assert.False(Field<Button>(form, "_cutOk").Enabled);
            Assert.Contains("source changed", Field<Label>(form, "_labelPreviewState").Text);
            Call(form, "ConfirmSelection"); Assert.Null(form.SelectedLoopOptions); Assert.Null(form.Selection);
        });
    }

    [Fact]
    public void RapidNativeCaptures_DiscardTheObsoleteImageAndBounds()
    {
        StaTest.Run(() =>
        {
            using var corpus = new VideoLoopTestCorpus();
            var probe = IrregularSource(corpus); Pump(probe);
            var finish = new TaskCompletionSource<Bitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            using var form = new CutVideoForm(corpus.Source, corpus.Tools.ResolveFfmpegPath(), probe.Result, corpus.Runner,
                (_, _) => { if (++calls == 1) { entered.SetResult(); return finish.Task; } return Task.FromResult(new Bitmap(9, 9)); });
            Handles(form); Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([new VideoLoopCandidate(0.333, 4, 99), new VideoLoopCandidate(0.8, 4, 90)], 8));
            var old = form.RequestPreviewAsync(1); Pump(entered.Task);
            var grid = Field<DataGridView>(form, "_loopResults");
            grid.CurrentCell = grid.Rows[1].Cells[0];
            var current = form.RequestPreviewAsync(1);
            var obsolete = new Bitmap(8, 8); finish.SetResult(obsolete);
            Pump(current); Pump(old);
            Assert.Equal("00:00:00.800", Field<TextBox>(form, "_textStartTime").Text);
            Assert.Equal(new Size(9, 9), Field<Bitmap>(form, "_currentPreviewBitmap").Size);
            Assert.ThrowsAny<Exception>(() => obsolete.GetPixel(0, 0));
        });
    }

    [Fact]
    public void RepeatedClose_DuringNativeIndexing_CancelsAndDeletesTheIndex()
    {
        StaTest.Run(() =>
        {
            using var corpus = new VideoLoopTestCorpus();
            Pump(corpus.GenerateAsync("periodic", 300, CancellationToken.None, lowResolution: true));
            using var form = new CutVideoForm(corpus.Source, corpus.Tools.ResolveFfmpegPath(), corpus.Probe, corpus.Runner,
                (_, _) => Task.FromResult(new Bitmap(8, 8)));
            using var timer = new System.Windows.Forms.Timer { Interval = 15 };
            Task? work = null; VideoLoopTimingIndex? index = null;
            var wasIndexing = false; var closed = false; var timeout = false;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            form.Shown += (_, _) => form.BeginInvoke(new Action(async () =>
            {
                await form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural);
                index = Field<VideoLoopTimingIndex>(form, "_timingIndex");
                work = form.StepNativeBoundaryAsync("start", 1);
                timer.Start();
            }));
            timer.Tick += (_, _) =>
            {
                if (!closed && index!.DecodedFrameCount > 20)
                {
                    wasIndexing = !work!.IsCompleted;
                    closed = true; form.DialogResult = DialogResult.Cancel; form.Close(); form.Close();
                }
                if (DateTime.UtcNow > deadline) { timeout = true; timer.Stop(); form.Dispose(); }
            };
            var result = form.ShowDialog(); timer.Stop();
            Assert.True(wasIndexing); Assert.False(timeout);
            Assert.Equal(DialogResult.Cancel, result); Assert.True(work!.IsCompleted);
            Assert.False(File.Exists(index!.IndexPath));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(index.IndexPath)!, Path.GetFileName(index.IndexPath) + ".*.chunk"));
        });
    }
}
