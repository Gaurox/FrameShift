using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI.VideoInterpolation;

internal static class RifeModelDownloader
{
    private const string LogPrefix = "RifeModelDownloader";

    public static async Task DownloadAsync(
        RifeModelDefinition model,
        string destinationPath,
        IProgress<AiModelDownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        await AiModelFileDownloader.DownloadAsync(
            model.DownloadUrl,
            destinationPath,
            model.ExpectedSha256,
            progress,
            cancellationToken,
            LogPrefix).ConfigureAwait(false);
    }

    public static bool IsModelFileValid(string modelPath, RifeModelDefinition model, AppLogger? logger = null)
    {
        if (!File.Exists(modelPath))
        {
            return false;
        }

        return AiModelFileDownloader.VerifySha256File(
            modelPath,
            model.ExpectedSha256,
            logger,
            LogPrefix);
    }
}
