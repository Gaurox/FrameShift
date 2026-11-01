using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI;

internal static class AiModelFileDownloader
{
    internal static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(60);
    private const long DiskReserveBytes = 16L * 1024 * 1024;

    public static async Task DownloadAsync(
        string url, string destinationPath, string expectedSha256, long maximumBytes,
        IProgress<AiModelDownloadProgress> progress, CancellationToken cancellationToken, string logPrefix,
        string? modelsRoot = null, TimeSpan? inactivityTimeout = null,
        Func<string, long>? getAvailableSpace = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumBytes <= 0 || maximumBytes > long.MaxValue - DiskReserveBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("A pinned SHA256 is required before downloading a model.", nameof(expectedSha256));

        modelsRoot ??= AiModelStorage.RootDirectory;
        destinationPath = AiModelPathLease.Normalize(destinationPath);
        var directory = Path.GetDirectoryName(destinationPath)!;
        AiModelStorage.EnsureDirectory(modelsRoot, directory);
        using var pathLease = AiModelPathLease.Acquire(directory, allowFileUpdates: true);
        AiModelPathLease.ValidateFile(destinationPath);
        using var downloadLease = await AcquireDownloadLeaseAsync(destinationPath, progress, cancellationToken).ConfigureAwait(false);
        AiModelPathLease.ValidateFile(destinationPath);

        // Recheck under the cross-process lease: a waiting downloader may now be done.
        if (await IsVerifiedAsync(destinationPath, expectedSha256, maximumBytes, cancellationToken).ConfigureAwait(false))
        {
            var existingSize = new FileInfo(destinationPath).Length;
            progress.Report(new(100, L.Get("AiModelFileDownloader_40bfe3ff12")) { BytesReceived = existingSize, TotalBytes = existingSize });
            return;
        }

        var space = getAvailableSpace ?? GetAvailableSpace;
        EnsureSpace(directory, maximumBytes, space);
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var timeout = inactivityTimeout ?? InactivityTimeout;
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(inactivityTimeout));
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        using var temporaryHandle = AiModelPathLease.Open(tempPath, 0xC0010000, FileShare.None, 1, 0x40000000);
        await using var output = new FileStream(temporaryHandle, FileAccess.ReadWrite, 81920, isAsync: true);
        var published = false;
        try
        {
            AppLogger.LogStatic($"{logPrefix}: starting bounded download. destination={destinationPath}, maximumBytes={maximumBytes}");
            progress.Report(new(0, L.Get("AiModelFileDownloader_Connecting_9db0c61f")));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("FrameShift/1.0 (model-downloader)");
            using var networkTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            networkTimeout.CancelAfter(timeout);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, networkTimeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            if (length > maximumBytes)
                throw new InvalidDataException(L.Get("AiModelFileDownloader_a0af013e3c"));

            await using var source = await response.Content.ReadAsStreamAsync(networkTimeout.Token).ConfigureAwait(false);
            networkTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long received = 0, nextSpaceCheck = 8L * 1024 * 1024;
            while (true)
            {
                networkTimeout.CancelAfter(timeout);
                var count = await source.ReadAsync(buffer, networkTimeout.Token).ConfigureAwait(false);
                networkTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
                if (count == 0) break;
                if (count > maximumBytes - received)
                    throw new InvalidDataException(L.Get("AiModelFileDownloader_a0af013e3c"));
                cancellationToken.ThrowIfCancellationRequested();
                if (received + count >= nextSpaceCheck)
                {
                    EnsureSpace(directory, count, space);
                    nextSpaceCheck = received + count + 8L * 1024 * 1024;
                }
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, count);
                received += count;
                var percent = (int)Math.Min(99, received * 100d / (length is > 0 ? length.Value : maximumBytes));
                progress.Report(new(percent, $"{received / 1048576d:0.0} MB / {(length ?? maximumBytes) / 1048576d:0.0} MB") { BytesReceived = received, TotalBytes = length });
            }

