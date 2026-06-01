using System;
using System.IO;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI;

internal static class AiModelStorage
{
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
        Directory.CreateDirectory(path);
    }

    public static void TryMigrateFile(string legacyPath, string targetPath, string logPrefix)
    {
        if (File.Exists(targetPath) || !File.Exists(legacyPath))
            return;

        try
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

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
}
