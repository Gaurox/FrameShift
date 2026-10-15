using System.Collections;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.Logging;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;
using Xunit;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class ImageToPdfMemoryTests
{
    [Fact]
    public void PruningSixtyEntryHistoryDisposesUnreachableSourcesAndSnapshots() => StaTest.Run(() =>
    {
        using var form = Create();
        var items = (IList)Field(form, "_items");
        var cache = (ImageToPdfBitmapCache)Field(form, "_bitmapCache");
        Bitmap? first = null;
        for (var i = 0; i < 65; i++)
        {
            var before = Invoke(form, "CaptureEditorHistoryState")!;
            var bitmap = new Bitmap(8, 8);
            if (i == 0) first = bitmap;
            Add(form, $"source été {i}.png", bitmap);
            Invoke(form, "CommitHistoryIfChanged", before);
            before = Invoke(form, "CaptureEditorHistoryState")!;
            items.Clear();
            Invoke(form, "CommitHistoryIfChanged", before);
        }
        Assert.Empty(items.Cast<object>());
        Assert.Equal(60, ((ICollection)Field(form, "_undoHistory")).Count);
        Assert.Equal(30, cache.Count);
        Assert.Equal(30, Directory.GetFiles(cache.TemporaryRoot).Length);
        AssertDisposed(first!);
        var root = cache.TemporaryRoot;
        form.Dispose();
        Assert.False(Directory.Exists(root));
    });

    [Fact]
    public void UndoAndRedoReloadEvictedSnapshotsAfterOriginalsDisappearAndClearRedoOnEdit() => StaTest.Run(() =>
    {
        using var form = Create();
        var cache = (ImageToPdfBitmapCache)Field(form, "_bitmapCache");
        var items = (IList)Field(form, "_items");
        var empty = Invoke(form, "CaptureEditorHistoryState")!;
        var bitmap = new Bitmap(8, 8);
        bitmap.SetPixel(2, 3, Color.FromArgb(42, 50, 80, 120));
        Add(form, "original disparu é.png", bitmap);
        Invoke(form, "CommitHistoryIfChanged", empty);
        var populated = Invoke(form, "CaptureEditorHistoryState")!;
        items.Clear();
        Invoke(form, "CommitHistoryIfChanged", populated);
        cache.Reserve(16384, 8192, []); // Evict cached history without allocating a large image.
        AssertDisposed(bitmap);
        Invoke(form, "UndoLastAction"); Pump((Task)Field(form, "_imageImportTask"));
        var restored = Assert.Single(items.Cast<object>());
        var image = (Bitmap)restored.GetType().GetProperty("Bitmap")!.GetValue(restored)!;
        Assert.Equal(Color.FromArgb(42, 50, 80, 120).ToArgb(), image.GetPixel(2, 3).ToArgb());
        Invoke(form, "RedoLastAction"); Pump((Task)Field(form, "_imageImportTask"));
        Assert.Empty(items.Cast<object>());
        Invoke(form, "UndoLastAction"); Pump((Task)Field(form, "_imageImportTask"));
        Invoke(form, "UndoLastAction"); Pump((Task)Field(form, "_imageImportTask"));
        Assert.Empty(items.Cast<object>());
        var before = Invoke(form, "CaptureEditorHistoryState")!;
        Add(form, "nouvelle source.png", new Bitmap(8, 8));
        Invoke(form, "CommitHistoryIfChanged", before);
        Assert.Empty(((IEnumerable)Field(form, "_redoHistory")).Cast<object>());
        Assert.Equal(1, cache.Count);
        AssertDisposed(image);
    });

    [Theory]
    [InlineData(0)] [InlineData(42)] [InlineData(255)]
    public void CacheSpillsAndRestoresLosslesslyWithinItsBudget(int alpha) => StaTest.Run(() =>
    {
        using var cache = new ImageToPdfBitmapCache(256);
        var first = new Bitmap(8, 8);
        first.SetPixel(0, 0, Color.FromArgb(alpha, 32, 64, 96));
        cache.Add("one", first, []);
        var second = new Bitmap(8, 8);
        cache.Add("two", second, []);
        AssertDisposed(first);
        Assert.Equal(256, cache.ResidentBytes);
        Pump(cache.LoadAsync(["one"], ["two"], CancellationToken.None));
        Assert.Equal(512, cache.ResidentBytes); // Bounded old/new transition.
        cache.RetainOnly(["one", "two"], ["one"]);
        Assert.Equal(256, cache.ResidentBytes);
        AssertDisposed(second);
        Assert.True(cache.TryGetValue("one", out var restored));
        Assert.Equal(Color.FromArgb(alpha, 32, 64, 96).ToArgb(), restored!.GetPixel(0, 0).ToArgb());
    });

    [Fact]
    public void BudgetRefusesAnOversizedActiveDocumentWithoutDisposingItsSources() => StaTest.Run(() =>
    {
        using var cache = new ImageToPdfBitmapCache(256);
        var first = new Bitmap(8, 8);
        cache.Add("one", first, []);
        using var second = new Bitmap(8, 8);
        Assert.Throws<InvalidOperationException>(() => cache.Add("two", second, ["one"]));
        Assert.Equal(1, cache.Count);
        Assert.Single(Directory.GetFiles(cache.TemporaryRoot));
        Assert.Equal(8, first.Width);
        Assert.Equal(8, second.Width);
        cache.RetainOnly([], []);
        AssertDisposed(first);
        Assert.Empty(Directory.GetFiles(cache.TemporaryRoot));
    });

    [Fact]
    public void CanceledSnapshotDoesNotTransferBitmapOwnershipOrLeaveFiles() => StaTest.Run(() =>
    {
        using var cache = new ImageToPdfBitmapCache();
        using var image = new Bitmap(8, 8);
        var task = cache.AddAsync("source", image, [], new CancellationToken(true));
        Assert.ThrowsAny<OperationCanceledException>(() => Pump(task));
        Assert.Equal(0, cache.Count);
        Assert.False(Directory.Exists(cache.TemporaryRoot));
        Assert.Equal(8, image.Width);
    });

    [Fact]
    public void MissingSnapshotDoesNotConsumeUndoOrChangeTheCurrentDocument() => StaTest.Run(() =>
    {
        using var form = Create();
        var cache = (ImageToPdfBitmapCache)Field(form, "_bitmapCache");
        var items = (IList)Field(form, "_items");
        Add(form, "source.png", new Bitmap(8, 8));
        var populated = Invoke(form, "CaptureEditorHistoryState")!;
        items.Clear();
        Invoke(form, "CommitHistoryIfChanged", populated);
        cache.Reserve(16384, 8192, []);
        File.Delete(Assert.Single(Directory.GetFiles(cache.TemporaryRoot)));
        Invoke(form, "UndoLastAction"); Pump((Task)Field(form, "_imageImportTask"));
        Assert.Empty(items.Cast<object>());
        Assert.Single(((IEnumerable)Field(form, "_undoHistory")).Cast<object>());
        Assert.Empty(((IEnumerable)Field(form, "_redoHistory")).Cast<object>());
        Assert.Equal(0, (int)Field(form, "_pendingImports"));
        Assert.NotEmpty(((TextBox)Field(form, "_loadStatus")).Text);
    });

    [Fact]
    public void SnapshotWriteFailureKeepsTheCallerBitmapAndDoesNotAddAnEntry() => StaTest.Run(() =>
    {
        using var cache = new ImageToPdfBitmapCache();
        using var bitmap = new Bitmap(8, 8);
        Directory.CreateDirectory(Path.GetDirectoryName(cache.TemporaryRoot)!);
        File.WriteAllText(cache.TemporaryRoot, "blocked directory fixture");
        try
        {
            Assert.ThrowsAny<IOException>(() => cache.Add("source", bitmap, []));
            Assert.Equal(0, cache.Count);
            Assert.Equal(8, bitmap.Width);
        }
        finally { File.Delete(cache.TemporaryRoot); }
    });

    [Fact]
    public void DisposalWaitsForPendingImportBeforeReleasingCacheAndTemporaryFiles() => StaTest.Run(() =>
    {
        using var form = Create();
        var cache = (ImageToPdfBitmapCache)Field(form, "_bitmapCache");
        var bitmap = new Bitmap(8, 8);
        Add(form, "existing.png", bitmap);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetField(form, "_imageImportTask", finish.Task);
        var queued = (Task)Invoke(form, "QueueImagesAsync", new[] { "absent.png" }, false)!;
        var root = cache.TemporaryRoot;
        form.Dispose();
        Assert.Equal(1, cache.Count);
        finish.SetResult();
        Pump(queued);
        PumpUntil(() => !Directory.Exists(root));
        AssertDisposed(bitmap);
        Assert.Equal(0, cache.Count);
    });

    private static ImageToPdfForm Create()
    {
        var form = new ImageToPdfForm("absent.png", "absent.exe", new FfmpegRunner(new AppLogger()));
        _ = form.Handle;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        return form;
    }
    private static void Add(ImageToPdfForm form, string path, Bitmap bitmap)
    {
        var args = new object?[] { path, bitmap, null, false, null, false };
        Assert.True((bool)typeof(ImageToPdfForm).GetMethod("TryAddBitmapItemInternal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args)!, args[4]?.ToString());
    }
    private static object Field(object root, string name) => root.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(root)!;
    private static void SetField(object root, string name, object value) => root.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(root, value);
    private static object? Invoke(object root, string name, params object[] args) => root.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(root, args);
    private static void AssertDisposed(Bitmap bitmap) => Assert.ThrowsAny<Exception>(() => bitmap.GetPixel(0, 0));
    private static void Pump(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void PumpUntil(Func<bool> done)
    {
        var context = SynchronizationContext.Current;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!done() && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            SynchronizationContext.SetSynchronizationContext(context);
            Thread.Sleep(1);
        }
        Assert.True(done(), "PDF work did not finish within the test timeout.");
    }
}
