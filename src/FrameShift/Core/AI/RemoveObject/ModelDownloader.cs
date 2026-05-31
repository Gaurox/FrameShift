using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI.RemoveObject;

internal static class ModelDownloader
{
    private const string LogPrefix = "RemoveObjectModelDownloader";

    public static async Task DownloadAsync(
        ObjectRemovalModelDefinition def,
        string destinationPath,
        IProgress<AiModelDownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        await AiModelFileDownloader.DownloadAsync(
            def.DownloadUrl,
            destinationPath,
            def.ExpectedSha256,
            progress,
            cancellationToken,
            LogPrefix).ConfigureAwait(false);
    }

    public static bool IsModelFileValid(string modelPath, ObjectRemovalModelDefinition def, AppLogger? logger = null)
    {
        if (!File.Exists(modelPath)) return false;
        return AiModelFileDownloader.VerifySha256File(modelPath, def.ExpectedSha256, logger, LogPrefix);
    }
}
