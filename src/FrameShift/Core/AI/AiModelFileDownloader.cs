using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI;

internal static class AiModelFileDownloader
{
    public static async Task DownloadAsync(
        string url,
        string destinationPath,
        string expectedSha256,
        IProgress<AiModelDownloadProgress> progress,
        CancellationToken cancellationToken,
        string logPrefix)
    {
        var directory = Path.GetDirectoryName(destinationPath)!;
        AiModelStorage.EnsureDirectory(directory);

        var tempPath = destinationPath + ".tmp";

        try
        {
            AppLogger.LogStatic($"{logPrefix}: starting. destination={destinationPath}");

            using var handler = new HttpClientHandler { AllowAutoRedirect = true };
            using var httpClient = new HttpClient(handler);
            httpClient.Timeout = Timeout.InfiniteTimeSpan;
            httpClient.DefaultRequestHeaders.Add("User-Agent", "FrameShift/1.0 (model-downloader)");

            progress.Report(new AiModelDownloadProgress(0, "Connecting..."));

            using var response = await httpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;

            using var sourceStream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            using var fileStream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            var buffer = new byte[81920];
            long totalBytesRead = 0L;
            int bytesRead;

            progress.Report(new AiModelDownloadProgress(0, "Downloading..."));

            while ((bytesRead = await sourceStream
                       .ReadAsync(buffer, cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                await fileStream
                    .WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                    .ConfigureAwait(false);

                totalBytesRead += bytesRead;

                var percent = totalBytes is > 0
                    ? (int)Math.Min(99, totalBytesRead * 100L / totalBytes.Value)
                    : 0;

                var mbReceived = totalBytesRead / (1024.0 * 1024.0);
                var totalDisplay = totalBytes.HasValue
                    ? $"{totalBytes.Value / (1024.0 * 1024.0):0.0} MB"
                    : "unknown";

                progress.Report(new AiModelDownloadProgress(
                    percent,
                    $"{mbReceived:0.0} MB / {totalDisplay}"));
            }

            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            fileStream.Close();

            progress.Report(new AiModelDownloadProgress(99, "Verifying integrity..."));
            VerifySha256(tempPath, expectedSha256, logPrefix);

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tempPath, destinationPath);
            progress.Report(new AiModelDownloadProgress(100, "Download complete."));
            AppLogger.LogStatic($"{logPrefix}: complete. bytesReceived={totalBytesRead}");
        }
        catch (InvalidDataException)
        {
            TryDeleteFile(tempPath);
            throw;
        }
        catch (OperationCanceledException)
        {
            AppLogger.LogStatic($"{logPrefix}: cancelled.");
            TryDeleteFile(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"{logPrefix}: failed. {ex}");
            TryDeleteFile(tempPath);
            throw;
        }
    }

    public static bool VerifySha256File(string filePath, string expectedSha256, AppLogger? logger, string logPrefix)
    {
        try
        {
            VerifySha256(filePath, expectedSha256, logPrefix);
            return true;
        }
        catch (Exception ex)
        {
            logger?.Log($"{logPrefix}: SHA256 verification failed for '{filePath}'. {ex.Message}");
            return false;
        }
    }

    private static void VerifySha256(string filePath, string expectedSha256, string logPrefix)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        var actual = Convert.ToHexString(hash);

        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            AppLogger.LogStatic($"{logPrefix}: SHA256 mismatch. expected={expectedSha256} actual={actual}");
            throw new InvalidDataException(
                $"Downloaded file {Path.GetFileName(filePath)} is corrupted (SHA256 mismatch). Please try again.");
        }

        AppLogger.LogStatic($"{logPrefix}: SHA256 verified OK.");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
