using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.AI.CreateSubtitles;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Logging;
using FrameShift.Core.Helpers;
using FrameShift.Windows.AI;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace FrameShift.Tests;

// Hidden controls only: no Show(), input injection or changes to desktop DPI settings.
public sealed class UiPilotTests
{
    private readonly ITestOutputHelper _output;
    public UiPilotTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void CompactPilots_OpenWithAllOptions_AndScrollOnlyWhenConstrained(int pilot)
    {
        StaTest.Run(() =>
        {
            using var form = CreatePilot(pilot);
            Handles(form);
            // Start with a short provisional viewport so fitting must also remove an existing scrollbar.
            form.ClientSize = new Size(form.ClientSize.Width, 320);
            Layout(form);
            typeof(Form).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [EventArgs.Empty]);
            if (form is CreateSubtitlesPickerForm)
                Descendants(form).OfType<RadioButton>().Single(r => Equals(r.Tag, CreateSubtitlesOutputFormat.AdvancedAss)).Checked = true;
            Layout(form);
            var root = (TableLayoutPanel)form.Controls[0];
            var viewport = (Panel)root.GetControlFromPosition(0, 1)!;
            var content = viewport.Controls[0];
            _output.WriteLine($"Pilot={pilot}, client={form.ClientSize}, viewport={viewport.ClientSize}, content={content.Bounds}, preferred={content.GetPreferredSize(new Size(viewport.Width, 0))}");
            Assert.True(content.Bottom <= viewport.ClientSize.Height, $"pilot={pilot}: content={content.Bounds}; viewport={viewport.ClientSize}");
            Assert.False(viewport.VerticalScroll.Visible);
            Assert.InRange(viewport.ClientSize.Height - content.Bottom, 0, 20);
            if (pilot == 1)
            {
                var cards = Descendants(form).OfType<FrameShiftChoiceCard>().ToArray();
                Assert.All(cards, card => Assert.Equal(cards[0].Top, card.Top));
                Assert.True(cards[0].Right < cards[1].Left && cards[1].Right < cards[2].Left);
            }
            if (pilot == 0)
            {
                var fps = Named<TextBox>(form, "targetFps");
                var originalWidth = fps.Width;
                form.Width += 300;
                Layout(form);
                Assert.Equal(originalWidth, fps.Width);
                Assert.InRange(fps.Width, 100, 160);
            }
            FrameShiftDialogLayout.FitInitialHeight(form, root, new Size(1600, 350));
            Layout(form);
            Assert.True(form.Height <= 350);
            Assert.True(((Control)form.AcceptButton!).Parent!.Bottom <= root.ClientSize.Height);
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void Cut_TemporalControlsStayBelowPreview_AndReflowInNarrowWindow()
    {
        StaTest.Run(() =>
        {
            using var form = CreatePilot(2);
            Handles(form);
            foreach (var width in new[] { 1120, 480, 900 })
            {
                form.ClientSize = new Size(width, 760);
                Layout(form);
                var workspace = Descendants(form).OfType<FrameShiftTimelineWorkspace>().Single();
                Assert.True(workspace.Controls[0].Bottom <= workspace.Controls[1].Top);
                Assert.Contains(Named<TextBox>(form, "startFrame"), Descendants(workspace.Controls[1]));
                Assert.Contains(Named<Panel>(form, "frameRange"), Descendants(workspace.Controls[1]));
            }
        });
    }

    private static FfmpegRunner Runner() => new(new AppLogger());
    private static MediaProbeResult Probe() => new(TimeSpan.FromSeconds(5), false, true,
        1280, 720, "h264", 15, 75, [], [], []);
    private static Form CreatePilot(int pilot) => pilot switch
    {
        0 => new InterpolateVideoForm("Vidéo très longue avec accents.mp4", 15),
        1 => new CompressImageForm("Image avec accents.png", ".png", 8192),
        2 => new CutVideoForm("absent.mp4", "absent.exe", Probe(), Runner()),
        3 => new CreateSubtitlesPickerForm("Create Subtitles", "Vidéo avec accents.mp4"),
        _ => new CropImageForm("absent.png", "absent.exe", Runner())
    };

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void Pilots_KeepCommandsOutsideScrollingContent_AfterResizeAndTextEnlargement(int pilot)
    {
        StaTest.Run(() =>
        {
            using var form = CreatePilot(pilot);
            Handles(form);
            Assert.False(form.Visible);
            Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
            var primary = Assert.IsAssignableFrom<Control>(form.AcceptButton);
            var cancel = Assert.IsAssignableFrom<Control>(form.CancelButton);
            var root = Assert.IsType<TableLayoutPanel>(form.Controls[0]);
            var actions = Assert.IsType<FrameShiftActionBar>(primary.Parent);
            Assert.Same(root, actions.Parent);
            // Text measurement stress, not a substitute for real OS DPI changes.
            var fonts = new[] { 1f, 1.5f, 2f, 3f, 1f }.Select(scale => new Font("Segoe UI", 9 * scale)).ToArray();
            try
            {
                foreach (var font in fonts)
                {
                    form.Font = font;
                    foreach (var size in new[] { new Size(1100, 760), new Size(600, 500), new Size(900, 640) })
                    {
                        form.ClientSize = size;
                        Layout(form);
                        Assert.True(actions.Top >= 0 && actions.Bottom <= root.ClientSize.Height);
                        Assert.True(actions.ClientRectangle.Contains(primary.Bounds), $"pilot {pilot}, {font.Size}, {size}: primary {primary.Bounds}, footer {actions.Size}");
                        Assert.True(actions.ClientRectangle.Contains(cancel.Bounds));
                        Assert.Equal(primary.Size, cancel.Size);
                        Assert.True(Rectangle.Intersect(cancel.Bounds, primary.Bounds).IsEmpty);
                    }
                }
            }
            finally { form.Dispose(); foreach (var font in fonts) font.Dispose(); }
            Assert.False(form.Visible);
        });
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Subtitles_OutputCycles_FitVisibleOptionsAndPreservePreset(int initialFormat)
    {
        StaTest.Run(() =>
        {
            using var form = new CreateSubtitlesPickerForm("Create Subtitles", "Essai.mp4", (CreateSubtitlesOutputFormat)initialFormat);
            Handles(form);
            Call(form, "OnLoad", EventArgs.Empty);
            var root = (TableLayoutPanel)form.Controls[0];
            var viewport = (Panel)root.GetControlFromPosition(0, 1)!;
            void AssertFits()
            {
                Layout(form);
                Assert.InRange(viewport.ClientSize.Height - viewport.Controls[0].Bottom, 0, 20);
                Assert.False(viewport.VerticalScroll.Visible);
                Assert.True(Screen.FromControl(form).WorkingArea.Contains(form.Bounds));
            }
            AssertFits();
            var radios = Descendants(form).OfType<RadioButton>().ToArray();
            Assert.All(radios, radio => Assert.IsType<RadioButton>(radio));
            foreach (var group in radios.GroupBy(r => r.Parent))
            {
                foreach (var radio in group)
                {
                    radio.Checked = true;
                    Assert.Single(group, r => r.Checked);
                }
            }
            var ass = radios.Single(r => Equals(r.Tag, CreateSubtitlesOutputFormat.AdvancedAss));
            var srt = radios.Single(r => Equals(r.Tag, CreateSubtitlesOutputFormat.StandardSrt));
            var project = radios.Single(r => Equals(r.Tag, CreateSubtitlesOutputFormat.FrameShiftSubtitleProject));
            var preset = radios.First(r => r.Tag is CreateSubtitlesAssPreset p && p != CreateSubtitlesAssPreset.Classic);
            srt.Checked = true;
            AssertFits();
            var compactSize = form.ClientSize;
            for (var i = 0; i < 4; i++)
            {
                ass.Checked = true;
                preset.Checked = true;
                Layout(form);
                Assert.True(form.IsAssPresetSectionVisible);
                AssertFits();
                Assert.True(form.ClientSize.Height > compactSize.Height);
                Assert.Equal(compactSize.Width, form.ClientSize.Width);
                Assert.Equal((CreateSubtitlesAssPreset)preset.Tag!, form.SelectedAssPreset);
                srt.Checked = true;
                Layout(form);
                Assert.False(form.IsAssPresetSectionVisible);
                AssertFits();
                Assert.Equal(compactSize, form.ClientSize);
                project.Checked = true;
                AssertFits();
                Assert.Equal(compactSize, form.ClientSize);
                Assert.Equal((CreateSubtitlesAssPreset)preset.Tag!, form.SelectedAssPreset);
            }
        });
    }

    [Theory]
    [InlineData("KB", "128", 131072L)]
    [InlineData("MB", "1,5", 1572864L)]
    public void Compression_FormatCycles_KeepTargetValuesAndSettings(string unit, string value, long bytes)
    {
        StaTest.Run(() =>
        {
            using var form = new CompressImageForm("Essai.png", ".png", 8192);
            var format = Named<ComboBox>(form, "outputFormat");
            var target = Named<CheckBox>(form, "useTarget");
            var text = Named<TextBox>(form, "targetSize");
            var units = Named<ComboBox>(form, "targetUnit");
            Assert.False(target.Enabled);
            format.SelectedItem = "JPG";
            target.Checked = true;
            text.Text = value;
            units.SelectedItem = unit;
            format.SelectedItem = "WEBP";
            Assert.True(target.Checked);
            format.SelectedItem = "PNG";
            Assert.False(target.Checked);
            Assert.True(text.ReadOnly);
            format.SelectedItem = "JPG";
            target.Checked = true;
            Assert.Equal(value, text.Text);
            Assert.Equal(unit, units.SelectedItem);
            Descendants(form).OfType<RadioButton>().Single(r => r.Text.StartsWith("Balanced")).Checked = true;
            form.DialogResult = DialogResult.OK;
            Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.UserClosing, false));
            Assert.Equal(new CompressImageSettings(CompressImageSettings.ProfileBalanced, "jpg", bytes), form.Selection);
        });
    }

    [Fact]
    public void Interpolation_PresetAndCustomValue_KeepMediaSettings()
    {
        StaTest.Run(() =>
        {
            using var form = new InterpolateVideoForm("Essai.mp4", 23.976);
            Call(form, "SetFpsText", 23.976 * 3);
            Assert.Equal("71.928", Named<TextBox>(form, "targetFps").Text);
            Named<TextBox>(form, "targetFps").Text = "59,94";
            Call(form, "OnInterpolateClicked", null!, EventArgs.Empty);
            Assert.Equal(new InterpolateVideoSettings(59.94), form.Selection);
        });
    }

    [Fact]
    public void CutPreview_CancelsObsoleteRequest_AndPublishesOnlyLatestBitmap()
    {
        StaTest.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var first = new TaskCompletionSource<Bitmap>();
            var tokens = new List<CancellationToken>();
            var calls = 0;
            using var form = new CutVideoForm("absent.mp4", "absent.exe", Probe(), Runner(), (_, token) =>
            {
                tokens.Add(token);
                return ++calls == 1 ? first.Task : Task.FromResult(new Bitmap(20, 10));
            });
            Handles(form);
            Assert.Equal(0, calls); // Constructor must not decode.
            var oldRequest = form.RequestPreviewAsync(1);
            Assert.False(oldRequest.IsCompleted);
            var latest = form.RequestPreviewAsync(16);
            Assert.True(tokens[0].IsCancellationRequested);
            Assert.Equal(1, calls); // Wait for old decoder to release its resources.
            var obsolete = new Bitmap(10, 10);
            first.SetResult(obsolete);
            Pump(latest);
            Assert.ThrowsAny<Exception>(() => obsolete.GetPixel(0, 0));
            Assert.Equal(2, calls);
            Assert.Equal(20, Field<Bitmap>(form, "_currentPreviewBitmap").Width);
            Assert.Contains("16", Field<Label>(form, "_labelPreviewState").Text);
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void CutPreview_ClosingWaitsWithoutBlocking_AndDiscardsLateBitmap()
    {
        StaTest.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var pending = new TaskCompletionSource<Bitmap>();
            CancellationToken observed = default;
            using var form = new CutVideoForm("absent.mp4", "absent.exe", Probe(), Runner(), (_, token) => { observed = token; return pending.Task; });
            Handles(form);
            var task = form.RequestPreviewAsync(1);
            var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
            Call(form, "CloseAfterPreviewAsync", form, closing);
            Assert.True(closing.Cancel);
            Assert.True(observed.IsCancellationRequested);
            Assert.False(task.IsCompleted);
            var bitmap = new Bitmap(10, 10);
            pending.SetResult(bitmap);
            Pump(task);
            Assert.ThrowsAny<Exception>(() => bitmap.GetPixel(0, 0));
            Assert.Null(Field<Bitmap?>(form, "_currentPreviewBitmap"));
        });
    }

