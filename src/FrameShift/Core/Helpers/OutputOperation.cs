using System.ComponentModel;
using System.Runtime.InteropServices;
using FrameShift.Core.Actions;

namespace FrameShift.Core.Helpers;

/// <summary>Write in an exclusively owned workspace, then publish by a move without replacement.</summary>
internal sealed class OutputOperation : IDisposable
{
    private readonly string _desiredPath;
    private readonly bool _directory;
    private bool _disposed;

    private OutputOperation(string desiredPath, bool directory)
    {
        _desiredPath = Path.GetFullPath(desiredPath);
        _directory = directory;
        var parent = Path.GetDirectoryName(_desiredPath)!;
        for (var attempt = 0; ; attempt++)
        {
            WorkspacePath = Path.Combine(parent, $".frameshift-{Guid.NewGuid():N}");
            if (CreateDirectory(WorkspacePath, IntPtr.Zero)) break;
            var error = Marshal.GetLastWin32Error();
            if (error == 183 && attempt < 9) continue;
            throw new IOException("Cannot create output workspace.", new Win32Exception(error));
        }
        WorkingPath = Path.Combine(WorkspacePath, directory ? "payload" : Path.GetFileName(_desiredPath));
    }

    public string WorkspacePath { get; }
    public string WorkingPath { get; }
    public string? PublishedPath { get; private set; }

    public static OutputOperation ForFile(string desiredPath) => new(desiredPath, false);
    public static OutputOperation ForDirectory(string desiredPath) => new(desiredPath, true);

    public string Publish(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (PublishedPath is not null) throw new InvalidOperationException("Output already published.");
        var extension = _directory ? string.Empty : Path.GetExtension(_desiredPath);
        var basePath = _directory ? _desiredPath : _desiredPath[..^extension.Length];
        for (var index = 0; index < 1000; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = index == 0 ? _desiredPath : $"{basePath}_{index:000}{extension}";
            if (File.Exists(candidate) || Directory.Exists(candidate)) continue;
            try
            {
                if (_directory) Directory.Move(WorkingPath, candidate);
                else File.Move(WorkingPath, candidate, overwrite: false);
                PublishedPath = candidate;
                return candidate;
            }
            catch (IOException ex) when (IsDestinationCollision(ex))
            {
                // Only an actual Win32 destination collision can be retried.
            }
        }
        throw new IOException(MediaActionMessages.UniqueOutputPathExhaustedErrorKey);
    }

    private static bool IsDestinationCollision(IOException exception) =>
        (exception.HResult & 0xffff) is 80 or 183;

    public static void NotifySaved(Action notification)
    {
        try { notification(); }
        catch (Exception ex) { LogCleanupFailure($"Output was saved; notification failed: {ex.Message}"); }
    }

    private static void LogCleanupFailure(string message)
    {
        try { Logging.AppLogger.LogStatic(message); }
        catch { /* Cleanup or notification must not hide a successful publication. */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Directory.Delete(WorkspacePath, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException ex) { LogCleanupFailure($"Output workspace cleanup failed: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { LogCleanupFailure($"Output workspace cleanup failed: {ex.Message}"); }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectory(string path, IntPtr securityAttributes);
}
