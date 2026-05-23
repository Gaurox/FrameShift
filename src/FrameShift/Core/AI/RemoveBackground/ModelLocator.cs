using System;
using System.IO;

namespace FrameShift.Core.AI.RemoveBackground;

public static class ModelLocator
{
    private static readonly string ModelsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FrameShift",
        "AI",
        "Models");

    public static string ModelPath => Path.Combine(ModelsDirectory, "model_fp16.onnx");

    public static bool ModelExists() => File.Exists(ModelPath);

    public static void EnsureDirectoryExists() => Directory.CreateDirectory(ModelsDirectory);
}
