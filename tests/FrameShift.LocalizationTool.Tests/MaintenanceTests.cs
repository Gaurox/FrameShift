using System.Text.Json;
using System.Xml.Linq;
using FrameShift.LocalizationTool;
using Xunit;

namespace FrameShift.LocalizationTool.Tests;

public sealed class MaintenanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FrameShift maintenance été " + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "src/FrameShift/Core/Localization/Strings.resx");
    private string Baseline => Path.Combine(_root, "baseline.json");

    public MaintenanceTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
        Directory.CreateDirectory(Path.Combine(_root, "installer"));
        Directory.CreateDirectory(Path.Combine(_root, "docs"));
        foreach (var lang in new[] { "", ".es" })
            new XDocument(new XElement("root", new[] { "Changed", "Removed", "Context", "Translation", "Blank" }.Select(key =>
                new XElement("data", new XAttribute("name", key), new XElement("value", key + (lang == "" ? "" : " español")), new XElement("comment", "note")))))
                .Save(Path.Combine(Path.GetDirectoryName(Source)!, "Strings" + lang + ".resx"));
        File.WriteAllText(Path.Combine(_root, "installer/FrameShift.iss"), "[CustomMessages]\nenglish.AppLanguage=en\nspanish.AppLanguage=es\nenglish.Caption=Caption\nspanish.Caption=Título\n[Code]\n");
        File.WriteAllText(Path.Combine(_root, "docs/TRANSLATING.md"), "Guide français 日本語");
        File.WriteAllText(Path.Combine(_root, "docs/TRANSLATION_GLOSSARY.md"), "Frame = video image");
        TranslationMaintenance.Snapshot(_root, "es", Baseline);
    }

    [Fact]
    public void Review_DistinguishesChangesAndDoesNotModifyInputs()
    {
        var xml = XDocument.Load(Source);
        xml.Root!.Elements("data").Single(e => (string)e.Attribute("name")! == "Changed").Element("value")!.Value = "New English {0}";
        xml.Root.Elements("data").Single(e => (string)e.Attribute("name")! == "Removed").Remove();
        xml.Root.Elements("data").Single(e => (string)e.Attribute("name")! == "Context").Element("comment")!.Value = "new context";
        xml.Root.Add(new XElement("data", new XAttribute("name", "Added"), new XElement("value", "New"))); xml.Save(Source);
        var targetPath = Path.Combine(Path.GetDirectoryName(Source)!, "Strings.es.resx");
        var target = XDocument.Load(targetPath);
        target.Root!.Elements("data").Single(e => (string)e.Attribute("name")! == "Translation").Element("value")!.Value = "Updated";
        target.Root.Elements("data").Single(e => (string)e.Attribute("name")! == "Blank").Element("value")!.Value = ""; target.Save(targetPath);
        var sourceBefore = File.ReadAllBytes(Source); var baselineBefore = File.ReadAllBytes(Baseline);
        var review = TranslationMaintenance.Review(_root, Baseline);
        foreach (var status in new[] { "new", "removed", "source-changed", "context-changed", "translation-changed", "blank" })
            Assert.Contains(review.Items, i => i.Status == status);
        Assert.Equal(sourceBefore, File.ReadAllBytes(Source)); Assert.Equal(baselineBefore, File.ReadAllBytes(Baseline));
    }

    [Fact]
    public void Bundle_IncludesGuidesSnapshotAndReview_WithoutOverwriting()
    {
        var folder = Path.Combine(_root, "contribution");
        TranslationMaintenance.Bundle(_root, "es", folder, Baseline);
        foreach (var name in new[] { "translation.csv", "START_HERE.txt", "GLOSSARY.md", "FEEDBACK.txt", "snapshot.json", "review.json", "READY.txt" })
            Assert.True(File.Exists(Path.Combine(folder, name)), name);
        Assert.Empty(TranslationMaintenance.Review(_root, Path.Combine(folder, "snapshot.json")).Items);
        Assert.Equal(0, TranslationExchange.Check(_root, Path.Combine(folder, "translation.csv"), true).Errors);
        Assert.Throws<IOException>(() => TranslationMaintenance.Bundle(_root, "es", folder, Baseline));
        Assert.Throws<IOException>(() => TranslationMaintenance.Snapshot(_root, "es", Baseline));
    }

    [Theory]
    [InlineData("qaa")]
    [InlineData("en")]
    public void Bundle_RejectsUnavailableOrSourceLanguage(string language)
    {
        var folder = Path.Combine(_root, "rejected");
        Assert.Throws<ArgumentException>(() => TranslationMaintenance.Bundle(_root, language, folder, null));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Review_RejectsDuplicateOrUnknownSnapshotSchema()
    {
        var snapshot = JsonSerializer.Deserialize<CatalogSnapshot>(File.ReadAllText(Baseline))!;
        var invalid = Path.Combine(_root, "invalid.json");
        TranslationMaintenance.WriteJson(invalid, snapshot with { Version = 99 });
        Assert.Throws<FormatException>(() => TranslationMaintenance.Review(_root, invalid));
        File.Delete(invalid);
        TranslationMaintenance.WriteJson(invalid, snapshot with { Entries = [snapshot.Entries[0], snapshot.Entries[0]] });
        Assert.Throws<ArgumentException>(() => TranslationMaintenance.Review(_root, invalid));
    }
    public void Dispose() => Directory.Delete(_root, true);
}
