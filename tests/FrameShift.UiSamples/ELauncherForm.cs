using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Windows.AI;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.UiSamples;

internal sealed class ELauncherForm : Form
{
    private readonly TextBox _video = new();
    private readonly TextBox _secondVideo = new();
    private readonly TextBox _audio = new();
    private readonly TextBox _image = new();
    private readonly TextBox _subtitles = new();
    private readonly CheckBox _slow = new() { Text = "Retarder les aperçus GIF / Crop Video de 3 secondes (test de fermeture)", AutoSize = true };
    private readonly TextBox _result;
    private readonly List<Button> _buttons = [];
    private readonly CancellationTokenSource _cancellation = new();
    private Task _openingTask = Task.CompletedTask;
    private bool _resourcesDisposed;

    internal ELauncherForm()
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(900, 780), new Size(480, 360));
        FrameShiftWindowChrome.Apply(this, "FrameShift - Recette E");
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var fixtures = Path.Combine(dir.FullName, "scratch", "phase-a");
            if (!Directory.Exists(fixtures)) continue;
            _video.Text = Directory.EnumerateFiles(fixtures, "*.mp4").FirstOrDefault() ?? "";
            _audio.Text = Directory.EnumerateFiles(fixtures, "*.wav").FirstOrDefault()
                ?? Directory.EnumerateFiles(fixtures, "*.mp3").FirstOrDefault() ?? "";
            _image.Text = Directory.EnumerateFiles(fixtures, "*.png").FirstOrDefault() ?? "";
            var subtitleFixtures = Path.Combine(dir.FullName, "scratch", "phase-e");
            if (Directory.Exists(subtitleFixtures))
                _subtitles.Text = Directory.EnumerateFiles(subtitleFixtures, "*.srt").FirstOrDefault() ?? "";
            break;
        }
        Control FileRow(string title, TextBox box, string filter)
        {
            var browse = FrameShiftUiFactory.CreateMeasuredActionButton($"Choisir {title}…", false);
            browse.Click += (_, _) =>
            {
                using var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
                if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName;
            };
            return FrameShiftUiFactory.CreateVerticalStack(FrameShiftUiFactory.CreateFieldRow(title, box), browse);
        }
        var files = FrameShiftUiFactory.CreateSection("Fichiers", FrameShiftUiFactory.CreateVerticalStack(
            FileRow("Vidéo", _video, "Vidéo|*.mp4;*.mkv;*.mov;*.webm|Tous|*.*"),
            FileRow("Vidéo 2 (facultative)", _secondVideo, "Vidéo|*.mp4;*.mkv;*.mov;*.webm|Tous|*.*"),
            FileRow("Audio", _audio, "Audio|*.wav;*.mp3;*.flac;*.m4a|Tous|*.*"),
            FileRow("Image", _image, "Image|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Tous|*.*"),
            FileRow("Sous-titres", _subtitles, "Sous-titres|*.srt;*.ass;*.frameshift-subtitles.json|Tous|*.*")));
        var choices = FrameShiftUiFactory.CreateChoiceRow();
        var names = new[] { "Cut Audio", "Create GIF", "Crop Video", "Join Videos", "Burn Subtitles", "Remove Object", "Image to PDF" };
        for (var i = 0; i < names.Length; i++)
        {
            var choice = i;
            var button = FrameShiftUiFactory.CreateMeasuredActionButton(names[i], false, 180);
            button.Click += async (_, _) => await (_openingTask = OpenAsync(choice));
            choices.Controls.Add(button);
            _buttons.Add(button);
        }
        _result = FrameShiftUiFactory.CreateStatusMessage("Aperçus réels. Les validations affichent les réglages sans export final. Remove Object : Apply effectue réellement l'opération et crée un PNG ; annuler suffit pour tester l'interface.");
        var close = FrameShiftUiFactory.CreateMeasuredActionButton("Fermer", false);
        close.Click += (_, _) => Close();
        CancelButton = close;
        Controls.Add(FrameShiftDialogLayout.Create(
            FrameShiftUiFactory.CreateHeader("Phase E — Éditeurs", "Build de développement • recette manuelle", IconPaths.AppIcon, "", "E"),
            FrameShiftUiFactory.CreateVerticalStack(files, FrameShiftUiFactory.CreateChoiceRow(_slow),
                FrameShiftUiFactory.CreateSection("Fenêtres", choices)), FrameShiftDialogLayout.CreateActions(close), _result));
        FormClosing += (_, _) => _cancellation.Cancel();
        ResumeLayout(true);
    }

    private async Task OpenAsync(int choice)
    {
        foreach (var button in _buttons) button.Enabled = false;
        try
        {
            var path = (choice == 0 ? _audio : choice >= 5 ? _image : _video).Text;
            if (!File.Exists(path)) throw new FileNotFoundException("Choisissez un fichier existant.", path);
            var locator = new ToolLocator();
            var runner = new FfmpegRunner(new AppLogger());
            var probeRunner = new FfprobeRunner(new AppLogger());
            var ffmpeg = locator.ResolveFfmpegPath();
            var ffprobe = locator.ResolveFfprobePath();
            MediaProbeResult? probe = null;
            if (choice < 5)
            {
                _result.Text = "Lecture des informations du média…";
                var result = await probeRunner.TryProbeMediaAsync(ffprobe, path, _cancellation.Token);
                probe = result.Probe ?? throw new InvalidOperationException(result.ErrorMessage);
            }
            _cancellation.Token.ThrowIfCancellationRequested();
            Func<double, CancellationToken, Task<Bitmap>>? loader = _slow.Checked ? async (seconds, token) =>
            {
                await Task.Delay(3000, token);
                return await PreviewFrameHelper.CaptureFrameAsync(ffmpeg, runner, path, seconds, "Phase E preview", token);
            } : null;
            using Form form = choice switch
            {
                0 => new CutAudioForm(path, ffmpeg, ffprobe, probe!.Duration!.Value.TotalSeconds, runner, probeRunner),
                1 => new CreateGifForm(path, ffmpeg, probe!, runner, loader),
                2 => new CropVideoForm(path, ffmpeg, probe!, runner, loader),
                3 => new JoinVideosForm([path, File.Exists(_secondVideo.Text) ? _secondVideo.Text : path], ffmpeg, ffprobe, runner, probeRunner),
                4 => new AddSubtitlesToVideoBurnEditorForm(path, ffmpeg, probe!, runner, new(_subtitles.Text, AddSubtitlesToVideoMode.BurnIntoVideo)),
                5 => new RemoveObjectEditorForm(path, new AppLogger()),
                _ => new ImageToPdfForm(path, ffmpeg, runner)
            };
            if (IsDisposed || _cancellation.IsCancellationRequested) return;
            var dialogResult = form.ShowDialog(this);
            object? settings = form switch
            {
                CutAudioForm f => f.Selection,
                CreateGifForm f => f.Selection,
                CropVideoForm f => f.Selection,
                JoinVideosForm f => f.Settings,
                AddSubtitlesToVideoBurnEditorForm f => f.SelectedSettings,
                ImageToPdfForm f => f.Settings,
                _ => null
            };
            if (!IsDisposed) _result.Text = dialogResult == DialogResult.OK
                ? System.Text.Json.JsonSerializer.Serialize(settings) : "Fenêtre fermée / sélection annulée.";
            if (form is CutAudioForm audio && audio.TemporaryRootPath is string temporary)
            {
                var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FrameShiftCutAudio")) + Path.DirectorySeparatorChar;
                var full = Path.GetFullPath(temporary);
                if (full.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full))
                    Directory.Delete(full, recursive: true); // The recipe does not hand the working file to an export action.
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (!IsDisposed) _result.Text = ex.Message; }
        finally { if (!IsDisposed) foreach (var button in _buttons) button.Enabled = true; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            _cancellation.Cancel();
            _ = DisposeCancellationAsync();
        }
        base.Dispose(disposing);
    }
    private async Task DisposeCancellationAsync() { try { await _openingTask; } finally { _cancellation.Dispose(); } }
}