    [Fact]
    public void CutPreview_ErrorRemainsInline_AndNextRequestRecovers()
    {
        StaTest.Run(() =>
        {
            var calls = 0;
            using var form = new CutVideoForm("absent.mp4", "absent.exe", Probe(), Runner(), (_, _) =>
                ++calls == 1 ? Task.FromException<Bitmap>(new IOException("test decode failure")) : Task.FromResult(new Bitmap(8, 8)));
            Pump(form.RequestPreviewAsync(1));
            Assert.Contains("test decode failure", Field<Label>(form, "_labelPreviewState").Text);
            Pump(form.RequestPreviewAsync(2));
            Assert.NotNull(Field<Bitmap>(form, "_currentPreviewBitmap"));
        });
    }

    [Fact]
    public void Cut_RealDecoder_UsesAccentedPath_AndReleasesPreviewFiles()
    {
        StaTest.Run(() =>
        {
            var video = Path.Combine(Path.GetTempPath(), $"FrameShift vidéo été {Guid.NewGuid():N}.mp4");
            var before = Directory.GetFiles(Path.GetTempPath(), "frameshift_preview_*.png").ToHashSet();
            var tools = new ToolLocator();
            var runner = Runner();
            try
            {
                var generation = runner.RunAsync(tools.ResolveFfmpegPath(),
                    ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=blue:s=160x90:r=15", "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", video],
                    null, null, video, "UI pilot fixture", "CPU", CancellationToken.None);
                Pump(generation);
                Assert.Equal(0, generation.Result.ExitCode);
                var probing = new FfprobeRunner(new AppLogger()).TryProbeMediaAsync(tools.ResolveFfprobePath(), video, CancellationToken.None);
                Pump(probing);
                Assert.True(probing.Result.Success);
                using (var form = new CutVideoForm(video, tools.ResolveFfmpegPath(), probing.Result.Probe!, runner))
                {
                    Handles(form);
                    Pump(form.RequestPreviewAsync(1));
                    Assert.Equal(new Size(160, 90), Field<Bitmap>(form, "_currentPreviewBitmap").Size);
                    var pending = form.RequestPreviewAsync(10);
                    form.Dispose();
                    Pump(pending);
                    Assert.Null(Field<Bitmap?>(form, "_currentPreviewBitmap"));
                }
                Assert.DoesNotContain(Directory.GetFiles(Path.GetTempPath(), "frameshift_preview_*.png"), p => !before.Contains(p));
            }
            finally { File.Delete(video); }
        });
    }

