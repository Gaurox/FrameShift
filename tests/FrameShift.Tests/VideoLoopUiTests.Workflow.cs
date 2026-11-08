using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;
using Xunit;

namespace FrameShift.Tests;

public sealed partial class VideoLoopUiTests
{
    private static readonly VideoLoopCandidate FirstCandidate = new(2d / 15, 32d / 15, 99);
    private static readonly VideoLoopCandidate SecondCandidate = new(1, 4, 85);

    [Theory]
    [InlineData(0, Keys.Enter)]
    [InlineData(1, Keys.Enter)]
    [InlineData(2, Keys.Enter)]
    [InlineData(1, Keys.Escape)]
    public void ModalKeyboard_UsesTheFooterAction_AndEscapeClosesAfterCleanup(int contextValue, Keys key)
    {
        StaTest.Run(() =>
        {
            using var form = Form();
            using var timeout = new System.Windows.Forms.Timer { Interval = 5000 };
            var timedOut = false;
            Exception? failure = null;
            timeout.Tick += (_, _) => { timedOut = true; timeout.Stop(); form.Dispose(); };
            form.Shown += (_, _) => form.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await form.SetLoopContextAsync((CutVideoForm.LoopContext)contextValue);
                    Assert.Equal(contextValue == 2, Field<Control>(form, "_loopSpeedRow").Visible);
                    Assert.Null(Field<Control>(form, "_loopAdvanced").Parent);
                    Field<Button>(form, "_cutOk").Focus();
                    Call(form, "ProcessDialogKey", key);
                }
                catch (Exception ex) { failure = ex; form.Dispose(); }
            }));
            timeout.Start();
            var result = form.ShowDialog();
            timeout.Stop();
            Assert.Null(failure); Assert.False(timedOut);
            Assert.Equal(key == Keys.Escape ? DialogResult.Cancel : DialogResult.OK, result);
            Assert.True(Field<Task>(form, "_loopTask").IsCompleted);
            Assert.True(Field<Task>(form, "_previewTask").IsCompleted);
            if (key == Keys.Enter)
            {
                Assert.NotNull(form.Selection);
                Assert.Equal(contextValue == 0 ? null : contextValue == 1 ? "natural" : "pingpong",
                    form.SelectedLoopOptions?.GetValueOrDefault(VideoLoopSettings.ModeKey));
            }
            else Assert.Null(form.Selection);
        });
    }

    [Fact]
    public void SourceAndCandidate_CanExceedTheAdvancedMinimumMaximum()
    {
        StaTest.Run(() =>
        {
            using var form = new CutVideoForm("absent.mp4", "absent.exe",
                new(TimeSpan.FromHours(1), true, true, 160, 96, "h264", 15, 54000, ["aac"], [], []),
                new(new AppLogger()), (_, _) => Task.FromResult(new Bitmap(8, 8)));
            Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            Field<TextBox>(form, "_loopMinimum").Text = "600";
            form.ApplyLoopAnalysis(new([new VideoLoopCandidate(10, 1800, 99)], 1));
            Assert.Equal("27000", Field<TextBox>(form, "_textEnd").Text);
            Call(form, "ConfirmSelection");
            Assert.Equal("1800", form.SelectedLoopOptions!["loop-end"]);
        });
    }

    [Fact]
    public void DurationSort_KeepsNativeBoundsAttachedToTheirRow()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([FirstCandidate, SecondCandidate], 8));
            var grid = Field<DataGridView>(form, "_loopResults");
            grid.Sort(grid.Columns["Duration"]!, System.ComponentModel.ListSortDirection.Descending);
            Assert.Same(SecondCandidate, grid.Rows[0].Tag);
            grid.CurrentCell = grid.Rows[0].Cells[0];
            Call(form, "SelectLoopCandidate");
            Assert.Same(SecondCandidate, Field<VideoLoopCandidate>(form, "_loopCandidate"));
            Assert.Null(form.Selection);
        });
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "natural")]
    [InlineData(2, "pingpong")]
    public void FooterAndEnter_ExportTheDisplayedContext(int contextValue, string? mode)
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            var context = (CutVideoForm.LoopContext)contextValue;
            Pump(form.SetLoopContextAsync(context));
            var primary = Field<Button>(form, "_cutOk");
            Assert.Same(primary, form.AcceptButton);
            Assert.Equal(mode is null ? "Export Selection" : "Export Loop", primary.Text);
            Assert.DoesNotContain(Descendants(form), control => control.Text == "OK");
            Call(form, "ConfirmSelection");
            Assert.NotNull(form.Selection);
            if (mode is null) Assert.Null(form.SelectedLoopOptions);
            else
            {
                Assert.Equal(mode, form.SelectedLoopOptions![VideoLoopSettings.ModeKey]);
                Assert.Equal("1", form.SelectedLoopOptions["loop-repeat"]);
                Assert.Equal(context == CutVideoForm.LoopContext.Natural ? "keep" : "mute", form.SelectedLoopOptions["loop-audio"]);
            }
        });
    }

    [Fact]
    public void BackToCut_RestoresPlainExport_WithoutDiscardingEditedFrames()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([FirstCandidate], 8));
            Field<TextBox>(form, "_textStart").Text = "8";
            Call(form, "ApplyBoundaryFromText", "start");
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Cut));
            Assert.Null(Field<Control>(form, "_loopOptions").Parent);
            Assert.Equal("Export Selection", Field<Button>(form, "_cutOk").Text);
            Call(form, "ConfirmSelection");
            Assert.Equal(8, form.Selection!.StartFrame);
            Assert.Null(form.SelectedLoopOptions);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void NativeList_AcceptsZeroOneOrManyCandidates_AndUnknownJunctionsNeedReview(int count)
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            var candidates = Enumerable.Range(0, count).Select(i => new VideoLoopCandidate(i * 0.1, 2 + i * 0.1, 90 - i)).ToArray();
            form.ApplyLoopAnalysis(new(candidates, 8) { Limits = VideoLoopSearchLimits.CandidateList });
            var grid = Field<DataGridView>(form, "_loopResults");
            Assert.True(grid.ReadOnly); Assert.False(grid.MultiSelect);
            Assert.Equal(count, grid.Rows.Count);
            Assert.Equal(count > 0, grid.Enabled);
            Assert.True(Field<Button>(form, "_cutOk").Enabled);
            Assert.Contains("coverage limited", Field<Label>(form, "_loopStatus").Text);
            foreach (DataGridViewRow row in grid.Rows)
            {
                Assert.Equal("Needs review", row.Cells["Junction"].Value);
                Assert.DoesNotContain("%", string.Join(" ", row.Cells.Cast<DataGridViewCell>().Select(cell => cell.Value)));
            }
            var batch = Field<Button>(form, "_exportAllLoops");
            Assert.Equal(count > 0, batch.Parent is not null);
            Assert.Equal($"Export All Candidates ({count})", batch.Text);
            if (count > 0) Assert.True(batch.Enabled);
        });
    }

    [Fact]
    public void ManualEdit_KeepsOriginalCandidate_AndEnterRestoresItsExactBounds()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([FirstCandidate, SecondCandidate], 8));
            var grid = Field<DataGridView>(form, "_loopResults");
            Field<TextBox>(form, "_textStart").Text = "8";
            Call(form, "ApplyBoundaryFromText", "start");
            Assert.Null(Field<VideoLoopCandidate?>(form, "_loopCandidate"));
            Assert.Contains("Custom selection", Field<Label>(form, "_labelSelection").Text);
            Assert.Same(FirstCandidate, grid.Rows[0].Tag);
            Assert.Equal(2d / 15, ((VideoLoopCandidate)grid.Rows[0].Tag!).StartSeconds);
            Assert.True((bool)Call(grid, "ProcessDialogKey", Keys.Enter)!);
            Assert.Same(FirstCandidate, Field<VideoLoopCandidate>(form, "_loopCandidate"));
            Assert.Equal("3", Field<TextBox>(form, "_textStart").Text);
            Call(form, "ConfirmSelection");
            Assert.Equal(FirstCandidate.StartSeconds, double.Parse(form.SelectedLoopOptions!["loop-start"], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(FirstCandidate.EndSeconds, double.Parse(form.SelectedLoopOptions!["loop-end"], System.Globalization.CultureInfo.InvariantCulture));
        });
    }

    [Theory]
    [InlineData(Keys.Enter)]
    [InlineData(Keys.Space)]
    [InlineData(Keys.Down)]
    [InlineData(Keys.Up)]
    [InlineData(Keys.Tab)]
    public void ListKeys_SelectOrNavigate_WithoutExport(Keys key)
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([FirstCandidate, SecondCandidate], 8));
            var grid = Field<DataGridView>(form, "_loopResults");
            grid.CurrentCell = grid.Rows[key == Keys.Up ? 1 : 0].Cells[0];
            if (key is Keys.Down or Keys.Up) Call(grid, "ProcessDataGridViewKey", new KeyEventArgs(key));
            else Call(grid, "ProcessDialogKey", key);
            if (key is Keys.Down) Assert.Same(SecondCandidate, Field<VideoLoopCandidate>(form, "_loopCandidate"));
            if (key is Keys.Up) Assert.Same(FirstCandidate, Field<VideoLoopCandidate>(form, "_loopCandidate"));
            Assert.Null(form.Selection); Assert.Null(form.SelectedLoopOptions);
            Call(grid, "OnCellDoubleClick", new DataGridViewCellEventArgs(0, 0));
            Assert.Null(form.Selection); Assert.Null(form.SelectedLoopOptions);
        });
    }

    [Fact]
    public void NewResults_PreserveChosenCandidate_AndThenPreserveCustomBounds()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([FirstCandidate, SecondCandidate], 8));
            var grid = Field<DataGridView>(form, "_loopResults");
            grid.CurrentCell = grid.Rows[1].Cells[0];
            form.ApplyLoopAnalysis(new([SecondCandidate with { Quality = 80 }, FirstCandidate], 8));
            Assert.Equal(SecondCandidate.StartSeconds, Field<VideoLoopCandidate>(form, "_loopCandidate").StartSeconds);
            Field<TextBox>(form, "_textStart").Text = "20";
            Call(form, "ApplyBoundaryFromText", "start");
            form.ApplyLoopAnalysis(new([FirstCandidate, SecondCandidate], 8));
            Assert.Equal("20", Field<TextBox>(form, "_textStart").Text);
            Assert.Null(Field<VideoLoopCandidate?>(form, "_loopCandidate"));
            Assert.Empty(grid.SelectedRows.Cast<DataGridViewRow>());
            Call(form, "ConfirmSelection");
            Assert.Equal(20, form.Selection!.StartFrame);
            Assert.False(form.SelectedLoopOptions!.ContainsKey("loop-start"));
        });
    }

    [Fact]
    public void CandidateAbsentFromNewResults_KeepsExactNativeBounds()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            var native = new VideoLoopCandidate(0.215, 2.275, 99);
            form.ApplyLoopAnalysis(new([native], 8));
            form.ApplyLoopAnalysis(new([], 8));
            Assert.Same(native, Field<VideoLoopCandidate>(form, "_loopCandidate"));
            Assert.Equal(CutVideoMath.FormatPreciseTime(native.StartSeconds), Field<TextBox>(form, "_textStartTime").Text);
            Assert.Equal(CutVideoMath.FormatPreciseTime(native.EndSeconds), Field<TextBox>(form, "_textEndTime").Text);
            Assert.Contains("Previous selection retained", Field<Label>(form, "_loopStatus").Text);
            Assert.True(Field<Button>(form, "_cutOk").Enabled);
            Call(form, "ConfirmSelection");
            Assert.Equal("0.215", form.SelectedLoopOptions!["loop-start"]);
            Assert.Equal("2.275", form.SelectedLoopOptions["loop-end"]);
        });
    }

    [Theory]
    [InlineData("", 1d)]
    [InlineData("  ", 1d)]
    [InlineData("0.1", 0.1d)]
    [InlineData("600", 600d)]
    public void AdvancedMinimum_EmptyIsAuto_AndNumericValueIsOnlyASearchMinimum(string text, double expected)
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            Assert.Null(Field<Control>(form, "_loopAdvanced").Parent);
            Field<TextBox>(form, "_loopMinimum").Text = text;
            Assert.Equal(expected, form.ReadLoopMinimum());
            Call(form, "ToggleLoopAdvancedOptions");
            Assert.NotNull(Field<Control>(form, "_loopAdvanced").Parent);
            Call(form, "ToggleLoopAdvancedOptions");
            Assert.Null(Field<Control>(form, "_loopAdvanced").Parent);
        });
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("0")]
    [InlineData("601")]
    [InlineData("abc")]
    public void AdvancedMinimum_RejectsInvalidValues(string text)
    {
        StaTest.Run(() =>
        {
            using var form = Form();
            Field<TextBox>(form, "_loopMinimum").Text = text;
            Assert.Throws<ArgumentException>(() => form.ReadLoopMinimum());
        });
    }

    [Fact]
    public void ForwardBackward_HasContextualSpeed_AndNaturalRestoresHistoricalDefaults()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.ForwardBackward));
            Field<ComboBox>(form, "_loopSpeed").SelectedIndex = 2;
            var backward = (VideoLoopSettings)Call(form, "ReadLoopSettings", (object?)null!)!;
            Assert.Equal(0.5, backward.Speed); Assert.False(backward.KeepAudio);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            var natural = (VideoLoopSettings)Call(form, "ReadLoopSettings", (object?)null!)!;
            Assert.Equal(1, natural.Speed); Assert.True(natural.KeepAudio);
            Assert.Equal(1, natural.Repetitions);
            Assert.Equal(0, Field<ComboBox>(form, "_loopSpeed").SelectedIndex);
        });
    }

    [Fact]
    public void NewSearch_CancelsAndReapsPreviousWork_BeforePublishingNewResults()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            var finishOld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken oldToken = default;
            var old = form.StartLoopWorkAsync(async token =>
            {
                oldToken = token;
                await finishOld.Task;
                token.ThrowIfCancellationRequested();
                form.ApplyLoopAnalysis(new([FirstCandidate], 8));
            });
            var enteredNew = false;
            var current = form.StartLoopWorkAsync(token =>
            {
                enteredNew = true;
                token.ThrowIfCancellationRequested();
                form.ApplyLoopAnalysis(new([SecondCandidate], 8));
                return Task.CompletedTask;
            });
            Assert.True(oldToken.IsCancellationRequested);
            Assert.False(enteredNew);
            Assert.False(Field<Button>(form, "_cutOk").Enabled);
            finishOld.SetResult(); Pump(current); Pump(old);
            Assert.True(enteredNew);
            Assert.Same(SecondCandidate, Field<VideoLoopCandidate>(form, "_loopCandidate"));
            Assert.True(Field<Button>(form, "_cutOk").Enabled);
            Assert.DoesNotContain("stopped", Field<Label>(form, "_loopStatus").Text);
        });
    }

    [Fact]
    public void BackToCut_DuringWork_WaitsForCancellationCleanup()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken token = default;
            var old = form.StartLoopWorkAsync(async t => { token = t; await finish.Task; t.ThrowIfCancellationRequested(); });
            var back = form.SetLoopContextAsync(CutVideoForm.LoopContext.Cut);
            Assert.True(token.IsCancellationRequested); Assert.False(back.IsCompleted);
            Assert.False(Field<Button>(form, "_cutOk").Enabled);
            finish.SetResult(); Pump(back); Pump(old);
            Assert.Equal("Export Selection", Field<Button>(form, "_cutOk").Text);
            Assert.True(Field<Button>(form, "_cutOk").Enabled);
            Assert.Null(Field<Control>(form, "_loopOptions").Parent);
        });
    }

    [Fact]
    public void RepeatedModalClose_DuringLoopWork_WaitsForCleanupAndKeepsCancel()
    {
        StaTest.Run(() =>
        {
            using var form = Form();
            using var timer = new System.Windows.Forms.Timer { Interval = 50 };
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var waited = false; var timedOut = false; var released = false;
            CancellationToken token = default;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            Task? work = null;
            form.Shown += (_, _) => form.BeginInvoke(new Action(() =>
            {
                work = form.StartLoopWorkAsync(async t => { token = t; await finish.Task; t.ThrowIfCancellationRequested(); });
                form.DialogResult = DialogResult.Cancel;
                form.Close(); form.Close();
                timer.Start();
            }));
            timer.Tick += (_, _) =>
            {
                if (!released)
                {
                    waited = form.Visible && token.IsCancellationRequested && !work!.IsCompleted;
                    released = true; finish.TrySetResult();
                }
                else if (DateTime.UtcNow > deadline) { timedOut = true; timer.Stop(); form.Dispose(); }
            };
            var result = form.ShowDialog();
            timer.Stop(); finish.TrySetResult();
            Assert.True(waited); Assert.False(timedOut);
            Assert.True(work!.IsCompleted);
            Assert.Equal(DialogResult.Cancel, result);
            Assert.Null(form.SelectedLoopOptions);
        });
    }

    [Fact]
    public void StopSearch_AndStopPreview_CancelOnlyTheirOperation_WithoutClosing()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            foreach (var busy in new[] { CutVideoForm.LoopBusy.Searching, CutVideoForm.LoopBusy.Rendering })
            {
                var canceled = false;
                var task = form.StartLoopWorkAsync(async token =>
                {
                    try { await Task.Delay(Timeout.Infinite, token); }
                    catch (OperationCanceledException) { canceled = true; throw; }
                }, busy, CutVideoForm.LoopContext.Natural);
                Assert.False(Field<Button>(form, "_cutOk").Enabled);
                Call(form, "ConfirmSelection"); Assert.Null(form.Selection);
                var stop = Field<Button>(form, busy == CutVideoForm.LoopBusy.Searching ? "_stopSearch" : "_stopPreview");
                Call(stop, "OnClick", EventArgs.Empty); Pump(task);
                Assert.True(canceled); Assert.False(form.IsDisposed);
                Assert.Equal(DialogResult.None, form.DialogResult);
                Assert.True(Field<Button>(form, "_cutOk").Enabled);
            }
        });
    }

    [Theory]
    [InlineData(FrameShiftThemePreference.Light)]
    [InlineData(FrameShiftThemePreference.Dark)]
    public void ExpandedLayout_KeepsFooterOutsideScroll_InBothThemes(FrameShiftThemePreference theme)
    {
        StaTest.Run(() =>
        {
            var previous = FrameShiftTheme.EffectiveTheme;
            try
            {
                FrameShiftTheme.ApplyPreference(theme);
                using var form = Form(); Handles(form);
                Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
                form.ApplyLoopAnalysis(new([FirstCandidate, SecondCandidate], 8));
                Call(form, "ToggleLoopAdvancedOptions");
                foreach (var size in new[] { new Size(1120, 760), new Size(480, 340), new Size(760, 540) })
                {
                    form.ClientSize = size;
                    for (var pass = 0; pass < 3; pass++) foreach (var c in Descendants(form).Prepend(form)) c.PerformLayout();
                    foreach (var button in new[] { (Control)form.AcceptButton!, (Control)form.CancelButton! })
                    {
                        var bounds = form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                        Assert.True(form.ClientRectangle.Contains(bounds), $"Footer outside {size}: {bounds}");
                        for (var parent = button.Parent; parent is not null; parent = parent.Parent)
                            Assert.False(parent is ScrollableControl { AutoScroll: true });
                    }
                }
                Assert.Equal(FrameShiftTheme.TextPrimary, Field<DataGridView>(form, "_loopResults").DefaultCellStyle.ForeColor);
            }
            finally { FrameShiftTheme.ApplyPreference(previous == FrameShiftThemeMode.Dark ? FrameShiftThemePreference.Dark : FrameShiftThemePreference.Light); }
        });
    }
}
