using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Logging;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;
using Xunit;

namespace FrameShift.Tests;

// Hidden native controls only. Font stress does not simulate Windows DPI transitions.
[Collection(WinFormsTestCollection.Name)]
public sealed class UiD2Tests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public UiD2Tests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(12)] [InlineData(13)] [InlineData(14)]
    public void CompactWindows_DoNotReserveEmptySpaceAtOpening(int variant)
    {
        StaTest.Run(() =>
        {
            using var fixture = new ImageFixture();
            using var form = Create(variant, fixture.Path);
            Handles(form);
            Invoke(form, "OnLoad", EventArgs.Empty);
            Layout(form);
            var root = (TableLayoutPanel)form.Controls[0];
            var viewport = (Panel)root.GetControlFromPosition(0, 1)!;
            var content = viewport.Controls[0];
            _output.WriteLine($"variant={variant} client={form.ClientSize}, viewport={viewport.Bounds}, content={content.Bounds}");
            foreach (var status in Descendants(form).OfType<FrameShiftStatusMessage>())
                Assert.InRange(status.Height, status.GetPreferredSize(new Size(status.Width, 0)).Height,
                    status.GetPreferredSize(new Size(status.Width, 0)).Height + 1);
            Assert.InRange(viewport.ClientSize.Height - content.Height, 0, FrameShiftUiMetrics.BlockGap);
            Assert.False(viewport.VerticalScroll.Visible);
            if (variant == 8)
            {
                var presets = Named<FrameShiftFlowRow>(form, "speedPresets");
                var buttons = presets.Controls.Cast<Control>().ToArray();
                Assert.Equal(8, buttons.Length);
                Assert.All(buttons, button => Assert.Equal(buttons[0].Top, button.Top));
                Assert.All(buttons, button => Assert.True(presets.ClientRectangle.Contains(button.Bounds)));
            }
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void SpeedAudio_KeepPitchHasAUsableHitAreaAndCanToggle()
    {
        StaTest.Run(() =>
        {
            using var form = new ChangeSpeedForm("Audio.wav", "missing.exe", Runner(), ChangeSpeedMediaKind.Audio, 60, true, 48000);
            Handles(form);
            Invoke(form, "OnLoad", EventArgs.Empty);
            Layout(form);
            var check = Named<CheckBox>(form, "keepPitch");
            Assert.True(check.Enabled);
            Assert.True(check.Height >= check.GetPreferredSize(Size.Empty).Height, $"checkbox={check.Bounds}, parent={check.Parent!.Bounds}");
            Assert.True(check.Parent!.ClientRectangle.Contains(check.Bounds), $"checkbox={check.Bounds}, parent={check.Parent.Bounds}");
            Invoke(check, "OnClick", EventArgs.Empty);
            Assert.False(check.Checked);
            Invoke(check, "OnClick", EventArgs.Empty);
            Assert.True(check.Checked);
            Assert.False(form.Visible);
        });
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    [InlineData(12)] [InlineData(13)] [InlineData(14)]
    public void Windows_KeepMeasuredCommandsReachable_AndFitCompactContent(int variant)
    {
        StaTest.Run(() =>
        {
            using var fixture = new ImageFixture();
            using var form = Create(variant, fixture.Path);
            Handles(form);
            Layout(form);
            Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
            Assert.Single(Descendants(form).OfType<FrameShiftHeader>());
            var root = Assert.IsType<TableLayoutPanel>(form.Controls[0]);
            if (variant is not (9 or 10 or 11))
            {
                FrameShiftDialogLayout.FitInitialHeight(form, root, new Size(2560, 1440));
                Layout(form);
                var viewport = (Panel)root.GetControlFromPosition(0, 1)!;
                Assert.False(viewport.VerticalScroll.Visible, $"variant={variant}, client={form.ClientSize}, viewport={viewport.ClientSize}, content={viewport.Controls[0].Bounds}");
            }
            using var large = new Font("Segoe UI", 18);
            foreach (var width in new[] { 1100, 480, 800 })
            {
                form.Font = width == 480 ? large : SystemFonts.MessageBoxFont;
                form.ClientSize = new Size(width, 640);
                Layout(form);
                var actions = Assert.IsType<FrameShiftActionBar>(root.GetControlFromPosition(0, 3));
                Assert.InRange(actions.Top, 0, root.ClientSize.Height);
                Assert.True(actions.Bottom <= root.ClientSize.Height, $"variant={variant}; actions={actions.Bounds}; root={root.ClientSize}");
                Assert.All(actions.Controls.Cast<Control>(), c => Assert.True(c.Right <= actions.Width && c.Bottom <= actions.Height));
                Assert.Equal(((Control)form.AcceptButton!).Size, ((Control)form.CancelButton!).Size);
                foreach (var box in Descendants(form).OfType<TextBox>())
                    Assert.True(box.Height >= box.Font.Height, $"variant={variant}, field={box.Name}: {box.Size}");
                Assert.False(form.Visible);
            }
        });
    }

    [Fact]
    public void ConversionAndSubtitleModes_PreserveSelectionAndDescriptions()
    {
        StaTest.Run(() =>
        {
            using var conversion = new ConversionPickerForm("Conversion", "Média avec accents.mp4", "",
                VideoConversionCatalog.GetTargets(), VideoConversionCatalog.GetProfiles(), "mkv", "remux");
            Handles(conversion);
            conversion.DialogResult = DialogResult.OK;
            Assert.Equal(new ConversionSelection("mkv", "remux"), conversion.Selection);
            Named<ComboBox>(conversion, "target").SelectedIndex = 0;
            Assert.Equal("mp4", conversion.Selection!.TargetId);
            using var picker = new AddSubtitlesToVideoPickerForm("Vidéo.mp4", AddSubtitlesToVideoMode.SelectableTrack,
                "E:\\Sous titres avec accents.srt", null);
            Handles(picker);
            Named<RadioButton>(picker, "burnIntoVideo").Checked = true;
            Assert.Equal(AddSubtitlesToVideoMode.BurnIntoVideo, picker.SelectedSettings.Mode);
            Assert.Equal("E:\\Sous titres avec accents.srt", picker.SelectedSettings.SubtitleFilePath);
            Assert.False(Named<RadioButton>(picker, "selectableTrack").Checked);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Compression_PreservesProfilesAndTargetUnits(bool audio)
    {
        StaTest.Run(() =>
        {
            using Form form = audio ? new CompressAudioForm("Audio.mp3", ".mp3", 10000, 48000, 2)
                : new CompressVideoForm("Vidéo.mp4", ".mp4", 10000, "1920 × 1080");
            Handles(form);
            var cards = Descendants(form).OfType<FrameShiftChoiceCard>().ToArray();
            cards[2].Checked = true;
            Assert.Single(cards, c => c.Checked);
            var target = Named<CheckBox>(form, "useTarget");
            Assert.True(target.Enabled);
            Invoke(target, "OnClick", EventArgs.Empty);
            Assert.True(target.Checked);
            Assert.False(Named<TextBox>(form, "targetSize").ReadOnly);
            Assert.True(Named<ComboBox>(form, "targetUnit").Enabled);
            Named<TextBox>(form, "targetSize").Text = "1,5";
            Named<ComboBox>(form, "targetUnit").SelectedItem = "MB";
            form.DialogResult = DialogResult.OK;
            Invoke(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.UserClosing, false));
            Assert.Equal("small", audio ? ((CompressAudioForm)form).SelectedProfileId : ((CompressVideoForm)form).SelectedProfileId);
            Assert.Equal(1572864L, audio ? ((CompressAudioForm)form).TargetBytes : ((CompressVideoForm)form).TargetBytes);
            using var lossless = new CompressAudioForm("Audio.flac", ".flac", 1000, 48000, 2);
            Assert.False(Named<CheckBox>(lossless, "useTarget").Enabled);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Resize_PreservesRatioPixelAndPercentValues(bool video)
    {
        StaTest.Run(() =>
        {
            using ResizeMediaFormBase form = video ? new ResizeVideoForm("Vidéo.mp4", 1920, 1080) : new ResizeImageForm("Image.png", 1920, 1080);
            Handles(form);
            var width = Named<TextBox>(form, "widthPx");
            Invoke(width, "OnEnter", EventArgs.Empty);
            width.Text = "960";
            Assert.Equal("540", Named<TextBox>(form, "heightPx").Text);
            Assert.Contains("50", Named<TextBox>(form, "widthPct").Text);
            Named<CheckBox>(form, "keepRatio").Checked = false;
            width.Text = "1000";
            form.DialogResult = DialogResult.OK;
            Invoke(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.UserClosing, false));
            Assert.Equal(new ResizeSettings(1000, 540), form.Selection);
        });
    }

    [Fact]
    public void Timing_PreservesPitchAndDurationSynchronization()
    {
        StaTest.Run(() =>
        {
            using var pitch = new ChangePitchForm("Audio.wav", "missing.exe", Runner());
            Named<TextBox>(pitch, "semitones").Text = "12";
            Invoke(pitch, "ApplySemitonesFromText");
            Assert.Equal("200", Named<TextBox>(pitch, "percent").Text);
            pitch.DialogResult = DialogResult.OK;
            Invoke(pitch, "OnFormClosing", new FormClosingEventArgs(CloseReason.UserClosing, false));
            Assert.Equal(new ChangePitchSettings(12, true), pitch.Selection);
            using var speed = new ChangeSpeedForm("Vidéo.mp4", "missing.exe", Runner(), ChangeSpeedMediaKind.Video, 60, false, 48000);
            Named<TextBox>(speed, "percent").Text = "200";
            Assert.False(Named<CheckBox>(speed, "keepPitch").Enabled);
            Named<TextBox>(speed, "duration").Text = "120";
            Assert.Equal("50", Named<TextBox>(speed, "percent").Text);
            speed.DialogResult = DialogResult.OK;
            Invoke(speed, "OnFormClosing", new FormClosingEventArgs(CloseReason.UserClosing, false));
            Assert.Equal(new ChangeSpeedSettings(0.5, false), speed.Selection);
        });
    }

    private static Form Create(int variant, string image) => variant switch
    {
        0 => new ConversionPickerForm("Conversion", "Long chemin avec accents.mp4", "", VideoConversionCatalog.GetTargets(), VideoConversionCatalog.GetProfiles()),
        1 => new ConversionPickerForm("Conversion", "Long chemin avec accents.png", "", VideoConversionCatalog.GetTargets(), []),
        2 => new CompressMultiFileChoiceForm(80),
        3 => new CompressAudioForm("Audio.mp3", ".mp3", 10000, 48000, 2),
        4 => new CompressVideoForm("Vidéo.mp4", ".mp4", 10000, "1920 × 1080"),
        5 => new ResizeImageForm("Image.png", 1920, 1080),
        6 => new ResizeVideoForm("Vidéo.mp4", 1920, 1080),
        7 => new ChangePitchForm("Audio.wav", "missing.exe", Runner()),
        8 => new ChangeSpeedForm("Vidéo.mp4", "missing.exe", Runner(), ChangeSpeedMediaKind.Video, 60, false, 48000),
        9 => new RotateFlipImageForm(image, "missing.exe", Runner()),
        10 => new RotateFlipVideoForm("Vidéo.mp4", "missing.exe", new MediaProbeResult(TimeSpan.FromSeconds(60), true, true, 1920, 1080, "h264", 30, 1800, [], [], []), Runner()),
        11 => new ConvertToIconForm(image, image),
        12 => new AddSubtitlesToVideoPickerForm("Vidéo.mp4", AddSubtitlesToVideoMode.SelectableTrack, "", null),
        13 => new ChangeSpeedForm("Audio.wav", "missing.exe", Runner(), ChangeSpeedMediaKind.Audio, 60, true, 48000),
        _ => new CompressAudioForm("Audio.flac", ".flac", 10000, 48000, 2)
    };

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Timing_CloseCancelsPreview_AndDeletesLateOutput(int variant)
    {
        StaTest.Run(() =>
        {
            var completion = new TaskCompletionSource<FfmpegRunResult>();
            CancellationToken capturedToken = default;
            string? output = null;
            Task<FfmpegRunResult> Run(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken token)
            {
                output = args[^1];
                capturedToken = token;
                Assert.Contains("-t", args);
                return completion.Task;
            }
            using Form form = variant == 0 ? new ChangePitchForm("Audio avec accents.wav", "missing.exe", Runner(), Run)
                : new ChangeSpeedForm("Média avec accents.mp4", "missing.exe", Runner(),
                    variant == 1 ? ChangeSpeedMediaKind.Audio : ChangeSpeedMediaKind.Video, 60, true, 48000, Run);
            Handles(form);
            var task = (Task)Invoke(form, "PreviewAsync")!;
            Assert.NotNull(output);
            Assert.False(capturedToken.IsCancellationRequested);
            try
            {
                form.Dispose();
                Assert.True(capturedToken.IsCancellationRequested);
                // Simulate a writer completing after cancellation; no audio/video may start.
                File.WriteAllText(output!, "late preview output");
                completion.SetResult(new FfmpegRunResult(0, "", false, CancellationScope.None));
                Pump(task);
                Assert.False(File.Exists(output));
                Assert.False(form.Visible);
            }
            finally { if (output is not null) File.Delete(output); }
        });
    }

    [Fact]
    public void RotateImage_LoadsWithoutShowing_AndPreservesTransforms()
    {
        StaTest.Run(() =>
        {
            using var fixture = new ImageFixture();
            using var form = new RotateFlipImageForm(fixture.Path, "missing.exe", Runner());
            Handles(form);
            Pump((Task)Invoke(form, "InitializePreviewAsync")!);
            Invoke(form, "ApplyRotate", true);
            Invoke(form, "ApplyFlipH");
            Assert.True(((Control)form.AcceptButton!).Enabled);
            Invoke((Control)form.AcceptButton, "OnClick", EventArgs.Empty);
            Assert.NotNull(form.Selection);
            Assert.Equal(RotateAngle.Cw90, form.Selection.Angle);
            Assert.True(form.Selection.FlipHorizontal);
            Assert.False(form.Selection.FlipVertical);
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void Icon_ReflowAndSelectionKeepExportSizesIndependentOfUiScale()
    {
        StaTest.Run(() =>
        {
            using var fixture = new ImageFixture();
            using var form = new ConvertToIconForm(fixture.Path, fixture.Path);
            Handles(form);
            Invoke(form, "OnLoad", EventArgs.Empty);
            Layout(form);
            var sizesSection = Named<CheckBox>(form, "size32").Parent!.Parent!.Parent!;
            var iconLayout = sizesSection.Parent!;
            Assert.IsAssignableFrom<TableLayoutPanel>(iconLayout);
            Assert.Equal(3, ((TableLayoutPanel)iconLayout).ColumnCount);
            var previewsSection = iconLayout.Controls.Cast<Control>().Last();
            Assert.True(sizesSection.Right <= previewsSection.Left);
            Assert.False(((Panel)form.Controls[0].Controls[1]).VerticalScroll.Visible);
            var initialHeight = form.ClientSize.Height;
            for (var i = 0; i < 3; i++) { Layout(form); Assert.Equal(initialHeight, form.ClientSize.Height); }
            Invoke(form, "SetAllSizes", false);
            Named<CheckBox>(form, "size32").Checked = true;
            Named<CheckBox>(form, "size256").Checked = true;
            using var large = new Font("Segoe UI", 18);
            form.Font = large;
            form.ClientSize = new Size(480, 640);
            Layout(form);
            var tiles = Descendants(form).OfType<PictureBox>().Where(p => p.AccessibleName?.StartsWith("Preview ") == true).ToArray();
            Assert.Equal(ConvertToIconSettings.GetSupportedSizes().Count, tiles.Length);
            Assert.All(tiles, p => Assert.True(p.Parent!.Right <= p.Parent.Parent!.ClientSize.Width));
            form.Font = SystemFonts.MessageBoxFont;
            form.ClientSize = new Size(900, 600);
            Layout(form);
            Assert.Equal(3, ((TableLayoutPanel)iconLayout).ColumnCount);
            Invoke(form, "ConfirmSelection");
            Assert.Equal(new[] { 32, 256 }, form.Selection!.Sizes);
            Assert.False(form.Visible);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Resize_FourInputsHaveIdenticalSizesAndStayInTwoRows(bool video)
    {
        StaTest.Run(() =>
        {
            using ResizeMediaFormBase form = video ? new ResizeVideoForm("Vidéo.mp4", 1920, 1080) : new ResizeImageForm("Image.png", 1920, 1080);
            Handles(form);
            using var large = new Font("Segoe UI", 18);
            foreach (var width in new[] { 640, 480, 1100, 640 })
            {
                form.Font = width == 480 ? large : SystemFonts.MessageBoxFont;
                form.ClientSize = new Size(width, 640);
                Layout(form);
                var boxes = new[] { Named<TextBox>(form, "widthPx"), Named<TextBox>(form, "widthPct"), Named<TextBox>(form, "heightPx"), Named<TextBox>(form, "heightPct") };
                Assert.All(boxes, box => Assert.Equal(boxes[0].Size, box.Size));
                Assert.Equal(FrameShiftUiMetrics.ToPixels(form, 128), boxes[0].Width);
                Assert.Equal(boxes[0].Left, boxes[2].Left);
                Assert.Equal(boxes[1].Left, boxes[3].Left);
                Assert.Equal(boxes[0].Top, boxes[1].Top);
                Assert.Equal(boxes[2].Top, boxes[3].Top);
                Assert.True(boxes[0].Right < boxes[1].Left);
                Assert.True(boxes[0].Bottom < boxes[2].Top);
                Assert.All(boxes, box => Assert.True(box.Parent!.ClientRectangle.Contains(box.Bounds)));
            }
            Assert.False(form.Visible);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Rotate_CommandsKeepStandardColorsAcrossTransformChanges(bool video)
    {
        StaTest.Run(() =>
        {
            using var fixture = new ImageFixture();
            using var form = Create(video ? 10 : 9, fixture.Path);
            var buttons = Descendants(form).OfType<Button>().Where(b => b.Text.Contains("Rotate") || b.Text.Contains("Flip")).ToArray();
            Assert.Equal(4, buttons.Length);
            var colors = buttons.Select(b => (b.BackColor, b.ForeColor, b.FlatAppearance.BorderColor)).ToArray();
            Invoke(form, "ApplyRotate", true);
            Invoke(form, "ApplyFlipH");
            Invoke(form, "ApplyFlipV");
            Assert.All(buttons.Where(b => b.Text.Contains("Flip")), b => Assert.Contains("✓", b.Text));
            Assert.Equal(colors, buttons.Select(b => (b.BackColor, b.ForeColor, b.FlatAppearance.BorderColor)).ToArray());
            Invoke(form, "ResetTransforms");
            Assert.Equal(colors, buttons.Select(b => (b.BackColor, b.ForeColor, b.FlatAppearance.BorderColor)).ToArray());
            Assert.All(buttons, b => Assert.DoesNotContain("✓", b.Text));
            Assert.False(form.Visible);
        });
    }

    private static void Pump(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Assert.True(task.IsCompleted, "Hidden preview request did not complete.");
        task.GetAwaiter().GetResult();
    }
    private static FfmpegRunner Runner() => new(new AppLogger());
    private static T Named<T>(Control root, string name) where T : Control => Descendants(root).OfType<T>().Single(c => c.Name == name);
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static void Handles(Control root) { _ = root.Handle; foreach (Control c in root.Controls) Handles(c); }
    private static void Layout(Control root) { for (var i = 0; i < 3; i++) { root.PerformLayout(); foreach (Control c in root.Controls) LayoutOnce(c); } }
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
    private sealed class ImageFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"FrameShift D2 é {Guid.NewGuid():N}.png");
        public ImageFixture() { using var bitmap = new Bitmap(120, 80); bitmap.Save(Path); }
        public void Dispose() => File.Delete(Path);
    }
}
