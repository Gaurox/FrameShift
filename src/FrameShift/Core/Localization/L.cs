using System.Globalization;
using System.Resources;

namespace FrameShift.Core.Localization;

// Only display text uses these resources. CLI options, model IDs, protocols,
// persisted values and FFmpeg arguments always retain their invariant values.
internal static class L
{
    private static readonly ResourceManager Resources = new("FrameShift.Core.Localization.Strings", typeof(L).Assembly);

    internal static string NormalizeLanguage(string? language) => AppLanguages.Resolve(language).Code;

    internal static void Initialize(string? language)
    {
        var culture = CultureInfo.GetCultureInfo(NormalizeLanguage(language));
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    internal static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new MissingManifestResourceException($"Missing FrameShift display resource: {key}");

    internal static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);
}
