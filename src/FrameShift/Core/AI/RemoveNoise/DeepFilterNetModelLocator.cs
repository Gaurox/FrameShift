using System;
using System.IO;

namespace FrameShift.Core.AI.RemoveNoise;

internal static class DeepFilterNetModelLocator
{
    private static readonly string ModelsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FrameShift",
        "AI",
        "Models",
        "deepfilternet3");

    public static string ConfigPath  => Path.Combine(ModelsDirectory, "config.ini");
    public static string EncPath     => Path.Combine(ModelsDirectory, "enc.onnx");
    public static string ErbDecPath  => Path.Combine(ModelsDirectory, "erb_dec.onnx");
    public static string DfDecPath   => Path.Combine(ModelsDirectory, "df_dec.onnx");

    public static bool AllModelsExist() =>
        File.Exists(ConfigPath) &&
        File.Exists(EncPath) &&
        File.Exists(ErbDecPath) &&
        File.Exists(DfDecPath);

    public static void EnsureDirectoryExists() => Directory.CreateDirectory(ModelsDirectory);
}
