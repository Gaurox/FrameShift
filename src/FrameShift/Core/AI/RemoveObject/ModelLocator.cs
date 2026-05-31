using System.IO;

namespace FrameShift.Core.AI.RemoveObject;

internal static class ModelLocator
{
    public static string GetModelDirectory(ObjectRemovalModelDefinition def) =>
        Path.Combine(AiModelStorage.RootDirectory, def.Folder);

    public static string GetModelPath(ObjectRemovalModelDefinition def) =>
        Path.Combine(GetModelDirectory(def), def.FileName);

    public static bool ModelExists(ObjectRemovalModelDefinition def) =>
        File.Exists(GetModelPath(def));

    public static void EnsureDirectoryExists(ObjectRemovalModelDefinition def) =>
        AiModelStorage.EnsureDirectory(GetModelDirectory(def));
}
