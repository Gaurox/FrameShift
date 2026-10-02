using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Logging;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;
using FrameShift.Windows.ProgressUI;

namespace FrameShift.UiSamples;

// Theme choices stay in this process. Child forms are modeless so this selector stays reachable.
internal sealed class F2LauncherForm : Form
{
    private readonly TextBox _result;
    private readonly FrameShiftThemeMode _initialTheme;

    internal F2LauncherForm()
    {
        SuspendLayout();
        FrameShiftTheme.Initialize();
        FrameShiftWindowPolicy.Initialize(this, new Size(740, 580), new Size(380, 300));
        _initialTheme = FrameShiftTheme.EffectiveTheme;
        FrameShiftWindowChrome.Apply(this, "FrameShift — Recette F2");
        _result = FrameShiftUiFactory.CreateStatusMessage("Les boutons de thème de ce lanceur ne sauvegardent aucune préférence. Les fenêtres restent ouvertes pendant le changement. Aucun export, téléchargement ou capture automatique.");
        Button Command(string caption, Action action)
        {
            var button = FrameShiftUiFactory.CreateMeasuredActionButton(caption, false);
            button.Click += (_, _) =>
            {
                try { action(); }
                catch (Exception ex) { _result.Text = ex.Message; }
            };
            return button;
        }
        Button Theme(string caption, FrameShiftThemePreference preference) => Command(caption, () =>
        {
            FrameShiftTheme.ApplyPreference(preference);
            _result.Text = $"Thème effectif : {FrameShiftTheme.EffectiveTheme}. Préférence sur disque inchangée. Vérifiez repos, survol, appui, Tab et commandes désactivées dans les fenêtres ouvertes.";
        });
        var themes = FrameShiftUiFactory.CreateSection("Thème temporaire — toutes les fenêtres ouvertes", FrameShiftUiFactory.CreateChoiceRow(
            Theme("Clair", FrameShiftThemePreference.Light), Theme("Sombre", FrameShiftThemePreference.Dark),
            Theme("Système", FrameShiftThemePreference.System)));
        var windows = FrameShiftUiFactory.CreateSection("Fenêtres de référence", FrameShiftUiFactory.CreateChoiceRow(
            Command("Main / Files", OpenMain),
            Command("Settings", () => Open(new SettingsForm())),
            Command("Compress Audio", () => Open(new CompressAudioForm("Audio avec espaces et accents.mp3", ".mp3", 12_000_000, 48000, 2))),
            Command("Join Videos", () => Open(new JoinVideosForm([], "absent.exe", "absent.exe", new FfmpegRunner(new AppLogger()),
                new FfprobeRunner(new AppLogger()), videoPicker: _ => [], uiSettingsPathForTesting: Path.Combine(Path.GetTempPath(), "frameshift-f2-join.json"))))));
        var progress = FrameShiftUiFactory.CreateSection("Progress — états simulés", FrameShiftUiFactory.CreateChoiceRow(
            Command("Attente", () => OpenProgress("waiting")), Command("Erreur", () => OpenProgress("failed")),
            Command("Succès", () => OpenProgress("completed"))));
        var close = FrameShiftUiFactory.CreateMeasuredActionButton("Fermer", false);
        close.Click += (_, _) => Close();
        CancelButton = close;
        var root = FrameShiftDialogLayout.Create(
            FrameShiftUiFactory.CreateHeader("F2 — Couleurs et états", "Recette ciblée · build de développement", IconPaths.AppIcon, "", "F"),
            FrameShiftUiFactory.CreateVerticalStack(themes, windows, progress), FrameShiftDialogLayout.CreateActions(close), _result);
        Controls.Add(root);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, root);
        FormClosed += (_, _) => FrameShiftTheme.ApplyPreference(_initialTheme == FrameShiftThemeMode.Dark
            ? FrameShiftThemePreference.Dark : FrameShiftThemePreference.Light);
        ResumeLayout(true);
    }

    private void Open(Form form)
    {
        // Native DialogResult buttons also need to close a modeless test window.
        foreach (var button in new[] { form.AcceptButton, form.CancelButton }.OfType<Button>().Distinct())
        {
            if (button.DialogResult == DialogResult.None) continue;
            button.Click += (_, _) =>
            {
                if (!form.IsDisposed && form.DialogResult == button.DialogResult) form.Close();
            };
        }
        form.FormClosed += (_, _) => form.Dispose();
        form.Show(this);
    }

    private void OpenMain()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Ouvrez le lanceur depuis le dossier du projet.");
        var fixtures = Path.Combine(directory.FullName, "scratch", "phase-f2", "fichiers fictifs");
        Directory.CreateDirectory(fixtures);
        var paths = new[] { "Vidéo avec accents.mp4", "Audio de démonstration.mp3", "Image avec espaces.png" }
            .Select(name => Path.Combine(fixtures, name)).ToArray();
        foreach (var path in paths) if (!File.Exists(path)) File.WriteAllText(path, "Fixture UI F2 — no media content.");
        Open(new MainForm(paths, e => _result.Text = $"Action sélectionnée : {e.Entry.DisplayName}. Aucun traitement lancé."));
    }

    private void OpenProgress(string state)
    {
        var form = new ProgressForm();
        form.ReportQueue(["Vidéo avec accents.mp4", "Autre vidéo.mp4"]);
        if (state != "waiting")
        {
            var message = state == "failed" ? string.Join("\r\n", Enumerable.Range(1, 12)
                .Select(i => $"Détail {i} — erreur simulée sur un chemin avec espaces et accents, à lire intégralement.")) : "Traitement simulé terminé.";
            form.ReportQueueItem("Vidéo avec accents.mp4", state, message);
            form.ReportState(state, message);
            form.EnableCloseMode();
        }
        else form.CancelRequested += (_, _) => form.EnableCloseMode("Annulation simulée.");
        Open(form);
    }
}
