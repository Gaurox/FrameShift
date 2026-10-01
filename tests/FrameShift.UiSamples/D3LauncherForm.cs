using FrameShift.Core.AI;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Windows.AI;
using FrameShift.Windows.Helpers;

namespace FrameShift.UiSamples;

// Real pickers, settings only. Download and BRIA verification use explicit local simulations.
internal sealed class D3LauncherForm : Form
{
    private readonly TextBox _video = new();
    private readonly TextBox _audio = new();
    private readonly TextBox _image = new();
    private readonly CheckBox _preview = new() { Text = "Activer l'aperçu audio réel (modèle déjà installé requis)", AutoSize = true };
    private readonly CheckBox _batch = new() { Text = "Simuler plusieurs fichiers pour Upscale", AutoSize = true };
    private readonly CheckBox _gpu = new() { Text = "Simuler DirectML disponible pour Separate Audio", AutoSize = true, Checked = true };
    private readonly TextBox _result;
    private readonly List<Button> _buttons = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public D3LauncherForm()
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(880, 780), new Size(440, 360));
        FrameShiftWindowChrome.Apply(this, "FrameShift - Recette D3");
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var fixtures = Path.Combine(dir.FullName, "scratch", "phase-a");
            if (!Directory.Exists(fixtures)) continue;
            _video.Text = Directory.EnumerateFiles(fixtures, "*.mp4").FirstOrDefault() ?? "";
            _audio.Text = Directory.EnumerateFiles(fixtures, "*.mp3").FirstOrDefault()
                ?? Directory.EnumerateFiles(fixtures, "*.wav").FirstOrDefault() ?? "";
            _image.Text = Directory.EnumerateFiles(fixtures, "*.png").FirstOrDefault() ?? "";
            break;
        }
        Control FileRow(string caption, TextBox box, string filter)
        {
            var browse = FrameShiftUiFactory.CreateMeasuredActionButton($"Choisir {caption}…", false);
            browse.Click += (_, _) =>
            {
                using var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
                if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName;
            };
            return FrameShiftUiFactory.CreateVerticalStack(FrameShiftUiFactory.CreateFieldRow(caption, box), browse);
        }
        var files = FrameShiftUiFactory.CreateSection("Fichiers de test", FrameShiftUiFactory.CreateVerticalStack(
            FileRow("Vidéo", _video, "Vidéo|*.mp4;*.mkv;*.mov;*.webm|Tous|*.*"),
            FileRow("Audio", _audio, "Audio|*.mp3;*.wav;*.flac;*.m4a;*.ogg|Tous|*.*"),
            FileRow("Image", _image, "Image|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Tous|*.*")));
        var variants = FrameShiftUiFactory.CreateSection("Variantes de recette", FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateChoiceRow(_preview), FrameShiftUiFactory.CreateChoiceRow(_batch), FrameShiftUiFactory.CreateChoiceRow(_gpu)));
        var captions = new[] { "Remove Noise Audio", "Remove Noise Video", "Separate Audio", "RIFE", "Upscale Image", "Upscale Video",
            "Download : succès", "Download : erreur longue", "Download : annulation", "BRIA : absent / valide", "BRIA : fichier incorrect" };
        var choices = FrameShiftUiFactory.CreateChoiceRow();
        for (var index = 0; index < captions.Length; index++)
        {
            var choice = index;
            var button = FrameShiftUiFactory.CreateMeasuredActionButton(captions[index], false, 210);
            button.Click += async (_, _) => await OpenAsync(choice);
            choices.Controls.Add(button);
            _buttons.Add(button);
        }
        _result = FrameShiftUiFactory.CreateStatusMessage(
            "Les validations affichent les réglages, sans export final. Download et BRIA sont simulés : aucun modèle téléchargé ou vérifié. Aperçu audio réel uniquement si activé, avec un modèle déjà installé.");
        var close = FrameShiftUiFactory.CreateMeasuredActionButton("Fermer", false);
        close.Click += (_, _) => Close();
        CancelButton = close;
        Controls.Add(FrameShiftDialogLayout.Create(
            FrameShiftUiFactory.CreateHeader("Phase D3 — Fenêtres IA", "Build de développement et scénarios de recette", IconPaths.FrameShiftAiIcon, IconPaths.AppIcon, "AI"),
            FrameShiftUiFactory.CreateVerticalStack(files, variants, FrameShiftUiFactory.CreateSection("Fenêtres", choices)),
            FrameShiftDialogLayout.CreateActions(close), _result));
        FormClosing += (_, _) => _lifetime.Cancel();
        ResumeLayout(true);
    }

    private async Task OpenAsync(int choice)
    {
        foreach (var button in _buttons) button.Enabled = false;
        try
        {
            using var form = choice >= 6 ? SimulatedForm(choice) : await PickerAsync(choice);
            if (IsDisposed || _lifetime.IsCancellationRequested) return;
            var result = form.ShowDialog(this);
            object? settings = form switch
            {
                RemoveNoiseAudioPickerForm f => new { f.SelectedStrength, f.ProcessStereo },
                RemoveNoiseVideoPickerForm f => new { f.SelectedStrength, f.ProcessStereo },
                SeparateAudioPickerForm f => f.SelectionOptions,
                RifeInterpolateVideoPickerForm f => f.Selection,
                UpscaleImagePickerForm f => new { f.SelectedModelId, f.SelectedScale, f.CustomTarget },
                BriaModelNoticeForm f => new { f.Proceed },
                _ => new { result = result.ToString(), simulation = true }
            };
            if (!IsDisposed) _result.Text = result == DialogResult.OK
                ? System.Text.Json.JsonSerializer.Serialize(settings) : "Sélection annulée.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (!IsDisposed) _result.Text = ex.Message; }
        finally { if (!IsDisposed) foreach (var button in _buttons) button.Enabled = true; }
    }

    private async Task<Form> PickerAsync(int choice)
    {
        var source = (choice is 0 or 2 ? _audio : choice == 4 ? _image : _video).Text;
        if (!File.Exists(source)) throw new FileNotFoundException("Choisissez un fichier existant.", source);
        var tools = new ToolLocator();
        _result.Text = "Lecture des informations du média…";
        var attempt = await new FfprobeRunner(new AppLogger()).TryProbeMediaAsync(tools.ResolveFfprobePath(), source, _lifetime.Token);
        var probe = attempt.Probe ?? throw new InvalidOperationException(attempt.ErrorMessage);
        _lifetime.Token.ThrowIfCancellationRequested();
        return choice switch
        {
            0 => new RemoveNoiseAudioPickerForm(Path.GetFileName(source), probe.PrimaryAudioChannels >= 2,
                _preview.Checked ? source : null, _preview.Checked ? tools.ResolveFfmpegPath() : null),
            1 => new RemoveNoiseVideoPickerForm(Path.GetFileName(source), probe.PrimaryAudioChannels >= 2,
                _preview.Checked ? source : null, _preview.Checked ? tools.ResolveFfmpegPath() : null),
            2 => new SeparateAudioPickerForm(Path.GetFileName(source), SeparateAudioPickerForm.BuildDurationLabel(probe, 1), _gpu.Checked),
            3 => new RifeInterpolateVideoPickerForm(source, probe.VideoFrameRate ?? 30),
            4 => new UpscaleImagePickerForm(_batch.Checked ? "12 selected files" : Path.GetFileName(source), probe.VideoWidth, probe.VideoHeight, !_batch.Checked),
            _ => new UpscaleVideoPickerForm(_batch.Checked ? "12 selected files" : Path.GetFileName(source), probe.DisplayVideoWidth, probe.DisplayVideoHeight, !_batch.Checked)
        };
    }

    private static Form SimulatedForm(int choice)
    {
        if (choice >= 9)
        {
            var checks = 0;
            return new BriaModelNoticeForm("BRIA_modele_exemple.onnx", Path.Combine(Path.GetTempPath(), "FrameShift recette D3 modèles"),
                "https://huggingface.co/briaai/RMBG-2.0", "1 GB — simulation",
                choice == 10 ? BriaModelStatus.Mismatch : BriaModelStatus.Missing,
                () => ++checks == 1 ? BriaModelStatus.Mismatch : BriaModelStatus.Valid);
        }
        var attempts = 0;
        return new DownloadModelForm("FrameShift - Download model", "Simulation locale — aucun réseau ni fichier modèle",
            IconPaths.FrameShiftAiIcon, "Modèle de démonstration", "Recette simulée", 300_000_000,
            async (progress, ct) =>
            {
                attempts++;
                for (var percent = 0; percent <= 100; percent += 5)
                {
                    await Task.Delay(choice == 8 ? 700 : 150, ct);
                    progress.Report(new AiModelDownloadProgress(percent, $"Simulation : {percent} %"));
                    if (choice == 7 && attempts == 1 && percent == 30)
                        throw new InvalidDataException(string.Join("\r\n", Enumerable.Range(1, 40).Select(i =>
                            $"Détail {i}: erreur simulée sur un chemin très long avec espaces et accents. Le message complet reste sélectionnable et copiable.")));
                }
            });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed) { _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
