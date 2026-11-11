using System.Text;
using System.Xml.Linq;
using FrameShift.LocalizationTool;
using Xunit;

namespace FrameShift.LocalizationTool.Tests;

public sealed class ImportTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "FrameShift import été 中文 " + Guid.NewGuid().ToString("N"));
    private string Csv => Path.Combine(_repo, "es.csv");
    private string Backup => Path.Combine(_repo, "backup");
    private string Resource => Path.GetFullPath(Path.Combine(_repo, "src/FrameShift/Core/Localization/Strings.es.resx"));
    private string Installer => Path.GetFullPath(Path.Combine(_repo, "installer/Languages/FrameShift.es.iss"));

    public ImportTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Resource)!);
        Directory.CreateDirectory(Path.GetDirectoryName(Installer)!);
        Directory.CreateDirectory(Path.Combine(_repo, "tools/FrameShift.LocalizationTool"));
        File.WriteAllText(Path.Combine(_repo, "tools/FrameShift.LocalizationTool/ExplorerMessageLinks.json"), "{\"Caption\":\"Caption\"}");
        foreach (var (name, values) in new[] { ("Strings.resx", new[] { "Hello {0}", "Caption", "Other" }), ("Strings.es.resx", new[] { "Hola {0}", "Título", "Otro" }) })
        {
            var keys = new[] { "Greeting", "Caption", "Other" };
            new XDocument(new XElement("root", keys.Select((key, i) => new XElement("data", new XAttribute("name", key),
                new XAttribute(XNamespace.Xml + "space", "preserve"), new XElement("value", values[i]), new XElement("comment", "Keep this note")))))
                .Save(Path.Combine(Path.GetDirectoryName(Resource)!, name));
        }
        File.WriteAllText(Path.Combine(_repo, "installer/FrameShift.iss"), "[Languages]\n[CustomMessages]\n#include \"Languages\\FrameShift.en.iss\"\n#include \"Languages\\FrameShift.es.iss\"\n[Code]\n// unchanged logic\n");
        File.WriteAllText(Path.Combine(_repo, "installer/Languages/FrameShift.en.iss"), "english.AppLanguage=en\nenglish.Caption=Caption\nenglish.Size=Size %1\n");
        File.WriteAllText(Installer, "; Keep this comment\nspanish.AppLanguage=es\nspanish.Caption=Título\nspanish.Size=Tamaño %1\n");
        TranslationExchange.Export(_repo, "es", Csv);
    }

    [Fact]
    public void NoOp_PreviewAndApplyLeaveFilesByteIdentical()
    {
        var before = Snapshot();
        var plan = TranslationImport.Prepare(_repo, Csv);
        Assert.Equal(0, plan.Report.Errors); Assert.Empty(plan.Files); Assert.Empty(plan.Changes);
        plan.Apply(Backup);
        Assert.False(Directory.Exists(Backup)); AssertSnapshot(before);
    }

    [Fact]
    public void PartialImport_SynchronizesCaptionAndPreservesBlanksCommentsAndOtherFiles()
    {
        Change("app", "Greeting", "Salut {0}; \"été\"\n<&>\rFin");
        Change("app", "Other", "");
        Change("installer", "Caption", "Nouveau titre");
        var before = Snapshot();
        var plan = TranslationImport.Prepare(_repo, Csv);
        Assert.Equal(0, plan.Report.Errors); Assert.Equal(2, plan.Files.Count); Assert.Equal(3, plan.Changes.Count);
        AssertSnapshot(before); // Preview has no effects.
        plan.Apply(Backup);
        Assert.Equal("Salut {0}; \"été\"\n<&>\rFin", Value("Greeting"));
        Assert.Equal("Otro", Value("Other")); Assert.Equal("Nouveau titre", Value("Caption"));
        Assert.Contains("spanish.Caption=Nouveau titre", File.ReadAllText(Installer));
        Assert.Contains("; Keep this comment", File.ReadAllText(Installer));
        Assert.Contains("Keep this note", File.ReadAllText(Resource));
        Assert.Equal(before[Resource], File.ReadAllBytes(Path.Combine(Backup, "000.original")));
        Assert.Equal(before[Installer], File.ReadAllBytes(Path.Combine(Backup, "001.original")));
        Assert.True(File.Exists(Path.Combine(Backup, "manifest.json")));
        foreach (var pair in before.Where(p => p.Key != Resource && p.Key != Installer)) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
    }

    [Fact]
    public void AppCaptionChange_AlsoUpdatesInstaller()
    {
        Change("app", "Caption", "Titre partagé");
        var plan = TranslationImport.Prepare(_repo, Csv); plan.Apply(Backup);
        Assert.Contains("spanish.Caption=Titre partagé", File.ReadAllText(Installer));
    }

    [Theory]
    [InlineData("conflict")]
    [InlineData("variables")]
    [InlineData("newline")]
    public void InvalidContribution_DoesNotWriteAnyFile(string kind)
    {
        if (kind == "conflict") { Change("app", "Caption", "Premier"); Change("installer", "Caption", "Deuxième"); }
        if (kind == "variables") Change("app", "Greeting", "Missing variable");
        if (kind == "newline") Change("installer", "Caption", "Text\n[Code]");
        var before = Snapshot(); var plan = TranslationImport.Prepare(_repo, Csv);
        Assert.True(plan.Report.Errors > 0); Assert.Empty(plan.Files);
        Assert.Throws<InvalidOperationException>(() => plan.Apply(Backup));
        AssertSnapshot(before); Assert.False(Directory.Exists(Backup));
    }

    [Fact]
    public void FailureOnSecondWrite_RestoresFirstFileAndRetainsBackups()
    {
        Change("app", "Greeting", "Changed {0}"); Change("installer", "Size", "Poids %1");
        var before = Snapshot(); var plan = TranslationImport.Prepare(_repo, Csv);
        Assert.Throws<IOException>(() => plan.Apply(Backup, i => { if (i == 1) throw new IOException("Simulated write failure"); }));
        AssertSnapshot(before);
        Assert.True(File.Exists(Path.Combine(Backup, "manifest.json")));
        Assert.Empty(Directory.GetFiles(_repo, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcurrentCatalogEdit_AbortsBeforeWriting(bool english)
    {
        Change("app", "Greeting", "Changed {0}");
        var plan = TranslationImport.Prepare(_repo, Csv);
        var path = english ? Path.Combine(Path.GetDirectoryName(Resource)!, "Strings.resx") : Resource;
        File.AppendAllText(path, "\n");
        var before = Snapshot();
        Assert.Throws<IOException>(() => plan.Apply(Backup));
        AssertSnapshot(before); Assert.False(Directory.Exists(Backup));
    }

    [Fact]
    public void ExistingBackupDirectory_IsNeverOverwritten()
    {
        Change("app", "Greeting", "Changed {0}"); Directory.CreateDirectory(Backup);
        File.WriteAllText(Path.Combine(Backup, "sentinel"), "keep");
        var before = Snapshot(); var plan = TranslationImport.Prepare(_repo, Csv);
        Assert.Throws<IOException>(() => plan.Apply(Backup)); AssertSnapshot(before);
    }

    [Fact]
    public void EnglishSource_CannotBeImported()
    {
        var en = Path.Combine(_repo, "en.csv"); TranslationExchange.Export(_repo, "en", en);
        Assert.True(TranslationImport.Prepare(_repo, en).Report.Errors > 0);
    }

    private void Change(string scope, string key, string value)
    {
        var rows = CsvFile.Read(Csv); rows.Single(r => r[1] == scope && r[2] == key)[4] = value;
        File.Delete(Csv); CsvFile.Write(Csv, rows);
    }
    private string Value(string key) => XDocument.Load(Resource).Root!.Elements("data").Single(e => (string?)e.Attribute("name") == key).Element("value")!.Value;
    private Dictionary<string, byte[]> Snapshot() => Directory.GetFiles(_repo, "*", SearchOption.AllDirectories).Where(p => p != Csv).ToDictionary(p => p, File.ReadAllBytes);
    private static void AssertSnapshot(Dictionary<string, byte[]> before) { foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key)); }
    public void Dispose() => Directory.Delete(_repo, true);
}
