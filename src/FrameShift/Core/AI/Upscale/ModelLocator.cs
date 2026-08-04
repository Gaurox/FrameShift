using System;
using System.IO;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI.Upscale;

internal static class ModelLocator
{
    public static string GetModelDirectory(UpscaleModelDefinition def) =>
        Path.Combine(AiModelStorage.RootDirectory, def.Folder);

    public static string GetModelPath(UpscaleModelDefinition def)
    {
        var targetPath = Path.Combine(GetModelDirectory(def), def.FileName);
        TryCopyLegacyModel(def, targetPath);
        return targetPath;
    }

    public static bool ModelExists(UpscaleModelDefinition def) =>
        File.Exists(GetModelPath(def));

    public static void EnsureDirectoryExists(UpscaleModelDefinition def) =>
        AiModelStorage.EnsureDirectory(GetModelDirectory(def));

    private static void TryCopyLegacyModel(UpscaleModelDefinition def, string targetPath)
    {
        if (File.Exists(targetPath) || string.IsNullOrWhiteSpace(def.LegacyFolder))
            return;

        var legacyPath = Path.Combine(AiModelStorage.RootDirectory, def.LegacyFolder, def.FileName);
        if (!File.Exists(legacyPath) || !ModelDownloader.IsModelFileValid(legacyPath, def))
            return;

        try
        {
            AiModelStorage.EnsureDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(legacyPath, targetPath, overwrite: false);
            AppLogger.LogStatic($"UpscaleModelLocator: copied legacy model to {targetPath}");
        }
        catch (IOException) when (File.Exists(targetPath))
        {
            // Another concurrent action completed the same migration.
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"UpscaleModelLocator: legacy model copy failed. {ex.Message}");
        }
    }
}
