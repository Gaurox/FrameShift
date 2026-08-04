using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI;

internal static class AiModelStorage
{
    internal const string ModelDirectoryMarkerFileName = ".frameshift-ai-model-directory";
    internal const string ModelDirectoryMarkerContent = "FrameShift AI model directory v1";

    internal static readonly string[] KnownModelDirectoryRelativePaths =
    [
        "birefnet_lite-onnx",
        "birefnet_hr-matting-onnx",
        "birefnet_hr-general-onnx",
        @"RemoveBackground\BriaBalanced",
        @"RemoveBackground\BriaHighQuality",
        "htdemucs",
        "htdemucs-split",
        "deepfilternet3_onnx",
        "rife",
        "lama-onnx",
        "lama-opencv-onnx",
        "upscale-image-onnx",
        "upscale-video-onnx",
        "whisper-base-onnx",
        "whisper-small-onnx",
        "whisper-large-v3-turbo-onnx"
    ];

    private static readonly IReadOnlyDictionary<string, string[]> s_expectedModelFiles =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["birefnet_lite-onnx"] = ["model_fp16.onnx"],
            ["birefnet_hr-matting-onnx"] = ["BiRefNet_HR-matting-epoch_135.onnx"],
            ["birefnet_hr-general-onnx"] = ["BiRefNet_HR-general-epoch_130.onnx"],
            [@"RemoveBackground\BriaBalanced"] = ["model_fp16.onnx", "README.txt", "LICENSE_NOTICE.txt"],
            [@"RemoveBackground\BriaHighQuality"] = ["model.onnx", "README.txt", "LICENSE_NOTICE.txt"],
            ["htdemucs"] = ["htdemucs.onnx"],
            ["htdemucs-split"] = ["htdemucs_split.onnx"],
            ["deepfilternet3_onnx"] = ["config.ini", "enc.onnx", "erb_dec.onnx", "df_dec.onnx"],
            ["rife"] = ["rife_v425_lite.onnx", "rife_v426_x2.onnx"],
            ["lama-onnx"] = ["lama_fp32.onnx"],
            ["lama-opencv-onnx"] = ["inpainting_lama_2025jan.onnx"],
            ["upscale-image-onnx"] = ["realesrgan_x4plus_fp16.onnx", "realesrgan_x4plus_anime_6b.onnx", "swin2sr_realworld_x4.onnx"],
            ["upscale-video-onnx"] = ["realesr_general_x4v3.onnx", "realesr_animevideov3.onnx", "realesr_animevideov3_x2.onnx", "realesr_animevideov3_x3.onnx", "realesrgan_x4plus_fp16.onnx"],
            ["whisper-base-onnx"] = ["base-encoder.onnx", "base-decoder.onnx", "base-tokens.txt"],
            ["whisper-small-onnx"] = ["small-encoder.onnx", "small-decoder.onnx", "small-tokens.txt"],
            ["whisper-large-v3-turbo-onnx"] = ["turbo-encoder.onnx", "turbo-decoder.onnx", "turbo-tokens.txt", "turbo-encoder.weights"]
        };

    // Resolved lazily from AiModelSettings, then cached for the process lifetime.
    private static string? _rootDirectory;

    public static string RootDirectory
    {
        get
        {
            if (_rootDirectory is not null)
                return _rootDirectory;

            _rootDirectory = AiModelSettings.Load().GetEffectiveModelsDirectory();
            AppLogger.LogStatic($"AiModelStorage: resolved RootDirectory={_rootDirectory}");
            return _rootDirectory;
        }
    }

    /// <summary>
    /// Forces the cached root to be re-read on next access (call after the user changes the folder).
    /// </summary>
    public static void InvalidateCache() => _rootDirectory = null;

    public static void EnsureDirectory(string path)
    {
        EnsureDirectory(RootDirectory, path);
    }

    internal static void EnsureDirectory(string rootDirectory, string path)
    {
        if (!AiModelDirectorySafety.IsSameOrChildPath(path, rootDirectory) ||
            string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory)),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("AI model directories must be children of the configured FrameShift models root.");
        }

        var directoryAlreadyExisted = Directory.Exists(path);
        Directory.CreateDirectory(path);

        // Existing folders may be shared with other software. Never retroactively claim one:
        // only a directory first created by FrameShift receives the ownership marker used by
        // the uninstaller.
        if (!directoryAlreadyExisted)
        {
            TryWriteOwnershipMarker(path);
        }
    }

    internal static string[] GetOwnedKnownModelDirectories(string modelsRoot)
    {
        var ownedDirectories = new System.Collections.Generic.List<string>();

        foreach (var relativePath in KnownModelDirectoryRelativePaths)
        {
            var candidate = Path.GetFullPath(Path.Combine(modelsRoot, relativePath));
            if (CanDeleteOwnedKnownModelDirectory(modelsRoot, candidate))
            {
                ownedDirectories.Add(candidate);
            }
        }

        return ownedDirectories.ToArray();
    }

    internal static bool CanDeleteOwnedKnownModelDirectory(string modelsRoot, string directory)
    {
        if (!AiModelDirectorySafety.IsSameOrChildPath(directory, modelsRoot) ||
            !HasOwnershipMarker(directory))
        {
            return false;
        }

        var relativePath = Path.GetRelativePath(modelsRoot, directory);
        if (!s_expectedModelFiles.TryGetValue(relativePath, out var expectedFiles))
        {
            return false;
        }

        try
        {
            var allowedFiles = new HashSet<string>(expectedFiles, StringComparer.OrdinalIgnoreCase)
            {
                ModelDirectoryMarkerFileName
            };
            allowedFiles.UnionWith(expectedFiles.Select(file => file + ".tmp"));

            return Directory.EnumerateFileSystemEntries(directory).All(entry =>
                File.Exists(entry) && allowedFiles.Contains(Path.GetFileName(entry)));
        }
        catch
        {
            return false;
        }
    }

    internal static bool HasOwnershipMarker(string path)
    {
        try
        {
            var markerPath = Path.Combine(path, ModelDirectoryMarkerFileName);
            return File.Exists(markerPath) &&
                   string.Equals(
                       File.ReadAllText(markerPath).Trim(),
                       ModelDirectoryMarkerContent,
                       StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public static void TryMigrateFile(string legacyPath, string targetPath, string logPrefix)
    {
        if (File.Exists(targetPath) || !File.Exists(legacyPath))
            return;

        try
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
                EnsureDirectory(directory);

            File.Move(legacyPath, targetPath);
            AppLogger.LogStatic($"{logPrefix}: migrated legacy file to {targetPath}");
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"{logPrefix}: failed to migrate legacy file '{legacyPath}'. {ex.Message}");
        }
    }

    public static void TryDeleteDirectoryIfEmpty(string path, string logPrefix)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            if (Directory.GetFileSystemEntries(path).Length == 0)
            {
                Directory.Delete(path);
                AppLogger.LogStatic($"{logPrefix}: removed empty legacy directory {path}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"{logPrefix}: failed to remove legacy directory '{path}'. {ex.Message}");
        }
    }

    private static void TryWriteOwnershipMarker(string directory)
    {
        try
        {
            var markerPath = Path.Combine(directory, ModelDirectoryMarkerFileName);
            using var marker = new FileStream(markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(marker);
            writer.WriteLine(ModelDirectoryMarkerContent);
        }
        catch (IOException) when (File.Exists(Path.Combine(directory, ModelDirectoryMarkerFileName)))
        {
            // Another FrameShift process created the marker first.
        }
        catch (Exception ex)
        {
            // Missing marker means the uninstaller will keep the directory, which is safer
            // than treating it as application-owned after a failed write.
            AppLogger.LogStatic($"AiModelStorage: failed to write ownership marker for '{directory}'. {ex.Message}");
        }
    }
}
