using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Forms;
using Xunit;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed partial class VideoLoopUiTests
{
    private static CutVideoForm Form() => new("absent.mp4", "absent.exe",
        new(TimeSpan.FromSeconds(6), true, true, 160, 96, "h264", 15, 90, ["aac"], [], []),
        new(new AppLogger()), (_, _) => Task.FromResult(new Bitmap(8, 8)));

    [Fact]
    public void CandidatePositionsExistingTimeline_ManualEditingInvalidatesExactBounds()
    {
        StaTest.Run(() =>
        {
            using var form = Form();
            Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            form.ApplyLoopAnalysis(new([new VideoLoopCandidate(2d / 15, 32d / 15, 99)], 8));
            Assert.Equal("3", Field<TextBox>(form, "_textStart").Text);
            Assert.Equal("32", Field<TextBox>(form, "_textEnd").Text);
            Call(form, "ApplyBoundaryFromTimeText", "start");
            Assert.Equal("3", Field<TextBox>(form, "_textStart").Text);
            Assert.NotNull(Field<VideoLoopCandidate?>(form, "_loopCandidate"));
            Field<TextBox>(form, "_textStart").Text = "8";
            Call(form, "ApplyBoundaryFromText", "start");
            Assert.Null(Field<VideoLoopCandidate?>(form, "_loopCandidate"));
            Pump(Field<Task>(form, "_previewTask"));
        });
    }

    [Theory]
    [InlineData("smpte2084", false)]
    [InlineData("arib-std-b67", false)]
    [InlineData("bt709", true)]
    [InlineData(null, false)]
    public void ColorPreflight_IsPresentBeforeSearch_AndDoesNotDisableOrdinaryCut(string? transfer, bool supported)
    {
        StaTest.Run(() =>
        {
            using var form = new CutVideoForm("absent.mp4", "absent.exe",
                VideoLoopColorPolicyTests.Probe(transfer, pixelFormat: "yuv420p10le", bits: 10, profile: "Main 10"),
                new(new AppLogger()), (_, _) => Task.FromResult(new Bitmap(8, 8)));
            Handles(form);
            Assert.Equal(supported, Field<Button>(form, "_findLoop").Enabled);
            Assert.Equal(supported, Field<Button>(form, "_pingPongLoop").Enabled);
            Assert.True(Field<Button>(form, "_cutOk").Enabled);
            Assert.Equal("Export Selection", Field<Button>(form, "_cutOk").Text);
            Assert.Null(Field<Control>(form, "_loopOptions").Parent);
            if (!supported)
            {
                var policy = Field<VideoLoopColorPolicy>(form, "_loopColorPolicy");
                Assert.Contains(Descendants(form), control => control.Text == policy.Message &&
                    !Field<Control>(form, "_loopOptions").Contains(control));
                var entered = false;
                Pump(form.StartLoopWorkAsync(_ => { entered = true; return Task.CompletedTask; }));
                Assert.False(entered);
            }
        });
    }

    [Theory]
    [InlineData("smpte2084")]
    [InlineData("arib-std-b67")]
    public void ConfirmedHdr_AnnouncesSdrBeforeSearch_AndUsesTheSdrFooter(string transfer)
    {
        StaTest.Run(() =>
        {
            var tools = new ToolLocator();
            using var form = new CutVideoForm("absent.mp4", tools.ResolveFfmpegPath(),
                VideoLoopColorPolicyTests.Probe(transfer, "bt2020", "yuv420p10le", 10, space: "bt2020nc"),
                new(new AppLogger()), (_, _) => Task.FromResult(new Bitmap(8, 8)));
            Handles(form);
            Assert.Contains("HDR source → SDR output", Field<Label>(form, "_loopColorStatus").Text);
            Assert.True(Field<Button>(form, "_findLoop").Enabled);
            Assert.Equal("Export Selection", Field<Button>(form, "_cutOk").Text);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Natural));
            Assert.Equal("Export SDR Loop", Field<Button>(form, "_cutOk").Text);
            Assert.True(Field<Button>(form, "_cutOk").Enabled);
            form.ClientSize = new Size(480, 340); form.PerformLayout();
            var button = Field<Button>(form, "_cutOk");
            Assert.True(form.ClientRectangle.Contains(form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle))));
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.Cut));
            Assert.Equal("Export Selection", Field<Button>(form, "_cutOk").Text);
        });
    }

    [Fact]
    public void PingPongDefaultsToSilence_AndFooterRemainsReachableAfterExpansion()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            Pump(form.SetLoopContextAsync(CutVideoForm.LoopContext.ForwardBackward));
            Assert.False(Field<CheckBox>(form, "_loopAudio").Checked);
            Assert.False(Field<CheckBox>(form, "_loopAudio").Enabled);
            Assert.True(Field<ComboBox>(form, "_loopSpeed").Enabled);
            foreach (var size in new[] { new Size(1120, 760), new Size(480, 340), new Size(760, 540) })
            {
                form.ClientSize = size;
                for (var pass = 0; pass < 3; pass++) foreach (var c in Descendants(form).Prepend(form)) c.PerformLayout();
                var ok = Field<Button>(form, "_cutOk");
                var bounds = form.RectangleToClient(ok.RectangleToScreen(ok.ClientRectangle));
                Assert.True(bounds.Bottom <= form.ClientSize.Height);
                Assert.True(bounds.Right <= form.ClientSize.Width);
            }
        });
    }

    [Fact]
    public void LongLoopWork_IsCancelable_AndDisposalDoesNotUseLateResults()
    {
        StaTest.Run(() =>
        {
            using var form = Form(); Handles(form);
            var entered = false; var canceled = false;
            var work = form.StartLoopWorkAsync(async token =>
            {
                entered = true;
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { canceled = true; throw; }
            });
            Assert.True(entered); Assert.False(Field<Button>(form, "_cutOk").Enabled);
            Field<CancellationTokenSource>(form, "_loopCancellation").Cancel(); Pump(work);
            Assert.True(canceled); Assert.True(Field<Button>(form, "_cutOk").Enabled);
            work = form.StartLoopWorkAsync(token => Task.Delay(Timeout.Infinite, token));
            form.Dispose(); Pump(work);
            Assert.True(form.IsDisposed);
        });
    }

    [Fact]
    public void PacedPlayer_PlaysEveryFrame_AndReleasesFile()
    {
        StaTest.Run(() =>
        {
            var source = Path.Combine(Path.GetTempPath(), $"FrameShift loop player été {Guid.NewGuid():N}.mp4");
            var tools = new ToolLocator(); var runner = new FfmpegRunner(new AppLogger());
            try
            {
                var generate = runner.RunAsync(tools.ResolveFfmpegPath(),
                    ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=15:d=3", "-c:v", "libx264", "-pix_fmt", "yuv420p", source],
                    null, null, source, "Loop player fixture", "CPU", CancellationToken.None);
                Pump(generate); Assert.Equal(0, generate.Result.ExitCode);
                using var window = new System.Windows.Forms.Form();
                using var player = new LoopVideoPlayer();
                window.Controls.Add(player); Handles(window);
                Pump(player.PlayAsync(tools.ResolveFfmpegPath(), runner, source, 160, 96, CancellationToken.None));
                Assert.Equal(45, player.PlayedFrames);
                window.Controls.Remove(player); player.Dispose();
                File.Delete(source); Assert.False(File.Exists(source));
            }
            finally { File.Delete(source); }
        });
    }

    private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o)!;
    private static object? Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(o, args);
    private static IEnumerable<Control> Descendants(Control c)
    { foreach (Control child in c.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static void Handles(Control c)
    {
        _ = c.Handle;
        foreach (Control child in c.Controls) Handles(child);
        if (c is System.Windows.Forms.Form) SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
    }
    private static void Pump(Task task)
    {
        var watch = Stopwatch.StartNew();
        var context = SynchronizationContext.Current;
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            Application.DoEvents();
            SynchronizationContext.SetSynchronizationContext(context);
            Thread.Sleep(1);
        }
        Assert.True(task.IsCompleted, "Loop UI task did not finish within 30 seconds.");
        task.GetAwaiter().GetResult(); Application.DoEvents();
        SynchronizationContext.SetSynchronizationContext(context);
    }
}
