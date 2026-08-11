using System;
using System.Collections.Generic;
using System.Linq;

namespace FrameShift.Core.Actions;

/// <summary>
/// Broad file family an action operates on. Drives grouping and type filtering
/// in the main window. <see cref="ActionCategory.General"/> is for cross-type
/// actions (e.g. Media Info) that accept video, audio and image files.
/// </summary>
public enum ActionCategory
{
    Video,
    Audio,
    Image,
    General
}

/// <summary>
/// How many source files an action consumes per run.
/// <list type="bullet">
/// <item><see cref="Batch"/> — accepts N files, shared options (or none).</item>
/// <item><see cref="Single"/> — interactive editor, exactly one file.</item>
/// <item><see cref="Combine"/> — merges N files into a single output.</item>
/// </list>
/// </summary>
public enum ActionArity
{
    Batch,
    Single,
    Combine
}

/// <summary>
/// One selectable action in the main window. <see cref="Key"/> is unique across
/// the catalog; <see cref="ActionId"/> is the CLI <c>--action</c> id passed to a
/// child <c>FrameShift.exe</c> and may repeat (the Remove Background variants all
/// share <c>remove-background</c> and differ only by <see cref="ExtraCliArgs"/>).
/// </summary>
public sealed record ActionCatalogEntry(
    string Key,
    string ActionId,
    string DisplayName,
    ActionCategory Category,
    ActionArity Arity,
    IReadOnlyList<string> AcceptedExtensions,
    IReadOnlyList<string> ExtraCliArgs,
    int MinimumInputCount = 1)
{
    /// <summary>
    /// True when this action can operate on a file with the given extension.
    /// Case-insensitive; a leading dot is optional on the input.
    /// </summary>
    public bool Accepts(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        var normalized = extension.Trim().ToLowerInvariant();
        if (!normalized.StartsWith('.'))
        {
            normalized = "." + normalized;
        }

        foreach (var accepted in AcceptedExtensions)
        {
            if (string.Equals(accepted, normalized, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Single source of truth mapping every FrameShift action to the file types it
/// accepts, its category and its arity. The main window reads this to filter,
/// group, count and enable actions, and to build the child-process command line.
/// Extension sets mirror each action's real support in code (see cross-checks in
/// the tests), not the broader sets the installer registers context menus for.
/// </summary>
public static class ActionCatalog
{
    // Video containers accepted across all video actions.
    private static readonly string[] VideoExts = [".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v"];

    // Full audio set used by the generic FFmpeg audio actions and subtitle creation.
    private static readonly string[] AudioExtsFull = [".mp3", ".wav", ".wave", ".flac", ".m4a", ".ogg", ".aac", ".wma"];

    // Convert audio supports a narrower set (no .aac / .wma).
    private static readonly string[] AudioConvertExts = [".mp3", ".wav", ".wave", ".flac", ".m4a", ".ogg"];

    // Audio separation (HTDemucs) and noise removal (DeepFilterNet) each support their own subset.
    private static readonly string[] SeparateAudioExts = [".wav", ".mp3", ".flac", ".m4a", ".ogg", ".aac", ".wma"];
    private static readonly string[] RemoveNoiseAudioExts = [".wav", ".flac", ".mp3", ".ogg", ".m4a"];

    private static readonly string[] ImageExts = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    // Media Info reads any supported media type.
    private static readonly string[] MediaInfoExts =
        VideoExts.Concat(AudioExtsFull).Concat(ImageExts).Distinct(StringComparer.Ordinal).ToArray();

    private static readonly string[] NoArgs = [];

    private static ActionCatalogEntry Batch(string actionId, string displayName, ActionCategory category, string[] exts)
        => new(actionId, actionId, displayName, category, ActionArity.Batch, exts, NoArgs);

    private static ActionCatalogEntry Single(string actionId, string displayName, ActionCategory category, string[] exts)
        => new(actionId, actionId, displayName, category, ActionArity.Single, exts, NoArgs);

    private static ActionCatalogEntry Combine(string actionId, string displayName, ActionCategory category, string[] exts, int minimumInputCount)
        => new(actionId, actionId, displayName, category, ActionArity.Combine, exts, NoArgs, minimumInputCount);

    private static readonly ActionCatalogEntry[] EntriesArray =
    [
        // ---- Video ----
        Batch("convert-video", "Convert video", ActionCategory.Video, VideoExts),
        Batch("compress-video", "Compress video", ActionCategory.Video, VideoExts),
        Batch("extract-frames", "Extract all frames", ActionCategory.Video, VideoExts),
        Batch("extract-audio", "Extract audio", ActionCategory.Video, VideoExts),
        Batch("remove-audio", "Remove audio", ActionCategory.Video, VideoExts),
        Batch("remove-noise-video", "Remove noise (video)", ActionCategory.Video, VideoExts),
        Batch("upscale-video", "Upscale video", ActionCategory.Video, VideoExts),
        Batch("create-subtitles-video", "Create subtitle file", ActionCategory.Video, VideoExts),
        Combine("join-videos", "Join videos", ActionCategory.Video, VideoExts, minimumInputCount: 2),
        Single("create-gif", "Create GIF", ActionCategory.Video, VideoExts),
        Single("cut-video", "Cut video", ActionCategory.Video, VideoExts),
        Single("crop-video", "Crop video", ActionCategory.Video, VideoExts),
        Single("resize-video", "Resize video", ActionCategory.Video, VideoExts),
        Single("rotate-flip-video", "Rotate / flip video", ActionCategory.Video, VideoExts),
        Single("change-video-speed", "Change video speed", ActionCategory.Video, VideoExts),
        Single("interpolate-video", "Interpolate video", ActionCategory.Video, VideoExts),
        Single("interpolate-video-rife", "Interpolate video (RIFE)", ActionCategory.Video, VideoExts),
        Single("add-subtitles-video", "Add subtitles to video", ActionCategory.Video, VideoExts),

        // ---- Audio ----
        Batch("convert-audio", "Convert audio", ActionCategory.Audio, AudioConvertExts),
        Batch("compress-audio", "Compress audio", ActionCategory.Audio, AudioExtsFull),
        Batch("reverse-audio", "Reverse audio", ActionCategory.Audio, AudioExtsFull),
        Batch("separate-audio", "Separate audio", ActionCategory.Audio, SeparateAudioExts),
        Batch("remove-noise", "Remove noise", ActionCategory.Audio, RemoveNoiseAudioExts),
        Batch("create-subtitles-audio", "Create subtitle file", ActionCategory.Audio, AudioExtsFull),
        Single("cut-audio", "Cut audio", ActionCategory.Audio, AudioExtsFull),
        Single("change-pitch", "Change pitch", ActionCategory.Audio, AudioExtsFull),
        Single("change-audio-speed", "Change audio speed", ActionCategory.Audio, AudioExtsFull),

        // ---- Image ----
        Batch("convert-image", "Convert image", ActionCategory.Image, ImageExts),
        Batch("compress-image", "Compress image", ActionCategory.Image, ImageExts),
        Batch("upscale-image", "Upscale image", ActionCategory.Image, ImageExts),
        // Remove Background: same actionId, one entry per model variant (no in-app picker).
        new("remove-background-fast", "remove-background", "Remove background (Fast)",
            ActionCategory.Image, ActionArity.Batch, ImageExts, ["--rmbg-model", "fast"]),
        new("remove-background-high-resolution", "remove-background", "Remove background (High resolution matting)",
            ActionCategory.Image, ActionArity.Batch, ImageExts, ["--rmbg-model", "high-resolution"]),
        new("remove-background-high-resolution-general", "remove-background", "Remove background (High resolution general)",
            ActionCategory.Image, ActionArity.Batch, ImageExts, ["--rmbg-model", "high-resolution-general"]),
        new("remove-background-bria-balanced", "remove-background", "Remove background (BRIA Balanced)",
            ActionCategory.Image, ActionArity.Batch, ImageExts, ["--rmbg-model", "bria-balanced"]),
        new("remove-background-bria-high-quality", "remove-background", "Remove background (BRIA High Quality)",
            ActionCategory.Image, ActionArity.Batch, ImageExts, ["--rmbg-model", "bria-high-quality"]),
        Single("crop-image", "Crop image", ActionCategory.Image, ImageExts),
        Single("resize-image", "Resize image", ActionCategory.Image, ImageExts),
        Single("rotate-flip-image", "Rotate / flip image", ActionCategory.Image, ImageExts),
        Single("convert-to-icon", "Convert to icon", ActionCategory.Image, ImageExts),
        Single("remove-object", "Remove object", ActionCategory.Image, ImageExts),
        Combine("image-to-pdf", "Image to PDF", ActionCategory.Image, ImageExts, minimumInputCount: 1),

        // ---- General (cross-type) ----
        Single("media-info", "Media info", ActionCategory.General, MediaInfoExts),
    ];

    /// <summary>All catalog entries, in declaration order.</summary>
    public static IReadOnlyList<ActionCatalogEntry> Entries => EntriesArray;

    /// <summary>Finds an entry by its unique <see cref="ActionCatalogEntry.Key"/>.</summary>
    public static bool TryGet(string key, out ActionCatalogEntry entry)
    {
        foreach (var candidate in EntriesArray)
        {
            if (string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                entry = candidate;
                return true;
            }
        }

        entry = null!;
        return false;
    }

    /// <summary>All entries that accept a file with the given extension.</summary>
    public static IReadOnlyList<ActionCatalogEntry> ForExtension(string extension)
        => EntriesArray.Where(entry => entry.Accepts(extension)).ToArray();
}
