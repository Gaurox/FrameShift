using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using FrameShift.Core.AI;
using FrameShift.Core.AI.Ocr;
using Xunit;

namespace FrameShift.Tests;

public sealed class AiModelDownloadSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FrameShift téléchargement modèles é " + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("model test é — integrity");
    private static readonly string Digest = Convert.ToHexString(SHA256.HashData(Payload));
    private string Destination => Path.Combine(_root, "rife", "rife_v426_x2.onnx");
    private static IProgress<AiModelDownloadProgress> Ignore => new ForwardProgress<AiModelDownloadProgress>(_ => { });

    public AiModelDownloadSafetyTests() => AiModelStorage.EnsureDirectory(_root, Path.GetDirectoryName(Destination)!);

    private Task Download(string url, long? maximum = null, CancellationToken token = default,
        IProgress<AiModelDownloadProgress>? progress = null, TimeSpan? timeout = null, Func<string, long>? space = null, string? digest = null) =>
        AiModelFileDownloader.DownloadAsync(url, Destination, digest ?? Digest, maximum ?? Payload.Length,
            progress ?? Ignore, token, "Download regression", modelsRoot: _root, inactivityTimeout: timeout ?? TimeSpan.FromSeconds(3), getAvailableSpace: space);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidBoundedResponsePublishesAndPreservesForeignTemporary(bool chunked)
    {
        File.WriteAllText(Destination + ".tmp", "foreign");
        await using var server = new LocalModelServer((stream, token) => Send(stream, Payload, chunked, token));
        await Download(server.Url);
        Assert.Equal(Payload, File.ReadAllBytes(Destination));
        Assert.Equal("foreign", File.ReadAllText(Destination + ".tmp"));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Destination)!, "*.tmp"));
        Assert.True(AiModelStorage.CanDeleteOwnedKnownModelDirectory(_root, Path.GetDirectoryName(Destination)!));
    }

    [Theory]
    [InlineData("header")]
    [InlineData("chunked")]
    [InlineData("hash")]
    [InlineData("short")]
    [InlineData("http")]
    public async Task RejectedResponseKeepsPreviousFileAndCleansTemporary(string failure)
    {
        File.WriteAllText(Destination, "previous");
        await using var server = new LocalModelServer(async (stream, token) =>
        {
            if (failure == "header") await Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 999999\r\nConnection: close\r\n\r\n", token);
            else if (failure == "short") await Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 24\r\nConnection: close\r\n\r\nx", token);
            else if (failure == "http") await Write(stream, "HTTP/1.1 500 Error\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", token);
            else await Send(stream, failure == "chunked" ? new byte[Payload.Length + 1] : new byte[Payload.Length], true, token);
        });
        var error = await Record.ExceptionAsync(() => Download(server.Url));
        Assert.NotNull(error);
        Assert.False(error is OperationCanceledException);
        Assert.Equal("previous", File.ReadAllText(Destination));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Destination)!, "*.tmp"));
        Assert.Equal(1, server.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SilentHeadersOrBodyTimesOutWithoutDeletingPreviousFile(bool headers)
    {
        File.WriteAllText(Destination, "previous");
        await using var server = new LocalModelServer(async (stream, token) =>
        {
            if (!headers) await Write(stream, "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n1\r\nx\r\n", token);
            await Task.Delay(Timeout.Infinite, token);
        });
        await Assert.ThrowsAsync<TimeoutException>(() => Download(server.Url, timeout: TimeSpan.FromMilliseconds(300)));
        Assert.Equal("previous", File.ReadAllText(Destination));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Destination)!, "*.tmp"));
    }

    [Fact]
    public async Task InsufficientDiskSpaceFailsBeforeNetworkOrTemporary()
    {
        await using var server = new LocalModelServer((stream, token) => Send(stream, Payload, false, token));
        await Assert.ThrowsAsync<IOException>(() => Download(server.Url, space: _ => 0));
        Assert.Equal(0, server.Requests);
        Assert.False(File.Exists(Destination));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Destination)!, "*.tmp"));
    }

    [Fact]
    public async Task VerifiedExistingModelAvoidsNetworkAndNeedsNoFreeSpace()
    {
        File.WriteAllBytes(Destination, Payload);
        await Download("http://127.0.0.1:1/unused", space: _ => 0);
        Assert.Equal(Payload, File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task LockedDestinationKeepsPreviousFileWhenPublicationFails()
    {
        File.WriteAllText(Destination, "previous");
        using var held = new FileStream(Destination, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var server = new LocalModelServer((stream, token) => Send(stream, Payload, false, token));
        await Assert.ThrowsAsync<IOException>(() => Download(server.Url));
        Assert.Equal("previous", File.ReadAllText(Destination));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Destination)!, "*.tmp"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WaitingDownloadIsSerializedAndCancellationDoesNotTouchOwner(bool cancelWaiter)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new LocalModelServer(async (stream, token) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            await Send(stream, Payload, true, token);
        });
        var first = Download(server.Url);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var second = Download(server.Url, token: cancellation.Token,
            progress: new ForwardProgress<AiModelDownloadProgress>(p => { if (p.Status.StartsWith("Waiting")) waiting.TrySetResult(); }));
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, server.Requests);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Destination)!, "*.tmp"));
        if (cancelWaiter)
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Destination)!, "*.tmp"));
        }
        release.TrySetResult();
        await first;
        if (!cancelWaiter) await second;
        Assert.Equal(1, server.Requests);
        Assert.Equal(Payload, File.ReadAllBytes(Destination));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Destination)!, "*.tmp"));
    }

    [Fact]
    public async Task CancelActiveDownloadThenRetrySucceeds()
    {
        var bodyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        File.WriteAllText(Destination, "previous");
        await using var server = new LocalModelServer(async (stream, token) =>
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                await Write(stream, "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n1\r\nx\r\n", token);
                bodyStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
            else await Send(stream, Payload, false, token);
        });
        using var cancellation = new CancellationTokenSource();
        var download = Download(server.Url, token: cancellation.Token);
        await bodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.Equal("previous", File.ReadAllText(Destination));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Destination)!, "*.tmp"));
        await Download(server.Url);
        Assert.Equal(Payload, File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task AncestorsCannotBeReplacedWhileDownloading()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new LocalModelServer(async (stream, token) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            await Send(stream, Payload, false, token);
        });
        var download = Download(server.Url);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<IOException>(() => Directory.Move(_root, _root + " swapped"));
        Assert.Throws<IOException>(() => Directory.Move(Path.GetDirectoryName(Destination)!, Destination + " swapped"));
        release.TrySetResult();
        await download;
        Assert.Equal(Payload, File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task PreCancelledDownloadDoesNotCreateFiles()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Download("http://127.0.0.1:1", token: new CancellationToken(true)));
        Assert.Equal(new[] { AiModelStorage.ModelDirectoryMarkerFileName }, Directory.GetFiles(Path.GetDirectoryName(Destination)!).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadWaitsForAnotherProcessAndCanBeCancelled(bool cancel)
    {
        var ready = Path.Combine(_root, "ready");
        var release = Path.Combine(_root, "release");
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(AppContext.BaseDirectory, "Assets", "HoldModelDownloadLease.ps1"), "-LockPath", Destination + ".download-lock", "-ReadyPath", ready, "-ReleasePath", release })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEndAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(ready))
            {
                Assert.False(process.HasExited, "The lease holder stopped before becoming ready.");
                await Task.Delay(25, deadline.Token);
            }
            await using var server = new LocalModelServer((stream, token) => Send(stream, Payload, false, token));
            using var cancellation = new CancellationTokenSource();
            var download = Download(server.Url, token: cancellation.Token,
                progress: new ForwardProgress<AiModelDownloadProgress>(p => { if (p.Status.StartsWith("Waiting")) waiting.TrySetResult(); }));
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, server.Requests);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Destination)!, "*.tmp"));
            if (cancel)
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
            }
            File.WriteAllText(release, "release");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(process.ExitCode == 0, await stderr);
            if (!cancel) await download;
            else await Download(server.Url);
            Assert.Equal(Payload, File.ReadAllBytes(Destination));
            Assert.Equal(1, server.Requests);
        }
        finally
        {
            File.WriteAllText(release, "release");
            if (!process.HasExited)
            {
                if (!process.WaitForExit(5000)) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            await Task.WhenAll(stderr, stdout);
        }
    }

    [Fact]
    public async Task DistinctDestinationsIncludingParentAndChildCanPublishIndependently()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var held = new LocalModelServer(async (stream, token) =>
        {
            firstStarted.TrySetResult();
            await release.Task.WaitAsync(token);
            await Send(stream, Payload, false, token);
        });
        await using var other = new LocalModelServer((stream, token) => Send(stream, Payload, false, token));
        var nested = Path.Combine(Path.GetDirectoryName(Destination)!, "rec", "inference.onnx");
        var first = AiModelFileDownloader.DownloadAsync(held.Url, nested, Digest, Payload.Length, Ignore, default, "nested", _root);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await Download(other.Url).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(first.IsCompleted);
            Assert.Equal(Payload, File.ReadAllBytes(Destination));
        }
        finally { release.TrySetResult(); await first; }
        Assert.Equal(Payload, File.ReadAllBytes(nested));
    }

    [Fact]
    public async Task LowSpaceDuringStreamingIsDetectedAndPreservesExistingModel()
    {
        var body = new byte[9 * 1024 * 1024];
        var calls = 0;
        File.WriteAllText(Destination, "previous");
        await using var server = new LocalModelServer((stream, token) => Send(stream, body, false, token));
        await Assert.ThrowsAsync<IOException>(() => Download(server.Url, maximum: body.Length,
            space: _ => Interlocked.Increment(ref calls) == 1 ? long.MaxValue : 0,
            digest: Convert.ToHexString(SHA256.HashData(body))));
        Assert.Equal(2, calls);
        Assert.Equal("previous", File.ReadAllText(Destination));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Destination)!, "*.tmp"));
    }

    [Fact]
    public async Task CancellationAfterReceptionPreventsPublication()
    {
        File.WriteAllText(Destination, "previous");
        using var cancellation = new CancellationTokenSource();
        await using var server = new LocalModelServer((stream, token) => Send(stream, Payload, false, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Download(server.Url, token: cancellation.Token,
            progress: new ForwardProgress<AiModelDownloadProgress>(p => { if (p.Status.StartsWith("Verifying")) cancellation.Cancel(); })));
        Assert.Equal("previous", File.ReadAllText(Destination));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Destination)!, "*.tmp"));
    }

    [Fact]
    public async Task LongAccentedPathPublishesWithoutLeavingAnyTemporary()
    {
        var directory = Path.Combine(_root, new string('a', 100), new string('b', 100), "modèles é");
        var destination = Path.Combine(directory, "model.onnx");
        Assert.True(destination.Length > 260);
        await using var server = new LocalModelServer((stream, token) => Send(stream, Payload, false, token));
        await AiModelFileDownloader.DownloadAsync(server.Url, destination, Digest, Payload.Length, Ignore, default, "long path", _root);
        Assert.Equal(Payload, File.ReadAllBytes(destination));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        Assert.Empty(Directory.GetFiles(directory, "*.lease"));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(100, true)]
    public async Task InvalidBudgetOrChecksumIsRejectedBeforeNetwork(long maximum, bool badChecksum)
    {
        await using var server = new LocalModelServer((stream, token) => Send(stream, Payload, false, token));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Download(server.Url, maximum: maximum, digest: badChecksum ? "placeholder" : Digest));
        Assert.Equal(0, server.Requests);
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task DestinationCannotEscapeTheConfiguredRoot()
    {
        var sibling = _root + "-outside";
        await Assert.ThrowsAsync<InvalidOperationException>(() => AiModelFileDownloader.DownloadAsync("http://127.0.0.1:1", Path.Combine(sibling, "model.onnx"),
            Digest, Payload.Length, Ignore, default, "escape", _root));
        Assert.False(Directory.Exists(sibling));
    }

    [Fact]
    public async Task ContinuingDataResetsTheInactivityDeadline()
    {
        await using var server = new LocalModelServer(async (stream, token) =>
        {
            await Write(stream, "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n", token);
            foreach (var b in Payload)
            {
                await Write(stream, "1\r\n", token);
                await stream.WriteAsync(new[] { b }, token);
                await Write(stream, "\r\n", token);
                await Task.Delay(30, token);
            }
            await Write(stream, "0\r\n\r\n", token);
        });
        await Download(server.Url, timeout: TimeSpan.FromMilliseconds(500));
        Assert.Equal(Payload, File.ReadAllBytes(Destination));
    }

    private static async Task Send(NetworkStream stream, byte[] body, bool chunked, CancellationToken token)
    {
        await Write(stream, chunked
            ? $"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n{body.Length:X}\r\n"
            : $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n", token);
        await stream.WriteAsync(body, token);
        if (chunked) await Write(stream, "\r\n0\r\n\r\n", token);
    }

    private static Task Write(NetworkStream stream, string text, CancellationToken token) => stream.WriteAsync(Encoding.ASCII.GetBytes(text), token).AsTask();

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class LocalModelServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly List<Task> _clients = [];
        private readonly Task _loop;
        private int _requests;
        public int Requests => Volatile.Read(ref _requests);
        public string Url { get; }

        public LocalModelServer(Func<NetworkStream, CancellationToken, Task> serve)
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/model";
            _loop = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                        _clients.Add(Handle(client, serve));
                    }
                }
                catch (OperationCanceledException) { }
                catch (SocketException) when (_stop.IsCancellationRequested) { }
                catch (InvalidOperationException) when (_stop.IsCancellationRequested) { }
            });
        }

        private async Task Handle(TcpClient client, Func<NetworkStream, CancellationToken, Task> serve)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) { }
                    Interlocked.Increment(ref _requests);
                    await serve(stream, _stop.Token);
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            await _loop;
            await Task.WhenAll(_clients);
            _stop.Dispose();
        }
    }
}
