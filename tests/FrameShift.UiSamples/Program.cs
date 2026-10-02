using FrameShift.Windows.Helpers;

namespace FrameShift.UiSamples;

// Manual validation only. Does not process files, capture the desktop or change Windows settings.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--check")
        {
            try
            {
                InitializeSampleApplication();
                var results = new List<object>();
                foreach (var editor in new[] { false, true })
                {
                    using var form = new SampleForm(editor);
                    CreateHiddenHandles(form);
                    form.PerformAutoScale();
                    form.PerformLayout();
                    var root = (TableLayoutPanel)form.Controls[0];
                    var actions = root.GetControlFromPosition(0, 3)!;
                    if (form.Visible || actions.Bottom > root.ClientSize.Height || actions.Top < 0)
                        throw new InvalidOperationException("Hidden sample footer is outside the client area.");
                    results.Add(new { editor, dpi = form.DeviceDpi, client = form.ClientSize.ToString(),
                        highDpiMode = Application.HighDpiMode.ToString(), visible = form.Visible });
                }
                using (var launcher = new PilotLauncherForm())
                {
                    CreateHiddenHandles(launcher);
                    launcher.PerformAutoScale();
                    launcher.PerformLayout();
                    if (launcher.Visible) throw new InvalidOperationException("Pilot launcher must stay hidden during checks.");
                    results.Add(new { pilotLauncher = true, dpi = launcher.DeviceDpi, highDpiMode = Application.HighDpiMode.ToString(), visible = launcher.Visible });
                }
                using (var launcher = new D1LauncherForm())
                {
                    CreateHiddenHandles(launcher);
                    launcher.PerformAutoScale();
                    launcher.PerformLayout();
                    if (launcher.Visible) throw new InvalidOperationException("D1 launcher must stay hidden during checks.");
                    results.Add(new { d1Launcher = true, dpi = launcher.DeviceDpi, highDpiMode = Application.HighDpiMode.ToString(), visible = launcher.Visible });
                }
                using (var launcher = new D2LauncherForm())
                {
                    CreateHiddenHandles(launcher);
                    launcher.PerformAutoScale();
                    launcher.PerformLayout();
                    if (launcher.Visible) throw new InvalidOperationException("D2 launcher must stay hidden during checks.");
                    results.Add(new { d2Launcher = true, dpi = launcher.DeviceDpi, highDpiMode = Application.HighDpiMode.ToString(), visible = launcher.Visible });
                }
                using (var launcher = new D3LauncherForm())
                {
                    CreateHiddenHandles(launcher);
                    launcher.PerformAutoScale();
                    launcher.PerformLayout();
                    if (launcher.Visible) throw new InvalidOperationException("D3 launcher must stay hidden during checks.");
                    results.Add(new { d3Launcher = true, dpi = launcher.DeviceDpi, highDpiMode = Application.HighDpiMode.ToString(), visible = launcher.Visible });
                }
                using (var launcher = new ELauncherForm())
                {
                    CreateHiddenHandles(launcher);
                    launcher.PerformAutoScale();
                    launcher.PerformLayout();
                    if (launcher.Visible) throw new InvalidOperationException("E launcher must stay hidden during checks.");
                    results.Add(new { eLauncher = true, dpi = launcher.DeviceDpi, highDpiMode = Application.HighDpiMode.ToString(), visible = launcher.Visible });
                }
                using (var launcher = new F2LauncherForm())
                {
                    CreateHiddenHandles(launcher);
                    launcher.PerformAutoScale();
                    launcher.PerformLayout();
                    if (launcher.Visible) throw new InvalidOperationException("F2 launcher must stay hidden during checks.");
                    results.Add(new { f2Launcher = true, dpi = launcher.DeviceDpi, highDpiMode = Application.HighDpiMode.ToString(), visible = launcher.Visible });
                }
                File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(args[1], ex.ToString());
                return 1;
            }
        }
        InitializeSampleApplication();
        Application.Run(args.Contains("--f2") ? new F2LauncherForm()
            : args.Contains("--e") ? new ELauncherForm()
            : args.Contains("--d3") ? new D3LauncherForm()
            : args.Contains("--d2") ? new D2LauncherForm()
            : args.Contains("--d1") ? new D1LauncherForm()
            : args.Contains("--pilots") ? new PilotLauncherForm() : new SampleForm(editor: false));
        return 0;
    }

    private static void CreateHiddenHandles(Control control)
    {
        _ = control.Handle;
        foreach (Control child in control.Controls) CreateHiddenHandles(child);
    }

    private static void InitializeSampleApplication()
    {
        // InternalsVisibleTo also exposes the application's generated global type.
        // This assembly's SDK-generated initializer wins and configures the sample's PMv2 mode.
#pragma warning disable CS0436
        global::ApplicationConfiguration.Initialize();
#pragma warning restore CS0436
    }
}

