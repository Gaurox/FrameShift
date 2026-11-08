using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Windows.Forms;
using Xunit;

namespace FrameShift.Tests;

public sealed partial class VideoLoopUiTests
{
    [Fact]
    public void JunctionAssessments_KeepQualityOrder_AndUpdateWithoutChangingSelectedBounds()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            var best = new VideoLoopCandidate(0.2, 4.2, 100) { Seam = new(0, 0, 0, 0, 0, false, 0.2, 5) };
            var approximate = new VideoLoopCandidate(0, 1, 70) { Seam = new(0.03, 0, 0, 0, 0, false, 0.2, 5) };
            var review = new VideoLoopCandidate(1, 3, 50) { Seam = new(0.06, 0, 0, 0, 0, false, 0.2, 5) };
            form.ApplyLoopAnalysis(new([best, approximate, review], 8));
            var grid = Field<DataGridView>(form, "_loopResults");
            Assert.True(grid.Columns["Junction"]!.MinimumWidth >= 120);
            Assert.Same(best, grid.Rows[0].Tag);
            Assert.Equal(new[] { "Promising", "Approximate", "Needs review" },
                grid.Rows.Cast<DataGridViewRow>().Select(r => (string)r.Cells["Junction"].Value));
            grid.CurrentCell = grid.Rows[1].Cells[0];
            var version = Field<int>(form, "_nativeWorkVersion");
            var updated = approximate with { Seam = new(0.01, 0, 0, 0, 0, false, 0.2, 5), Quality = 90 };
            form.ApplyLoopAnalysis(new([best, updated, review], 8));
            Assert.Equal("Promising", grid.Rows[1].Cells["Junction"].Value);
            Assert.Same(updated, Field<VideoLoopCandidate>(form, "_loopCandidate"));
            Assert.Equal(version, Field<int>(form, "_nativeWorkVersion"));
            Assert.Contains("estimates", Field<Label>(form, "_loopStatus").Text);
            Assert.DoesNotContain("%", string.Join(" ", grid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["Junction"].Value)));
        });
    }

    [Fact]
    public void IncrementalResults_AllowSelectionDuringSearch_WithoutResettingBoundsOrSort()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var work = form.StartLoopWorkAsync(async token =>
            {
                form.ApplyLoopAnalysis(new([FirstCandidate, SecondCandidate], 8) { SearchCompleted = false });
                await finish.Task;
                token.ThrowIfCancellationRequested();
                form.ApplyLoopAnalysis(new([FirstCandidate, new(0, 5, 90), SecondCandidate with { Quality = 99 }], 8) { SearchCompleted = false });
            });
            var grid = Field<DataGridView>(form, "_loopResults");
            Assert.True(grid.Enabled);
            grid.CurrentCell = grid.Rows[1].Cells[0];
            grid.Sort(grid.Columns["Duration"]!, System.ComponentModel.ListSortDirection.Descending);
            var version = Field<int>(form, "_nativeWorkVersion");
            var startText = Field<TextBox>(form, "_textStartTime").Text;
            var endText = Field<TextBox>(form, "_textEndTime").Text;
            finish.SetResult(); Pump(work);
            Assert.Equal(version, Field<int>(form, "_nativeWorkVersion"));
            Assert.Equal(startText, Field<TextBox>(form, "_textStartTime").Text);
            Assert.Equal(endText, Field<TextBox>(form, "_textEndTime").Text);
            Assert.Equal(SecondCandidate.StartSeconds, Field<VideoLoopCandidate>(form, "_loopCandidate").StartSeconds);
            Assert.Equal(SortOrder.Descending, grid.SortOrder);
            Assert.Equal(5d, grid.Rows[0].Cells["Duration"].Value);
            Assert.Contains("Search in progress", Field<Label>(form, "_loopStatus").Text);
            Assert.Null(form.Selection);
        });
    }

    [Fact]
    public void ExplicitTemporalBudget_IsShownWithoutClaimingAnExhaustiveSearch()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([FirstCandidate], 8)
            {
                Limits = VideoLoopSearchLimits.TemporalCoverage | VideoLoopSearchLimits.SeedSelection,
                Diagnostics = new() { CoveredSeconds = 7200, FullTemporalCoverage = false }
            });
            var status = Field<Label>(form, "_loopStatus").Text;
            Assert.Contains("7200", status); Assert.Contains("gaps were not sampled", status);
            Assert.Contains("other loops may exist", status);
        });
    }
}
