using System;
using System.Collections.Generic;

namespace FrameShift.Core.Actions;

/// <summary>
/// Ordered, de-duplicated set of file paths backing the main window's queue.
/// Pure state (no filesystem, no WinForms) so it can be unit-tested; the panel
/// filters to existing files and normalizes paths before adding. De-duplication
/// is case-insensitive to match Windows path semantics.
/// </summary>
public sealed class FileQueueModel
{
    private readonly List<string> _items = new();
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Queued paths in insertion order.</summary>
    public IReadOnlyList<string> Items => _items;

    public int Count => _items.Count;

    /// <summary>Adds a path. Returns false when blank or already present.</summary>
    public bool Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!_seen.Add(path))
        {
            return false;
        }

        _items.Add(path);
        return true;
    }

    /// <summary>Adds several paths, returning how many were newly added.</summary>
    public int AddRange(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var added = 0;
        foreach (var path in paths)
        {
            if (Add(path))
            {
                added++;
            }
        }

        return added;
    }

    /// <summary>Removes a path (case-insensitive). Returns false when it was not present.</summary>
    public bool Remove(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !_seen.Remove(path))
        {
            return false;
        }

        for (var index = 0; index < _items.Count; index++)
        {
            if (string.Equals(_items[index], path, StringComparison.OrdinalIgnoreCase))
            {
                _items.RemoveAt(index);
                return true;
            }
        }

        return false;
    }

    public bool Contains(string path)
        => !string.IsNullOrWhiteSpace(path) && _seen.Contains(path);

    public void Clear()
    {
        _items.Clear();
        _seen.Clear();
    }

    /// <summary>Number of queued files per <see cref="MediaFamily"/> (only non-zero kinds).</summary>
    public IReadOnlyDictionary<MediaFamily, int> CountByKind()
    {
        var counts = new Dictionary<MediaFamily, int>();
        foreach (var path in _items)
        {
            var kind = MediaFileClassifier.Classify(path);
            counts[kind] = counts.TryGetValue(kind, out var current) ? current + 1 : 1;
        }

        return counts;
    }
}
