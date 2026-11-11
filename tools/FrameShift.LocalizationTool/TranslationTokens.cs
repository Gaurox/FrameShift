using System.Text;
using System.Text.RegularExpressions;

namespace FrameShift.LocalizationTool;

internal static class TranslationTokens
{
    internal static string[] Read(string scope, string value)
    {
        if (scope == "installer")
            return Regex.Matches(value, @"\{[^}]+\}|%[1-9n]").Select(m => m.Value)
                // Existing Chinese captions deliberately omit this spacing macro.
                .Where(t => t != "{#InstallerSpace}").Order(StringComparer.Ordinal).ToArray();

        _ = CompositeFormat.Parse(value);
        var tokens = new List<string>();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] is not ('{' or '}')) continue;
            if (i + 1 < value.Length && value[i + 1] == value[i]) { i++; continue; }
            if (value[i] != '{') continue;
            var end = value.IndexOf('}', i);
            tokens.Add(value[i..(end + 1)]);
            i = end;
        }
        return tokens.Order(StringComparer.Ordinal).ToArray();
    }
}
