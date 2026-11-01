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
        Batch("convert-video", L.Get("ActionCatalog_Convert_video_e9dc486e"), ActionCategory.Video, VideoExts),
        Batch("compress-video", L.Get("ActionCatalog_Compress_video_8492a883"), ActionCategory.Video, VideoExts),
        Batch("extract-frames", L.Get("ActionCatalog_Extract_all_frames_06fecba4"), ActionCategory.Video, VideoExts),
        Batch("extract-audio", L.Get("ActionCatalog_Extract_audio_b37b0fc1"), ActionCategory.Video, VideoExts),
        Batch("remove-audio", L.Get("ActionCatalog_Remove_audio_6542f155"), ActionCategory.Video, VideoExts),
        Batch("remove-noise-video", L.Get("ActionCatalog_Remove_noise_video_f4d7d4fe"), ActionCategory.Video, VideoExts),
        Batch("upscale-video", L.Get("ActionCatalog_Upscale_video_f43b0a95"), ActionCategory.Video, VideoExts),
        Batch("create-subtitles-video", L.Get("ActionCatalog_Create_subtitle_file_6cc78186"), ActionCategory.Video, VideoExts),
        Combine("join-videos", L.Get("ActionCatalog_Join_videos_ad3db798"), ActionCategory.Video, VideoExts, minimumInputCount: 2),
        Single("create-gif", L.Get("ActionCatalog_Create_GIF_456a6ed8"), ActionCategory.Video, VideoExts),
        Single("cut-video", L.Get("ActionCatalog_Cut_video_6a4b7479"), ActionCategory.Video, VideoExts),
        Single("crop-video", L.Get("ActionCatalog_Crop_video_76372945"), ActionCategory.Video, VideoExts),
        Single("resize-video", L.Get("ActionCatalog_Resize_video_c5082574"), ActionCategory.Video, VideoExts),
        Single("rotate-flip-video", L.Get("ActionCatalog_Rotate_flip_video_771b5e28"), ActionCategory.Video, VideoExts),
        Single("change-video-speed", L.Get("ActionCatalog_Change_video_speed_5d7c4927"), ActionCategory.Video, VideoExts),
        Single("interpolate-video", L.Get("ActionCatalog_Interpolate_video_883eaee0"), ActionCategory.Video, VideoExts),
        Single("interpolate-video-rife", L.Get("ActionCatalog_Interpolate_video_RIFE_b207590f"), ActionCategory.Video, VideoExts),
        Single("add-subtitles-video", L.Get("ActionCatalog_Add_subtitles_to_video_fcb5e679"), ActionCategory.Video, VideoExts),

        // ---- Audio ----
        Batch("convert-audio", L.Get("ActionCatalog_Convert_audio_76959d23"), ActionCategory.Audio, AudioConvertExts),
        Batch("compress-audio", L.Get("ActionCatalog_Compress_audio_4ae3c038"), ActionCategory.Audio, AudioExtsFull),
        Batch("reverse-audio", L.Get("ActionCatalog_Reverse_audio_4cc0eba0"), ActionCategory.Audio, AudioExtsFull),
        Batch("separate-audio", L.Get("ActionCatalog_Separate_audio_246fe0aa"), ActionCategory.Audio, SeparateAudioExts),
        Batch("remove-noise", L.Get("ActionCatalog_Remove_noise_214da7d4"), ActionCategory.Audio, RemoveNoiseAudioExts),
        Batch("create-subtitles-audio", L.Get("ActionCatalog_Create_subtitle_file_6cc78186"), ActionCategory.Audio, AudioExtsFull),
        Single("cut-audio", L.Get("ActionCatalog_Cut_audio_1eea5b2a"), ActionCategory.Audio, AudioExtsFull),
        Single("change-pitch", L.Get("ActionCatalog_Change_pitch_f1486341"), ActionCategory.Audio, AudioExtsFull),
        Single("change-audio-speed", L.Get("ActionCatalog_Change_audio_speed_8d833ea8"), ActionCategory.Audio, AudioExtsFull),

        // ---- Image ----
        Batch("convert-image", L.Get("ActionCatalog_Convert_image_4949df1e"), ActionCategory.Image, ImageExts),
        Batch("compress-image", L.Get("ActionCatalog_Compress_image_01301a8f"), ActionCategory.Image, ImageExts),
        Batch("upscale-image", L.Get("ActionCatalog_Upscale_image_6b361bda"), ActionCategory.Image, ImageExts),
        // Remove Background: same actionId, one entry per model variant (no in-app picker).
        new("remove-background-fast", "remove-background", L.Get("ActionCatalog_Remove_background_Fast_03439e4f"),
            ActionCategory.Image, ActionArity.Batch, ImageExts, ["--rmbg-model", "fast"]),
        new("remove-background-high-resolution", "remove-background", L.Get("ActionCatalog_Remove_background_High_resolution_ma_e7d211ef"),
            ActionCategory.Image, ActionArity.Batch, ImageExts, ["--rmbg-model", "high-resolution"]),
        new("remove-background-high-resolution-general", "remove-background", L.Get("ActionCatalog_Remove_background_High_resolution_ge_5bcbdee3"),
            ActionCategory.Image, ActionArity.Batch, ImageExts, ["--rmbg-model", "high-resolution-general"]),
        new("remove-background-bria-balanced", "remove-background", L.Get("ActionCatalog_Remove_background_BRIA_Balanced_56e0eb2b"),
            ActionCategory.Image, ActionArity.Batch, ImageExts, ["--rmbg-model", "bria-balanced"]),
        new("remove-background-bria-high-quality", "remove-background", L.Get("ActionCatalog_Remove_background_BRIA_High_Quality_f8554332"),
            ActionCategory.Image, ActionArity.Batch, ImageExts, ["--rmbg-model", "bria-high-quality"]),
        Single("crop-image", L.Get("ActionCatalog_Crop_image_fc109567"), ActionCategory.Image, ImageExts),
        Single("resize-image", L.Get("ActionCatalog_Resize_image_0f020b55"), ActionCategory.Image, ImageExts),
        Single("rotate-flip-image", L.Get("ActionCatalog_Rotate_flip_image_fd75f467"), ActionCategory.Image, ImageExts),
        Single("convert-to-icon", L.Get("ActionCatalog_Convert_to_icon_520117bd"), ActionCategory.Image, ImageExts),
        Single("remove-object", L.Get("ActionCatalog_Remove_object_907b36d9"), ActionCategory.Image, ImageExts),
        Combine("image-to-pdf", L.Get("ActionCatalog_Image_to_PDF_c9e73f3d"), ActionCategory.Image, ImageExts, minimumInputCount: 1),

        // ---- General (cross-type) ----
        Batch("extract-text", L.Get("ActionCatalog_Extract_text_4fc6b30b"), ActionCategory.General, [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".pdf"]),
        Single("media-info", L.Get("ActionCatalog_Media_info_a740c022"), ActionCategory.General, MediaInfoExts),
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
