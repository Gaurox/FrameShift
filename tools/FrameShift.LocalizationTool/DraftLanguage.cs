using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using FrameShift.Core.Localization;

namespace FrameShift.LocalizationTool;

internal sealed record DraftLanguage(int Version, string Language)
{
    internal string InstallerLanguage => Language.Replace('-', '_');
    internal string ResourcePath(string directory) => Path.Combine(directory, $"Strings.{Language}.resx");
    internal string InstallerPath(string directory) => Path.Combine(directory, $"FrameShift.{Language}.iss");

    internal static void Create(string repo, string language, string directory)
    {
        ValidateLanguage(language);
        directory = ValidateDirectory(repo, directory);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Draft directory must be new.");
        // Validate the English catalog before creating anything. No English values
        // are copied into the draft: an untranslated entry must remain blank.
        TranslationCatalog.Load(repo, "en");
        var source = Path.Combine(repo, "src/FrameShift/Core/Localization/Strings.resx");
        using var reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var xml = XDocument.Load(reader);
        xml.Root!.Elements("data").Remove();
        var draft = new DraftLanguage(1, language);
        Directory.CreateDirectory(directory);
        using (var stream = new FileStream(draft.ResourcePath(directory), FileMode.CreateNew, FileAccess.Write)) xml.Save(stream);
        using (var writer = new StreamWriter(new FileStream(draft.InstallerPath(directory), FileMode.CreateNew, FileAccess.Write)))
            writer.Write($"; Draft only. Not included by the installer.\n{draft.InstallerLanguage}.AppLanguage={language}\n");
        // Metadata is the final marker; an incomplete directory cannot be opened.
        TranslationMaintenance.WriteJson(Path.Combine(directory, "draft.json"), draft);
    }

    internal static DraftLanguage Open(string repo, string directory)
    {
        directory = ValidateDirectory(repo, directory);
        var metadata = Path.Combine(directory, "draft.json");
        RejectReparsePoints(metadata);
        var draft = JsonSerializer.Deserialize<DraftLanguage>(File.ReadAllText(metadata))
            ?? throw new FormatException("Empty draft metadata.");
        if (draft.Version != 1) throw new FormatException("Unsupported draft version.");
        ValidateLanguage(draft.Language);
        foreach (var path in new[] { draft.ResourcePath(directory), draft.InstallerPath(directory) })
        {
            RejectReparsePoints(path);
            if (!File.Exists(path)) throw new IOException("Incomplete draft: missing " + path);
        }
        return draft;
    }

    private static void ValidateLanguage(string language)
    {
        if (string.IsNullOrEmpty(language) || !Regex.IsMatch(language, @"\A[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*\z")
            || CultureInfo.GetCultureInfo(language).Name != language)
            throw new ArgumentException("Use a canonical culture code, for example fr, ja or id.");
        if (AppLanguages.Supported.Any(l => l.Code == language || l.Aliases.Contains(language, StringComparer.OrdinalIgnoreCase))
            || TranslationCatalog.Languages.Contains(CultureInfo.GetCultureInfo(language).TwoLetterISOLanguageName))
            throw new ArgumentException("This language is already registered. Use the existing-language workflow.");
    }

    private static string ValidateDirectory(string repo, string directory)
    {
        directory = Path.GetFullPath(directory);
        var root = Path.GetFullPath(repo);
        foreach (var folder in new[] { "src", "installer", "references", ".git", ".agents", ".codex" })
        {
            var excluded = Path.Combine(root, folder);
            if (directory.Equals(excluded, StringComparison.OrdinalIgnoreCase)
                || directory.StartsWith(excluded + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Drafts must stay outside runtime sources, installer, references and repository metadata.");
        }
        RejectReparsePoints(directory);
        return directory;
    }

    private static void RejectReparsePoints(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Draft paths must not use symbolic links or directory junctions: " + current);
    }
}
