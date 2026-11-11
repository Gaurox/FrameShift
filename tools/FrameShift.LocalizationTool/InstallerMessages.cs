using System.Text.RegularExpressions;

namespace FrameShift.LocalizationTool;

internal static class InstallerMessages
{
    internal static string ReadSource(string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var inMessages = false;
        var lines = new List<string>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.TrimStart().StartsWith('[')) inMessages = line.Trim() == "[CustomMessages]";
            if (inMessages && line.TrimStart().StartsWith("#include", StringComparison.Ordinal))
            {
                var match = Regex.Match(line, "^#include \"(Languages[\\\\/]FrameShift\\.[a-zA-Z-]+\\.iss)\"$");
                if (!match.Success) throw new FormatException("Unexpected custom-message include: " + line);
                lines.AddRange(File.ReadAllLines(Path.Combine(folder, match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar))));
            }
            else lines.Add(line);
        }
        return string.Join('\n', lines) + "\n";
    }

    internal static Dictionary<(string Language, string Key), string> Read(string path)
    {
        var messages = new Dictionary<(string, string), string>();
        var inMessages = false;
        foreach (var line in ReadSource(path).Split('\n'))
        {
            if (line.TrimStart().StartsWith('[')) { inMessages = line.Trim() == "[CustomMessages]"; continue; }
            if (!inMessages || string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(';')) continue;
            var match = Regex.Match(line, @"^(\w+)\.(\w+)=(.*)$");
            if (!match.Success) throw new FormatException("Unsupported installer message syntax: " + line);
            messages.Add((match.Groups[1].Value, match.Groups[2].Value), match.Groups[3].Value);
        }
        return messages;
    }
}
