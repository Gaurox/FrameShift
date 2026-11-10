using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using FrameShift.Windows.Controls;
using Xunit;

namespace FrameShift.Tests;

public sealed partial class VideoLoopUiTests
{
    [Fact]
    public void LoopOn1080WorkingArea_UsesAvailableHeight_AndCapsBothAdvancedStates()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(FrameShift.Windows.Forms.CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([FirstCandidate, SecondCandidate], 8));
            var area = new Rectangle(0, 0, 1920, 1040);
            form.FitCutWindowToContent(area);
            var collapsed = form.Size;
            Assert.Equal(area.Height, collapsed.Height);
            Call(form, "ToggleLoopAdvancedOptions"); form.FitCutWindowToContent(area);
            Assert.Equal(area.Height, form.Height);
            Call(form, "ToggleLoopAdvancedOptions"); form.FitCutWindowToContent(area);
            Assert.Equal(collapsed, form.Size);
        });
    }

    [Fact]
    public void StandardLoopOnLargeScreen_FitsControlsBeforeAdvanced_AndKeepsGridHeightBounded()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            var area = new Rectangle(0, 0, 1920, 2600);
            form.FitCutWindowToContent(area);
            var cutSize = form.Size;
            Pump(form.SetLoopContextAsync(FrameShift.Windows.Forms.CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([FirstCandidate, SecondCandidate], 8));
            form.FitCutWindowToContent(area); LayoutCut(form);
            Assert.True(form.Height > cutSize.Height);
            Assert.Null(Field<Control>(form, "_loopAdvanced").Parent);
            var workspace = Descendants(form).OfType<FrameShiftTimelineWorkspace>().Single();
            var viewport = (Panel)workspace.Controls[1];
            Assert.InRange(viewport.DisplayRectangle.Height - viewport.ClientSize.Height, 0, 2);
            var grid = Field<DataGridView>(form, "_loopResults");
            var gridHeight = grid.Height;
            var standardSize = form.Size;
            var candidates = Enumerable.Range(0, 42).Select(i => new FrameShift.Core.Actions.VideoLoopCandidate(i / 100d, 2 + i / 100d, 90)).ToArray();
            form.ApplyLoopAnalysis(new(candidates, 8)); LayoutCut(form);
            Assert.Equal(standardSize, form.Size);
            Assert.Equal(gridHeight, grid.Height);
            Call(form, "ToggleLoopAdvancedOptions"); form.FitCutWindowToContent(area);
            Assert.True(form.Height > standardSize.Height);
            Call(form, "ToggleLoopAdvancedOptions"); form.FitCutWindowToContent(area);
            Assert.Equal(standardSize, form.Size);
            Pump(form.SetLoopContextAsync(FrameShift.Windows.Forms.CutVideoForm.LoopContext.Cut));
            form.FitCutWindowToContent(area);
            Assert.Equal(cutSize, form.Size);
        });
    }

    [Theory]
    [InlineData(1, 9f)]
    [InlineData(2, 9f)]
    [InlineData(1, 18f)]
    public void AdvancedOptions_GrowAndShrinkWindow_WithoutChangingWidthOrBounds(int context, float textSize)
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            using var font = new Font("Segoe UI", textSize);
            form.Font = font;
            Assert.True(form.ClientSize.Height > 760);
            Pump(form.SetLoopContextAsync((FrameShift.Windows.Forms.CutVideoForm.LoopContext)context));
            var area = new Rectangle(0, 0, 1800, 2400);
            form.FitCutWindowToContent(area);
            var collapsed = form.Size;
            SaveCutSnapshot(form, "loop-" + context + "-collapsed");
            var start = Field<TextBox>(form, "_textStart").Text;
            var end = Field<TextBox>(form, "_textEnd").Text;
            for (var pass = 0; pass < 3; pass++)
            {
                Call(form, "ToggleLoopAdvancedOptions");
                form.FitCutWindowToContent(area);
                Assert.True(form.Height > collapsed.Height, $"Expanded {form.Size}; collapsed {collapsed}");
                Assert.Equal(collapsed.Width, form.Width);
                if (pass == 0) SaveCutSnapshot(form, "loop-" + context + "-advanced");
                Call(form, "ToggleLoopAdvancedOptions");
                form.FitCutWindowToContent(area);
                Assert.Equal(collapsed, form.Size);
            }
            Assert.Equal(start, Field<TextBox>(form, "_textStart").Text);
            Assert.Equal(end, Field<TextBox>(form, "_textEnd").Text);
        });
    }

    [Fact]
    public void BusyStateChanges_KeepProgressAndStopCommandGeometryStable()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(FrameShift.Windows.Forms.CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([FirstCandidate, SecondCandidate], 8));
            LayoutCut(form);
            var controls = new Control[] { Field<DataGridView>(form, "_loopResults"), Field<Button>(form, "_previewLoop"),
                Field<Button>(form, "_stopPreview"), Field<Button>(form, "_advancedLoop") };
            var bounds = controls.Select(control => control.Bounds).ToArray();
            var progress = Field<ProgressBar>(form, "_loopProgress");
            var slot = progress.Parent!.Bounds;
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var work = form.StartLoopWorkAsync(_ => finish.Task, FrameShift.Windows.Forms.CutVideoForm.LoopBusy.Rendering, null);
            LayoutCut(form);
            Assert.True(Field<Button>(form, "_stopPreview").Enabled);
            Assert.Equal(slot, progress.Parent.Bounds);
            Assert.Equal(bounds, controls.Select(control => control.Bounds).ToArray());
            finish.SetResult(); Pump(work); LayoutCut(form);
            Assert.False(Field<Button>(form, "_stopPreview").Enabled);
            Assert.Equal(slot, progress.Parent.Bounds);
            Assert.Equal(bounds, controls.Select(control => control.Bounds).ToArray());
        });
    }

    [Fact]
    public void PartlyVisibleCandidateGrid_DoesNotSnapOuterViewportOnFocus()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(FrameShift.Windows.Forms.CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([FirstCandidate, SecondCandidate], 8));
            Pump(form.RequestPreviewAsync(3));
            form.ClientSize = new Size(900, 680); LayoutCut(form);
            var workspace = Descendants(form).OfType<FrameShiftTimelineWorkspace>().Single();
            var viewport = (Panel)workspace.Controls[1];
            var grid = Field<DataGridView>(form, "_loopResults");
            Rectangle GridBounds()
            {
                var bounds = grid.Bounds;
                for (var parent = grid.Parent; parent != viewport && parent is not null; parent = parent.Parent)
                    bounds.Offset(parent.Location);
                return bounds;
            }
            var top = GridBounds().Y - viewport.AutoScrollPosition.Y;
            // AutoScrollPosition does not move a hidden viewport whose native
            // scrollbar is not visible. Exercise the same protected scroll primitive
            // directly, without showing a window or injecting desktop input.
            Call(viewport, "SetDisplayRectLocation", 0, -(top + 30));
            var gridBounds = GridBounds();
            Assert.True(viewport.ClientRectangle.IntersectsWith(gridBounds),
                $"Grid={gridBounds}; viewport={viewport.ClientRectangle}; scroll={viewport.AutoScrollPosition}; display={viewport.DisplayRectangle}; targetTop={top}");
            var position = viewport.DisplayRectangle.Location;
            Assert.Equal(position, (Point)Call(viewport, "ScrollToControl", grid)!);
            grid.CurrentCell = grid.Rows[1].Cells[0];
            Assert.Equal(position, viewport.DisplayRectangle.Location);
        });
    }

    [Fact]
    public void ContentFit_RespectsSmallWorkingArea_AndKeepsFooterOutsideScroll()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(FrameShift.Windows.Forms.CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([FirstCandidate, SecondCandidate], 8));
            Call(form, "ToggleLoopAdvancedOptions");
            var area = new Rectangle(50, 40, 800, 680);
            form.FitCutWindowToContent(area);
            LayoutCut(form);
            Assert.True(area.Contains(form.Bounds), $"{form.Bounds} outside {area}");
            var workspace = Descendants(form).OfType<FrameShiftTimelineWorkspace>().Single();
            var viewport = (Panel)workspace.Controls[1];
            Assert.True(viewport.AutoScroll);
            Assert.True(viewport.DisplayRectangle.Height > viewport.ClientSize.Height);
            var actions = Field<FlowLayoutPanel>(form, "_cutActions");
            var button = Field<Button>(form, "_cutOk");
            Assert.False(viewport.Contains(actions));
            Assert.True(form.ClientRectangle.Contains(form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle))));
            Assert.True(workspace.Controls[0].Bottom <= viewport.Top);
            SaveCutSnapshot(form, "loop-small-screen");
            // A manual resize below the preferred preview height must still leave
            // both panes reachable rather than reducing the options viewport to zero.
            form.ClientSize = new Size(480, 340); LayoutCut(form);
            Assert.True(viewport.Height > 0);
            Assert.True(workspace.Controls[0].Height > 0);
        });
    }

    [Theory]
    [InlineData("en", 1120)]
    [InlineData("zh-CN", 1120)]
    [InlineData("es", 1120)]
    [InlineData("pt-BR", 1120)]
    [InlineData("ru", 1120)]
    [InlineData("en", 480)]
    [InlineData("ru", 480)]
    public void CandidateSelectionAndPreview_KeepControlsAndWindowStationary(string language, int width)
    {
        StaTest.Run(() =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                using var form = Form(); Handles(form);
                Pump(form.SetLoopContextAsync(FrameShift.Windows.Forms.CutVideoForm.LoopContext.Natural));
                form.ApplyLoopAnalysis(new([FirstCandidate, SecondCandidate], 8));
                form.ClientSize = new Size(width, 900);
                Pump(form.RequestPreviewAsync(3));
                LayoutCut(form);
                var grid = Field<DataGridView>(form, "_loopResults");
                var controls = new Control[] { grid, Field<Button>(form, "_previewLoop"), Field<Button>(form, "_advancedLoop"),
                    Field<Label>(form, "_labelSelection"), Field<Label>(form, "_labelPreviewState") };
                var bounds = controls.Select(control => control.Bounds).ToArray();
                var window = form.Bounds;
                for (var pass = 0; pass < 6; pass++)
                {
                    grid.CurrentCell = grid.Rows[pass % 2].Cells[0];
                    var version = Field<int>(form, "_nativeWorkVersion");
                    Call(grid, "OnCellClick", new DataGridViewCellEventArgs(0, pass % 2));
                    Assert.Equal(version, Field<int>(form, "_nativeWorkVersion"));
                    Pump(form.RequestPreviewAsync(pass % 2 == 0 ? 3 : 16));
                    LayoutCut(form);
                    Assert.Equal(bounds, controls.Select(control => control.Bounds).ToArray());
                    Assert.Equal(window, form.Bounds);
                }
                var before = Field<int>(form, "_nativeWorkVersion");
                Call(form, "ApplyBoundaryFromText", "start");
                Assert.Equal(before, Field<int>(form, "_nativeWorkVersion"));
            }
            finally { CultureInfo.CurrentUICulture = previous; }
        });
    }

    private static void LayoutCut(Control form)
    {
        form.PerformLayout();
        foreach (Control control in form.Controls) control.PerformLayout();
    }

    private static void SaveCutSnapshot(System.Windows.Forms.Form form, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FRAMESHIFT_CUT_LAYOUT_ARTIFACTS");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        LayoutCut(form);
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        image.Save(Path.Combine(directory, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
    }
}
