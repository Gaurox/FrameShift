using System;
using System.IO;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI;

internal static class AiModelStorage
{
    public static readonly string RootDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FrameShift",
        "AI",
        "Models");

    public static void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public static void TryMigrateFile(string legacyPath, string targetPath, string logPrefix)
    {
        if (File.Exists(targetPath) || !File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

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
        {
            return;
        }

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
}
