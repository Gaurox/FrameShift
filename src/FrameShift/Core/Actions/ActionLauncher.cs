using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FrameShift.Core.Actions;

/// <summary>
/// Builds and launches the child <c>FrameShift.exe</c> process that runs a catalog
/// action on a full file selection. Because the main window launches this itself
/// (not Windows Explorer), the shell's per-verb multi-select cap does not apply.
///
/// The command shape is: <c>FrameShift.exe --action &lt;id&gt; [extra-args…] &lt;path…&gt;</c>.
/// No <c>--target</c>/<c>--profile</c> is ever supplied, so conversion and compression
/// actions take their in-app picker path in the child (see <c>Program.ShouldRunConversionBatch</c>).
/// All input paths are passed as discrete <see cref="ProcessStartInfo.ArgumentList"/>
/// entries, so paths with spaces or accents need no manual quoting.
/// </summary>
public static class ActionLauncher
{
    /// <summary>
    /// Builds the ordered argument list (excluding the executable itself) for a run:
    /// <c>--action</c>, the action id, the entry's extra CLI args, then each input path.
    /// </summary>
    /// <exception cref="ArgumentException">No paths, or a blank path, was supplied.</exception>
    public static IReadOnlyList<string> BuildArguments(ActionCatalogEntry entry, IReadOnlyList<string> inputPaths)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(inputPaths);

        if (inputPaths.Count == 0)
        {
            throw new ArgumentException("At least one input path is required.", nameof(inputPaths));
        }

        var args = new List<string>(inputPaths.Count + entry.ExtraCliArgs.Count + 2)
        {
            "--action",
            entry.ActionId
        };

        args.AddRange(entry.ExtraCliArgs);

        foreach (var path in inputPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Input paths must not be blank.", nameof(inputPaths));
            }

            args.Add(path);
        }

        return args;
    }

    /// <summary>
    /// Creates a configured <see cref="ProcessStartInfo"/> for the child run without
    /// starting it. Kept separate from <see cref="Launch"/> so it can be unit-tested.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(
        ActionCatalogEntry entry,
        IReadOnlyList<string> inputPaths,
        string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path is required.", nameof(executablePath));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in BuildArguments(entry, inputPaths))
        {
            startInfo.ArgumentList.Add(arg);
        }

        return startInfo;
    }

    /// <summary>
    /// Fire-and-forget launch of the child process against the running executable.
    /// Returns the started process; the caller does not wait on it.
    /// </summary>
    public static Process Launch(ActionCatalogEntry entry, IReadOnlyList<string> inputPaths)
    {
        var startInfo = CreateStartInfo(entry, inputPaths, ResolveExecutablePath());
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the FrameShift child process.");
    }

    /// <summary>Resolves the path to the running FrameShift executable.</summary>
    public static string ResolveExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        path = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        throw new InvalidOperationException("Could not resolve the FrameShift executable path.");
    }
}
