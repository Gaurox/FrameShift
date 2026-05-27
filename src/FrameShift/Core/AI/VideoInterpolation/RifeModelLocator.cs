using System;
using System.IO;
using FrameShift.Core.AI;

namespace FrameShift.Core.AI.VideoInterpolation;

internal static class RifeModelLocator
{
    private const string LogPrefix = "RifeModelLocator";
    private static readonly string ModelsDirectory = Path.Combine(AiModelStorage.RootDirectory, "rife");

    public static string GetModelPath(RifeModelDefinition model)
    {
        TryMigrateLegacyLayout(model);
        return Path.Combine(ModelsDirectory, model.FileName);
    }

    public static bool ModelExists(RifeModelDefinition model)
    {
        TryMigrateLegacyLayout(model);
        return File.Exists(GetModelPath(model));
    }

    public static void EnsureDirectoryExists()
    {
        AiModelStorage.EnsureDirectory(ModelsDirectory);
    }

    private static void TryMigrateLegacyLayout(RifeModelDefinition model)
    {
        var legacyPath = Path.Combine(AiModelStorage.RootDirectory, model.FileName);
        var targetPath = Path.Combine(ModelsDirectory, model.FileName);
        AiModelStorage.TryMigrateFile(legacyPath, targetPath, LogPrefix);
    }
}
