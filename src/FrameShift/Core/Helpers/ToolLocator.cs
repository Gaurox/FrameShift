using System;
using System.IO;
using FrameShift.Core.Actions;

namespace FrameShift.Core.Helpers;

public sealed class ToolLocator
{
    public string ResolveFfmpegPath()
    {
        return ResolveToolPath("ffmpeg");
    }

    public string ResolveFfprobePath()
    {
        return ResolveToolPath("ffprobe");
    }

    private static string ResolveToolPath(string toolName)
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { $"{toolName}.exe", toolName }
            : new[] { toolName, $"{toolName}.exe" };

        foreach (var candidate in candidates)
        {
            var localPath = Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg", candidate);
            if (File.Exists(localPath))
            {
                return localPath;
            }

            var projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Tools", "ffmpeg", candidate));
            if (File.Exists(projectPath))
            {
                return projectPath;
            }
        }

        throw new FileNotFoundException(
            MediaActionMessages.RequiredToolMissing(toolName),
            Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg", OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName));
    }
}
