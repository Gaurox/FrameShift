using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using FrameShift.Core.Localization;

namespace FrameShift.LocalizationTool;

internal sealed record TranslationEntry(string Scope, string Key, string Source, string Translation, string Context)
{
    internal string Identity => Scope + ":" + Key;
    internal string SourceHash => Hash(Source);
    internal string TranslationHash => Hash(Translation);
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

internal static class TranslationCatalog
{
    internal static string[] Languages => AppLanguages.Supported.Select(l => l.Code).ToArray();

    internal static bool Accepts(string repo, string language, string? draftDirectory) => draftDirectory is null
        ? Languages.Contains(language) : DraftLanguage.Open(repo, draftDirectory).Language == language;

    internal static (string Resource, string Installer, string InstallerLanguage) Target(string repo, string language, string? draftDirectory)
    {
        if (draftDirectory is not null)
        {
            draftDirectory = Path.GetFullPath(draftDirectory);
            var draft = DraftLanguage.Open(repo, draftDirectory);
            if (draft.Language != language) throw new ArgumentException("Draft language differs from the requested language.");
            return (draft.ResourcePath(draftDirectory), draft.InstallerPath(draftDirectory), draft.InstallerLanguage);
        }
        var selected = AppLanguages.Supported.SingleOrDefault(l => l.Code == language)
            ?? throw new ArgumentException("Choose an existing language: " + string.Join(", ", Languages));
        var messages = InstallerMessages.Read(Path.Combine(repo, "installer", "FrameShift.iss"));
        var installerNames = messages.Where(p => p.Key.Key == "AppLanguage" && p.Value == selected.Code).Select(p => p.Key.Language).ToArray();
        if (installerNames.Length != 1) throw new FormatException("Expected exactly one installer AppLanguage mapping for " + selected.Code);
        return (Path.Combine(repo, "src", "FrameShift", "Core", "Localization", language == "en" ? "Strings.resx" : $"Strings.{language}.resx"),
            Path.Combine(repo, "installer", "Languages", $"FrameShift.{language}.iss"), installerNames[0]);
    }

    internal static List<TranslationEntry> Load(string repo, string language, string? draftDirectory = null)
    {
        var targetFiles = Target(repo, language, draftDirectory);
        var folder = Path.Combine(repo, "src", "FrameShift", "Core", "Localization");
        var source = ReadResx(Path.Combine(folder, "Strings.resx"));
        var target = language == "en" ? source : ReadResx(targetFiles.Resource);
        var entries = source.Select(pair => new TranslationEntry("app", pair.Key, pair.Value.Value,
            target.GetValueOrDefault(pair.Key).Value ?? "", pair.Value.Comment ?? $"Application area: {pair.Key.Split('_')[0]}. No translator note yet.")).ToList();

        // Read-only access to the existing installer. AppLanguage is configuration,
        // not a sentence to translate. No .isl file or Pascal code is exported.
        var messages = InstallerMessages.Read(Path.Combine(repo, "installer", "FrameShift.iss"));
        var targetMessages = draftDirectory is null ? messages : InstallerMessages.ReadCustomMessages(targetFiles.Installer);
        var installerName = targetFiles.InstallerLanguage;
        if (draftDirectory is not null && (targetMessages.Any(p => p.Key.Language != installerName)
            || targetMessages.GetValueOrDefault((installerName, "AppLanguage")) != language))
            throw new FormatException("Draft installer messages do not match the draft language.");
        foreach (var pair in messages.Where(p => p.Key.Language == "english" && p.Key.Key != "AppLanguage"))
            entries.Add(new("installer", pair.Key.Key, pair.Value,
                targetMessages.GetValueOrDefault((installerName, pair.Key.Key), ""), "FrameShift installer custom message. Preserve Inno Setup tokens."));
        return entries.OrderBy(e => e.Scope, StringComparer.Ordinal).ThenBy(e => e.Key, StringComparer.Ordinal).ToList();
    }

    private static Dictionary<string, (string Value, string? Comment)> ReadResx(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var root = XDocument.Load(reader).Root ?? throw new FormatException("Empty resource file: " + path);
        return root.Elements("data").ToDictionary(e => (string?)e.Attribute("name") ?? throw new FormatException("Resource key missing."),
            e => ((string?)e.Element("value") ?? throw new FormatException("Resource value missing."), (string?)e.Element("comment")), StringComparer.Ordinal);
    }

}
