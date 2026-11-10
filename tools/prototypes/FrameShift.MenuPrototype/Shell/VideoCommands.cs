namespace FrameShift.MenuPrototype;

internal sealed record MenuCommand(string Caption, string Verb);

internal static class VideoCommands
{
    // Snapshot of the installed non-AI video menu, with the three extraction modes flattened.
    // Verbs are identifiers only. This prototype has no execution path to FrameShift or FFmpeg.
    internal static readonly MenuCommand[] All =
    [
        new("Add subtitles to video", "frameshift.add-subtitles-video"),
        new("Change video speed", "frameshift.change-video-speed"),
        new("Compress video", "frameshift.compress-video"),
        new("Convert video", "frameshift.convert-video"),
        new("Create GIF", "frameshift.create-gif"),
        new("Crop video", "frameshift.crop-video"),
        new("Cut video", "frameshift.cut-video"),
        new("Extract all frames", "frameshift.extract-frames.all"),
        new("Extract audio", "frameshift.extract-audio"),
        new("Extract first frame", "frameshift.extract-frames.first"),
        new("Extract last frame", "frameshift.extract-frames.last"),
        new("Extract keyframes", "frameshift.extract-frames.keyframes"),
        new("Interpolate video", "frameshift.interpolate-video"),
        new("Join videos", "frameshift.join-videos"),
        new("Media Info", "frameshift.media-info"),
        new("Remove audio", "frameshift.remove-audio"),
        new("Resize video", "frameshift.resize-video"),
        new("Rotate / Flip video", "frameshift.rotate-flip-video")
    ];
}
