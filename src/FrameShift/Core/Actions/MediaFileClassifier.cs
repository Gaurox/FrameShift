using System;
using System.Collections.Generic;
using System.IO;

namespace FrameShift.Core.Actions;

/// <summary>Broad media family of a file, used for queue grouping and type filtering.</summary>
public enum MediaFamily
{
    Video,
    Audio,
    Image,
    Other
}

/// <summary>
/// Classifies a file (by its extension) into a <see cref="MediaFamily"/>. The sets are the
/// broad per-family sets; they are intentionally coarser than the per-action extension
/// sets in <see cref="ActionCatalog"/> (a file is "Audio" even if a given audio action
/// supports a narrower subset). Consistency with the catalog is covered by tests.
/// </summary>
public static class MediaFileClassifier
{
    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v" };

    private static readonly HashSet<string> AudioExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".wave", ".flac", ".m4a", ".ogg", ".aac", ".wma" };

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

    /// <summary>Classifies a file path or a bare extension.</summary>
    public static MediaFamily Classify(string pathOrExtension)
    {
        if (string.IsNullOrWhiteSpace(pathOrExtension))
        {
            return MediaFamily.Other;
        }

        var extension = pathOrExtension.Contains('.') && !pathOrExtension.StartsWith('.')
            ? Path.GetExtension(pathOrExtension)
            : pathOrExtension;

        return ClassifyExtension(extension);
    }

    /// <summary>Classifies a bare extension (leading dot optional, case-insensitive).</summary>
    public static MediaFamily ClassifyExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return MediaFamily.Other;
        }

        var normalized = extension.Trim().ToLowerInvariant();
        if (!normalized.StartsWith('.'))
        {
            normalized = "." + normalized;
        }

        if (VideoExtensions.Contains(normalized))
        {
            return MediaFamily.Video;
        }

        if (AudioExtensions.Contains(normalized))
        {
            return MediaFamily.Audio;
        }

        if (ImageExtensions.Contains(normalized))
        {
            return MediaFamily.Image;
        }

        return MediaFamily.Other;
    }
}
