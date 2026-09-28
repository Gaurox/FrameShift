using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;
using FrameShift.Windows.ProgressUI;
using Xunit;

namespace FrameShift.Tests;

// Native handles stay hidden. No desktop input, settings writes or media processing.
public sealed class UiD1Tests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void D1_CommandsRemainReachable_WithLongTextAndNarrowWidth(int window)
    {
        StaTest.Run(() =>
        {
            using Form form = window switch
            {
                0 => new ProgressForm(), 1 => new MainForm(), 2 => new SettingsForm(),
                _ => new MediaInfoForm(string.Join("\r\n", Enumerable.Repeat(new string('x', 200), 80)), MediaKind.Video, "Vidéo avec accents.mp4")
            };
            Handles(form);
            if (form is ProgressForm progress)
            {
                progress.ReportQueue(Enumerable.Range(0, 120).Select(i => $"E:\\Dossier très long avec accents\\Vidéo numéro {i}.mp4").ToArray());
                progress.ReportProgress(355, "Vidéo avec accents.mp4", "Conversion", string.Join("\r\n", Enumerable.Repeat("Un message long qui doit rester consultable intégralement.", 10)), "Remaining: 00:12:35");
            }
            var root = Assert.IsType<TableLayoutPanel>(form.Controls[0]);
            Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
            Assert.Single(Descendants(form).OfType<FrameShiftHeader>());
            using var largeFont = new Font("Segoe UI", 18);
            foreach (var width in new[] { 1000, 480, 800 })
            {
                form.ClientSize = new Size(width, 600);
                form.Font = width == 480 ? largeFont : SystemFonts.MessageBoxFont;
                Layout(form);
                var actions = Assert.IsType<FrameShiftActionBar>(root.GetControlFromPosition(0, 3));
                Assert.True(actions.Top >= 0 && actions.Bottom <= root.ClientSize.Height,
                    $"window={window}, width={width}, actions={actions.Bounds}, root={root.ClientSize}");
                Assert.All(actions.Controls.Cast<Control>(), button => Assert.True(button.Right <= actions.ClientSize.Width && button.Bottom <= actions.ClientSize.Height));
                Assert.False(form.Visible);
            }
            if (form is MediaInfoForm)
                Assert.Equal(80, Descendants(form).OfType<TextBox>().Single(t => t.Name == "mediaInformation").Lines.Length);
        });
    }

    [Fact]
    public void Progress_HandlesWaitingNumericFailureCancellationAndCompletion()
    {
        StaTest.Run(() =>
        {
            using var form = new ProgressForm();
            Handles(form);
            var bar = Descendants(form).OfType<ProgressBar>().Single();
            Assert.Equal(ProgressBarStyle.Marquee, bar.Style);
            form.ReportQueue(["Vidéo.mp4", "Vidéo.mp4", "Image.png"]);
            form.ReportProgress(523, "Vidéo.mp4", "Conversion", "Processing", "00:01:00");
            Assert.Equal(ProgressBarStyle.Continuous, bar.Style);
            Assert.Equal(523, bar.Value);
            form.ReportQueueItem("Vidéo.mp4", "failed", "A very long error");
            Assert.Equal(0, bar.Value);
            var events = 0;
            form.CancelRequested += (_, _) => events++;
            form.RequestCancel(); form.RequestCancel();
            Assert.Equal(1, events);
            Assert.True(form.IsCancellationRequested);
            Assert.False(((Button)form.CancelButton!).Enabled);
            form.EnableCloseMode("Canceled cleanly.");
            Assert.Same(form.CancelButton, form.AcceptButton);
            Assert.Equal("Close", ((Button)form.AcceptButton!).Text);
            Assert.True(((Button)form.AcceptButton!).Enabled);
            Assert.EndsWith("Canceled cleanly.", Descendants(form).OfType<TextBox>().Single(t => t.Name == "progressStatus").Text);
        });
    }

    [Fact]
    public void Main_ReflowsAndKeepsSelectionScope_WhenActionsAreFiltered()
    {
        StaTest.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FrameShift-D1-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var paths = new[] { "Vidéo avec espaces.mp4", "Audio accentué.mp3", "Image.png" }.Select(n => Path.Combine(directory, n)).ToArray();
                foreach (var path in paths) File.WriteAllText(path, "");
                ActionInvokedEventArgs? invoked = null;
                using var form = new MainForm(paths, e => invoked = e);
                Handles(form);
                var split = Descendants(form).OfType<SplitContainer>().Single();
                form.ClientSize = new Size(1000, 700); Layout(form);
                Assert.Equal(Orientation.Vertical, split.Orientation);
                form.ClientSize = new Size(480, 650); Layout(form);
                Assert.Equal(Orientation.Horizontal, split.Orientation);
                var panel = Descendants(form).OfType<ActionsPanel>().Single();
                panel.SetFiles(paths, [paths[0]]);
                var search = Descendants(panel).OfType<TextBox>().Single();
                search.Text = "Cut video";
                Layout(form);
                var button = Descendants(panel).OfType<Button>().Single(b => b.Tag is ActionCatalogEntry);
                // Invoke handler directly: PerformClick on a hidden parent intentionally does nothing.
                typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, [EventArgs.Empty]);
                Assert.NotNull(invoked);
                Assert.Equal(new[] { paths[0] }, invoked.Files);
                for (var i = 0; i < 20; i++) search.Text = i % 2 == 0 ? "zz-no-match" : "Cut video";
                Layout(form);
                Assert.Single(Descendants(panel).OfType<Button>(), b => b.Tag is ActionCatalogEntry);
                Descendants(form).OfType<FileQueuePanel>().Single().Clear();
                Assert.DoesNotContain(Descendants(panel).OfType<Button>(), b => b.Tag is ActionCatalogEntry);
            }
            finally { Directory.Delete(directory, true); }
        });
    }

    [Fact]
    public void Settings_FitsItsContentWithoutAnEmptyScrollingTail()
    {
        StaTest.Run(() =>
        {
            using var form = new SettingsForm();
            Handles(form);
            var root = (TableLayoutPanel)form.Controls[0];
            FrameShiftDialogLayout.FitInitialHeight(form, root, new Size(1600, 1000));
            Layout(form);
            var body = (Panel)root.GetControlFromPosition(0, 1)!;
            Assert.False(body.VerticalScroll.Visible);
            Assert.InRange(body.ClientSize.Height - body.Controls[0].Bottom, 0, 20);
        });
    }

    [Fact]
    public void Progress_LongErrorsHaveARealReadingArea_AndFileDetailsStayPinned()
    {
        StaTest.Run(() =>
        {
            using var form = new ProgressForm();
            Handles(form);
            var paths = new[] { @"E:\Essais avec accents\Vidéo échouée.mp4", @"E:\Essais avec accents\Autre vidéo.mp4" };
            var error = string.Join("\r\n", Enumerable.Range(1, 40).Select(i => $"Détail {i} : erreur longue à lire et copier intégralement, avec espaces et accents."));
            form.ReportQueue(paths);
            form.ReportQueueItem(paths[0], "failed", error);
            form.ReportState("failed", error);
            form.EnableCloseMode();
            var details = Descendants(form).OfType<TextBox>().Single(t => t.Name == "progressStatus");
            var workspace = Descendants(form).OfType<TableLayoutPanel>().Single(t => t.Name == "progressWorkspace");
            Layout(form);
            Assert.True(details.Height >= 100, $"Default reading area: {details.Bounds}");
            var viewport = (Panel)((TableLayoutPanel)form.Controls[0]).GetControlFromPosition(0, 1)!;
            Assert.False(viewport.VerticalScroll.Visible, $"viewport={viewport.ClientSize}; body={viewport.Controls[0].Bounds}; workspace={workspace.Bounds}/{workspace.MinimumSize}; details={details.Bounds}");
            using var largeFont = new Font("Segoe UI", 18);
            foreach (var width in new[] { 1100, 480, 1000 })
            {
                form.ClientSize = new Size(width, 720);
                form.Font = width == 480 ? largeFont : SystemFonts.MessageBoxFont;
                Layout(form);
                Assert.True(details.Height >= details.Font.Height * 6, $"width={width}; editor={details.Bounds}; line={details.Font.Height}; workspace={workspace.Bounds}");
                Assert.Equal(1, workspace.ColumnCount);
                var filesPanel = workspace.GetControlFromPosition(0, 0)!;
                var detailsPanel = workspace.GetControlFromPosition(0, 1)!;
                Assert.True(detailsPanel.Top >= filesPanel.Bottom);
                Assert.Equal(filesPanel.Width, detailsPanel.Width);
                Assert.Equal(ScrollBars.Vertical, details.ScrollBars);
                Assert.Contains(error, details.Text);
                Assert.Equal(0, details.SelectionStart);
            }

            var grid = Descendants(form).OfType<DataGridView>().Single();
            grid.CurrentCell = grid.Rows[0].Cells[0];
            typeof(DataGridView).GetMethod("OnCellClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(grid, [new DataGridViewCellEventArgs(0, 0)]);
            details.Select(details.TextLength - 10, 10);
            var selection = details.SelectionStart;
            form.ReportProgress(300, paths[1], "Conversion", "Processing another file.");
            Layout(form);
            Assert.Contains(error, details.Text);
            Assert.Equal(selection, details.SelectionStart);

            form.ReportQueueItem(paths[0], "failed", error + "\r\nAdditional diagnostic.");
            Assert.EndsWith("Additional diagnostic.", details.Text);
            var follow = Descendants(form).OfType<Button>().Single(b => b.Name == "followCurrentTask");
            typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(follow, [EventArgs.Empty]);
            Assert.Contains(paths[1], details.Text);
            Assert.EndsWith("Processing another file.", details.Text);
            Assert.False(follow.Enabled);
            Assert.False(form.Visible);
        });
    }

    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static void Handles(Control control) { _ = control.Handle; foreach (Control child in control.Controls) Handles(child); }
    private static void Layout(Control control)
    {
        for (var i = 0; i < 3; i++) { control.PerformLayout(); foreach (var child in Descendants(control).ToArray()) child.PerformLayout(); Application.DoEvents(); }
    }
}
