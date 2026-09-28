using FrameShift.Core.Actions;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;
using FrameShift.Windows.ProgressUI;

namespace FrameShift.UiSamples;

internal sealed class D1LauncherForm : Form
{
    private readonly TextBox _result;

    public D1LauncherForm()
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(700, 570), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, "FrameShift — Recette D1");
        _result = FrameShiftUiFactory.CreateStatusMessage("Progress utilise une file simulée. Main affiche l’action choisie ici sans la lancer. Settings conserve son fonctionnement réel : tout changement de préférence y est enregistré.");
        Button Launch(string caption, Action action)
        {
            var button = FrameShiftUiFactory.CreateMeasuredActionButton(caption, false);
            button.Click += (_, _) =>
            {
                try { action(); }
                catch (Exception ex) { _result.Text = ex.Message; }
            };
            return button;
        }
        var progress = FrameShiftUiFactory.CreateSection("Progress — états simulés", FrameShiftUiFactory.CreateChoiceRow(
            Launch("En cours", () => OpenProgress("running")),
            Launch("Attente", () => OpenProgress("waiting")),
            Launch("Erreur longue", () => OpenProgress("failed")),
            Launch("Terminé", () => OpenProgress("completed"))));
        var windows = FrameShiftUiFactory.CreateSection("Autres fenêtres D1", FrameShiftUiFactory.CreateChoiceRow(
            Launch("Main vide", () => OpenMain(false)), Launch("Main avec fichiers", () => OpenMain(true)),
            Launch("Settings", () => { using var form = new SettingsForm(); form.ShowDialog(this); }),
            Launch("Media Info", () =>
            {
                using var form = new MediaInfoForm(string.Join("\r\n", Enumerable.Range(1, 80).Select(i =>
                    $"Stream {i:00} — Valeur de démonstration avec accents | " + new string('x', 160))), MediaKind.Video,
                    "Vidéo de démonstration avec un très long nom de fichier et des accents.mp4");
                form.ShowDialog(this);
            })));
        var close = Launch("Fermer", Close);
        CancelButton = close;
        var root = FrameShiftDialogLayout.Create(
            FrameShiftUiFactory.CreateHeader("Phase D1 — Recette", "Build de développement · aucune capture automatique", IconPaths.AppIcon, "", "F"),
            FrameShiftUiFactory.CreateVerticalStack(progress, windows), FrameShiftDialogLayout.CreateActions(close), _result);
        Controls.Add(root);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, root);
        ResumeLayout(true);
    }

    private void OpenMain(bool populated)
    {
        var paths = Array.Empty<string>();
        if (populated)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
            if (root is null) throw new InvalidOperationException("Ouvrez ce lanceur depuis le dossier du projet.");
            var directory = Path.Combine(root.FullName, "scratch", "phase-d1", "fichiers fictifs");
            Directory.CreateDirectory(directory);
            paths = new[] { "Vidéo avec accents.mp4", "Audio de démonstration.mp3", "Image avec espaces.png" }
                .Select(name => Path.Combine(directory, name)).ToArray();
            foreach (var path in paths) if (!File.Exists(path)) File.WriteAllText(path, "Fixture UI D1 — no media content.");
        }
        using var form = new MainForm(paths, e =>
        {
            var message = $"Action : {e.Entry.DisplayName}\r\nFichiers :\r\n{string.Join("\r\n", e.Files)}\r\n\r\nAucun traitement lancé.";
            _result.Text = message;
            MessageBox.Show(message, "Recette D1", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
        form.ShowDialog(this);
    }

    private void OpenProgress(string scenario)
    {
        using var form = new ProgressForm();
        using var timer = new System.Windows.Forms.Timer { Interval = 250 };
        var paths = Enumerable.Range(1, 80).Select(i => $@"E:\Dossier de démonstration avec accents\Vidéo numéro {i:00} avec un très long nom.mp4").ToArray();
        var value = 0;
        timer.Tick += (_, _) =>
        {
            if (form.IsQueueItemCancellationRequested(paths[0]))
            {
                timer.Stop();
                form.ReportQueueItem(paths[0], "canceled", "Fichier actif annulé. Les autres restent dans la file de démonstration.");
                form.ReportState("waiting", "Fichier actif annulé. Utilisez Cancel all pour terminer la recette.");
                return;
            }
            value = (value + 3) % 950;
            form.ReportProgress(value, paths[0], "Conversion de démonstration", "Traitement simulé — annulez pour tester la fin propre.", "Remaining: 00:12:35");
        };
        form.CancelRequested += (_, _) =>
        {
            timer.Stop();
            form.ReportQueueItem(paths[0], "canceled", "Annulation simulée terminée.");
            form.EnableCloseMode("Annulation simulée terminée. Vous pouvez fermer.");
        };
        form.QueueItemRemoveRequested += (_, id) =>
        {
            if (id != ActionQueueRunner.CreateQueueItemId(0)) return;
            timer.Stop();
            form.ReportQueueItem(paths[0], "canceled", "Fichier actif annulé. Les autres restent dans la file de démonstration.");
            form.ReportState("waiting", "Fichier actif annulé. Utilisez Cancel all pour terminer la recette.");
        };
        form.Shown += (_, _) =>
        {
            form.ReportQueue(paths);
            if (scenario == "running") timer.Start();
            else if (scenario == "failed")
            {
                var error = string.Join("\r\n", Enumerable.Range(1, 12).Select(i => $"Détail {i} : erreur simulée sur un chemin très long avec espaces et accents. Le texte complet reste consultable."));
                form.ReportQueueItem(paths[0], "failed", error);
                form.ReportState("failed", error);
                form.EnableCloseMode();
            }
            else if (scenario == "completed")
            {
                form.ReportProgress(1000, paths[0], "Conversion", "Terminé.");
                foreach (var path in paths) form.ReportQueueItem(path, "completed", "Sortie simulée.");
                form.EnableCloseMode("Les 80 fichiers simulés sont terminés.");
            }
        };
        form.ShowDialog(this);
        timer.Stop();
    }
}
