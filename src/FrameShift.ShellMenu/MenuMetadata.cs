using Microsoft.Win32;
using System.Diagnostics;

namespace FrameShift.ShellMenu;

internal sealed record VisibleCommand(MenuCommand Definition, string Caption, string? Icon);
internal sealed record MenuSnapshot(string Executable, IReadOnlyList<VisibleCommand> Commands);

internal static class MenuMetadata
{
    internal static MenuSnapshot Read(IReadOnlyList<string> paths)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var root = machine.OpenSubKey(MenuCommands.MetadataPath);
        if (root is null) return new("", []);
        var executable = NativeSelection.InstallExecutable();
        var version = FileVersionInfo.GetVersionInfo(executable);
        // An older installer does not know our COM registrations. Do not duplicate its static
        // menus if the application was replaced with a pre-handler version.
        if (version.FileMajorPart < 1 || (version.FileMajorPart == 1 && version.FileMinorPart < 23)) return new(executable, []);
        return Read(root, executable, paths);
    }

    internal static MenuSnapshot Read(RegistryKey root, string executable, IReadOnlyList<string> paths)
    {
        if (!MenuCommands.IsSupportedSelection(paths) || root.GetValue("SchemaVersion") is not int schema || schema != MenuCommands.SchemaVersion
            || root.GetValue("ExecutablePath") is not string owner
            || !string.Equals(owner, executable, StringComparison.OrdinalIgnoreCase)) return new(executable, []);
        var commands = new List<VisibleCommand>();
        // Offer only components installed for every selected extension; never silently filter inputs.
        var extensions = paths.Select(path => Path.GetExtension(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var definition in MenuCommands.All)
        {
            if (extensions.Any(extension => !definition.Accepts(extension))) continue;
            string? caption = null, icon = null;
            var present = true;
            foreach (var extension in extensions)
            {
                using var key = root.OpenSubKey(extension + @"\shell\" + definition.Group + @"\shell\" + definition.RegistryPath);
                using var command = key?.OpenSubKey("command");
                var prefix = '"' + executable + "\" --action " + definition.Action;
                if (key?.GetValue(definition.CaptionValue) is not string text || string.IsNullOrWhiteSpace(text)
                    || command?.GetValue(null) is not string launch || !launch.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                { present = false; break; }
                caption ??= text;
                icon ??= key.GetValue("Icon") as string;
            }
            if (present && caption is not null) commands.Add(new(definition, caption, icon));
        }
        return new(executable, commands);
    }
}
