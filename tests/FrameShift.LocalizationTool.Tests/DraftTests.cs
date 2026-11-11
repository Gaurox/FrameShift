using System.Text.Json;
using System.Xml.Linq;
using FrameShift.LocalizationTool;
using Xunit;

namespace FrameShift.LocalizationTool.Tests;

public sealed class DraftTests : IDisposable
{
    private const string Language = "qaa"; // Reserved for local use; no real translation is introduced.
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "FrameShift draft été 日本 " + Guid.NewGuid().ToString("N"));
    private string Draft => Path.Combine(_repo, "translations/drafts", Language);
    private string Csv => Path.Combine(_repo, "contribution.csv");
    private string Backup => Path.Combine(_repo, "backup");
    private string Resource => Path.Combine(Draft, $"Strings.{Language}.resx");
    private string Messages => Path.Combine(Draft, $"FrameShift.{Language}.iss");

    public DraftTests()
    {
        var source = Path.Combine(_repo, "src/FrameShift/Core/Localization/Strings.resx");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        new XDocument(new XElement("root", new[] { ("Greeting", "Hello {0}"), ("Caption", "Caption"), ("Other", "Other") }
            .Select(p => new XElement("data", new XAttribute("name", p.Item1), new XElement("value", p.Item2), new XElement("comment", "Useful context")))))
            .Save(source);
        Directory.CreateDirectory(Path.Combine(_repo, "installer/Languages"));
        File.WriteAllText(Path.Combine(_repo, "installer/FrameShift.iss"), "[CustomMessages]\n#include \"Languages\\FrameShift.en.iss\"\n[Code]\n");
        File.WriteAllText(Path.Combine(_repo, "installer/Languages/FrameShift.en.iss"), "english.AppLanguage=en\nenglish.Caption=Caption\nenglish.Size=Size %1\n");
        Directory.CreateDirectory(Path.Combine(_repo, "tools/FrameShift.LocalizationTool"));
        File.WriteAllText(Path.Combine(_repo, "tools/FrameShift.LocalizationTool/ExplorerMessageLinks.json"), "{\"Caption\":\"Caption\"}");
        Directory.CreateDirectory(Path.Combine(_repo, "docs"));
        File.WriteAllText(Path.Combine(_repo, "docs/TRANSLATING.md"), "Translator guide");
        File.WriteAllText(Path.Combine(_repo, "docs/TRANSLATION_GLOSSARY.md"), "Glossary");
    }

    [Fact]
    public void Prepare_CreatesOnlyEmptyDraftFiles_WithoutRegisteringALanguage()
    {
        var before = ProtectedFiles();
        DraftLanguage.Create(_repo, Language, Draft);
        Assert.Equal(3, Directory.GetFiles(Draft).Length);
        Assert.Empty(XDocument.Load(Resource).Root!.Elements("data"));
        Assert.Single(InstallerMessages.ReadCustomMessages(Messages));
        Assert.Equal(Language, DraftLanguage.Open(_repo, Draft).Language);
        Assert.DoesNotContain(Language, TranslationCatalog.Languages);
        AssertProtected(before);
        var draftBefore = File.ReadAllBytes(Resource);
        Assert.Throws<IOException>(() => DraftLanguage.Create(_repo, Language, Draft));
        Assert.Equal(draftBefore, File.ReadAllBytes(Resource));
    }

    [Fact]
    public void ExportAndCheck_BlanksAreIncomplete_AndDraftIsAlwaysExplicit()
    {
        CreateExport();
        var rows = CsvFile.Read(Csv);
        Assert.Equal(6, rows.Length);
        Assert.All(rows.Skip(1), row => { Assert.Equal(Language, row[0]); Assert.Empty(row[4]); });
        Assert.DoesNotContain(rows.Skip(1), row => row[2] == "AppLanguage");
        var check = TranslationExchange.Check(_repo, Csv, false, Draft);
        Assert.Equal(0, check.Errors); Assert.Equal(5, check.BlankTranslations);
        Assert.Equal(5, TranslationExchange.Check(_repo, Csv, true, Draft).Errors);
        Assert.True(TranslationExchange.Check(_repo, Csv, false).Errors > 0);
        Assert.True(TranslationImport.Prepare(_repo, Csv).Report.Errors > 0);
        Assert.Throws<ArgumentException>(() => TranslationExchange.Export(_repo, Language, Path.Combine(_repo, "rejected.csv")));
        var plan = TranslationImport.Prepare(_repo, Csv, Draft);
        Assert.Empty(plan.Files); plan.Apply(Backup); Assert.False(Directory.Exists(Backup));
    }

    [Fact]
    public void Import_PreviewsAndAppliesOnlyDraftFiles_WithBackupsAndLinkedCaptions()
    {
        CreateExport();
        Edit("app", "Greeting", "Test été 日本 {0}; \"quoted\"\nNext line");
        Edit("app", "Caption", "Shared test caption");
        var before = ProtectedFiles(); var resourceBefore = File.ReadAllBytes(Resource); var messagesBefore = File.ReadAllBytes(Messages);
        var plan = TranslationImport.Prepare(_repo, Csv, Draft);
        Assert.Equal(0, plan.Report.Errors); Assert.Equal(3, plan.Changes.Count); Assert.Equal(2, plan.Files.Count);
        Assert.All(plan.Files, file => Assert.Equal(Path.GetFullPath(Draft), Path.GetDirectoryName(file.Path)));
        Assert.Equal(resourceBefore, File.ReadAllBytes(Resource)); Assert.Equal(messagesBefore, File.ReadAllBytes(Messages));
        plan.Apply(Backup);
        Assert.Contains("qaa.Caption=Shared test caption", File.ReadAllText(Messages));
        Assert.DoesNotContain(XDocument.Load(Resource).Root!.Elements("data"), e => (string?)e.Attribute("name") == "Other");
        Assert.Equal(resourceBefore, File.ReadAllBytes(Path.Combine(Backup, "000.original")));
        Assert.Equal(messagesBefore, File.ReadAllBytes(Path.Combine(Backup, "001.original")));
        AssertProtected(before);
        var export = Path.Combine(_repo, "updated.csv"); TranslationExchange.Export(_repo, Language, export, Draft);
        Assert.Equal("Shared test caption", CsvFile.Read(export).Single(row => row[1] == "app" && row[2] == "Caption")[4]);
        Assert.Equal("Test été 日本 {0}; \"quoted\"\nNext line", CsvFile.Read(export).Single(row => row[1] == "app" && row[2] == "Greeting")[4]);
    }

    [Fact]
    public void BundleSnapshotAndReview_WorkForDraftsWithoutActivatingThem()
    {
        DraftLanguage.Create(_repo, Language, Draft);
        var snapshot = Path.Combine(_repo, "baseline.json");
        TranslationMaintenance.Snapshot(_repo, Language, snapshot, Draft);
        var bundle = Path.Combine(_repo, "bundle");
        TranslationMaintenance.Bundle(_repo, Language, bundle, snapshot, Draft);
        Assert.Contains("Draft (not enabled in FrameShift): True", File.ReadAllText(Path.Combine(bundle, "READY.txt")));
        Assert.True(File.Exists(Path.Combine(bundle, "review.json")));
        Assert.Equal(0, TranslationExchange.Check(_repo, Path.Combine(bundle, "translation.csv"), false, Draft).Errors);
        Assert.Equal(5, TranslationMaintenance.Review(_repo, snapshot, Draft).Items.Count(i => i.Status == "blank"));
        Assert.Throws<FormatException>(() => TranslationMaintenance.Review(_repo, snapshot));
        File.WriteAllText(Messages, File.ReadAllText(Messages) + "qaa.Size=Test %1\n");
        Assert.Contains(TranslationMaintenance.Review(_repo, snapshot, Draft).Items, i => i.Reference == "installer:Size" && i.Status == "translation-changed");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("ja")]
    [InlineData("id")]
    [InlineData("en-US")]
    [InlineData("zh-Hans")]
    [InlineData("../qaa")]
    [InlineData("qaa/other")]
    [InlineData("QAA")]
    [InlineData("")]
    public void Prepare_RejectsRegisteredOrInvalidCodes_WithoutCreatingFiles(string code)
    {
        Assert.ThrowsAny<ArgumentException>(() => DraftLanguage.Create(_repo, code, Draft));
        Assert.False(Directory.Exists(Draft));
    }

    [Theory]
    [InlineData("src")]
    [InlineData("src/FrameShift/Core/Localization/draft")]
    [InlineData("installer/Languages/draft")]
    [InlineData("references/draft")]
    [InlineData(".git/draft")]
    [InlineData("src/../installer/draft")]
    public void Prepare_RejectsProtectedLocations(string relative)
    {
        var before = ProtectedFiles();
        Assert.Throws<ArgumentException>(() => DraftLanguage.Create(_repo, Language, Path.Combine(_repo, relative)));
        AssertProtected(before);
    }

    [Fact]
    public void DraftLanguageMustMatchCsvSnapshotAndExplicitLanguage()
    {
        CreateExport();
        Assert.Throws<ArgumentException>(() => TranslationExchange.Export(_repo, "es", Path.Combine(_repo, "rejected.csv"), Draft));
        var rows = CsvFile.Read(Csv); foreach (var row in rows.Skip(1)) row[0] = "es";
        Assert.True(TranslationExchange.CheckRows(_repo, rows, false, Draft).Errors > 0);
        var snapshot = Path.Combine(_repo, "mismatch.json");
        TranslationMaintenance.WriteJson(snapshot, new CatalogSnapshot(1, "es", []));
        Assert.Throws<FormatException>(() => TranslationMaintenance.Review(_repo, snapshot, Draft));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("language")]
    [InlineData("missing")]
    [InlineData("message-language")]
    [InlineData("message-mapping")]
    [InlineData("message-directive")]
    public void MalformedOrIncompleteDraft_IsRejected(string kind)
    {
        DraftLanguage.Create(_repo, Language, Draft);
        if (kind == "schema") File.WriteAllText(Path.Combine(Draft, "draft.json"), "{\"Version\":99,\"Language\":\"qaa\"}");
        if (kind == "language") File.WriteAllText(Path.Combine(Draft, "draft.json"), "{\"Version\":1,\"Language\":\"../source\"}");
        if (kind == "missing") File.Delete(Resource);
        if (kind == "message-language") File.AppendAllText(Messages, "english.Caption=Wrong\n");
        if (kind == "message-mapping") File.WriteAllText(Messages, "qaa.AppLanguage=es\n");
        if (kind == "message-directive") File.AppendAllText(Messages, "#include \"somewhere.iss\"\n");
        Assert.ThrowsAny<Exception>(() => TranslationCatalog.Load(_repo, Language, Draft));
    }

    [Fact]
    public void ConcurrentDraftCorrection_BlocksOldConflictingContribution()
    {
        CreateExport(); Edit("app", "Greeting", "Contributor {0}");
        var xml = XDocument.Load(Resource);
        xml.Root!.Add(new XElement("data", new XAttribute("name", "Greeting"), new XElement("value", "Maintainer {0}"))); xml.Save(Resource);
        var plan = TranslationImport.Prepare(_repo, Csv, Draft);
        Assert.True(plan.Report.Errors > 0); Assert.Empty(plan.Files);
        Assert.Throws<InvalidOperationException>(() => plan.Apply(Backup));
    }

    [Theory]
    [InlineData("draft.json")]
    [InlineData("Strings.qaa.resx")]
    [InlineData("FrameShift.qaa.iss")]
    public void ConcurrentDraftEditAfterPreview_AbortsBeforeWriting(string name)
    {
        CreateExport(); Edit("app", "Greeting", "Test {0}");
        var plan = TranslationImport.Prepare(_repo, Csv, Draft);
        File.AppendAllText(Path.Combine(Draft, name), "\n");
        var before = File.ReadAllBytes(Resource);
        Assert.Throws<IOException>(() => plan.Apply(Backup));
        Assert.Equal(before, File.ReadAllBytes(Resource)); Assert.False(Directory.Exists(Backup));
    }

    [Fact]
    public void FailureOnSecondDraftWrite_RollsBackAndKeepsBackups()
    {
        CreateExport(); Edit("app", "Caption", "Shared caption");
        var before = File.ReadAllBytes(Resource); var protectedBefore = ProtectedFiles();
        var plan = TranslationImport.Prepare(_repo, Csv, Draft);
        Assert.Throws<IOException>(() => plan.Apply(Backup, i => { if (i == 1) throw new IOException("Simulated failure"); }));
        Assert.Equal(before, File.ReadAllBytes(Resource));
        Assert.True(File.Exists(Path.Combine(Backup, "manifest.json"))); AssertProtected(protectedBefore);
    }

    [Fact]
    public void InvalidTokensOrLinkedCaptions_BlockDraftImport()
    {
        CreateExport(); Edit("app", "Greeting", "Missing variable");
        Assert.True(TranslationImport.Prepare(_repo, Csv, Draft).Report.Errors > 0);
        Edit("app", "Greeting", "Test {0}"); Edit("app", "Caption", "One"); Edit("installer", "Caption", "Two");
        Assert.True(TranslationImport.Prepare(_repo, Csv, Draft).Report.Errors > 0);
        Assert.Empty(XDocument.Load(Resource).Root!.Elements("data"));
    }

    private void CreateExport()
    {
        DraftLanguage.Create(_repo, Language, Draft);
        TranslationExchange.Export(_repo, Language, Csv, Draft);
    }

    private void Edit(string scope, string key, string value)
    {
        var rows = CsvFile.Read(Csv); rows.Single(r => r[1] == scope && r[2] == key)[4] = value;
        File.Delete(Csv); CsvFile.Write(Csv, rows);
    }

    private Dictionary<string, byte[]> ProtectedFiles() => new[] { "src", "installer", "tools" }
        .SelectMany(folder => Directory.GetFiles(Path.Combine(_repo, folder), "*", SearchOption.AllDirectories))
        .ToDictionary(path => path, File.ReadAllBytes);

    private void AssertProtected(Dictionary<string, byte[]> before)
    {
        var after = ProtectedFiles(); Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var pair in before) Assert.Equal(pair.Value, after[pair.Key]);
    }

    public void Dispose() => Directory.Delete(_repo, true);
}
