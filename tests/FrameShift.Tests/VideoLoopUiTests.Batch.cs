using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;
using FrameShift.Windows.ProgressUI;
using FrameShift.Core.Logging;
using System.Reflection;
using Xunit;

namespace FrameShift.Tests;

public sealed partial class VideoLoopUiTests
{
    [Theory]
    [InlineData(FrameShiftThemePreference.Light)]
    [InlineData(FrameShiftThemePreference.Dark)]
    public void BatchFooter_CountAndVisibilityFollowContext_AndRemainOutsideScrolling(FrameShiftThemePreference theme)
    {
        StaTest.Run(() =>
        {
            var previous = FrameShiftTheme.EffectiveTheme;
            try
            {
                FrameShiftTheme.ApplyPreference(theme);
                using var form = Form(); Handles(form);
                var batch = Field<Button>(form, "_exportAllLoops");
                Assert.False(batch.Visible);
                Assert.Null(batch.Parent);
                Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
                form.ApplyLoopAnalysis(new([], 8)); Assert.False(batch.Visible);
                var candidates = Enumerable.Range(0, 5).Select(i => new VideoLoopCandidate(i * 0.2, 2 + i * 0.4, 99)).ToArray();
                form.ApplyLoopAnalysis(new(candidates, 8));
                Assert.Equal("Export All Candidates (5)", batch.Text); Assert.True(batch.Enabled);
                foreach (var size in new[] { new Size(1120, 760), new Size(480, 340), new Size(760, 540) })
                {
                    form.ClientSize = size;
                    for (var pass = 0; pass < 3; pass++) foreach (var c in Descendants(form).Prepend(form)) c.PerformLayout();
                    foreach (var button in new[] { batch, (Control)form.AcceptButton!, (Control)form.CancelButton! })
                        Assert.True(form.ClientRectangle.Contains(form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle))),
                            $"{button.Text} footer outside {size}");
                }
                Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.ForwardBackward)); Assert.False(batch.Visible);
                Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Cut)); Assert.False(batch.Visible);
                Assert.Null(batch.Parent);
            }
            finally { FrameShiftTheme.ApplyPreference(previous == FrameShiftThemeMode.Dark ? FrameShiftThemePreference.Dark : FrameShiftThemePreference.Light); }
        });
    }

    [Fact]
    public void BatchClick_CapturesAllDisplayedCandidates_InSortOrder_AndIgnoresCustomEdits()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([FirstCandidate, SecondCandidate, FirstCandidate with { Quality = 10 }], 8));
            Assert.Equal("Export All Candidates (2)", Field<Button>(form, "_exportAllLoops").Text);
            var grid = Field<DataGridView>(form, "_loopResults");
            grid.Sort(grid.Columns["Duration"]!, ListSortDirection.Descending);
            Field<TextBox>(form, "_textStart").Text = "8"; Call(form, "ApplyBoundaryFromText", "start");
            Assert.Null(Field<VideoLoopCandidate?>(form, "_loopCandidate"));
            Field<NumericUpDown>(form, "_loopRepeat").Value = 3;
            Field<CheckBox>(form, "_loopAudio").Checked = false;
            Call(form, "ConfirmAllLoopCandidates");
            var batch = Assert.IsAssignableFrom<IReadOnlyList<VideoLoopExportSelection>>(form.SelectedLoopExports);
            Assert.Equal(2, batch.Count);
            Assert.Equal(SecondCandidate.StartSeconds, batch[0].Bounds.StartSeconds);
            Assert.Equal(FirstCandidate.StartSeconds, batch[1].Bounds.StartSeconds);
            Assert.All(batch, item => { Assert.Equal(3, item.Settings.Repetitions); Assert.False(item.Settings.KeepAudio); });
            Assert.NotEqual(7d / 15, batch[1].Bounds.StartSeconds);
            Assert.Null(form.SelectedLoopOptions); Assert.NotNull(form.Selection);
            Assert.Throws<NotSupportedException>(() => ((IList<VideoLoopExportSelection>)batch).Clear());
            grid.Rows.Clear(); Field<NumericUpDown>(form, "_loopRepeat").Value = 9;
            Assert.Equal(2, batch.Count); Assert.Equal(3, batch[0].Settings.Repetitions);
        });
    }

    [Fact]
    public void BatchCommand_IsDisabledDuringWork_AndSingleExportStillUsesTheSelectedCandidate()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([FirstCandidate, SecondCandidate], 8));
            var finish = new TaskCompletionSource();
            var task = form.StartLoopWorkAsync(_ => finish.Task);
            Assert.False(Field<Button>(form, "_exportAllLoops").Enabled);
            Call(form, "ConfirmAllLoopCandidates"); Assert.Null(form.SelectedLoopExports);
            finish.SetResult(); Pump(task);
            Assert.True(Field<Button>(form, "_exportAllLoops").Enabled);
            Call(form, "ConfirmSelection");
            Assert.Null(form.SelectedLoopExports); Assert.NotNull(form.SelectedLoopOptions);
            Assert.Equal(FirstCandidate.StartSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture), form.SelectedLoopOptions!["loop-start"]);
        });
    }

    [Fact]
    public void SourceChange_InvalidatesBatchBeforeConfirmation()
    {
        StaTest.Run(() =>
        {
            using var corpus = new VideoLoopTestCorpus(); Pump(corpus.GenerateAsync("periodic", 3, CancellationToken.None));
            using var form = new CutVideoForm(corpus.Source, corpus.Tools.ResolveFfmpegPath(), corpus.Probe, corpus.Runner,
                (_, _) => Task.FromResult(new Bitmap(8, 8)));
            Handles(form); Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([new(0, 1, 99), new(1, 2, 99)], 8));
            File.SetLastWriteTimeUtc(corpus.Source, DateTime.UtcNow.AddMinutes(1));
            Call(form, "ConfirmAllLoopCandidates");
            Assert.Null(form.SelectedLoopExports); Assert.Null(form.Selection);
            Assert.False(Field<Button>(form, "_exportAllLoops").Enabled);
            Pump(Field<Task>(form, "_previewTask"));
        });
    }

    [Fact]
    public void ModalPicker_BatchResult_RunsThroughTheSharedProgressWindow()
    {
        StaTest.Run(() =>
        {
            using var fixture = new VideoLoopExportFixture(); Pump(fixture.GenerateAsync(audio: true));
            var evidence = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../scratch/loop-validation/improvements/lot9"));
            Directory.CreateDirectory(evidence);
            var handled = false; var sawQueue = false; var timedOut = false; Exception? failure = null;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using var timer = new System.Windows.Forms.Timer { Interval = 30 };
            timer.Tick += async (_, _) =>
            {
                try
                {
                    if (watch.Elapsed > TimeSpan.FromSeconds(20))
                    {
                        timedOut = true; timer.Stop();
                        foreach (Form window in Application.OpenForms.Cast<Form>().ToArray()) window.Dispose();
                        return;
                    }
                    if (!handled && Application.OpenForms.OfType<CutVideoForm>().FirstOrDefault() is { } picker)
                    {
                        handled = true;
                        await picker.SetLoopContextAsync(CutVideoForm.LoopContext.Natural);
                        picker.ApplyLoopAnalysis(new([new(0, 0.8, 99), new(1.2, 2.4, 98)], 8));
                        await picker.RequestPreviewAsync(1);
                        using (var bitmap = new Bitmap(picker.Width, picker.Height))
                        {
                            picker.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            bitmap.Save(Path.Combine(evidence, "batch-picker.png"));
                        }
                        Field<Button>(picker, "_exportAllLoops").PerformClick();
                    }
                    if (!sawQueue && Application.OpenForms.OfType<ProgressForm>().FirstOrDefault() is { } progress)
                    {
                        var grid = Field<DataGridView>(progress, "_queueGrid");
                        if (grid.Rows.Count != 2) return;
                        Assert.Equal("generic:0", grid.Rows[0].Tag); Assert.Equal("generic:1", grid.Rows[1].Tag);
                        Assert.Contains("Candidate 1/2", Convert.ToString(grid.Rows[0].Cells[0].Value));
                        Assert.Contains("Candidate 2/2", Convert.ToString(grid.Rows[1].Cells[0].Value));
                        Assert.All(grid.Rows.Cast<DataGridViewRow>(), row => Assert.Contains("Candidate", Convert.ToString(row.Cells[2].Value)));
                        using var bitmap = new Bitmap(progress.Width, progress.Height);
                        progress.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(evidence, "batch-progress.png"));
                        sawQueue = true;
                    }
                }
                catch (Exception ex)
                {
                    failure = ex; timer.Stop();
                    foreach (Form window in Application.OpenForms.Cast<Form>().ToArray()) window.Dispose();
                }
            };
            timer.Start();
            var logger = new AppLogger();
            object?[] args = [new List<string> { fixture.Source }, new Dictionary<string, string>(), logger, null];
            var prepared = (bool)typeof(Program).GetMethod("EnsureCutVideoOptions", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
            Assert.Null(failure); Assert.False(timedOut); Assert.True(prepared);
            var requests = Assert.IsAssignableFrom<IReadOnlyList<ActionRequest>>(args[3]);
            Assert.Equal(2, requests.Count); Assert.All(requests, request => Assert.NotNull(request.ExpectedLoopSource));
            var action = new CutVideoAction(fixture.Runner, fixture.Prober, fixture.Tools);
            var exit = (int)typeof(Program).GetMethod("RunQueuedRequestsWithProgressForm", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [action, requests, logger, "loop-candidates-test"])!;
            timer.Stop();
            Assert.Null(failure); Assert.False(timedOut); Assert.True(sawQueue); Assert.Equal(0, exit);
            var outputs = Directory.GetFiles(fixture.Root, "*_loop*.mp4"); Assert.Equal(2, outputs.Length);
            var durations = outputs.Select(file => { var task = fixture.ReadProbeAsync(file); Pump(task); return task.Result.Duration!.Value.TotalSeconds; }).Order().ToArray();
            Assert.InRange(durations[0], 0.79, 0.84); Assert.InRange(durations[1], 1.19, 1.24);
            File.WriteAllText(Path.Combine(evidence, "batch-modal-result.json"), System.Text.Json.JsonSerializer.Serialize(new
                { PickerReturnedBatch = prepared, SharedQueueObserved = sawQueue, ExitCode = exit, Durations = durations }));
        });
    }
}
