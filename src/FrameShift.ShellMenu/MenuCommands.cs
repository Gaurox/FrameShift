using System.Diagnostics;

namespace FrameShift.ShellMenu;

[Flags]
internal enum MediaFamily { None = 0, Video = 1, Audio = 2, Image = 4, Pdf = 8, Media = Video | Audio | Image }

internal sealed record MenuCommand(string Key, string Action, int Minimum = 1, int Maximum = int.MaxValue,
    string? FrameMode = null, MediaFamily Family = MediaFamily.Video, bool Ai = false, string? Model = null)
{
    internal string Verb => "frameshift." + Action + (FrameMode is null ? "" : "." + FrameMode) + (Model is null ? "" : "." + Model);
    internal string Group => Ai ? "FrameShiftAI" : "FrameShift";
    internal string RegistryPath => FrameMode is not null && FrameMode != "all"
        ? @"extract_specific_frames\shell\" + Key : Key;
    internal string CaptionValue => FrameMode is not null && FrameMode != "all" ? "FlatMUIVerb" : "MUIVerb";
    internal bool Accepts(string extension)
    {
        if ((Family & MenuCommands.FamilyOf(extension)) == 0) return false;
        // These actions support a narrower set than the legacy installer associations.
        return Action switch
        {
            "convert-audio" => extension.ToLowerInvariant() is not (".aac" or ".wma"),
            "separate-audio" => !extension.Equals(".wave", StringComparison.OrdinalIgnoreCase),
            "remove-noise" => extension.ToLowerInvariant() is ".wav" or ".flac" or ".mp3" or ".ogg" or ".m4a",
            _ => true
        };
    }
}

