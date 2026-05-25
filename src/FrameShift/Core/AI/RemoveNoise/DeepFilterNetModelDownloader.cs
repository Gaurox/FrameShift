using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.AI;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI.RemoveNoise;

internal static class DeepFilterNetModelDownloader
{
    public const string ModelDisplayName = "DeepFilterNet3 (enc + erb_dec + df_dec + config)";
    public const string ModelLicense     = "Apache 2.0 — Free for commercial and non-commercial use";
    public const long   TotalSizeBytes   = 9_738_000L;

    private const string BaseUrl =
        "https://huggingface.co/Gaurox/frameshift-models/resolve/main/deepfilternet3_onnx/";

    private readonly record struct ModelFile(string Name, string Sha256);

    private static readonly ModelFile[] Files =
    [
        new("config.ini",   "415EB925D44990D938FB739F514AA3662C1EC0EA836CFF044FA1291B82CB4290"),
        new("enc.onnx",     "7C5399D3DA8A50EBEF1C1A0AE421B33376AA5E45D0E92DF16DA7E83C9C131916"),
        new("erb_dec.onnx", "AB669A1D10AFE20911728B33053A452071042317A90581092B325DA7B2F9D895"),
        new("df_dec.onnx",  "23114CE3B0F6464B763EE62F7BB8AAB6B2A129A21EABD5BCFE59413DB05F278A"),
    ];

    public static async Task DownloadAllAsync(
        string modelsDirectory,
        IProgress<AiModelDownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = true };
        using var httpClient = new HttpClient(handler);
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        httpClient.DefaultRequestHeaders.Add("User-Agent", "FrameShift/1.0 (model-downloader)");

        for (var i = 0; i < Files.Length; i++)
        {
            var file = Files[i];
            var destPath = Path.Combine(modelsDirectory, file.Name);

            var fileIndex = i;
            var fileProgress = new Progress<(int percent, string status)>(p =>
            {
                var composite = Math.Clamp(fileIndex * 25 + p.percent * 25 / 100, 0, 99);
                progress.Report(new AiModelDownloadProgress(
                    composite,
                    $"[{fileIndex + 1}/{Files.Length}] {file.Name} — {p.status}"));
            });

            await DownloadFileAsync(
                httpClient,
                BaseUrl + file.Name,
                destPath,
                file.Sha256,
                fileProgress,
                cancellationToken).ConfigureAwait(false);
        }

        progress.Report(new AiModelDownloadProgress(100, "All files downloaded."));
    }

    private static async Task DownloadFileAsync(
        HttpClient httpClient,
        string url,
        string destPath,
        string expectedSha256,
        IProgress<(int percent, string status)> progress,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destPath))
        {
            progress.Report((100, "Already present."));
            return;
        }

        var tempPath = destPath + ".tmp";

        try
        {
            AppLogger.LogStatic($"DeepFilterNetModelDownloader: downloading {Path.GetFileName(destPath)}");

            progress.Report((0, "Connecting..."));

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

                progress.Report((percent, $"{mbReceived:0.0} MB / {totalDisplay}"));
            }

            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            fileStream.Close();

            if (File.Exists(destPath))
            {
                TryDeleteFile(tempPath);
                progress.Report((100, "Already present."));
                return;
            }

            progress.Report((99, "Verifying integrity..."));
            VerifySha256(tempPath, expectedSha256);

            File.Move(tempPath, destPath);

            AppLogger.LogStatic($"DeepFilterNetModelDownloader: {Path.GetFileName(destPath)} complete.");
            progress.Report((100, "Done."));
        }
        catch (InvalidDataException)
        {
            TryDeleteFile(tempPath);
            throw;
        }
        catch (OperationCanceledException)
        {
            AppLogger.LogStatic($"DeepFilterNetModelDownloader: cancelled on {Path.GetFileName(destPath)}.");
            TryDeleteFile(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"DeepFilterNetModelDownloader: failed on {Path.GetFileName(destPath)}. {ex}");
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
            AppLogger.LogStatic(
                $"DeepFilterNetModelDownloader: SHA256 mismatch on {Path.GetFileName(filePath)}. expected={expectedHex} actual={actual}");
            throw new InvalidDataException(
                $"Downloaded file {Path.GetFileName(filePath)} is corrupted (SHA256 mismatch). Please try again.");
        }

        AppLogger.LogStatic($"DeepFilterNetModelDownloader: SHA256 OK for {Path.GetFileName(filePath)}.");
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
