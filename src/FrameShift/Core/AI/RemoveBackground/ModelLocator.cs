using System;
using System.IO;
using FrameShift.Core.AI;

namespace FrameShift.Core.AI.RemoveBackground;

public static class ModelLocator
{
    private const string LogPrefix = "RemoveBackgroundModelLocator";
    private static readonly string ModelsDirectory = Path.Combine(
        AiModelStorage.RootDirectory,
        "birefnet_lite-onnx");
    private static readonly string LegacyModelPath = Path.Combine(
        AiModelStorage.RootDirectory,
        "model_fp16.onnx");
    private static readonly string LegacyNestedModelPath = Path.Combine(
        AiModelStorage.RootDirectory,
        "birefnet_lite-onnx",
        "onnx",
        "model_fp16.onnx");

    public static string ModelPath => Path.Combine(ModelsDirectory, "model_fp16.onnx");

    public static bool ModelExists()
    {
        TryMigrateLegacyLayout();
        return File.Exists(ModelPath);
    }

    public static void EnsureDirectoryExists()
    {
        AiModelStorage.EnsureDirectory(ModelsDirectory);
        TryMigrateLegacyLayout();
    }

    private static void TryMigrateLegacyLayout()
    {
        AiModelStorage.TryMigrateFile(LegacyModelPath, ModelPath, LogPrefix);
        AiModelStorage.TryMigrateFile(LegacyNestedModelPath, ModelPath, LogPrefix);
        AiModelStorage.TryDeleteDirectoryIfEmpty(Path.Combine(ModelsDirectory, "onnx"), LogPrefix);
    }
}