internal static class MenuCommands
{
    internal const string MetadataPath = @"Software\FrameShift\ShellMenus";
    internal const int SchemaVersion = 2;
    internal static readonly string[] VideoExtensions = [".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v"];
    internal static readonly string[] AudioExtensions = [".mp3", ".wav", ".wave", ".flac", ".m4a", ".ogg", ".aac", ".wma"];
    internal static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];
    internal static readonly string[] Extensions = [.. VideoExtensions, .. AudioExtensions, .. ImageExtensions, ".pdf"];
    internal static readonly MenuCommand[] All =
    [
        new("add_subtitles_video", "add-subtitles-video", Maximum: 1),
        new("change_video_speed", "change-video-speed", Maximum: 1),
        new("compress_video", "compress-video"), new("convert_video", "convert-video"),
        new("create_gif", "create-gif", Maximum: 1), new("crop_video", "crop-video", Maximum: 1),
        new("cut_video", "cut-video", Maximum: 1),
        new("extract_all_frames", "extract-frames", FrameMode: "all"), new("extract_audio", "extract-audio"),
        new("first_frame", "extract-frames", FrameMode: "first"),
        new("last_frame", "extract-frames", FrameMode: "last"),
        new("keyframes", "extract-frames", FrameMode: "keyframes"),
        new("interpolate_video", "interpolate-video", Maximum: 1), new("join_videos", "join-videos", Minimum: 2),
        new("media_info", "media-info", Maximum: 1, Family: MediaFamily.Media), new("remove_audio", "remove-audio"),
        new("resize_video", "resize-video", Maximum: 1), new("rotate_flip_video", "rotate-flip-video", Maximum: 1),

        new("change_audio_speed", "change-audio-speed", Maximum: 1, Family: MediaFamily.Audio),
        new("change_pitch", "change-pitch", Maximum: 1, Family: MediaFamily.Audio),
        new("compress_audio", "compress-audio", Family: MediaFamily.Audio),
        new("convert_audio", "convert-audio", Family: MediaFamily.Audio),
        new("cut_audio", "cut-audio", Maximum: 1, Family: MediaFamily.Audio),
        new("reverse_audio", "reverse-audio", Family: MediaFamily.Audio),

        new("compress_image", "compress-image", Family: MediaFamily.Image),
        new("convert_icon", "convert-to-icon", Maximum: 1, Family: MediaFamily.Image),
        new("convert_image", "convert-image", Family: MediaFamily.Image),
        new("crop_image", "crop-image", Maximum: 1, Family: MediaFamily.Image),
        new("image_to_pdf", "image-to-pdf", Family: MediaFamily.Image),
        new("resize_image", "resize-image", Maximum: 1, Family: MediaFamily.Image),
        new("rotate_flip_image", "rotate-flip-image", Maximum: 1, Family: MediaFamily.Image),

        new("create_subtitles_audio", "create-subtitles-audio", Family: MediaFamily.Audio, Ai: true),
        new("create_subtitles_video", "create-subtitles-video", Ai: true),
        new("extract_text", "extract-text", Family: MediaFamily.Image | MediaFamily.Pdf, Ai: true),
        new("interpolate_video_rife", "interpolate-video-rife", Maximum: 1, Ai: true),
        new("remove_background_bria_balanced", "remove-background", Family: MediaFamily.Image, Ai: true, Model: "bria-balanced"),
        new("remove_background_bria_high_quality", "remove-background", Family: MediaFamily.Image, Ai: true, Model: "bria-high-quality"),
        new("remove_background_fast", "remove-background", Family: MediaFamily.Image, Ai: true, Model: "fast"),
        new("remove_background_high_resolution", "remove-background", Family: MediaFamily.Image, Ai: true, Model: "high-resolution"),
        new("remove_background_high_resolution_general", "remove-background", Family: MediaFamily.Image, Ai: true, Model: "high-resolution-general"),
        new("remove_noise", "remove-noise", Family: MediaFamily.Audio, Ai: true),
        new("remove_noise_video", "remove-noise-video", Ai: true),
        new("remove_object", "remove-object", Maximum: 1, Family: MediaFamily.Image, Ai: true),
        new("separate_audio", "separate-audio", Family: MediaFamily.Audio, Ai: true),
        new("upscale_image", "upscale-image", Family: MediaFamily.Image, Ai: true),
        new("upscale_video", "upscale-video", Ai: true)
    ];

    internal static MediaFamily FamilyOf(string extension)
        => VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ? MediaFamily.Video
        : AudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ? MediaFamily.Audio
        : ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ? MediaFamily.Image
        : extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? MediaFamily.Pdf : MediaFamily.None;

    internal static bool IsSupportedSelection(IReadOnlyList<string> paths) => paths.Count is > 0 and <= 1000
        && paths.All(path => Path.IsPathFullyQualified(path) && !path.Contains('\0')
            && FamilyOf(Path.GetExtension(path)) != MediaFamily.None)
        && paths.All(path => FamilyOf(Path.GetExtension(path)) == FamilyOf(Path.GetExtension(paths[0])));

    internal static ProcessStartInfo BuildLaunch(string executable, MenuCommand command, IReadOnlyList<string> paths)
    {
        if (!IsSupportedSelection(paths) || paths.Any(path => !command.Accepts(Path.GetExtension(path)))
            || paths.Count < command.Minimum || paths.Count > command.Maximum)
            throw new ArgumentException("Selection is not supported by this command.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        start.ArgumentList.Add("--action");
        start.ArgumentList.Add(command.Action);
        if (command.FrameMode is not null) { start.ArgumentList.Add("--frame-mode"); start.ArgumentList.Add(command.FrameMode); }
        if (command.Model is not null) { start.ArgumentList.Add("--rmbg-model"); start.ArgumentList.Add(command.Model); }
        foreach (var path in paths) start.ArgumentList.Add(path);
        // Conservative upper bound after Windows quoting; never drop files to fit the command line.
        if (executable.Length + 3 + start.ArgumentList.Sum(argument => 2 * argument.Length + 3) >= 32767)
            throw new ArgumentException("Selection exceeds the Windows command-line limit.");
        return start;
    }
}
