using System.Globalization;

namespace FrameShift.Core.Localization;

internal sealed record AppLanguage(string Code, string NativeName, params string[] Aliases);

// Register a language only when its resources and installer translation are ready.
// Native names remain readable regardless of the currently selected display language.
internal static class AppLanguages
{
    internal static AppLanguage Default { get; } = new("en", "English", "english");

    internal static IReadOnlyList<AppLanguage> Supported { get; } = Array.AsReadOnly<AppLanguage>(
    [
        Default,
        new("zh-CN", "简体中文", "zh", "zh-Hans", "chinesesimp"),
        new("es", "Español", "spanish")
    ]);

    internal static AppLanguage Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Default;
        var name = value.Trim().Replace('_', '-');
        var match = Find(name);
        if (match is not null) return match;

        try
        {
            // Regional variants use an available parent (e.g. en-GB -> en).
            // Resource files alone never enable an unregistered language.
            var culture = CultureInfo.GetCultureInfo(name);
            while (!string.IsNullOrEmpty(culture.Name))
            {
                match = Find(culture.Name);
                if (match is not null) return match;
                culture = culture.Parent;
            }
        }
        catch (CultureNotFoundException)
        {
            // Invalid preferences and installation markers fall back to English.
        }

        return Default;
    }

    private static AppLanguage? Find(string name) => Supported.FirstOrDefault(language =>
        string.Equals(language.Code, name, StringComparison.OrdinalIgnoreCase) ||
        language.Aliases.Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase)));
}
