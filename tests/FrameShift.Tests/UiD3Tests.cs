using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.AI;
using FrameShift.Core.AI.Upscale;
using FrameShift.Core.AI.VideoInterpolation;
using FrameShift.Windows.AI;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Helpers;
using Xunit;

namespace FrameShift.Tests;

// Native handles stay hidden. Font/width stress is not Windows DPI certification.
[Collection(WinFormsTestCollection.Name)]
public sealed class UiD3Tests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    [InlineData(10)] [InlineData(11)] [InlineData(12)]
    public void Windows_KeepCommandsReachable_AndOpenWithoutUnusedScroll(int variant)
    {
        StaTest.Run(() =>
        {
            using var form = Create(variant);
            Handles(form);
            Invoke(form, "OnLoad", EventArgs.Empty);
            Layout(form);
            Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
            Assert.Single(Descendants(form).OfType<FrameShiftHeader>());
            var root = Assert.IsType<TableLayoutPanel>(form.Controls[0]);
            var viewport = Assert.IsType<Panel>(root.GetControlFromPosition(0, 1));
            Assert.False(viewport.VerticalScroll.Visible, $"variant={variant}; client={form.ClientSize}; viewport={viewport.Bounds}; content={viewport.Controls[0].Bounds}");
            using var large = new Font("Segoe UI", 18);
            foreach (var width in new[] { 1000, 480, 800 })
            {
                form.Font = width == 480 ? large : SystemFonts.MessageBoxFont;
                form.ClientSize = new Size(width, 700);
                Layout(form);
                var actions = Assert.IsType<FrameShiftActionBar>(root.GetControlFromPosition(0, 3));
                Assert.InRange(actions.Top, 0, root.ClientSize.Height);
                Assert.True(actions.Bottom <= root.Height);
                Assert.All(actions.Controls.Cast<Control>(), button => Assert.True(actions.ClientRectangle.Contains(button.Bounds)));
                Assert.Equal(((Control)form.AcceptButton!).Size, ((Control)form.CancelButton!).Size);
                foreach (var box in Descendants(form).OfType<TextBox>()) Assert.True(box.Height >= box.Font.Height);
                foreach (var choice in Descendants(form).OfType<RadioButton>())
                    Assert.True(choice.Parent!.ClientRectangle.Contains(choice.Bounds));
                Assert.False(form.Visible);
            }
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Noise_NativeChoicesStayExclusive_AndStereoFollowsSource(bool video)
    {
        StaTest.Run(() =>
        {
            using var form = Create(video ? 1 : 0);
            var choices = Descendants(form).OfType<FrameShiftChoiceCard>().ToArray();
            Assert.Equal(4, choices.Length);
            Assert.Equal("Maximum", choices.Single(c => c.Checked).Text);
            foreach (var choice in choices)
            {
                Invoke(choice, "OnClick", EventArgs.Empty);
                Assert.Single(choices, c => c.Checked);
                var selected = video ? ((RemoveNoiseVideoPickerForm)form).SelectedStrength : ((RemoveNoiseAudioPickerForm)form).SelectedStrength;
                Assert.Equal(choice.Tag, selected);
            }
            var stereo = Named<CheckBox>(form, "stereo");
            Invoke(stereo, "OnClick", EventArgs.Empty);
            Assert.True(stereo.Checked);
            using var mono = Create(video ? 8 : 7);
            Assert.False(Named<CheckBox>(mono, "stereo").Enabled);
            Assert.False(Named<CheckBox>(mono, "stereo").Checked);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Noise_ClosingWaitsForLifetimeAndKeepsRequestedResult(bool video)
    {
        StaTest.Run(() =>
        {
            using var form = Create(video ? 1 : 0);
            Handles(form);
            var lifetime = (OnnxFormLifetime)Field(form, "_lifetime")!;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var work = lifetime.RunAsync(_ => release.Task);
            Invoke(form, "RequestClose", DialogResult.OK);
            Assert.True(lifetime.Token.IsCancellationRequested);
            Assert.False(form.IsDisposed);
            Assert.False(((Button)form.AcceptButton!).Enabled);
            release.SetResult();
            Pump(work);
            Pump((Task)Field(form, "_closingTask")!);
            Assert.Equal(DialogResult.OK, form.DialogResult);
            Assert.False(form.Visible);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Separation_PreservesStemsAndAvailability(bool gpuAvailable)
    {
        StaTest.Run(() =>
        {
            using var form = new SeparateAudioPickerForm("Audio é.wav", "01:23", gpuAvailable,
                new Dictionary<string, string> { [ActionOptionKeys.Stems] = "vocals,instrumental", [ActionOptionKeys.SeparateEngine] = "gpu" });
            form.DialogResult = DialogResult.OK;
            Assert.Equal("vocals,instrumental", form.SelectionOptions![ActionOptionKeys.Stems]);
            Assert.Equal(gpuAvailable ? "gpu" : "auto", form.SelectionOptions[ActionOptionKeys.SeparateEngine]);
            foreach (var check in Descendants(form).OfType<CheckBox>()) check.Checked = false;
            Assert.False(((Button)form.AcceptButton!).Enabled);
            Descendants(form).OfType<CheckBox>().First().Checked = true;
            Assert.True(((Button)form.AcceptButton!).Enabled);
            var cpu = Descendants(form).OfType<RadioButton>().Single(r => r.Text == "CPU only");
            cpu.Checked = true;
            Assert.Single(Descendants(form).OfType<RadioButton>(), r => r.Checked);
            Assert.Equal("cpu", form.SelectionOptions[ActionOptionKeys.SeparateEngine]);
        });
    }

    [Fact]
    public void Rife_NativeSelectorsPreserveFrameRateAndAudioModes()
    {
        StaTest.Run(() =>
        {
            using var form = new RifeInterpolateVideoPickerForm("Vidéo é.mp4", 29.97);
            var model = Named<ComboBox>(form, "model");
            Assert.Equal(RifeModelCatalog.GetAll().Count, model.Items.Count);
            model.SelectedIndex = 0;
            Named<ComboBox>(form, "target").SelectedItem = 4;
            var playback = Named<ComboBox>(form, "playback");
            playback.SelectedIndex = 1;
            Assert.Equal("59.94", Named<TextBox>(form, "outputFps").Text);
            var pitch = Named<CheckBox>(form, "keepPitch");
            var remove = Named<CheckBox>(form, "removeAudio");
            Assert.True(pitch.Enabled);
            remove.Checked = true;
            Assert.False(pitch.Enabled);
            Invoke(form, "OnStartClicked", form, EventArgs.Empty);
            Assert.Equal(new RifeInterpolateVideoSettings(((RifeModelDefinition)model.SelectedItem!).Id, 4, 2, true, true), form.Selection);
            using var normal = new RifeInterpolateVideoPickerForm("source.mp4", 15);
            Named<ComboBox>(normal, "playback").SelectedIndex = 2;
            Named<CheckBox>(normal, "removeAudio").Checked = true;
            Named<ComboBox>(normal, "playback").SelectedIndex = 0;
            Assert.False(Named<CheckBox>(normal, "removeAudio").Checked);
            Assert.False(Named<CheckBox>(normal, "removeAudio").Enabled);
            Assert.True(Named<CheckBox>(normal, "keepPitch").Checked);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Upscale_PreservesModelCatalogAspectRatioAndMaximum(bool video)
    {
        StaTest.Run(() =>
        {
            using UpscaleImagePickerForm form = video ? new UpscaleVideoPickerForm("source", 1920, 1080, true)
                : new UpscaleImagePickerForm("source", 1920, 1080, true);
            var model = Named<ComboBox>(form, "model");
            var catalog = video ? UpscaleModelCatalog.GetVideoModels() : UpscaleModelCatalog.GetImageModels();
            Assert.Equal(catalog.Select(m => m.Id), model.Items.Cast<UpscaleModelDefinition>().Select(m => m.Id));
            model.SelectedIndex = 0;
            form.DialogResult = DialogResult.OK;
            Assert.Equal(catalog[0].Id, form.SelectedModelId);
            Assert.Equal("4", form.SelectedScale);
            Descendants(form).OfType<RadioButton>().Single(r => r.Text == "Custom size").Checked = true;
            Named<TextBox>(form, "width").Text = "3840";
            Assert.Equal("2160", Named<TextBox>(form, "height").Text);
            Assert.Equal((3840, 2160), form.CustomTarget);
            Named<TextBox>(form, "height").Text = "1080";
            Assert.Equal("1920", Named<TextBox>(form, "width").Text);
            Named<TextBox>(form, "width").Text = "99999";
            Assert.Equal((7680, 4320), form.CustomTarget);
            form.ClientSize = new Size(480, 600);
            Layout(form);
            Assert.Equal((7680, 4320), form.CustomTarget);
            using var batch = Create(video ? 11 : 10);
            Assert.False(Descendants(batch).OfType<RadioButton>().Single(r => r.Text == "Custom size").Enabled);
        });
    }

    [Fact]
    public void Bria_RecheckPreservesMissingMismatchAndProceedRoutes()
    {
        StaTest.Run(() =>
        {
            var next = BriaModelStatus.Mismatch;
            using var form = new BriaModelNoticeForm("model.onnx", "E:\\Dossier avec espaces é", "https://example.invalid", "1 GB",
                BriaModelStatus.Missing, () => next);
            var use = (Button)Field(form, "_useAnywayButton")!;
            Assert.Null(use.Parent);
            Invoke(form, "OnRecheckClick", form, EventArgs.Empty);
            Assert.Same(((Control)form.AcceptButton!).Parent, use.Parent);
            Assert.Contains("does not match", ((Label)Field(form, "_messageLabel")!).Text);
            Assert.False(form.Proceed);
            next = BriaModelStatus.Missing;
            Invoke(form, "OnRecheckClick", form, EventArgs.Empty);
            Assert.Null(use.Parent);
            Assert.Contains("not installed", ((Label)Field(form, "_messageLabel")!).Text);
            next = BriaModelStatus.Valid;
            Invoke(form, "OnRecheckClick", form, EventArgs.Empty);
            Assert.True(form.Proceed);
            Assert.Equal(DialogResult.OK, form.DialogResult);
            using var mismatch = Create(12);
            Invoke(Descendants(mismatch).OfType<Button>().Single(b => b.Text == use.Text), "OnClick", EventArgs.Empty);
            Assert.True(((BriaModelNoticeForm)mismatch).Proceed);
        });
    }

    [Fact]
    public void Download_LongErrorIsReadableAndRetryCanSucceed()
    {
        StaTest.Run(() =>
        {
            var calls = 0;
            var message = string.Join("\r\n", Enumerable.Range(1, 40).Select(i => $"Détail {i}: échec sur un chemin avec espaces et accents."));
            using var form = Download((_, _) => ++calls == 1 ? Task.FromException(new InvalidDataException(message)) : Task.CompletedTask);
            Handles(form);
            Pump(form.StartDownloadAsync());
            Layout(form);
            var error = Named<TextBox>(form, "errorDetails");
            Assert.Contains(message, error.Text);
            Assert.True(error.Height >= error.Font.Height * 7);
            Assert.Equal(0, error.SelectionStart);
            Assert.True(((Button)form.AcceptButton!).Enabled);
            Pump(form.StartDownloadAsync());
            Assert.Equal(2, calls);
            Assert.Equal(DialogResult.OK, form.DialogResult);
            Assert.False(form.Visible);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Download_CancelOrCloseWaitsAndIgnoresLateProgress(bool close)
    {
        StaTest.Run(() =>
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken token = default;
            IProgress<AiModelDownloadProgress>? reporter = null;
            using var form = Download((progress, ct) => { reporter = progress; token = ct; return release.Task; });
            Handles(form);
            var task = form.StartDownloadAsync();
            Assert.Same(task, form.StartDownloadAsync());
            if (close)
            {
                var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
                Invoke(form, "OnFormClosing", form, closing);
                Assert.True(closing.Cancel);
            }
            else Invoke((Button)form.CancelButton!, "OnClick", EventArgs.Empty);
            Assert.True(token.IsCancellationRequested);
            Assert.False(task.IsCompleted);
            Assert.False(form.IsDisposed);
            reporter!.Report(new AiModelDownloadProgress(99, "Late result"));
            Application.DoEvents();
            Assert.Equal(0, Named<ProgressBar>(form, "downloadProgress").Value);
            release.SetResult();
            Pump(task);
            if (close)
            {
                // The final close is deliberately posted after the download task
                // finishes, even if cancellation unwinds inline in FormClosing.
                Application.DoEvents();
                Assert.Equal(DialogResult.Cancel, form.DialogResult);
            }
            else
            {
                Assert.Equal("Close", ((Button)form.CancelButton!).Text);
                Assert.True(((Button)form.CancelButton!).Enabled);
                Assert.True(((Button)form.AcceptButton!).Enabled);
            }
            Assert.False(form.Visible);
        });
    }

    private static DownloadModelForm Download(Func<IProgress<AiModelDownloadProgress>, CancellationToken, Task> action)
        => new("FrameShift - Download", "Test only", "", "Modèle é", "Test license", 1000000, action);
    private static Form Create(int variant) => variant switch
    {
        0 or 7 => new RemoveNoiseAudioPickerForm("Audio avec accents é.wav", variant == 0),
        1 or 8 => new RemoveNoiseVideoPickerForm("Vidéo avec accents é.mp4", variant == 1),
        2 or 9 => new SeparateAudioPickerForm("Audio é.wav", "01:23", variant == 2),
        3 => new RifeInterpolateVideoPickerForm("Vidéo é.mp4", 29.97),
        4 or 10 => new UpscaleImagePickerForm("Image é.png", 1920, 1080, variant == 4),
        5 or 11 => new UpscaleVideoPickerForm("Vidéo é.mp4", 1920, 1080, variant == 5),
        6 => Download((_, _) => Task.CompletedTask),
        _ => new BriaModelNoticeForm("model.onnx", "E:\\Modèles é", "https://example.invalid", "1 GB", BriaModelStatus.Mismatch, () => BriaModelStatus.Missing)
    };
    private static T Named<T>(Control root, string name) where T : Control => Descendants(root).OfType<T>().Single(c => c.Name == name);
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static object? Field(object root, string name) => root.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(root);
    private static void Handles(Control root) { _ = root.Handle; foreach (Control c in root.Controls) Handles(c); }
    private static void Layout(Control root) { for (var i = 0; i < 3; i++) LayoutOnce(root); }
    private static void LayoutOnce(Control root) { root.PerformLayout(); foreach (Control c in root.Controls) LayoutOnce(c); }
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
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
        Assert.True(task.IsCompleted);
        task.GetAwaiter().GetResult();
    }
}
