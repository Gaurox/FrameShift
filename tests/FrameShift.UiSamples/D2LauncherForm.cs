using System.Drawing.Imaging;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.UiSamples;

// Real dialogs; accepting reports settings only. Previews run only at the user's request/opening.
internal sealed class D2LauncherForm : Form
{
    private readonly TextBox _video = new();
    private readonly TextBox _audio = new();
    private readonly TextBox _image = new();
    private readonly TextBox _result;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly FfmpegRunner _runner = new(new AppLogger());
    private readonly List<Button> _launchButtons = [];
    private bool _disposed;

    public D2LauncherForm()
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(900, 850), new Size(440, 360));
        FrameShiftWindowChrome.Apply(this, "FrameShift — Recette D2");
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var fixtures = Path.Combine(dir.FullName, "scratch", "phase-a");
            if (!Directory.Exists(fixtures)) continue;
            _video.Text = Directory.EnumerateFiles(fixtures, "*.mp4").FirstOrDefault() ?? "";
            _audio.Text = Directory.EnumerateFiles(fixtures, "*.wav").FirstOrDefault() ?? "";
            _image.Text = Directory.EnumerateFiles(fixtures, "*.png").FirstOrDefault() ?? "";
            break;
        }
        Control FileRow(string caption, TextBox target, string filter)
        {
            var browse = FrameShiftUiFactory.CreateMeasuredActionButton($"Choisir {caption}…", false);
            browse.Click += (_, _) =>
            {
                using var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
                if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
            };
            return FrameShiftUiFactory.CreateVerticalStack(FrameShiftUiFactory.CreateFieldRow(caption, target), browse);
        }
        var files = FrameShiftUiFactory.CreateSection("Fichiers de test", FrameShiftUiFactory.CreateVerticalStack(
            FileRow("Vidéo", _video, "Vidéo|*.mp4;*.mkv;*.mov;*.webm;*.avi|Tous|*.*"),
            FileRow("Audio", _audio, "Audio|*.mp3;*.wav;*.flac;*.m4a;*.ogg|Tous|*.*"),
            FileRow("Image", _image, "Image|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tif;*.tiff|Tous|*.*")));
        var captions = new[] { "Conversion vidéo", "Conversion audio", "Conversion image", "Compression multiple",
            "Compress Audio", "Compress Video", "Resize Image", "Resize Video", "Change Pitch", "Speed Audio", "Speed Video",
            "Rotate / Flip Image", "Rotate / Flip Video", "Convert to Icon", "Add Subtitles" };
        var buttons = FrameShiftUiFactory.CreateChoiceRow();
        for (var i = 0; i < captions.Length; i++)
        {
            var choice = i;
            var button = FrameShiftUiFactory.CreateMeasuredActionButton(captions[i], false, 200);
            button.Click += async (_, _) => await OpenAsync(choice);
            _launchButtons.Add(button);
            buttons.Controls.Add(button);
        }
        _result = FrameShiftUiFactory.CreateStatusMessage("OK / Apply affiche uniquement les réglages. Aucun export final. Les aperçus lisent vos sources ; Preview 5s produit un fichier temporaire et peut ouvrir le lecteur vidéo habituel.");
        var close = FrameShiftUiFactory.CreateMeasuredActionButton("Fermer", false);
        close.Click += (_, _) => Close();
        CancelButton = close;
        Controls.Add(FrameShiftDialogLayout.Create(
            FrameShiftUiFactory.CreateHeader("Phase D2 — Dialogues", "Validation du build de développement", IconPaths.AppIcon, "", "▶"),
            FrameShiftUiFactory.CreateVerticalStack(files, FrameShiftUiFactory.CreateSection("Fenêtres", buttons)),
            FrameShiftDialogLayout.CreateActions(close), _result));
        FormClosing += (_, _) => _lifetime.Cancel();
        ResumeLayout(true);
    }

    private async Task OpenAsync(int choice)
    {
        foreach (var button in _launchButtons) button.Enabled = false;
        string? temporaryPreview = null;
        try
        {
            var source = (choice is 1 or 4 or 8 or 9 ? _audio : choice is 2 or 6 or 11 or 13 ? _image : _video).Text;
            var extension = Path.GetExtension(source).ToLowerInvariant();
            var needsFile = choice is >= 4 and <= 13;
            if (needsFile && !File.Exists(source)) throw new FileNotFoundException("Choisissez un fichier existant.", source);
            var tools = new ToolLocator();
            MediaProbeResult? probe = null;
            if (choice is 4 or 5 or 6 or 7 or 9 or 10 or 12)
            {
                _result.Text = "Lecture des informations du média…";
                var attempt = await new FfprobeRunner(new AppLogger()).TryProbeMediaAsync(tools.ResolveFfprobePath(), source, _lifetime.Token);
                probe = attempt.Probe ?? throw new InvalidOperationException(attempt.ErrorMessage);
            }
            var preview = source;
            if (choice == 13 && extension == ".webp")
            {
                using var bitmap = await PreviewFrameHelper.CaptureFrameAsync(tools.ResolveFfmpegPath(), _runner, source, 0, "D2 icon preview", _lifetime.Token);
                temporaryPreview = Path.Combine(Path.GetTempPath(), $"FrameShift D2 {Guid.NewGuid():N}.png");
                bitmap.Save(temporaryPreview, ImageFormat.Png);
                preview = temporaryPreview;
            }
            if (IsDisposed || _lifetime.IsCancellationRequested) return;
            using Form form = choice switch
            {
                0 => new ConversionPickerForm("Convert Video", Path.GetFileName(source), "", VideoConversionCatalog.GetTargets(), VideoConversionCatalog.GetProfiles()),
                1 => new ConversionPickerForm("Convert Audio", Path.GetFileName(source), "", AudioConversionCatalog.GetTargets(), AudioConversionCatalog.GetProfiles()),
                2 => new ConversionPickerForm("Convert Image", Path.GetFileName(source), "", ImageConversionCatalog.GetTargets(), []),
                3 => new CompressMultiFileChoiceForm(12),
                4 => new CompressAudioForm(source, extension, new FileInfo(source).Length, probe!.PrimaryAudioSampleRate, probe.PrimaryAudioChannels),
                5 => new CompressVideoForm(source, extension, new FileInfo(source).Length, probe!.GetDisplayGeometrySummary()),
                6 => new ResizeImageForm(source, probe!.VideoWidth, probe.VideoHeight),
                7 => new ResizeVideoForm(source, probe!.VideoWidth, probe.VideoHeight),
                8 => new ChangePitchForm(source, tools.ResolveFfmpegPath(), _runner),
                9 or 10 => new ChangeSpeedForm(source, tools.ResolveFfmpegPath(), _runner,
                    choice == 9 ? ChangeSpeedMediaKind.Audio : ChangeSpeedMediaKind.Video, probe!.Duration?.TotalSeconds ?? 0, probe.HasAudio, probe.PrimaryAudioSampleRate),
                11 => new RotateFlipImageForm(source, tools.ResolveFfmpegPath(), _runner),
                12 => new RotateFlipVideoForm(source, tools.ResolveFfmpegPath(), probe!, _runner),
                13 => new ConvertToIconForm(source, preview),
                _ => new AddSubtitlesToVideoPickerForm(Path.GetFileName(source), AddSubtitlesToVideoMode.SelectableTrack, null, null)
            };
            var result = form.ShowDialog(this);
            object? settings = form switch
            {
                ConversionPickerForm f => f.Selection,
                CompressMultiFileChoiceForm f => f.Choice,
                CompressAudioForm f => new { f.SelectedProfileId, f.TargetBytes },
                CompressVideoForm f => new { f.SelectedProfileId, f.TargetBytes },
                ResizeMediaFormBase f => f.Selection,
                ChangePitchForm f => f.Selection,
                ChangeSpeedForm f => f.Selection,
                RotateFlipImageForm f => f.Selection,
                RotateFlipVideoForm f => f.Selection,
                ConvertToIconForm f => f.Selection,
                AddSubtitlesToVideoPickerForm f => f.SelectedSettings,
                _ => null
            };
            if (!IsDisposed) _result.Text = result == DialogResult.OK
                ? System.Text.Json.JsonSerializer.Serialize(settings) : "Sélection annulée.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (!IsDisposed) _result.Text = ex.Message; }
        finally
        {
            if (temporaryPreview is not null) ConversionActionHelper.DeleteIfExists(temporaryPreview);
            if (!IsDisposed) foreach (var button in _launchButtons) button.Enabled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed) { _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