    [Fact]
    public void Cut_FrameTimeFieldsAndTrack_KeepSelectionAfterReflow()
    {
        StaTest.Run(() =>
        {
            using var form = new CutVideoForm("absent.mp4", "absent.exe", Probe(), Runner());
            Named<TextBox>(form, "startTime").Text = "00:00:01.000";
            Assert.Equal(true, Call(form, "ApplyBoundaryFromTimeText", "start"));
            Named<TextBox>(form, "endTime").Text = "00:00:04.000";
            Assert.Equal(true, Call(form, "ApplyBoundaryFromTimeText", "end"));
            Assert.Equal("16", Named<TextBox>(form, "startFrame").Text);
            Assert.Equal("60", Named<TextBox>(form, "endFrame").Text);
            foreach (var width in new[] { 1120, 600, 1000, 480 })
            {
                form.ClientSize = new Size(width, 700);
                Layout(form);
                foreach (var frame in new[] { 1, 16, 60, 75 })
                {
                    var x = (int)Call(form, "ConvertFrameToTrackX", frame)!;
                    Assert.Equal(frame, Call(form, "ConvertTrackXToFrame", x));
                }
            }
            Call(form, "ConfirmCut");
            Assert.Equal(new CutVideoSettings(16, 60, 15), form.Selection);
        });
    }

