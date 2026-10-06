using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.AI;
using FrameShift.Core.AI.Ocr;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Logging;
using FrameShift.Windows.AI;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;
using Xunit;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class ModalClosingAuditTests
{
    public static IEnumerable<object[]> IdleCloseCases()
    {
        foreach (var window in new[] { "cut-audio", "create-gif", "crop-video", "join-videos", "burn-subtitles", "remove-object", "image-to-pdf", "cut-video", "crop-image", "extract-text", "download-model", "remove-noise-audio", "remove-noise-video" })
        foreach (var route in new[] { "ok", "cancel", "close" })
            yield return [window, route];
    }

    [Theory]
    [MemberData(nameof(IdleCloseCases))]
    public void IdleModalClose_ReturnsAfterOneRequestAndPreservesResult(string window, string route) => StaTest.Run(() =>
    {
        using var form = Create(window);
        using var timeout = new System.Windows.Forms.Timer { Interval = 3000 };
        var timedOut = false;
        timeout.Tick += (_, _) => { timedOut = true; timeout.Stop(); form.Dispose(); };
        form.Shown += (_, _) => form.BeginInvoke(new Action(() =>
        {
            if (window.StartsWith("remove-noise", StringComparison.Ordinal) && route != "close")
                ((Button)(route == "ok" ? form.AcceptButton! : form.CancelButton!)).PerformClick();
            else
            {
                if (route != "close") form.DialogResult = route == "ok" ? DialogResult.OK : DialogResult.Cancel;
                form.Close();
            }
        }));
        timeout.Start();
        var result = form.ShowDialog();
        timeout.Stop();
        Assert.False(timedOut, $"{window} remained open after {route}.");
        Assert.Equal(route == "ok" ? DialogResult.OK : DialogResult.Cancel, result);
    });

    [Fact]
    public void DownloadModalClose_WithInlineCancellation_DoesNotReenterClosing() => StaTest.Run(() =>
    {
        // Deliberately allow inline continuations, as cancellation of an awaited
        // operation can finish synchronously on the UI thread.
        var finish = new TaskCompletionSource();
        using var form = Download((_, token) =>
        {
            token.Register(() => finish.TrySetCanceled(token));
            return finish.Task;
        });
        using var timeout = new System.Windows.Forms.Timer { Interval = 3000 };
        var timedOut = false;
        var insideClose = false;
        var reentered = false;
        var closingCalls = 0;
        Task? download = null;
        form.FormClosing += (_, _) =>
        {
            if (++closingCalls > 1 && insideClose) reentered = true;
        };
        timeout.Tick += (_, _) => { timedOut = true; timeout.Stop(); form.Dispose(); };
        form.Shown += (_, _) => form.BeginInvoke(new Action(() =>
        {
            download = form.StartDownloadAsync();
            insideClose = true;
            form.Close();
            insideClose = false;
        }));
        timeout.Start();
        var result = form.ShowDialog();
        timeout.Stop();
        Assert.False(reentered, "Download cleanup reentered the cancelled modal close.");
        Assert.False(timedOut, "Download dialog remained open after cancellation.");
        Assert.True(download!.IsCompleted);
        Assert.Equal(DialogResult.Cancel, result);
    });

    [Theory]
    [InlineData("cut-audio")]
    [InlineData("create-gif")]
    [InlineData("crop-video")]
    [InlineData("join-videos")]
    [InlineData("burn-subtitles")]
    [InlineData("remove-object")]
    [InlineData("image-to-pdf")]
    [InlineData("cut-video")]
    [InlineData("crop-image")]
    [InlineData("extract-text")]
    [InlineData("download-model")]
    [InlineData("remove-noise-audio")]
    [InlineData("remove-noise-video")]
    public void RepeatedModalClose_DuringPreviewWaitsUntilWorkFinishes(string window) => StaTest.Run(() =>
    {
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var form = window == "download-model" ? Download((_, _) => finish.Task) : Create(window);
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        var waited = false;
        var timedOut = false;
        var released = false;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        form.Shown += (_, _) => form.BeginInvoke(new Action(() =>
        {
            StartPendingWork(window, form, finish.Task);
            form.Close();
            form.Close();
            timer.Start();
        }));
        timer.Tick += (_, _) =>
        {
            if (!released)
            {
                waited = form.Visible && !finish.Task.IsCompleted;
                released = true;
                finish.TrySetResult();
            }
            else if (DateTime.UtcNow > deadline)
            {
                timedOut = true;
                timer.Stop();
                form.Dispose();
            }
        };
        var result = form.ShowDialog();
        timer.Stop();
        finish.TrySetResult();
        Assert.False(timedOut, $"{window} did not close after work finished.");
        Assert.True(waited, $"{window} closed while preview cleanup was pending.");
        Assert.Equal(DialogResult.Cancel, result);
    });

    private static void StartPendingWork(string window, Form form, Task work)
    {
        switch (window)
        {
            case "cut-audio":
                _ = ((CutAudioFormLifetime)Field(form, "_lifetime")).RunAsync(_ => work);
                break;
            case "create-gif":
            case "crop-video":
            case "burn-subtitles":
                _ = ((EditorPreviewLifetime)Field(form, "_previewLifetime")).RunAsync(_ => work);
                break;
            case "remove-object":
            case "remove-noise-audio":
            case "remove-noise-video":
                _ = ((OnnxFormLifetime)Field(form, "_lifetime")).RunAsync(_ => work);
                break;
            case "join-videos": SetField(form, "_loadingTask", work); break;
            case "image-to-pdf": SetField(form, "_imageImportTask", work); break;
            case "cut-video": SetField(form, "_previewTask", work); break;
            case "crop-image": SetField(form, "_initializationTask", work); break;
            case "extract-text": SetField(form, "_preparation", work); break;
            case "download-model": _ = ((DownloadModelForm)form).StartDownloadAsync(); break;
            default: throw new ArgumentOutOfRangeException(nameof(window));
        }
    }

    private static Form Create(string window)
    {
        var logger = new AppLogger();
        var runner = new FfmpegRunner(logger);
        var probe = new MediaProbeResult(TimeSpan.FromSeconds(10), true, true, 1280, 720, "h264", 30, 300, [], [], []);
        Task<Bitmap> Capture(double _, CancellationToken token) => Task.FromResult(new Bitmap(32, 16));
        Form form = window switch
        {
            "cut-audio" => new CutAudioForm("absent.wav", "absent.exe", "absent.exe", 10, runner, new FfprobeRunner(logger)),
            "create-gif" => new CreateGifForm("absent.mp4", "absent.exe", probe, runner, Capture),
            "crop-video" => new CropVideoForm("absent.mp4", "absent.exe", probe, runner, Capture),
            "join-videos" => new JoinVideosForm([], "absent.exe", "absent.exe", runner, new FfprobeRunner(logger), uiSettingsPathForTesting: Path.Combine(Path.GetTempPath(), "frameshift-modal-audit-settings.json")),
            "burn-subtitles" => new AddSubtitlesToVideoBurnEditorForm("absent.mp4", "absent.exe", probe, runner, new("absent.srt", AddSubtitlesToVideoMode.BurnIntoVideo)),
            "remove-object" => new RemoveObjectEditorForm("absent.png", logger),
            "image-to-pdf" => new ImageToPdfForm("absent.png", "absent.exe", runner),
            "cut-video" => new CutVideoForm("absent.mp4", "absent.exe", probe, runner, Capture),
            "crop-image" => new CropImageForm("absent.png", "absent.exe", runner),
            "extract-text" => new ExtractTextPickerForm(() => ["absent.png"], new OcrSettings()),
            "download-model" => Download((_, _) => Task.CompletedTask),
            "remove-noise-audio" => new RemoveNoiseAudioPickerForm("absent.wav", false),
            "remove-noise-video" => new RemoveNoiseVideoPickerForm("absent.mp4", false),
            _ => throw new ArgumentOutOfRangeException(nameof(window))
        };
        // Isolate idle closure from disk/process/model work at Shown.
        if (window == "cut-audio") SetField(form, "_initializationStarted", true);
        if (window == "crop-image") SetField(form, "_initializationTask", Task.CompletedTask);
        if (window == "remove-object") SetField(form, "_imageLoadTask", Task.CompletedTask);
        if (window == "image-to-pdf") SetField(form, "_initialStarted", true);
        return form;
    }

    private static DownloadModelForm Download(Func<IProgress<AiModelDownloadProgress>, CancellationToken, Task> action) =>
        new("Download audit", "Test only", "", "Test model", "Test license", 100, action);

    private static void SetField(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static object Field(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
}
