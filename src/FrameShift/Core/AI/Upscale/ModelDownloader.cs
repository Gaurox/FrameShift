using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI.Upscale;

internal static class ModelDownloader
{
    private const string LogPrefix = "UpscaleModelDownloader";

    public static async Task DownloadAsync(
        UpscaleModelDefinition def,
        string destinationPath,
        IProgress<AiModelDownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        // Hard guard: refuse any network download while the SHA256 is still the placeholder.
        if (UpscaleModelCatalog.IsSha256Placeholder(def.ExpectedSha256))
        {
            AppLogger.LogStatic(
                $"{LogPrefix}: download blocked — SHA256 is a placeholder (model not finalized). modelId={def.Id}.");
            throw new InvalidOperationException(
                "This AI model is not finalized yet: its integrity checksum (SHA256) is missing, so the " +
                "download cannot be verified and has been blocked. The model still needs to be uploaded to " +
                "the FrameShift model host.");
        }

        await AiModelFileDownloader.DownloadAsync(
            def.DownloadUrl,
            destinationPath,
            def.ExpectedSha256,
            progress,
            cancellationToken,
            LogPrefix).ConfigureAwait(false);
    }

    public static bool IsModelFileValid(string modelPath, UpscaleModelDefinition def, AppLogger? logger = null)
    {
        if (!File.Exists(modelPath)) return false;

        if (UpscaleModelCatalog.IsSha256Placeholder(def.ExpectedSha256))
        {
            var message = $"{LogPrefix}: SHA256 is a PLACEHOLDER — model validation rejected. modelId={def.Id}.";
            if (logger is not null) logger.Log(message);
            else AppLogger.LogStatic(message);
            return false;
        }

        return AiModelFileDownloader.VerifySha256File(modelPath, def.ExpectedSha256, logger, LogPrefix);
    }
}
