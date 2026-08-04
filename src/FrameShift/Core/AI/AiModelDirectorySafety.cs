using System;
using System.IO;

namespace FrameShift.Core.AI;

/// <summary>
/// Validates a user-selected AI model root before FrameShift writes to it.
/// The uninstaller independently applies the same conservative rules because
/// settings.json must be treated as untrusted input at uninstall time.
/// </summary>
internal static class AiModelDirectorySafety
{
    public static bool TryNormalizeCustomDirectory(string? value, out string normalizedDirectory)
    {
        normalizedDirectory = string.Empty;

        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value.Trim()))
        {
            return false;
        }

        try
        {
            var candidate = TrimEndingDirectorySeparator(Path.GetFullPath(value.Trim()));
            var defaultDirectory = TrimEndingDirectorySeparator(AiModelSettings.DefaultModelsDirectoryPath);

            if (IsVolumeRoot(candidate))
            {
                return false;
            }

            // The default is deliberately inside the profile and is the sole exception to
            // the protected-location checks below.
            if (PathsEqual(candidate, defaultDirectory))
            {
                normalizedDirectory = candidate;
                return true;
            }

            if (IsSameOrChildPath(defaultDirectory, candidate) ||
                IsSameOrChildPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), candidate))
            {
                return false;
            }

            foreach (var protectedDirectory in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         AppContext.BaseDirectory
                     })
            {
                if (string.IsNullOrWhiteSpace(protectedDirectory))
                {
                    continue;
                }

                if (IsSameOrChildPath(candidate, protectedDirectory) ||
                    IsSameOrChildPath(protectedDirectory, candidate))
                {
                    return false;
                }
            }

            normalizedDirectory = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static bool IsSameOrChildPath(string? candidatePath, string? parentPath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(parentPath))
        {
            return false;
        }

        try
        {
            var candidate = TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
            var parent = TrimEndingDirectorySeparator(Path.GetFullPath(parentPath));
            return PathsEqual(candidate, parent) ||
                   candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsVolumeRoot(string path) =>
        PathsEqual(path, Path.GetPathRoot(path) ?? string.Empty);

    private static string TrimEndingDirectorySeparator(string path) =>
        Path.TrimEndingDirectorySeparator(path);
}
