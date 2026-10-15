using System.Drawing;
using System.Drawing.Imaging;

namespace FrameShift.Windows.Helpers;

// UI-thread ownership. Only PNG encoding/decoding runs in the background.
// Snapshots keep Undo independent of the lifetime of the original files.
internal sealed class ImageToPdfBitmapCache : IDisposable
{
    internal const long DefaultBudgetBytes = 512L * 1024 * 1024;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly long _budget;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FrameShiftPdfCache", Guid.NewGuid().ToString("N"));
    internal string TemporaryRoot => _root;
    internal int Count => _entries.Count;
    internal long ResidentBytes => _entries.Values.Where(e => e.Bitmap is not null).Sum(e => e.Bytes);

    internal ImageToPdfBitmapCache(long budgetBytes = DefaultBudgetBytes) => _budget = budgetBytes;
    internal bool Contains(string path) => _entries.ContainsKey(path);
    internal bool TryGetValue(string path, out Bitmap? bitmap)
    {
        bitmap = _entries.TryGetValue(path, out var entry) ? entry.Bitmap : null;
        return bitmap is not null;
    }

    internal void Reserve(int width, int height, IEnumerable<string> activePaths)
        => MakeRoom(checked((long)width * height * 4), activePaths, _budget);

    internal async Task AddAsync(string path, Bitmap bitmap, IEnumerable<string> activePaths, CancellationToken token)
    {
        Reserve(bitmap.Width, bitmap.Height, activePaths);
        var snapshot = await Task.Run(() => SaveSnapshot(bitmap, token), token);
        try
        {
            token.ThrowIfCancellationRequested();
            _entries.Add(path, new Entry(bitmap, snapshot));
        }
        catch { File.Delete(snapshot); throw; }
    }

    internal void Add(string path, Bitmap bitmap, IEnumerable<string> activePaths)
    {
        if (_entries.ContainsKey(path)) return;
        Reserve(bitmap.Width, bitmap.Height, activePaths);
        var snapshot = SaveSnapshot(bitmap, CancellationToken.None);
        try { _entries.Add(path, new Entry(bitmap, snapshot)); }
        catch { File.Delete(snapshot); throw; }
    }

    internal async Task LoadAsync(IEnumerable<string> paths, IEnumerable<string> currentPaths, CancellationToken token)
    {
        var targets = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (targets.Sum(path => _entries[path].Bytes) > _budget)
            throw new InvalidOperationException("The PDF document exceeds the 512 MiB image memory budget. Remove an image before adding more.");
        var pinned = targets.Concat(currentPaths).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in targets)
        {
            token.ThrowIfCancellationRequested();
            var entry = _entries[path];
            if (entry.Bitmap is not null) continue;
            // Atomic history transitions may temporarily hold the old and new
            // document together (at most two budgets); prune after the swap.
            MakeRoom(entry.Bytes, pinned, checked(_budget * 2));
            var bitmap = await Task.Run(() =>
            {
                using var image = (Bitmap)Image.FromFile(entry.Snapshot);
                // Clone pixels directly: new Bitmap(Image) draws through GDI+
                // and rounds RGB at semi-transparent edges.
                return image.Clone(new Rectangle(0, 0, image.Width, image.Height), PixelFormat.Format32bppArgb);
            }, token);
            if (token.IsCancellationRequested) { bitmap.Dispose(); token.ThrowIfCancellationRequested(); }
            entry.Bitmap = bitmap;
        }
    }

    internal void RetainOnly(IEnumerable<string> reachablePaths, IEnumerable<string> activePaths)
    {
        var reachable = reachablePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _entries.Keys.Where(path => !reachable.Contains(path)).ToArray())
        {
            var entry = _entries[path];
            entry.Bitmap?.Dispose();
            TryDelete(entry.Snapshot);
            _entries.Remove(path);
        }
        MakeRoom(0, activePaths, _budget);
    }

    private void MakeRoom(long additionalBytes, IEnumerable<string> pinnedPaths, long limit)
    {
        var pinned = pinnedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pinnedBytes = _entries.Where(pair => pinned.Contains(pair.Key) && pair.Value.Bitmap is not null).Sum(pair => pair.Value.Bytes);
        if (additionalBytes > limit - pinnedBytes)
            throw new InvalidOperationException("The PDF document exceeds the 512 MiB image memory budget. Remove an image before adding more.");
        var resident = ResidentBytes;
        foreach (var pair in _entries)
        {
            if (resident + additionalBytes <= limit) break;
            if (pinned.Contains(pair.Key) || pair.Value.Bitmap is null) continue;
            pair.Value.Bitmap.Dispose();
            pair.Value.Bitmap = null;
            resident -= pair.Value.Bytes;
        }
    }

    private string SaveSnapshot(Bitmap bitmap, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.png");
        try
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                bitmap.Save(stream, ImageFormat.Png);
            token.ThrowIfCancellationRequested();
            return path;
        }
        catch { TryDelete(path); throw; }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
        {
            entry.Bitmap?.Dispose();
            TryDelete(entry.Snapshot);
        }
        _entries.Clear();
        try { if (Directory.Exists(_root)) Directory.Delete(_root); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class Entry(Bitmap bitmap, string snapshot)
    {
        internal Bitmap? Bitmap = bitmap;
        internal readonly string Snapshot = snapshot;
        internal readonly long Bytes = (long)bitmap.Width * bitmap.Height * 4;
    }
}
