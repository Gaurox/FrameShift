using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Logging;
using FrameShift.Core.Helpers;
using FrameShift.Windows.AI;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Forms;
using Xunit;

namespace FrameShift.Tests;

// Hidden native controls; font stress does not claim real OS DPI qualification.
public sealed class UiEditorTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, ref NativeRect rectangle);

    [Fact]
    public void Seek_MapsNativeThumbCentersRatherThanControlEdges() => StaTest.Run(() =>
    {
        using var track = new SeekTrackBar { Width = 500, Minimum = 0, Maximum = 1000 };
        _ = track.Handle;
        foreach (var expected in new[] { 0, 500, 1000 })
        {
            track.Value = expected;
            var thumb = new NativeRect();
            SendMessage(track.Handle, 0x0400 + 25, IntPtr.Zero, ref thumb);
            var x = (thumb.Left + thumb.Right) / 2;
            Invoke(track, "SetValueFromPoint", x);
            Assert.InRange(Math.Abs(track.Value - expected), 0, 3);
        }
    });
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void Editors_KeepEqualCommandsOutsideScrollableOptions(int editor) => StaTest.Run(() =>
    {
        using var form = Create(editor);
        Handles(form);
        Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
        var primary = (Button)form.AcceptButton!;
        var cancel = (Button)form.CancelButton!;
        var actions = Assert.IsType<FrameShiftActionBar>(primary.Parent);
        Assert.Same(actions, cancel.Parent);
        var root = Assert.IsType<TableLayoutPanel>(form.Controls[0]);
        Assert.Same(root, actions.Parent);
        using var font = new Font("Segoe UI", 18);
        foreach (var size in new[] { form.ClientSize, new Size(600, 620), new Size(1000, 760) })
        {
            form.ClientSize = size;
            Layout(form);
            Assert.True(actions.Top >= 0 && actions.Bottom <= root.ClientSize.Height, $"{editor}: {actions.Bounds}, {root.Size}");
            Assert.True(actions.ClientRectangle.Contains(primary.Bounds));
            Assert.True(actions.ClientRectangle.Contains(cancel.Bounds));
            Assert.Equal(primary.Size, cancel.Size);
            Assert.True(Rectangle.Intersect(primary.Bounds, cancel.Bounds).IsEmpty);
            form.Font = font;
        }
        Assert.False(form.Visible);
    });

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AudioAndGif_TimeMappingSurvivesResize(bool gif) => StaTest.Run(() =>
    {
        using var form = Create(gif ? 1 : 0);
        Handles(form);
        foreach (var width in new[] { 960, 480, 1280 })
        {
            form.ClientSize = new Size(width, 740);
            Layout(form);
            var workspace = Descendants(form).OfType<FrameShiftTimelineWorkspace>().Single();
            Assert.True(workspace.Controls[0].Bottom <= workspace.Controls[1].Top);
            var middle = (int)Invoke(form, gif ? "ConvertSecondsToTrackX" : "TimeToX", 5d)!;
            var seconds = (double)Invoke(form, gif ? "ConvertTrackXToSeconds" : "XToTime", middle)!;
            Assert.InRange(seconds, 4.98, 5.02);
        }
        if (gif)
        {
            var settings = (CreateGifSettings)Invoke(form, "BuildSelection")!;
            Assert.Equal(0d, settings.StartSeconds);
            Assert.Equal(10d, settings.DurationSeconds);
        }
    });

    [Theory]
    [InlineData(0, DialogResult.Cancel)] [InlineData(1, DialogResult.Cancel)] [InlineData(2, DialogResult.Cancel)]
    [InlineData(3, DialogResult.Cancel)] [InlineData(4, DialogResult.Cancel)] [InlineData(5, DialogResult.Cancel)] [InlineData(6, DialogResult.Cancel)]
    [InlineData(0, DialogResult.OK)] [InlineData(1, DialogResult.OK)] [InlineData(2, DialogResult.OK)]
    [InlineData(3, DialogResult.OK)] [InlineData(4, DialogResult.OK)] [InlineData(5, DialogResult.OK)] [InlineData(6, DialogResult.OK)]
    public void Editors_IdleModalClosePreservesResultWithoutReentering(int editor, DialogResult result) => StaTest.Run(() =>
    {
        using var form = Create(editor);
        Handles(form);
        var closingCalls = 0;
        var insideModalCheck = true;
        var closed = new TaskCompletionSource();
        form.FormClosing += (_, _) =>
        {
            if (++closingCalls > 1) Assert.False(insideModalCheck, "Close reentered the original modal completion check.");
        };
        form.FormClosed += (_, _) => closed.SetResult();
        form.DialogResult = result;
        // Exercise WinForms' actual modal completion check without showing a window.
        Invoke(form, "CheckCloseDialog", true);
        insideModalCheck = false;
        Assert.Equal(DialogResult.None, form.DialogResult);
        Assert.Equal(1, closingCalls);
        Pump(closed.Task);
        Assert.Equal(result, form.DialogResult);
        Assert.Equal(2, closingCalls);
        Assert.True(form.IsDisposed);
        Assert.False(form.Visible);
    });

    [Theory] [InlineData(false)] [InlineData(true)]
    public void Preview_RequestsSerializeAndRejectLateBitmapOnClose(bool crop) => StaTest.Run(() =>
    {
        var release = new TaskCompletionSource<Bitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        CancellationToken token = default;
        Task<Bitmap> Capture(double _, CancellationToken ct) { calls++; token = ct; return release.Task; }
        using Form form = crop ? new CropVideoForm("absent.mp4", "absent.exe", Probe(), Runner(), Capture)
            : new CreateGifForm("absent.mp4", "absent.exe", Probe(), Runner(), Capture);
        Handles(form);
        Task Request() => crop ? ((CropVideoForm)form).RequestFrameAsync(0, false) : ((CreateGifForm)form).RequestFrameAsync(0);
        var first = Request();
        var second = Request();
        Assert.Equal(1, calls);
        Assert.True(token.IsCancellationRequested);
        var closed = new TaskCompletionSource();
        form.FormClosed += (_, _) => closed.SetResult();
        form.DialogResult = DialogResult.Cancel;
        Invoke(form, "CheckCloseDialog", true);
        Assert.Equal(DialogResult.None, form.DialogResult);
        // A repeated native WM_CLOSE must not dispose controls during decoding.
        form.Close();
        Assert.False(form.IsDisposed);
        var ping = new TaskCompletionSource();
        form.BeginInvoke(new Action(() => ping.SetResult()));
        Pump(ping.Task);
        Assert.False(closed.Task.IsCompleted);
        var bitmap = new Bitmap(32, 16);
        release.SetResult(bitmap);
        Pump(Task.WhenAll(first, second, closed.Task));
        Assert.True(form.IsDisposed);
        Assert.Equal(DialogResult.Cancel, form.DialogResult);
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0));
        Assert.False(form.Visible);
    });

    [Fact]
    public void CropVideo_SourceSelectionSurvivesCanvasResizeAndFrameRefresh() => StaTest.Run(() =>
    {
        using var form = new CropVideoForm("absent.mp4", "absent.exe", Probe(), Runner(),
            (_, _) => Task.FromResult(new Bitmap(1280, 720)));
        Handles(form);
        Layout(form);
        Pump(form.RequestFrameAsync(0, false));
        var original = (VideoCropSettings)Invoke(form, "GetSourceCropRect")!;
        foreach (var size in new[] { new Size(700, 620), new Size(1120, 720) })
        {
            form.ClientSize = size;
            Layout(form);
            Invoke(form, "RefreshPreviewLayout");
            Pump(form.RequestFrameAsync(3, true));
            var current = (VideoCropSettings)Invoke(form, "GetSourceCropRect")!;
            Assert.InRange(Math.Abs(current.X - original.X), 0, 2);
            Assert.InRange(Math.Abs(current.Y - original.Y), 0, 2);
            Assert.InRange(Math.Abs(current.Width - original.Width), 0, 2);
            Assert.InRange(Math.Abs(current.Height - original.Height), 0, 2);
        }
    });

    [Fact]
    public void Pdf_AsyncImportsKeepHistoryAndGeometryInPageUnits() => StaTest.Run(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"UI E image avec accents é {Guid.NewGuid():N}.png");
        using (var image = new Bitmap(320, 180)) image.Save(path);
        try
        {
            using var form = new ImageToPdfForm(path, "absent.exe", Runner());
            Handles(form);
            Layout(form);
            Pump(form.StartInitialImagesAsync());
            var items = (System.Collections.IList)Field(form, "_items")!;
            Assert.Single(items.Cast<object>());
            var settings = items[0]!.GetType().GetProperty("Settings")!.GetValue(items[0]);
            var before = System.Text.Json.JsonSerializer.Serialize(settings);
            form.ClientSize = new Size(600, 620);
            Layout(form);
            Invoke(form, "FitPreviewToView");
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(settings));
            Assert.True(((Button)form.AcceptButton!).Enabled);
            var history = Invoke(form, "CaptureEditorHistoryState")!;
            var item = Assert.IsType<ImageToPdfItemSettings>(settings);
            item.RotationAngleDegrees = 27;
            item.Crop = new ImageToPdfCropSettings { Left = .1, Top = .1, Right = .1, Bottom = .1 };
            var changed = System.Text.Json.JsonSerializer.Serialize(item);
            Invoke(form, "CommitHistoryIfChanged", history);
            // Undo/redo must use the decoded cache, even after the source is unavailable.
            File.Delete(path);
            Invoke(form, "UndoLastAction");
            items = (System.Collections.IList)Field(form, "_items")!;
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(items[0]!.GetType().GetProperty("Settings")!.GetValue(items[0])));
            Invoke(form, "RedoLastAction");
            items = (System.Collections.IList)Field(form, "_items")!;
            Assert.Equal(changed, System.Text.Json.JsonSerializer.Serialize(items[0]!.GetType().GetProperty("Settings")!.GetValue(items[0])));
            Assert.False(form.Visible);
        }
        finally { File.Delete(path); }
    });

    [Fact]
    public async Task Editors_LoadRealMediaAndDisposePreviewTemporaries()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"FrameShift E vidéo été {Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var video = Path.Combine(directory, "Vidéo paysage é.mp4");
        var audio = Path.Combine(directory, "Audio é.wav");
        var image = Path.Combine(directory, "Image portrait é.png");
        var webp = Path.Combine(directory, "Image portrait é.webp");
        var subtitle = Path.Combine(directory, "Sous-titres é.srt");
        var locator = new ToolLocator();
        var ffmpeg = locator.ResolveFfmpegPath();
        var ffprobe = locator.ResolveFfprobePath();
        var runner = Runner();
        var probeRunner = new FfprobeRunner(new AppLogger());
        try
        {
            async Task Generate(params string[] arguments)
            {
                var result = await runner.RunAsync(ffmpeg, arguments, TimeSpan.FromSeconds(2), null, video,
                    "UI E test fixture", "CPU", CancellationToken.None);
                Assert.Equal(0, result.ExitCode);
                Assert.True(result.ProcessTerminationConfirmed);
            }
            await Generate("-f", "lavfi", "-i", "testsrc2=s=320x180:r=15:d=2", "-c:v", "libx264", video);
            await Generate("-f", "lavfi", "-i", "sine=frequency=880:sample_rate=22050:d=2", audio);
            using (var bitmap = new Bitmap(180, 320)) bitmap.Save(image);
            await Generate("-i", image, "-frames:v", "1", webp);
            await File.WriteAllTextAsync(subtitle, "1\r\n00:00:00,000 --> 00:00:01,900\r\nEssai été\r\n\r\n");
            var probe = (await probeRunner.TryProbeMediaAsync(ffprobe, video, CancellationToken.None)).Probe!;
            Assert.NotNull(probe);

            StaTest.Run(() =>
            {
                string audioTemporary;
                using (var form = new CutAudioForm(audio, ffmpeg, ffprobe, 2, runner, probeRunner))
                {
                    Handles(form); Layout(form);
                    Pump(form.StartWorkspaceAsync());
                    Assert.True(form.IsWorkspaceReadyForTesting);
                    Assert.NotEmpty((double[])Field(form, "_waveformPoints")!);
                    audioTemporary = form.TemporaryRootPathForTesting;
                    Assert.False(form.Visible);
                    CloseHiddenEditor(form);
                }
                Assert.False(Directory.Exists(audioTemporary));
            });
            StaTest.Run(() =>
            {
                string gifTemporary;
                using (var form = new CreateGifForm(video, ffmpeg, probe, runner))
                {
                    Handles(form); Layout(form);
                    Pump(form.RequestFrameAsync(.5));
                    Assert.IsType<Bitmap>(Field(form, "_currentPreviewBitmap"));
                    Pump((Task)Invoke(form, "StartGifPreviewAsync")!);
                    gifTemporary = Assert.IsType<string>(Field(form, "_gifPreviewPath"));
                    Assert.True(File.Exists(gifTemporary));
                    Assert.False(form.Visible);
                    CloseHiddenEditor(form);
                }
                Assert.False(File.Exists(gifTemporary));
            });
            StaTest.Run(() =>
            {
                using (var form = new CropVideoForm(video, ffmpeg, probe, runner))
                {
                    Handles(form); Layout(form);
                    Pump(form.RequestFrameAsync(.5, false));
                    var bitmap = Assert.IsType<Bitmap>(Field(form, "_previewBitmap"));
                    Assert.Equal(new Size(320, 180), bitmap.Size);
                    Assert.True(((Button)form.AcceptButton!).Enabled);
                    Assert.False(form.Visible);
                    CloseHiddenEditor(form);
                }
            });
            StaTest.Run(() =>
            {
                using (var form = new JoinVideosForm([video, video], ffmpeg, ffprobe, runner, probeRunner,
                    uiSettingsPathForTesting: Path.Combine(directory, "ui.json")))
                {
                    Handles(form); Layout(form);
                    Invoke(form, "AddPaths", new[] { video, video }, -1);
                    Pump(form.EnsureItemsLoadedAsync());
                    Assert.Equal(2, form.TimelinePaths.Count);
                    Assert.True(((Button)form.AcceptButton!).Enabled);
                    Assert.False(form.Visible);
                    CloseHiddenEditor(form);
                }
            });
            StaTest.Run(() =>
            {
                using (var form = new AddSubtitlesToVideoBurnEditorForm(video, ffmpeg, probe, runner,
                    new(subtitle, AddSubtitlesToVideoMode.BurnIntoVideo)))
                {
                    Handles(form); Layout(form);
                    Pump(form.RenderPreviewAsync(.5));
                    Assert.IsType<Bitmap>(Field(form, "_previewBitmap"));
                    Assert.False(form.Visible);
                    CloseHiddenEditor(form);
                }
            });
            StaTest.Run(() =>
            {
                using (var form = new RemoveObjectEditorForm(image, new AppLogger()))
                {
                    Handles(form); Layout(form);
                    Pump(form.StartImageAsync());
                    Assert.Equal(new Size(180, 320), Assert.IsType<Bitmap>(Field(form, "_imageBitmap")).Size);
                    Assert.True(((Button)form.AcceptButton!).Enabled);
                    Assert.False(form.Visible);
                    CloseHiddenEditor(form);
                }
            });
            StaTest.Run(() =>
            {
                using (var form = new ImageToPdfForm(webp, ffmpeg, runner))
                {
                    Handles(form); Layout(form);
                    Pump(form.StartInitialImagesAsync());
                    var imported = ((System.Collections.IList)Field(form, "_items")!).Cast<object>().ToArray();
                    Assert.True(imported.Length == 1, ((TextBox)Field(form, "_loadStatus")!).Text);
                    Assert.True(((Button)form.AcceptButton!).Enabled);
                    Assert.False(form.Visible);
                    CloseHiddenEditor(form);
                }
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void RemoveObject_BrushDiameterStaysInImagePixels() => StaTest.Run(() =>
    {
        using var form = new RemoveObjectEditorForm("absent.png", new AppLogger());
        using var bitmap = new Bitmap(320, 180);
        SetField(form, "_imageBitmap", new Bitmap(bitmap));
        SetField(form, "_maskData", new bool[320, 180]);
        Handles(form);
        Layout(form);
        Invoke(form, "FitToWindow");
        var destination = (RectangleF)Invoke(form, "GetImageDestRect")!;
        var point = new Point((int)(destination.Left + destination.Width / 2), (int)(destination.Top + destination.Height / 2));
        var imagePoint = (PointF)Invoke(form, "ScreenToImage", point)!;
        Assert.InRange(imagePoint.X, 159, 161);
        Assert.InRange(imagePoint.Y, 89, 91);
        var diameter = (NumericUpDown)Field(form, "_brushSizeNum")!;
        diameter.Value = 40;
        Invoke(form, "PaintCircle", 160, 90, true);
        var mask = (bool[,])Field(form, "_maskData")!;
        Assert.True(mask[160, 90]);
        Assert.False(mask[181, 90]);
        form.ClientSize = new Size(600, 620);
        Layout(form);
        Invoke(form, "FitToWindow");
        Assert.Equal(40, Field(form, "_brushSize"));
        Assert.True(mask[160, 90]);
    });

    private static FfmpegRunner Runner() => new(new AppLogger());
    private static void CloseHiddenEditor(Form form)
    {
        var closed = new TaskCompletionSource();
        form.FormClosed += (_, _) => closed.SetResult();
        form.Close();
        Pump(closed.Task);
        Assert.True(form.IsDisposed);
        Assert.False(form.Visible);
    }
    private static MediaProbeResult Probe() => new(TimeSpan.FromSeconds(10), true, true, 1280, 720, "h264", 30, 300, [], [], []);
    private static Form Create(int editor) => editor switch
    {
        0 => new CutAudioForm("absent.wav", "absent.exe", "absent.exe", 10, Runner(), new FfprobeRunner(new AppLogger())),
        1 => new CreateGifForm("absent.mp4", "absent.exe", Probe(), Runner()),
        2 => new CropVideoForm("absent.mp4", "absent.exe", Probe(), Runner()),
        3 => new JoinVideosForm([], "absent.exe", "absent.exe", Runner(), new FfprobeRunner(new AppLogger()), uiSettingsPathForTesting: Path.Combine(Path.GetTempPath(), "frameshift-ui-e-settings.json")),
        4 => new AddSubtitlesToVideoBurnEditorForm("absent.mp4", "absent.exe", Probe(), Runner(), new("absent.srt", AddSubtitlesToVideoMode.BurnIntoVideo)),
        5 => new RemoveObjectEditorForm("absent.png", new AppLogger()),
        _ => new ImageToPdfForm("absent.png", "absent.exe", Runner())
    };
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static void Handles(Control root)
    {
        _ = root.Handle;
        foreach (Control c in root.Controls) Handles(c);
        // Hidden forms never enter Application.Run; reproduce its UI continuation context explicitly.
        if (root is Form) SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
    }
    private static void Layout(Control root) { for (var i = 0; i < 4; i++) LayoutOnce(root); }
    private static void LayoutOnce(Control root) { root.PerformLayout(); foreach (Control c in root.Controls) LayoutOnce(c); }
    private static object? Field(object root, string name) => root.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(root);
    private static void SetField(object root, string name, object value) => root.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(root, value);
    private static object? Invoke(object target, string name, params object[] args)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (method is not null) return method.Invoke(target, args);
        }
        throw new MissingMethodException(name);
    }
    private static void Pump(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var context = SynchronizationContext.Current;
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            // DoEvents tears down its temporary message loop; keep the context for the next request.
            SynchronizationContext.SetSynchronizationContext(context);
            Thread.Sleep(5);
        }
        Assert.True(task.IsCompleted);
        task.GetAwaiter().GetResult();
    }
}
