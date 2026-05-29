using System;
using System.IO;

namespace FrameShift.Core.Logging;

public sealed class AppLogger
{
    private static readonly object _writeLock = new();
    private const long MaxLogSizeBytes = 1L * 1024L * 1024L; // 1 MB

    public static string LogPath
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                return Path.Combine(AppContext.BaseDirectory, "logs", "FrameShift_diagnostic.log");
            }

            return Path.Combine(localAppData, "FrameShift", "logs", "FrameShift_diagnostic.log");
        }
    }

    public void Log(string message)
    {
        WriteLine(message);
    }

    public static void LogStatic(string message)
    {
        WriteLine(message);
    }

    private static void WriteLine(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [T{Environment.CurrentManagedThreadId}] {message}";
        try
        {
            var logDirectory = Path.GetDirectoryName(LogPath) ?? Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            lock (_writeLock)
            {
                RotateIfNeeded();
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(LogPath))
            {
                return;
            }

            if (new FileInfo(LogPath).Length < MaxLogSizeBytes)
            {
                return;
            }

            var archivePath = LogPath + ".old";
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            File.Move(LogPath, archivePath);
        }
        catch
        {
        }
    }
}
