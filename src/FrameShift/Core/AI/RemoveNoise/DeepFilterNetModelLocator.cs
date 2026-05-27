using System;
using System.IO;
using FrameShift.Core.AI;

namespace FrameShift.Core.AI.RemoveNoise;

internal static class DeepFilterNetModelLocator
{
    private const string LogPrefix = "DeepFilterNetModelLocator";
    private static readonly string ModelsDirectory = Path.Combine(
        AiModelStorage.RootDirectory,
        "deepfilternet3_onnx");
    private static readonly string LegacyModelsDirectory = Path.Combine(
        AiModelStorage.RootDirectory,
        "deepfilternet3");

    public static string ConfigPath  => Path.Combine(ModelsDirectory, "config.ini");
    public static string EncPath     => Path.Combine(ModelsDirectory, "enc.onnx");
    public static string ErbDecPath  => Path.Combine(ModelsDirectory, "erb_dec.onnx");
    public static string DfDecPath   => Path.Combine(ModelsDirectory, "df_dec.onnx");

    public static bool AllModelsExist()
    {
        TryMigrateLegacyLayout();
        return
            File.Exists(ConfigPath) &&
            File.Exists(EncPath) &&
            File.Exists(ErbDecPath) &&
            File.Exists(DfDecPath);
    }

    public static void EnsureDirectoryExists()
    {
        AiModelStorage.EnsureDirectory(ModelsDirectory);
        TryMigrateLegacyLayout();
    }

    private static void TryMigrateLegacyLayout()
    {
        AiModelStorage.TryMigrateFile(Path.Combine(LegacyModelsDirectory, "config.ini"), ConfigPath, LogPrefix);
        AiModelStorage.TryMigrateFile(Path.Combine(LegacyModelsDirectory, "enc.onnx"), EncPath, LogPrefix);
        AiModelStorage.TryMigrateFile(Path.Combine(LegacyModelsDirectory, "erb_dec.onnx"), ErbDecPath, LogPrefix);
        AiModelStorage.TryMigrateFile(Path.Combine(LegacyModelsDirectory, "df_dec.onnx"), DfDecPath, LogPrefix);
        AiModelStorage.TryDeleteDirectoryIfEmpty(LegacyModelsDirectory, LogPrefix);
    }
}
