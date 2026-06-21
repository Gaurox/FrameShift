using System;
using System.IO;

namespace FrameShift.Windows.Helpers;

public static class IconPaths
{
    private static readonly string IconsRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons");

    public static string AppIcon => Combine("app", "app.ico");
    public static string AppPng => Combine("app", "app.png");
    public static string FrameShiftAiIcon => Combine("ai", "frameshift_ai.ico");
    public static string RemoveBackgroundAiIcon => Combine("ai", "remove_background.ico");
    public static string SeparateAudioAiIcon => Combine("ai", "separate_audio_icon.ico");
    public static string RemoveNoiseAiIcon => Combine("ai", "remove_noise_icon.ico");
    public static string InterpolateAiIcon => Combine("ai", "Interpolate_icon.ico");
    public static string RemoveObjectAiIcon => Combine("ai", "remove_object.ico");
    public static string UpscaleImageAiIcon => Combine("ai", "upscale_image.ico");
    public static string UpscaleImageAiPng => Combine("ai", "upscale_image.png");
    public static string UpscaleVideoAiIcon => Combine("ai", "upscale_video.ico");
    public static string UpscaleVideoAiPng => Combine("ai", "upscale_video.png");
    public static string CompressVideoIcon => Combine("menus", "context", "ico", "compress-video-image-audio-icon.ico");
    public static string ConvertVideoIcon => Combine("menus", "context", "ico", "convert-audio-video-image-icon.ico");
    public static string MediaInfoIcon => Combine("menus", "context", "ico", "media-info-video-image-audio-icon.ico");
    public static string ResizeImageVideoIcon => Combine("menus", "context", "ico", "resize-image-video-icon.ico");

    public static string ContextMenuIco(string fileName) => Combine("menus", "context", "ico", fileName);
    public static string ContextMenuPng(string fileName) => Combine("menus", "context", "png", fileName);

    public static string ImageToPdfIco(string fileName) => Combine("ui", "image-to-pdf", "ico", fileName);

    public static string RotateFlipIco(string fileName) => Combine("ui", "rotate-flip", "ico", fileName);
    public static string RotateFlipPng(string fileName) => Combine("ui", "rotate-flip", "png", fileName);
    public static string RotateFlipPreviewPng(string fileName) => Combine("ui", "rotate-flip", "preview", "png", fileName);

    private static string Combine(params string[] parts)
    {
        return Path.Combine([IconsRoot, .. parts]);
    }
}
