using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Windows.Forms;
using Xunit;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class FileQueueBatchTests
{
    [Theory]
    [InlineData(300, false)] [InlineData(1000, false)]
    [InlineData(300, true)] [InlineData(1000, true)]
    public void BulkAddAndDeletePublishOnlyTheFinalState(int count, bool partial) => StaTest.Run(() =>
    {
        using var files = new QueueFiles(count);
        using var panel = new FileQueuePanel();
        Handles(panel);
        var grid = Grid(panel);
        var changes = 0; var selections = 0;
        panel.QueueChanged += (_, _) =>
        {
            changes++;
            Assert.Equal(panel.Items, grid.Rows.Cast<DataGridViewRow>().Select(row => (string)row.Tag!));
        };
        panel.SelectionChanged += (_, _) => selections++;
        panel.AddFiles(files.Paths);
        Assert.Equal(1, changes); Assert.Equal(0, selections);
        if (partial) for (var i = 0; i < count; i += 2) grid.Rows[i].Selected = true;
        else grid.SelectAll();
        changes = selections = 0;
        var watch = Stopwatch.StartNew();
        var args = new KeyEventArgs(Keys.Delete);
        InvokeGrid(grid, "OnKeyDown", args);
        watch.Stop();
        Assert.True(args.Handled && args.SuppressKeyPress);
        Assert.Equal(1, changes); Assert.Equal(0, selections);
        Assert.Equal(partial ? files.Paths.Where((_, i) => i % 2 != 0).ToArray() : Array.Empty<string>(), panel.Items);
        Assert.Empty(panel.SelectedPaths);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Batch delete took {watch.Elapsed}.");
        panel.Clear();
        Assert.Empty(panel.Items);
    });

    [Fact]
    public void RemoveButtonPreservesSurvivingSelectionAndClearNotifiesOnce() => StaTest.Run(() =>
    {
        using var files = new QueueFiles(4);
        using var panel = new FileQueuePanel(); Handles(panel); panel.AddFiles(files.Paths);
        var grid = Grid(panel);
        grid.Rows[0].Selected = grid.Rows[2].Selected = true;
        var changes = 0; var selections = 0;
        panel.QueueChanged += (_, _) => changes++;
        panel.SelectionChanged += (_, _) => selections++;
        InvokeGrid(grid, "OnCellContentClick", new DataGridViewCellEventArgs(2, 2));
        Assert.Equal(new[] { files.Paths[0] }, panel.SelectedPaths);
        Assert.Equal(new[] { files.Paths[0], files.Paths[1], files.Paths[3] }, panel.Items);
        Assert.Equal(1, changes); Assert.Equal(0, selections);
        panel.Clear(); panel.Clear();
        Assert.Equal(2, changes); Assert.Equal(0, selections);
    });

    [Fact]
    public void MainActionsRebuildOnceForABulkRemovalAndUseTheSurvivingQueue() => StaTest.Run(() =>
    {
        using var files = new QueueFiles(300);
        using var form = new MainForm(); Handles(form);
        var queue = Descendants(form).OfType<FileQueuePanel>().Single();
        queue.AddFiles(files.Paths);
        var grid = Grid(queue);
        grid.SelectAll();
        var actions = Descendants(form).OfType<ActionsPanel>().Single();
        var content = (Control)Field(actions, "_content");
        var oldCount = content.Controls.Count;
        var removedControls = 0;
        content.ControlRemoved += (_, _) => removedControls++;
        InvokeGrid(grid, "OnKeyDown", new KeyEventArgs(Keys.Delete));
        Assert.Equal(oldCount, removedControls);
        Assert.Empty(content.Controls.Cast<Control>());
        queue.AddFiles(files.Paths.Take(2));
        Assert.Equal(files.Paths.Take(2), (IReadOnlyList<string>)Field(actions, "_currentScope"));
    });

    [Theory]
    [InlineData("window")] [InlineData("panel")] [InlineData("grid")]
    public void FolderDropHasTheSameShallowContentsOnEverySurface(string surface) => StaTest.Run(() =>
    {
        using var files = new QueueFiles(3);
        var nested = Directory.CreateDirectory(Path.Combine(files.Root, "sous dossier")).FullName;
        File.WriteAllText(Path.Combine(nested, "nested.png"), "");
        using var form = new MainForm(); Handles(form);
        var queue = Descendants(form).OfType<FileQueuePanel>().Single();
        var target = surface == "window" ? (Control)form : surface == "panel" ? queue : Grid(queue);
        var data = new DataObject(DataFormats.FileDrop, new[] { files.Root });
        var drop = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy);
        typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, [drop]);
        Pump(queue.PathImportTask);
        Assert.Equal(files.Paths.Order(), queue.Items.Order());
        Assert.Empty(queue.SelectedPaths);
    });

    [Fact]
    public void FolderImportPublishesBoundedBatchesAndYieldsToTheUi() => StaTest.Run(() =>
    {
        using var files = new QueueFiles(1000);
        using var panel = new FileQueuePanel(); Handles(panel);
        var lastCount = 0; var batches = 0;
        var uiThread = Environment.CurrentManagedThreadId;
        panel.QueueChanged += (_, _) =>
        {
            Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
            Assert.InRange(panel.Items.Count - lastCount, 1, FileQueuePanel.ImportBatchSize);
            lastCount = panel.Items.Count; batches++;
        };
        var task = panel.AddPathsAsync([files.Root]);
        var responsive = false;
        SynchronizationContext.Current!.Post(_ => responsive = !task.IsCompleted, null);
        Pump(task);
        Assert.True(responsive);
        Assert.Equal(1000, task.Result);
        Assert.Equal(4, batches);
        Assert.Equal(files.Paths.Order(), panel.Items.Order());
    });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CancelOrClearStopsAfterTheCurrentBatchAndANewImportCanStart(bool clear) => StaTest.Run(() =>
    {
        using var files = new QueueFiles(1000);
        using var panel = new FileQueuePanel(); Handles(panel);
        var cancel = true;
        panel.QueueChanged += (_, _) =>
        {
            if (!cancel) return;
            cancel = false;
            if (clear) panel.Clear(); else panel.CancelPathImports();
        };
        Pump(panel.AddPathsAsync([files.Root]));
        Assert.Equal(clear ? 0 : FileQueuePanel.ImportBatchSize, panel.Items.Count);
        Pump(panel.AddPathsAsync([files.Root, files.Paths[0]]));
        Assert.Equal(1000, panel.Items.Count);
    });

    [Fact]
    public void PreCanceledImportAndDisposalDoNotAddLateRows() => StaTest.Run(() =>
    {
        using var files = new QueueFiles(1000);
        using var panel = new FileQueuePanel(); Handles(panel);
        var canceled = panel.AddPathsAsync([files.Root], new CancellationToken(true));
        Pump(canceled); Assert.Equal(0, canceled.Result); Assert.Empty(panel.Items);
        var pending = panel.AddPathsAsync([files.Root]);
        panel.Dispose();
        var count = panel.Items.Count;
        Pump(pending);
        Assert.Equal(count, panel.Items.Count);
        Assert.Empty(((System.Collections.IEnumerable)Field(panel, "_imports")).Cast<object>());
    });

    [Fact]
    public void ConcurrentDropsAreSerializedAndDeduplicated() => StaTest.Run(() =>
    {
        using var files = new QueueFiles(300);
        using var panel = new FileQueuePanel(); Handles(panel);
        var first = panel.AddPathsAsync([files.Root]);
        var second = panel.AddPathsAsync(files.Paths.Reverse());
        Pump(second); Pump(first);
        Assert.Equal(300, first.Result); Assert.Equal(0, second.Result);
        Assert.Equal(files.Paths.Order(), panel.Items.Order());
    });

    [Fact]
    public void MainClosingWaitsForPendingEnumerationAndRejectsNewImports() => StaTest.Run(() =>
    {
        using var form = new MainForm(); Handles(form);
        var queue = Descendants(form).OfType<FileQueuePanel>().Single();
        var finish = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(FileQueuePanel).GetField("_pathImportTask", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(queue, finish.Task);
        var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
        typeof(Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [closing]);
        Assert.True(closing.Cancel); Assert.False(form.IsDisposed);
        Assert.Equal(0, queue.AddPathsAsync(["absent.mp4"]).Result);
        finish.SetResult(0);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!form.IsDisposed && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
        Assert.True(form.IsDisposed);
    });

    [Fact]
    public void EmptyFolderShowsImportStatusImmediatelyAndClearResetsIt() => StaTest.Run(() =>
    {
        using var files = new QueueFiles(0);
        using var panel = new FileQueuePanel(); Handles(panel);
        var updates = 0;
        panel.ImportStateChanged += (_, _) => updates++;
        var task = panel.AddPathsAsync([files.Root]);
        Assert.True(panel.HasImportStatus);
        Pump(task);
        Assert.Equal("0 files added.", ((Label)Field(panel, "_importStatus")).Text);
        Assert.Equal(2, updates);
        panel.Clear();
        Assert.False(panel.HasImportStatus);
        Assert.Equal("", ((Label)Field(panel, "_importStatus")).Text);
    });

    [Fact]
    public void CancelPendingImportsAlsoCancelsAnAlreadyCanceledQueuedRequest() => StaTest.Run(() =>
    {
        using var files = new QueueFiles(1000);
        using var panel = new FileQueuePanel(); Handles(panel);
        var first = panel.AddPathsAsync([files.Root]);
        var second = panel.AddPathsAsync([files.Root], new CancellationToken(true));
        panel.CancelPathImports();
        Pump(second); Pump(first);
        Assert.Empty(((System.Collections.IEnumerable)Field(panel, "_imports")).Cast<object>());
        Assert.True(panel.Items.Count <= FileQueuePanel.ImportBatchSize);
    });

    private static void Handles(Control root)
    {
        CreateHandles(root);
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
    }
    private static void CreateHandles(Control root)
    {
        _ = root.Handle;
        foreach (Control child in root.Controls) CreateHandles(child);
    }
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static DataGridView Grid(FileQueuePanel panel) => (DataGridView)Field(panel, "_grid");
    private static object Field(object root, string name) => root.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(root)!;
    private static void InvokeGrid(DataGridView grid, string method, object args) => typeof(DataGridView).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grid, [args]);
    private static void Pump(Task task)
    {
        var context = SynchronizationContext.Current;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Application.DoEvents(); SynchronizationContext.SetSynchronizationContext(context); Thread.Sleep(1);
        }
        Assert.True(task.IsCompleted); task.GetAwaiter().GetResult();
    }

    private sealed class QueueFiles : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"FrameShift file queue été {Guid.NewGuid():N}");
        public string[] Paths { get; }
        public QueueFiles(int count)
        {
            Directory.CreateDirectory(Root);
            Paths = Enumerable.Range(0, count).Select(i => Path.Combine(Root, $"vidéo {i:D4}.mp4")).ToArray();
            foreach (var path in Paths) File.WriteAllText(path, "");
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
