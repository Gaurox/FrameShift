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
        // Hard guard: refuse any network download while the SHA256 is still the placeholder. Integrity
        // cannot be verified, so a silent download is not allowed. A model already present locally is
        // still usable (see IsModelFileValid) — only the auto-download path is blocked here.
        if (UpscaleModelCatalog.IsSha256Placeholder(def.ExpectedSha256))
        {
            AppLogger.LogStatic(
                $"{LogPrefix}: download blocked — SHA256 is a placeholder (model not finalized). modelId={def.Id}.");
            throw new InvalidOperationException(
                "This AI model is not finalized yet: its integrity checksum (SHA256) is missing, so the " +
                "download cannot be verified and has been blocked. The model still needs to be uploaded to " +
                "the FrameShift model host. To test now, place the model file manually in the model folder.");
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

        // Pre-release: while the SHA256 is still the placeholder (model not yet hosted on Gaurox),
        // we cannot verify integrity. Accept a present file so local testing works, but log loudly.
        // This path MUST NOT survive into a release build with the real hash in place.
        if (UpscaleModelCatalog.IsSha256Placeholder(def.ExpectedSha256))
        {
            var message = $"{LogPrefix}: SHA256 is a PLACEHOLDER — integrity NOT verified (pre-release). modelId={def.Id}.";
            if (logger is not null) logger.Log(message);
            else AppLogger.LogStatic(message);
            return true;
        }

        return AiModelFileDownloader.VerifySha256File(modelPath, def.ExpectedSha256, logger, LogPrefix);
    }
}
