using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Windows.AI;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.UiSamples;

// Opens the actual pilot forms. Accepting a selection only reports settings; no export or AI run.
internal sealed class PilotLauncherForm : Form
{
    private readonly TextBox _video = new();
    private readonly TextBox _image = new();
    private readonly CheckBox _slowPreview = new() { Text = "Cut Video : retarder l’aperçu de 3 s pour tester la fermeture", AutoSize = true };
    private readonly TextBox _result;
    private readonly CancellationTokenSource _cancel = new();
    private readonly List<Button> _buttons = [];
    private readonly FfmpegRunner _runner = new(new AppLogger());
    private bool _disposed;

    public PilotLauncherForm()
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(760, 700), new Size(440, 360));
        FrameShiftWindowChrome.Apply(this, "FrameShift — Recette des cinq pilotes C");
        var root = FindRepository();
        var fixtureDirectory = root is null ? "" : Path.Combine(root, "scratch", "phase-a");
        if (Directory.Exists(fixtureDirectory))
        {
            _video.Text = Directory.EnumerateFiles(fixtureDirectory, "*.mp4").FirstOrDefault() ?? "";
            _image.Text = Directory.EnumerateFiles(fixtureDirectory, "*.png").FirstOrDefault() ?? "";
        }
        var videoBrowse = Button("Choisir une vidéo…", () => Browse(_video, "Vidéos|*.mp4;*.mkv;*.mov;*.avi;*.webm|Tous les fichiers|*.*"));
        var imageBrowse = Button("Choisir une image…", () => Browse(_image, "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tif;*.tiff|Tous les fichiers|*.*"));
        var choices = FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateFieldRow("Vidéo", _video), videoBrowse,
            FrameShiftUiFactory.CreateFieldRow("Image", _image), imageBrowse, _slowPreview);
        var pilotButtons = Enumerable.Range(0, 5).Select(i =>
        {
            var names = new[] { "Interpolate Video (FFmpeg)", "Compress Image", "Cut Video", "Create Subtitles", "Crop Image" };
            var button = FrameShiftUiFactory.CreateMeasuredActionButton(names[i], false);
            button.Click += async (_, _) => await OpenPilotAsync(i);
            _buttons.Add(button);
            return (Control)button;
        }).ToArray();
        var close = Button("Fermer", Close);
        CancelButton = close;
        _result = FrameShiftUiFactory.CreateStatusMessage("Choisissez un pilote. OK / Create File / Compress affiche seulement les réglages retenus ici. Aucun fichier de sortie ni téléchargement IA. Les aperçus lisent les sources.");
        var body = FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateSection("Fichiers de test", choices),
            FrameShiftUiFactory.CreateSection("Fenêtres réelles du build de développement", FrameShiftUiFactory.CreateVerticalStack(pilotButtons)));
        var footer = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        footer.Controls.Add(close);
        Controls.Add(FrameShiftDialogLayout.Create(
            FrameShiftUiFactory.CreateHeader("Phase C — Cinq pilotes", "Validation manuelle du build de développement", IconPaths.AppIcon, "", "▶"), body, footer, _result));
        FormClosing += (_, _) => _cancel.Cancel();
        ResumeLayout(true);
    }

    private async Task OpenPilotAsync(int pilot)
    {
        foreach (var button in _buttons) button.Enabled = false;
        try
        {
            var source = (pilot is 1 or 4 ? _image : _video).Text;
            if (!File.Exists(source)) throw new FileNotFoundException("Choisissez un fichier de test existant.", source);
            var tools = new ToolLocator();
            MediaProbeResult? probe = null;
            if (pilot is 0 or 2)
            {
                _result.Text = "Lecture des informations vidéo…";
                var attempt = await new FfprobeRunner(new AppLogger()).TryProbeMediaAsync(tools.ResolveFfprobePath(), source, _cancel.Token);
                probe = attempt.Probe ?? throw new InvalidOperationException(attempt.ErrorMessage);
            }
            if (IsDisposed || _cancel.IsCancellationRequested) return;
            Func<double, CancellationToken, Task<Bitmap>>? loader = null;
            if (pilot == 2 && _slowPreview.Checked)
                loader = async (seconds, token) =>
                {
                    await Task.Delay(3000, token);
                    return await PreviewFrameHelper.CaptureFrameAsync(tools.ResolveFfmpegPath(), _runner, source, seconds, "Cut Video manual slow preview", token);
                };
            using Form form = pilot switch
            {
                0 => new InterpolateVideoForm(source, probe!.VideoFrameRate ?? throw new InvalidOperationException("FPS indisponible")),
                1 => new CompressImageForm(source, Path.GetExtension(source), new FileInfo(source).Length),
                2 => new CutVideoForm(source, tools.ResolveFfmpegPath(), probe!, _runner, loader),
                3 => new CreateSubtitlesPickerForm("Create Subtitles", Path.GetFileName(source)),
                _ => new CropImageForm(source, tools.ResolveFfmpegPath(), _runner)
            };
            var result = form.ShowDialog(this);
            var settings = form switch
            {
                InterpolateVideoForm f => f.Selection?.ToString(),
                CompressImageForm f => f.Selection?.ToString(),
                CutVideoForm f => f.Selection?.ToString(),
                CropImageForm f => f.Selection?.ToString(),
                CreateSubtitlesPickerForm f => $"{f.SelectedModelId}, {f.SelectedOutputFormat}, {f.SelectedAssPreset}",
                _ => ""
            };
            _result.Text = $"{form.Text} : {result}. " + (result == DialogResult.OK ? settings : "Sélection annulée.");
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
        catch (Exception ex) { if (!IsDisposed) _result.Text = ex.Message; }
        finally { if (!IsDisposed) foreach (var button in _buttons) button.Enabled = true; }
    }

    private static Button Button(string text, Action action)
    {
        var button = FrameShiftUiFactory.CreateMeasuredActionButton(text, false);
        button.Click += (_, _) => action();
        return button;
    }
    private void Browse(TextBox target, string filter)
    {
        using var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
    }
    private static string? FindRepository()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src", "FrameShift", "FrameShift.csproj"))) return dir.FullName;
        return null;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed) { _disposed = true; _cancel.Cancel(); _cancel.Dispose(); }
        base.Dispose(disposing);
    }
}
