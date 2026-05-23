using System;
using System.IO;

namespace FrameShift.Core.Logging;

public sealed class AppLogger
{
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
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch
        {
        }
    }
}
