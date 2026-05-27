using System;
using System.IO;
using System.Linq;
using FrameShift.Core.AI;
using Microsoft.ML.OnnxRuntime;

namespace FrameShift.Core.AI.SeparateAudio;

internal static class ModelLocator
{
    private const string LogPrefix = "SeparateAudioModelLocator";
    private static readonly string CpuModelsDirectory = Path.Combine(AiModelStorage.RootDirectory, "htdemucs");
    private static readonly string GpuModelsDirectory = Path.Combine(AiModelStorage.RootDirectory, "htdemucs-split");
    private static readonly string LegacyCpuModelPath = Path.Combine(AiModelStorage.RootDirectory, "htdemucs.onnx");
    private static readonly string LegacyGpuModelPath = Path.Combine(AiModelStorage.RootDirectory, "htdemucs_split.onnx");

    public static string CpuModelPath => Path.Combine(CpuModelsDirectory, "htdemucs.onnx");
    public static string GpuModelPath => Path.Combine(GpuModelsDirectory, "htdemucs_split.onnx");

    public static bool CpuModelExists()
    {
        TryMigrateLegacyLayout();
        return File.Exists(CpuModelPath);
    }

    public static bool GpuModelExists()
    {
        TryMigrateLegacyLayout();
        return File.Exists(GpuModelPath);
    }

    public static void EnsureDirectoryExists()
    {
        AiModelStorage.EnsureDirectory(CpuModelsDirectory);
        AiModelStorage.EnsureDirectory(GpuModelsDirectory);
        TryMigrateLegacyLayout();
    }

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

    private static void TryMigrateLegacyLayout()
    {
        AiModelStorage.TryMigrateFile(LegacyCpuModelPath, CpuModelPath, LogPrefix);
        AiModelStorage.TryMigrateFile(LegacyGpuModelPath, GpuModelPath, LogPrefix);
    }
}
