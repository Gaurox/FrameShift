using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI.CreateSubtitles;

internal static class CreateSubtitlesModelDownloader
{
    private const string LogPrefix = "CreateSubtitlesModelDownloader";

    public static bool IsModelReady(CreateSubtitlesModelDefinition model, AppLogger? logger = null)
    {
        return model.Artifacts.All(artifact =>
            AiModelFileDownloader.VerifySha256File(
                CreateSubtitlesModelLocator.GetArtifactPath(model, artifact),
                artifact.ExpectedSha256,
                logger,
                LogPrefix));
    }

    public static async Task DownloadAsync(
        CreateSubtitlesModelDefinition model,
        IProgress<AiModelDownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        CreateSubtitlesModelLocator.EnsureDirectoryExists(model);

        var totalBytes = Math.Max(1L, model.ExpectedTotalSizeBytes);
        var downloadedBytesBeforeCurrent = 0L;

        foreach (var artifact in model.Artifacts)
        {
            var destinationPath = CreateSubtitlesModelLocator.GetArtifactPath(model, artifact);
            var artifactBase = downloadedBytesBeforeCurrent;

            var artifactProgress = new Progress<AiModelDownloadProgress>(update =>
            {
                var artifactBytes = artifact.ExpectedSizeBytes <= 0
                    ? 0L
                    : (long)Math.Clamp(
                        Math.Round(update.Percent / 100d * artifact.ExpectedSizeBytes, MidpointRounding.AwayFromZero),
                        0d,
                        artifact.ExpectedSizeBytes);
                var aggregateBytes = artifactBase + artifactBytes;
                var aggregatePercent = (int)Math.Clamp(
                    Math.Round(aggregateBytes * 100d / totalBytes, MidpointRounding.AwayFromZero),
                    0d,
                    100d);

                progress.Report(new AiModelDownloadProgress(
                    aggregatePercent,
                    $"{artifact.FileName} — {update.Status}"));
            });

            await AiModelFileDownloader.DownloadAsync(
                artifact.DownloadUrl,
                destinationPath,
                artifact.ExpectedSha256,
                artifactProgress,
                cancellationToken,
                $"{LogPrefix}:{artifact.FileName}").ConfigureAwait(false);

            downloadedBytesBeforeCurrent += artifact.ExpectedSizeBytes;
        }

        progress.Report(new AiModelDownloadProgress(100, $"{model.DisplayName} is ready."));
    }

    public static void RemoveInvalidFiles(CreateSubtitlesModelDefinition model, AppLogger logger)
    {
        foreach (var artifact in model.Artifacts)
        {
            var artifactPath = CreateSubtitlesModelLocator.GetArtifactPath(model, artifact);
            if (!File.Exists(artifactPath))
            {
                continue;
            }

            if (AiModelFileDownloader.VerifySha256File(artifactPath, artifact.ExpectedSha256, logger, LogPrefix))
            {
                continue;
            }

            try
            {
                File.Delete(artifactPath);
            }
            catch (Exception ex)
            {
                logger.Log($"{LogPrefix}: failed to delete invalid artifact '{artifactPath}'. {ex.Message}");
            }
        }
    }
}