internal sealed class SampleForm : Form
{
    public SampleForm(bool editor)
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, editor ? new Size(1000, 660) : new Size(680, 540), new Size(400, 300));
        FrameShiftWindowChrome.Apply(this, editor ? "FrameShift - UI test editor" : "FrameShift - UI test compact");
        var header = FrameShiftUiFactory.CreateHeader(
            editor ? "FrameShift - Editor layout verification" : "FrameShift - Compact layout verification",
            @"Source: E:\Vidéos de démonstration avec accents\Un très long nom de fichier pour vérifier les métadonnées complètes.mp4",
            IconPaths.AppIcon, "", "▶");
        var fields = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top, ColumnCount = 1, Margin = Padding.Empty
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void AddField(Control control)
        {
            var index = fields.Controls.Count;
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.Controls.Add(control, 0, index);
        }
        AddField(FrameShiftUiFactory.CreateFieldRow("&Start time", new TextBox { Text = "00:00:04.733" }));
        AddField(FrameShiftUiFactory.CreateFieldRow("&Format", new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Items = { "MP4 — Standard", "Un choix très long avec espaces et accents" }, SelectedIndex = 0
        }));
        for (var i = 1; i <= 12; i++)
            AddField(FrameShiftUiFactory.CreateFieldRow($"Option {i}", new TextBox { Text = "Valeur de démonstration" }));
        var dynamicButton = FrameShiftUiFactory.CreateMeasuredActionButton("Add a field", false);
        dynamicButton.Click += (_, _) => AddField(FrameShiftUiFactory.CreateFieldRow(
            "Nouveau libellé très long après affichage", new TextBox { Text = "Texte ajouté dynamiquement" }));
        AddField(dynamicButton);
        var other = FrameShiftUiFactory.CreateMeasuredActionButton(editor ? "Open compact sample" : "Open editor sample", false);
        other.Click += (_, _) =>
        {
            var sample = new SampleForm(!editor);
            sample.FormClosed += (_, _) => sample.Dispose();
            sample.Show(this);
        };
        AddField(other);
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Close", false);
        cancel.Click += (_, _) => Close();
        CancelButton = cancel;
        var primary = FrameShiftUiFactory.CreateMeasuredActionButton("Test long caption", true);
        var longCaption = false;
        primary.Click += (_, _) =>
        {
            longCaption = !longCaption;
            primary.Text = longCaption ? "Confirmer le traitement avec le préréglage sélectionné" : "Test long caption";
            header.TitleLabel.Text = longCaption ? "FrameShift - Une fonction avec un titre beaucoup plus long après affichage" : "FrameShift - Layout verification";
        };
        AcceptButton = primary;
        var status = FrameShiftUiFactory.CreateStatusMessage("");
        void UpdateStatus()
        {
            status.Text = $"DPI: {DeviceDpi} ({DeviceDpi * 100 / 96} %) | Client: {ClientSize.Width} × {ClientSize.Height} | " +
                $"Working area: {Screen.FromControl(this).WorkingArea.Size}. No media processing.";
        }
        var actions = FrameShiftDialogLayout.CreateActions(cancel, primary);
        var options = FrameShiftUiFactory.CreateSection("Options — scrolling content", fields);
        var preview = new Panel { BackColor = Color.FromArgb(32, 32, 32) };
        preview.Paint += (_, e) =>
        {
            var edge = FrameShiftUiMetrics.ToPixels(preview, 12);
            var bounds = Rectangle.Inflate(preview.ClientRectangle, -edge, -edge);
            if (bounds.Width > 0 && bounds.Height > 0)
                e.Graphics.DrawRectangle(Pens.CornflowerBlue, bounds);
        };
        preview.Resize += (_, _) => preview.Invalidate();
        Controls.Add(editor
            ? FrameShiftEditorShellUi.Create(header, preview, actions, options, status, 340)
            : FrameShiftDialogLayout.Create(header, options, actions, status));
        if (!editor) preview.Dispose();
        Shown += (_, _) => UpdateStatus();
        Resize += (_, _) => UpdateStatus();
        DpiChanged += (_, _) => BeginInvoke((Action)UpdateStatus);
        ResumeLayout(true);
    }
}
