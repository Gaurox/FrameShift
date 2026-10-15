namespace FrameShift.Core.Actions;

internal static class FileQueuePathExpander
{
    // Match the hub's existing policy: immediate files only, all extensions.
    // Directory iterators and existence checks are consumed on a worker thread.
    internal static IEnumerable<string> Enumerate(
        IEnumerable<string> paths, CancellationToken token, Action<string>? reportError = null,
        Func<string, IEnumerable<string>>? enumerateDirectory = null)
    {
        foreach (var rawPath in paths)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(rawPath)) continue;
            string path;
            try { path = Path.GetFullPath(rawPath); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { reportError?.Invoke($"Cannot read path: {ex.Message}"); continue; }

            IEnumerable<string> candidates;
            try
            {
                if (Directory.Exists(path))
                    candidates = enumerateDirectory is null
                        ? Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
                        : enumerateDirectory(path);
                else if (File.Exists(path))
                    candidates = [path];
                else
                { reportError?.Invoke($"Path is missing or inaccessible: {path}"); continue; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { reportError?.Invoke($"Cannot read path '{path}': {ex.Message}"); continue; }

            using var iterator = GetEnumerator(candidates, path, reportError);
            if (iterator is null) continue;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                string file;
                try
                {
                    if (!iterator.MoveNext()) break;
                    file = iterator.Current;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { reportError?.Invoke($"Cannot read folder '{path}': {ex.Message}"); break; }
                // Enumeration can include a vanished entry or a broken file
                // link. Keep the same existing-file filter as AddFiles.
                if (File.Exists(file)) yield return file;
            }
        }
    }

    private static IEnumerator<string>? GetEnumerator(IEnumerable<string> candidates, string path, Action<string>? reportError)
    {
        try { return candidates.GetEnumerator(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { reportError?.Invoke($"Cannot read folder '{path}': {ex.Message}"); return null; }
    }
}
