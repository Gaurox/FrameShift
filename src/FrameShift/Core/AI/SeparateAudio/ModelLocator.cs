using System;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;

namespace FrameShift.Core.AI.SeparateAudio;

internal static class ModelLocator
{
    private static readonly string ModelsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FrameShift",
        "AI",
        "Models");

    public static string CpuModelPath => Path.Combine(ModelsDirectory, "htdemucs.onnx");
    public static string GpuModelPath => Path.Combine(ModelsDirectory, "htdemucs_split.onnx");

    public static bool CpuModelExists() => File.Exists(CpuModelPath);
    public static bool GpuModelExists() => File.Exists(GpuModelPath);

    public static void EnsureDirectoryExists() => Directory.CreateDirectory(ModelsDirectory);

    public static bool IsDmlAvailable()
    {
        try
        {
            return OrtEnv.Instance().GetAvailableProviders()
                .Contains("DmlExecutionProvider", StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