    [Fact]
    public void Crop_LoadFailureIsInline_AndDisposeDuringLoadIsSafe()
    {
        StaTest.Run(() =>
        {
            using (var form = new CropImageForm("absent.png", "absent.exe", Runner()))
            {
                Pump(form.StartPreviewAsync());
                Assert.Contains("Preview unavailable:", Field<Label>(form, "_sourceLabel").Text);
                Assert.Null(Field<Bitmap?>(form, "_previewBitmap"));
                Assert.False(((Control)form.AcceptButton!).Enabled);
            }
            using var closing = new CropImageForm("absent.png", "absent.exe", Runner());
            var task = closing.StartPreviewAsync();
            closing.Dispose();
            Pump(task);
            Assert.Null(Field<Bitmap?>(closing, "_previewBitmap"));
        });
    }

    [Fact]
    public void Crop_ResizeAndFit_PreserveSourcePixels_WithAccentedFilePath()
    {
        StaTest.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"FrameShift image été {Guid.NewGuid():N}.png");
            try
            {
                using (var image = new Bitmap(1200, 800)) image.Save(path);
                using var form = new CropImageForm(path, "absent.exe", Runner());
                Assert.False(((Control)form.AcceptButton!).Enabled);
                Handles(form);
                Layout(form);
                Pump(form.StartPreviewAsync());
                Assert.NotNull(Field<Bitmap>(form, "_previewBitmap"));
                Assert.True(((Control)form.AcceptButton!).Enabled);
                var selection = new VideoCropSettings(100, 120, 600, 400);
                Call(form, "SetCropRectFromSource", selection);
                foreach (var size in new[] { new Size(700, 500), new Size(1200, 800), new Size(600, 600), new Size(1120, 720) })
                {
                    form.ClientSize = size;
                    Layout(form);
                    Call(form, "RefreshPreviewLayout");
                    Call(form, "FitPreviewToView");
                    Assert.Equal(selection, Call(form, "GetSourceCropRect"));
                }
                form.Dispose();
                form.Dispose(); // Cleanup is idempotent.
            }
            finally { File.Delete(path); }
        });
    }

    private static T Named<T>(Control control, string name) where T : Control => (T)Descendants(control).Single(c => c.Name == name);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Handles(Control control) { _ = control.Handle; foreach (Control child in control.Controls) Handles(child); }
    private static void Layout(Control control) { for (var i = 0; i < 3; i++) { control.PerformLayout(); foreach (var child in Descendants(control)) child.PerformLayout(); } }
    private static void Pump(Task task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(1); }
        Assert.True(task.IsCompleted, "Hidden preview operation did not finish within 10 s.");
        task.GetAwaiter().GetResult();
        Application.DoEvents();
    }
}
