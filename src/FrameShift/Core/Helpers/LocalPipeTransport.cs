using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using FrameShift.Core.Logging;

namespace FrameShift.Core.Helpers;

internal static class LocalPipeTransport
{
    internal const int MaximumMessageBytes = 1024 * 1024;
    internal const int MaximumPaths = 4096;
    internal static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(5);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly string Scope = CurrentScope();
    private static string CurrentScope()
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        return MakeScope(identity.User!.Value, process.SessionId);
    }
    internal static string MakeScope(string userSid, int sessionId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userSid)))[..24] + "_" + sessionId;
    internal static string Name(string name) => name + "_" + Scope;

    internal static NamedPipeServerStream CreateServer(string name, PipeDirection direction) => new(name, direction,
        1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    internal static NamedPipeClientStream CreateClient(string name, PipeDirection direction) => new(".", name, direction,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.Identification);

    internal static async Task<string?> ReadAsync(Stream stream, CancellationToken token, bool untilEnd = false)
    {
        using var message = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (count == 0) break;
            var newline = untilEnd ? -1 : Array.IndexOf(buffer, (byte)'\n', 0, count);
            var content = newline < 0 ? count : newline;
            if (message.Length + content > MaximumMessageBytes)
                throw new InvalidDataException("The local queue message exceeds its permitted size.");
            message.Write(buffer, 0, content);
            if (newline >= 0) break;
        }
        if (message.Length == 0) return null;
        try
        {
            var text = Utf8.GetString(message.GetBuffer(), 0, (int)message.Length);
            return (text.StartsWith('\uFEFF') ? text[1..] : text).TrimEnd('\r');
        }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("The local queue message is not valid UTF-8.", ex); }
    }

    internal static async Task WriteAsync(Stream stream, string message, CancellationToken token)
    {
        if (Utf8.GetByteCount(message) > MaximumMessageBytes)
            throw new InvalidDataException("The local queue message exceeds its permitted size.");
        await stream.WriteAsync(Utf8.GetBytes(message + "\n"), token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    internal static async Task SendPathsAsync(string pipeName, IReadOnlyList<string> paths, CancellationToken token = default)
    {
        ValidatePaths(paths);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(ConnectionTimeout);
        using var pipe = CreateClient(pipeName, PipeDirection.Out);
        await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
        await WriteAsync(pipe, string.Join('\n', paths), deadline.Token).ConfigureAwait(false);
    }

    internal static void ValidatePaths(IReadOnlyList<string> paths)
    {
        if (paths.Count > MaximumPaths || paths.Any(path => string.IsNullOrWhiteSpace(path) || path.Length > 32767 || path.IndexOfAny(['\r', '\n', '\0']) >= 0))
            throw new InvalidDataException("The local queue contains too many paths or an invalid path.");
        long bytes = 0;
        foreach (var path in paths)
        {
            bytes += Utf8.GetByteCount(path) + 1L;
            if (bytes > MaximumMessageBytes) throw new InvalidDataException("The local queue message exceeds its permitted size.");
        }
    }

    internal static async Task ListenForPathsAsync(string pipeName, Action<IReadOnlyList<string>> receive,
        CancellationToken token, TimeSpan? connectionTimeout = null)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = CreateServer(pipeName, PipeDirection.In);
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(connectionTimeout ?? ConnectionTimeout);
                var message = await ReadAsync(pipe, deadline.Token, untilEnd: true).ConfigureAwait(false);
                if ((message ?? "").Count(c => c == '\n') > MaximumPaths)
                    throw new InvalidDataException("Too many paths in the local queue message.");
                var paths = (message ?? "").Split('\n').Select(p => p.TrimEnd('\r')).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
                ValidatePaths(paths);
                if (paths.Length > 0 && !token.IsCancellationRequested) receive(paths);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException or InvalidOperationException)
            {
                AppLogger.LogStatic($"Local queue connection rejected: {ex.Message}");
                if (!token.IsCancellationRequested) await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    internal static string Serialize<T>(T value, JsonSerializerOptions? options = null)
    {
        using var stream = new MessageStream();
        JsonSerializer.Serialize(stream, value, options);
        return Utf8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    private sealed class MessageStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Length + count > MaximumMessageBytes) throw new InvalidDataException("The local queue message exceeds its permitted size.");
            base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Length + buffer.Length > MaximumMessageBytes) throw new InvalidDataException("The local queue message exceeds its permitted size.");
            var bytes = buffer.ToArray();
            base.Write(bytes, 0, bytes.Length);
        }
    }
}
