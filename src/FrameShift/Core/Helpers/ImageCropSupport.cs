using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrameShift.Core.Actions;

namespace FrameShift.Core.Helpers;

public static class ImageCropSupport
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".bmp"
    };

    public static bool IsSupportedExtension(string? extension)
    {
        return !string.IsNullOrWhiteSpace(extension) && SupportedExtensions.Contains(extension);
    }

    public static string GetSupportedExtensionsText()
    {
        return ".png, .jpg, .jpeg, .webp and .bmp";
    }

    public static IReadOnlyList<string> GetOutputArguments(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ["-q:v", "2"],
            ".webp" => ["-c:v", "libwebp", "-quality", "90", "-compression_level", "4"],
            ".png" => ["-compression_level", "6"],
            ".bmp" => Array.Empty<string>(),
            _ => throw new NotSupportedException($"Unsupported image format '{extension}'.")
        };
    }

    public static string GetFriendlyError(string stderr)
    {
        return ConversionActionHelper.GetFriendlyFfmpegError(stderr, MediaActionMessages.FfmpegFailure("image"));
    }

    public static string GetFriendlyLoadError(Exception exception)
    {
        if (exception is ArgumentException argumentException &&
            string.Equals(argumentException.Message, "Parameter is not valid.", StringComparison.Ordinal))
        {
            return MediaActionMessages.FileCorrupted();
        }

        if (exception is OutOfMemoryException)
        {
            return MediaActionMessages.FileCorrupted();
        }

        return ConversionActionHelper.GetFriendlyExceptionMessage(exception, MediaActionMessages.Failed("Crop Image"));
    }
}
