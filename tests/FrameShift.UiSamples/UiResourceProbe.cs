using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;
using FrameShift.Windows.ProgressUI;

namespace FrameShift.UiSamples;

// A dedicated STA process: hidden HWNDs and in-memory drawing only, never desktop capture or input.
internal static class UiResourceProbe
{
    private static int _receivedDpi;
    internal static int Run(string outputPath)
    {
        try
        {
            FrameShiftTheme.Initialize();
            var scenarios = new List<Scenario>();
            Measure("Window chrome: replacement, reuse, handle recreation", ExerciseChrome, scenarios);
            Measure("Header/tool bitmaps and shared fonts", ExerciseComponents, scenarios);
            Measure("Main filters, picker and Progress", ExerciseWindows, scenarios);
            var passed = scenarios.All(s => s.Passed);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(new
            {
                passed, processId = Environment.ProcessId, dpi = _receivedDpi,
                highDpiMode = Application.HighDpiMode.ToString(), visibleWindows = Application.OpenForms.Cast<Form>().Count(f => f.Visible),
                warmupCycles = 8, batches = 4, cyclesPerBatch = 20,
                // Native caches may settle after the baseline. This is a bounded tolerance, not a per-cycle budget.
                allowedGdiGrowth = 4, allowedUserGrowth = 4,
                scenarios
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            File.WriteAllText(outputPath, ex.ToString());
            return 1;
        }
    }

    private static void Measure(string name, Action cycle, List<Scenario> scenarios)
    {
        for (var i = 0; i < 8; i++) cycle();
        Settle();
        var samples = new List<Sample> { ReadSample(0) };
        for (var batch = 1; batch <= 4; batch++)
        {
            for (var cycleIndex = 0; cycleIndex < 20; cycleIndex++) cycle();
            Settle();
            samples.Add(ReadSample(batch * 20));
        }
        var baseline = samples[0];
        var passed = samples.All(s => s.Gdi <= baseline.Gdi + 4 && s.User <= baseline.User + 4);
        scenarios.Add(new Scenario(name, passed, samples));
    }

    private static Sample ReadSample(int cycles)
    {
        using var process = Process.GetCurrentProcess();
        var gdi = GetGuiResources(process.Handle, 0);
        var user = GetGuiResources(process.Handle, 1);
        if (gdi == 0 || user == 0) throw new InvalidOperationException("Native GDI/USER counters are unavailable.");
        return new Sample(cycles, gdi, user, process.HandleCount, process.PrivateMemorySize64);
    }

    private static void Settle()
    {
        Application.DoEvents();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Application.DoEvents();
        if (Application.OpenForms.Cast<Form>().Any(f => f.Visible)) throw new InvalidOperationException("Resource probe displayed a window.");
    }

    private static void ExerciseChrome()
    {
        using var form = new HiddenRecreatedForm();
        FrameShiftWindowPolicy.Initialize(form, new Size(480, 300), new Size(320, 200));
        FrameShiftWindowChrome.Apply(form, "FrameShift - Resource test");
        _ = form.Handle;
        _receivedDpi = form.DeviceDpi;
        for (var i = 0; i < 12; i++)
        {
            var path = i % 2 == 0 ? IconPaths.FrameShiftAiIcon : IconPaths.AppIcon;
            FrameShiftWindowChrome.Apply(form, "FrameShift - Resource test", path, IconPaths.AppIcon);
            FrameShiftWindowChrome.Apply(form, "FrameShift - Resource test", path, IconPaths.AppIcon);
            form.RecreateHiddenHandle();
        }
        if (form.Visible) throw new InvalidOperationException("Chrome form must stay hidden.");
    }

    private static void ExerciseComponents()
    {
        using var form = new Form();
        FrameShiftWindowPolicy.Initialize(form, new Size(640, 360), new Size(320, 240));
        FrameShiftWindowChrome.Apply(form, "FrameShift - Components test");
        var header = FrameShiftUiFactory.CreateHeader("Resource test", "Source avec accents et espaces.mp4", IconPaths.AppIcon, "", "");
        var tile = new FrameShiftToolTile("Fit page");
        tile.SetIcon(IconPaths.AppIcon);
        form.Controls.Add(FrameShiftDialogLayout.Create(header,
            FrameShiftUiFactory.CreateChoiceRow(tile, new FrameShiftChoiceCard("Standard", "Description")),
            FrameShiftDialogLayout.CreateActions(FrameShiftUiFactory.CreateMeasuredActionButton("Close", false))));
        CreateHiddenHandles(form);
        var cache = typeof(FrameShiftHeader).GetField("_iconSize", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var i = 0; i < 12; i++)
        {
            // Invalidating the cache exercises replacement at the received DPI, without changing Windows scaling.
            cache.SetValue(header, Size.Empty);
            header.PerformLayout();
            tile.SetIcon(i % 2 == 0 ? IconPaths.FrameShiftAiIcon : IconPaths.AppIcon);
            using var bitmap = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height));
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        }
        if (form.Visible) throw new InvalidOperationException("Component form must stay hidden.");
    }

    private static void ExerciseWindows()
    {
        using var form = new MainForm();
        CreateHiddenHandles(form);
        var actions = FindControls(form).OfType<ActionsPanel>().Single();
        actions.SetFiles([@"C:\missing\Vidéo é.mp4", @"C:\missing\Audio.wav", @"C:\missing\Image.png"], []);
        var search = FindControls(actions).OfType<TextBox>().Single();
        for (var i = 0; i < 12; i++)
        {
            search.Text = i % 2 == 0 ? "Compress" : "";
            var chip = FindControls(actions).OfType<Button>().FirstOrDefault(b => b.Text.StartsWith(i % 2 == 0 ? "Video" : "All"));
            if (chip is not null)
                typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(chip, [EventArgs.Empty]);
        }
        using var picker = new CompressAudioForm("Audio avec espaces et accents.mp3", ".mp3", 12_000_000, 48000, 2);
        using var progress = new ProgressForm();
        progress.ReportQueue(["Vidéo é.mp4", "Autre vidéo.mp4"]);
        CreateHiddenHandles(picker);
        CreateHiddenHandles(progress);
        foreach (var preference in new[] { FrameShiftThemePreference.Light, FrameShiftThemePreference.Dark, FrameShiftThemePreference.Light })
        {
            FrameShiftTheme.ApplyPreference(preference);
            foreach (var window in new Form[] { form, picker, progress }) FrameShiftTheme.ApplyToControl(window);
        }
        progress.ReportQueueItem("Vidéo é.mp4", "failed", "Erreur simulée avec accents et espaces.");
        if (form.Visible || picker.Visible || progress.Visible) throw new InvalidOperationException("Reference windows must stay hidden.");
    }

    private static IEnumerable<Control> FindControls(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var descendant in FindControls(child)) yield return descendant;
        }
    }

    private static void CreateHiddenHandles(Control control)
    {
        _ = control.Handle;
        foreach (Control child in control.Controls) CreateHiddenHandles(child);
    }

    private sealed class HiddenRecreatedForm : Form
    {
        public void RecreateHiddenHandle() => RecreateHandle();
    }
    private sealed record Sample(int Cycles, uint Gdi, uint User, int ProcessHandles, long PrivateBytes);
    private sealed record Scenario(string Name, bool Passed, IReadOnlyList<Sample> Samples);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetGuiResources(IntPtr process, uint flags);
}
