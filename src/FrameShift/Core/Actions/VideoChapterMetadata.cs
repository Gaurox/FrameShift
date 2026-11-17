using System.Text;
using FrameShift.Core.FFprobe;

namespace FrameShift.Core.Actions;

internal static class VideoChapterMetadata
{
    internal static async Task<string?> WriteAsync(string workspace, IReadOnlyList<MediaChapter> chapters,
        double startSeconds, double endSeconds, double speedFactor, CancellationToken token)
    {
        var metadata = new StringBuilder(";FFMETADATA1\n");
        var headerLength = metadata.Length;
        foreach (var chapter in chapters)
        {
            var start = (long)Math.Round((Math.Max(chapter.Start, startSeconds) - startSeconds) / speedFactor * 1_000_000);
            var end = (long)Math.Round((Math.Min(chapter.End, endSeconds) - startSeconds) / speedFactor * 1_000_000);
            if (end <= start) continue;

            metadata.Append(FormattableString.Invariant($"[CHAPTER]\nTIMEBASE=1/1000000\nSTART={start}\nEND={end}\n"));
            foreach (var (key, value) in chapter.Tags)
                metadata.Append(Escape(key)).Append('=').Append(Escape(value)).Append('\n');
        }

        if (metadata.Length == headerLength) return null;
        var path = Path.Combine(workspace, "chapters.ffmetadata");
        await File.WriteAllTextAsync(path, metadata.ToString(), token).ConfigureAwait(false);
        return path;
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("=", "\\=", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal)
        .Replace("#", "\\#", StringComparison.Ordinal).ReplaceLineEndings("\n")
        .Replace("\n", "\\\n", StringComparison.Ordinal);
}
