using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI.SeparateAudio;

public readonly record struct ModelDownloadProgress(
    long BytesReceived,
    long? TotalBytes,
    int Percent,
    string Status);

internal static class ModelDownloader
{
    public const string CpuModelDisplayName = "HTDemucs V1 CPU";
    public const string GpuModelDisplayName = "HTDemucs V2 GPU (split)";
    public const string ModelLicense = "MIT License — Free for commercial and non-commercial use";

    public const long CpuExpectedSizeBytes = 289L * 1024L * 1024L;
    public const long GpuExpectedSizeBytes = 161L * 1024L * 1024L;

    private const string CpuModelUrl =
        "https://huggingface.co/Gaurox/frameshift-models/resolve/main/htdemucs/htdemucs.onnx";

    private const string GpuModelUrl =
        "https://huggingface.co/Gaurox/frameshift-models/resolve/main/htdemucs-split/htdemucs_split.onnx";

    private const string CpuExpectedSha256 =
        "A56BEF35B4C1B776502A53A36D4E1CAC1CB903BD9AE225939668E310B6DB1D44";

    private const string GpuExpectedSha256 =
        "7776FA928E69720966694CDF622A7F887AF543EF67E23A31979AAB81F0C97206";

    public static Task DownloadAsync(
        bool forGpu,
        string destinationPath,
        IProgress<ModelDownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        var url = forGpu ? GpuModelUrl : CpuModelUrl;
        var expectedSha256 = forGpu ? GpuExpectedSha256 : CpuExpectedSha256;
        return DownloadCoreAsync(url, destinationPath, expectedSha256, progress, cancellationToken);
    }

    private static async Task DownloadCoreAsync(
        string url,
        string destinationPath,
        string expectedSha256,
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
            AppLogger.LogStatic($"ModelDownloader (audio): starting. destination={destinationPath}");

            using var handler = new HttpClientHandler { AllowAutoRedirect = true };
            using var httpClient = new HttpClient(handler);
            httpClient.Timeout = Timeout.InfiniteTimeSpan;
            httpClient.DefaultRequestHeaders.Add("User-Agent", "FrameShift/1.0 (model-downloader)");

            progress.Report(new ModelDownloadProgress(0, null, 0, "Connecting..."));

            using var response = await httpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;

            AppLogger.LogStatic($"ModelDownloader (audio): connected. contentLength={totalBytes?.ToString() ?? "unknown"}");

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
            VerifySha256(tempPath, expectedSha256);

            File.Move(tempPath, destinationPath);

            AppLogger.LogStatic($"ModelDownloader (audio): complete. bytesReceived={totalBytesRead}");
            progress.Report(new ModelDownloadProgress(totalBytesRead, totalBytes, 100, "Download complete."));
        }
        catch (InvalidDataException)
        {
            TryDeleteFile(tempPath);
            throw;
        }
        catch (OperationCanceledException)
        {
            AppLogger.LogStatic("ModelDownloader (audio): cancelled.");
            TryDeleteFile(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"ModelDownloader (audio): failed. {ex}");
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
            AppLogger.LogStatic($"ModelDownloader (audio): SHA256 mismatch. expected={expectedHex} actual={actual}");
            throw new InvalidDataException(
                "Downloaded model file is corrupted (SHA256 mismatch). Please try again.");
        }

        AppLogger.LogStatic("ModelDownloader (audio): SHA256 verified OK.");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cleanup failure must never propagate.
        }
    }
}
