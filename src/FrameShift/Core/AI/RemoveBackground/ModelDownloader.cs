using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.AI;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI.RemoveBackground;

public static class ModelDownloader
{
    private const string LogPrefix = "RemoveBackgroundModelDownloader";

    public static async Task DownloadAsync(
        BackgroundRemovalModelDefinition def,
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

    public static bool IsModelFileValid(
        string modelPath,
        BackgroundRemovalModelDefinition def,
        AppLogger? logger = null)
    {
        if (!File.Exists(modelPath))
        {
            return false;
        }

        return AiModelFileDownloader.VerifySha256File(modelPath, def.ExpectedSha256, logger, LogPrefix);
    }
}
