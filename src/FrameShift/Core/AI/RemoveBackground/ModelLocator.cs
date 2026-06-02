using System.IO;
using FrameShift.Core.AI;

namespace FrameShift.Core.AI.RemoveBackground;

public static class ModelLocator
{
    private const string LogPrefix = "RemoveBackgroundModelLocator";
    private static readonly string LegacyModelPath = Path.Combine(
        AiModelStorage.RootDirectory,
        "model_fp16.onnx");
    private static readonly string LegacyNestedModelPath = Path.Combine(
        AiModelStorage.RootDirectory,
        "birefnet_lite-onnx",
        "onnx",
        "model_fp16.onnx");

    public static string GetModelDirectory(BackgroundRemovalModelDefinition def) =>
        Path.Combine(AiModelStorage.RootDirectory, def.Folder);

    public static string GetModelPath(BackgroundRemovalModelDefinition def) =>
        Path.Combine(GetModelDirectory(def), def.FileName);

    public static bool ModelExists(BackgroundRemovalModelDefinition def)
    {
        TryMigrateLegacyLayout(def);
        return File.Exists(GetModelPath(def));
    }

    public static void EnsureDirectoryExists(BackgroundRemovalModelDefinition def)
    {
        AiModelStorage.EnsureDirectory(GetModelDirectory(def));
        TryMigrateLegacyLayout(def);
    }

    private static void TryMigrateLegacyLayout(BackgroundRemovalModelDefinition def)
    {
        if (!string.Equals(def.Id, "fast", System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var targetPath = GetModelPath(def);
        AiModelStorage.TryMigrateFile(LegacyModelPath, targetPath, LogPrefix);
        AiModelStorage.TryMigrateFile(LegacyNestedModelPath, targetPath, LogPrefix);
        AiModelStorage.TryDeleteDirectoryIfEmpty(Path.Combine(GetModelDirectory(def), "onnx"), LogPrefix);
    }
}
