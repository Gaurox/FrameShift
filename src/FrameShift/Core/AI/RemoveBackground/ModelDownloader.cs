using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI.RemoveBackground;

public readonly record struct ModelDownloadProgress(
    long BytesReceived,
    long? TotalBytes,
    int Percent,
    string Status);

public static class ModelDownloader
{
    public const string ModelDisplayName = "BiRefNet Lite FP16";
    public const string ModelLicense = "MIT License — Free for commercial and non-commercial use";
    public const long ExpectedSizeBytes = 115L * 1024L * 1024L;

    private const string ModelUrl =
        "https://huggingface.co/onnx-community/BiRefNet_lite-ONNX/resolve/main/onnx/model_fp16.onnx";

    private const string ExpectedSha256 =
        "D39B897CEB16AE654C1731F3DBA0CF9B368D9CAE74B5A57459B455CC8BFEC402";

    public static async Task DownloadAsync(
        string destinationPath,
        IProgress<ModelDownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destinationPath))
        {
            progress.Report(new ModelDownloadProgress(0, 0, 100, "Model already present."));
            return;
        }

        var directory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = destinationPath + ".tmp";

        try
        {
            AppLogger.LogStatic($"ModelDownloader: starting. destination={destinationPath}");

            using var handler = new HttpClientHandler { AllowAutoRedirect = true };
            using var httpClient = new HttpClient(handler);
            httpClient.Timeout = Timeout.InfiniteTimeSpan;
            httpClient.DefaultRequestHeaders.Add("User-Agent", "FrameShift/1.0 (model-downloader)");

            progress.Report(new ModelDownloadProgress(0, null, 0, "Connecting..."));

            using var response = await httpClient
                .GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;

            AppLogger.LogStatic($"ModelDownloader: connected. contentLength={totalBytes?.ToString() ?? "unknown"}");

            progress.Report(new ModelDownloadProgress(0, totalBytes, 0, "Downloading..."));

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
                    ? $"{totalBytes.Value / (1024.0 * 1024.0):0} MB"
                    : "unknown";

                progress.Report(new ModelDownloadProgress(
                    totalBytesRead,
                    totalBytes,
                    percent,
                    $"{mbReceived:0.0} MB / {totalDisplay}"));
            }

            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            fileStream.Close();

            if (File.Exists(destinationPath))
            {
                TryDeleteFile(tempPath);
                progress.Report(new ModelDownloadProgress(totalBytesRead, totalBytes, 100, "Model already present."));
                return;
            }

            progress.Report(new ModelDownloadProgress(totalBytesRead, totalBytes, 99, "Verifying integrity..."));
            VerifySha256(tempPath, ExpectedSha256);

            File.Move(tempPath, destinationPath);

            AppLogger.LogStatic($"ModelDownloader: complete. bytesReceived={totalBytesRead}");

            progress.Report(new ModelDownloadProgress(totalBytesRead, totalBytes, 100, "Download complete."));
        }
        catch (InvalidDataException)
        {
            TryDeleteFile(tempPath);
            throw;
        }
        catch (OperationCanceledException)
        {
            AppLogger.LogStatic("ModelDownloader: cancelled.");
            TryDeleteFile(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"ModelDownloader: failed. {ex}");
            TryDeleteFile(tempPath);
            throw;
        }
    }

    private static void VerifySha256(string filePath, string expectedHex)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        var actual = Convert.ToHexString(hash);

        if (!string.Equals(actual, expectedHex, StringComparison.OrdinalIgnoreCase))
        {
            AppLogger.LogStatic($"ModelDownloader: SHA256 mismatch. expected={expectedHex} actual={actual}");
            throw new InvalidDataException(
                "Downloaded model file is corrupted (SHA256 mismatch). Please try again.");
        }

        AppLogger.LogStatic("ModelDownloader: SHA256 verified OK.");
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
            // Ignore — cleanup failure must never propagate.
        }
    }
}