            if (length.HasValue && received != length.Value)
                throw new InvalidDataException(L.Get("AiModelFileDownloader_0d5a56c52b"));
            progress.Report(new(99, L.Get("AiModelFileDownloader_Verifying_integrity_9906a66d")) { BytesReceived = received, TotalBytes = length });
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.Get("AiModelFileDownloader_9ff115ea91"));
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            AiModelPathLease.ValidateFile(destinationPath);
            // Short path validations in another instance can briefly deny parent writes.
            // Bound retries; a persistently locked destination always preserves the old file.
            for (var attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { AiModelPathLease.Publish(temporaryHandle, destinationPath); break; }
                catch (IOException ex) when (attempt < 20 && ex.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 32 or 33 })
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }
            published = true;
            progress.Report(new(100, L.Get("AiModelFileDownloader_Download_complete_af00af67")) { BytesReceived = received, TotalBytes = length });
            AppLogger.LogStatic($"{logPrefix}: complete. bytesReceived={received}");
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            AppLogger.LogStatic($"{logPrefix}: download timed out after network inactivity.");
            throw new TimeoutException(L.Get("AiModelFileDownloader_896e66785f"), ex);
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"{logPrefix}: download failed or cancelled. {ex.Message}");
            throw;
        }
        finally
        {
            if (!published)
            {
                try { AiModelPathLease.DeleteOnClose(temporaryHandle); }
                catch (Exception ex) { AppLogger.LogStatic($"{logPrefix}: temporary cleanup failed. {ex.Message}"); }
            }
        }
    }

    private static async Task<FileStream> AcquireDownloadLeaseAsync(string destination, IProgress<AiModelDownloadProgress> progress, CancellationToken token)
    {
        var lockPath = destination + ".download-lock";
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var handle = AiModelPathLease.Open(lockPath, 0xC0000000, FileShare.None, 4);
                try
                {
                    AiModelPathLease.CheckRegular(handle);
                    return new FileStream(handle, FileAccess.ReadWrite);
                }
                catch { handle.Dispose(); throw; }
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
            {
                progress.Report(new(0, L.Get("AiModelFileDownloader_5d3d677ed2")));
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }
        // Keep the empty lease file: deleting it by name after release races the next owner.
    }

    private static async Task<bool> IsVerifiedAsync(string path, string digest, long maximumBytes, CancellationToken token)
    {
        if (!File.Exists(path)) return false;
        using var handle = AiModelPathLease.Open(path, 0x80000000, FileShare.Read, 3, 0x40000000);
        AiModelPathLease.CheckRegular(handle);
        await using var stream = new FileStream(handle, FileAccess.Read, 81920, isAsync: true);
        return stream.Length <= maximumBytes && Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).Equals(digest, StringComparison.OrdinalIgnoreCase);
    }

    public static bool VerifySha256File(string filePath, string expectedSha256, AppLogger? logger, string logPrefix)
    {
        try
        {
            using var lease = AiModelPathLease.Acquire(Path.GetDirectoryName(filePath)!);
            using var handle = AiModelPathLease.Open(filePath, 0x80000000, FileShare.Read, 3);
            AiModelPathLease.CheckRegular(handle);
            lease.Dispose(); // The read handle now pins the file; do not block directory updates while hashing.
            using var stream = new FileStream(handle, FileAccess.Read);
            return Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            logger?.Log($"{logPrefix}: SHA256 verification failed for '{filePath}'. {ex.Message}");
            return false;
        }
    }

    private static void EnsureSpace(string directory, long bytes, Func<string, long> availableSpace)
    {
        if (availableSpace(directory) < bytes + DiskReserveBytes)
            throw new IOException(L.Get("AiModelFileDownloader_263c162228"));
    }

    private static long GetAvailableSpace(string directory)
    {
        if (!GetDiskFreeSpaceExW(AiModelPathLease.GetNativePath(directory), out var available, out _, out _))
            throw new IOException(L.Get("AiModelFileDownloader_6251938ea7"));
        return (long)Math.Min(available, (ulong)long.MaxValue);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(string directory, out ulong available, out ulong total, out ulong free);
}
