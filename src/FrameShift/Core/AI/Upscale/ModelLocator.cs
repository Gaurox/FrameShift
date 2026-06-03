using System.IO;

namespace FrameShift.Core.AI.Upscale;

internal static class ModelLocator
{
    public static string GetModelDirectory(UpscaleModelDefinition def) =>
        Path.Combine(AiModelStorage.RootDirectory, def.Folder);

    public static string GetModelPath(UpscaleModelDefinition def) =>
        Path.Combine(GetModelDirectory(def), def.FileName);

    public static bool ModelExists(UpscaleModelDefinition def) =>
        File.Exists(GetModelPath(def));

    public static void EnsureDirectoryExists(UpscaleModelDefinition def) =>
        AiModelStorage.EnsureDirectory(GetModelDirectory(def));
}
