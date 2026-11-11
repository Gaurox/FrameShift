using System.Text;
using System.Xml.Linq;
using FrameShift.LocalizationTool;
using Xunit;

namespace FrameShift.LocalizationTool.Tests;

public sealed class ExchangeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FrameShift traduction été 日本 " + Guid.NewGuid().ToString("N"));
    private string Csv => Path.Combine(_root, "traduction.csv");

    public ExchangeTests()
    {
        var resources = Path.Combine(_root, "src/FrameShift/Core/Localization");
        Directory.CreateDirectory(resources);
        Directory.CreateDirectory(Path.Combine(_root, "installer"));
        foreach (var (file, text) in new[] { ("Strings.resx", "Hello {0:0.0}; \"world\"\nSecond line"), ("Strings.es.resx", "Hola {0:0.0}; \"mundo\"\nSegunda línea") })
            new XDocument(new XElement("root", new XElement("data", new XAttribute("name", "Greeting"), new XElement("value", text), new XElement("comment", "A greeting with a measured value."))))
                .Save(Path.Combine(resources, file));
        File.WriteAllText(Path.Combine(_root, "installer/FrameShift.iss"), "[Languages]\n[CustomMessages]\nenglish.AppLanguage=en\nspanish.AppLanguage=es\nenglish.Size=Size: %1{#InstallerSpace}\nspanish.Size=Tamaño: %1\n[Code]\nThis is not a translation\n");
    }

    [Fact]
    public void ExportAndCheck_PreserveUnicodeMultilineQuotesAndContext_WithoutSourceWrites()
    {
        var before = SourceBytes();
        TranslationExchange.Export(_root, "es", Csv);
        Assert.Equal(new byte[] { 239, 187, 191 }, File.ReadAllBytes(Csv).Take(3));
        var rows = CsvFile.Read(Csv);
        Assert.Equal(3, rows.Length); // Header + app + installer; configuration excluded.
        Assert.Contains('\n', rows[1][3]);
        Assert.Contains("\"mundo\"", rows[1][4]);
        Assert.Equal("A greeting with a measured value.", rows[1][5]);
        Assert.DoesNotContain(rows.Skip(1), r => r[2] == "AppLanguage");
        Assert.Equal(0, TranslationExchange.Check(_root, Csv, true).Errors);
        Assert.Equal(before, SourceBytes());
        Assert.Throws<IOException>(() => TranslationExchange.Export(_root, "es", Csv));
    }

    [Fact]
    public void Csv_RoundTripsEverySignificantCharacterAndEmptyField()
    {
        string[][] rows = [["", "a;b", "a\"b", "a\rb\nc\r\nd", " 日本語 ", ""], ["{{{0}}}", "last"]];
        CsvFile.Write(Csv, rows);
        var restored = CsvFile.Read(Csv);
        Assert.Equal(rows.Length, restored.Length);
        for (var i = 0; i < rows.Length; i++) Assert.Equal(rows[i], restored[i]);
    }

    [Theory]
    [InlineData("\"unclosed")]
    [InlineData("abc\"def")]
    [InlineData("\"closed\"unexpected")]
    public void Csv_RejectsMalformedQuoting(string value)
    {
        File.WriteAllText(Csv, value, Encoding.UTF8);
        Assert.Throws<FormatException>(() => CsvFile.Read(Csv));
    }

    [Fact]
    public void ReorderedRowsAndArguments_AreAccepted()
    {
        TranslationExchange.Export(_root, "es", Csv);
        Change(rows => { var first = rows[1]; rows[1] = rows[2]; rows[2] = first; });
        Assert.Equal(0, TranslationExchange.Check(_root, Csv, true).Errors);
        Assert.Equal(TranslationTokens.Read("app", "{0} {{literal}} {1:0.00}"), TranslationTokens.Read("app", "{1:0.00} {{texte}} {0}"));
        Assert.Equal(new[] { "{0}" }, TranslationTokens.Read("app", "{{{0}}}"));
    }

    [Theory]
    [InlineData("missing", "Reference missing")]
    [InlineData("duplicate", "Duplicate")]
    [InlineData("unknown", "Unknown")]
    [InlineData("source", "English source changed")]
    [InlineData("hash", "English source changed")]
    [InlineData("language", "Language differs")]
    [InlineData("format", "Variables or their formats")]
    [InlineData("braces", "Invalid .NET")]
    [InlineData("inno", "Variables or their formats")]
    [InlineData("columns", "Wrong number")]
    [InlineData("translation-hash", "Invalid original translation hash")]
    public void Check_ReportsDamagedContributions_WithoutChangingFiles(string damage, string expected)
    {
        TranslationExchange.Export(_root, "es", Csv);
        Change(rows =>
        {
            switch (damage)
            {
                case "missing": rows.RemoveAt(2); break;
                case "duplicate": rows.Add(rows[1].ToArray()); break;
                case "unknown": rows[1][2] = "Unknown"; break;
                case "source": rows[1][3] = "Edited English"; break;
                case "hash": rows[1][7] = "BAD"; break;
                case "language": rows[2][0] = "ru"; break;
                case "format": rows[1][4] = "Hola {0}"; break;
                case "braces": rows[1][4] = "Hola {"; break;
                case "inno": rows[2][4] = "Tamaño: %2"; break;
                case "columns": rows[1] = ["es"]; break;
                case "translation-hash": rows[1][8] = "invalid"; break;
            }
        });
        var before = SourceBytes(); var csvBefore = File.ReadAllBytes(Csv);
        var report = TranslationExchange.Check(_root, Csv, true);
        Assert.True(report.Errors > 0);
        Assert.Contains(report.Issues, i => i.Message.Contains(expected));
        Assert.Equal(before, SourceBytes()); Assert.Equal(csvBefore, File.ReadAllBytes(Csv));
    }

    [Fact]
    public void BlankTranslation_IsIncomplete_AndStrictModeFails()
    {
        TranslationExchange.Export(_root, "es", Csv);
        Change(rows => rows[1][4] = "");
        var report = TranslationExchange.Check(_root, Csv, false);
        Assert.Equal(0, report.Errors); Assert.Equal(1, report.Warnings); Assert.Equal(1, report.BlankTranslations);
        Assert.Equal(1, TranslationExchange.Check(_root, Csv, true).Errors);
    }

    [Fact]
    public void ChangedSourceSinceExport_IsDetected()
    {
        TranslationExchange.Export(_root, "es", Csv);
        var source = Path.Combine(_root, "src/FrameShift/Core/Localization/Strings.resx");
        var xml = XDocument.Load(source); xml.Root!.Element("data")!.Element("value")!.Value = "Changed {0:0.0}"; xml.Save(source);
        Assert.Contains(TranslationExchange.Check(_root, Csv, true).Issues, i => i.Message.Contains("English source changed"));
    }

    [Fact]
    public void UnsupportedLanguage_IsNotCreatedOrEnabled()
    {
        Assert.Throws<ArgumentException>(() => TranslationExchange.Export(_root, "qaa", Csv));
        Assert.False(File.Exists(Csv));
    }

    [Fact]
    public void Chinese_MultipleCultureAliases_UseInstallerAppLanguageMapping()
    {
        var folder = Path.Combine(_root, "src/FrameShift/Core/Localization");
        File.Copy(Path.Combine(folder, "Strings.es.resx"), Path.Combine(folder, "Strings.zh-CN.resx"));
        var installer = Path.Combine(_root, "installer/FrameShift.iss");
        File.WriteAllText(installer, File.ReadAllText(installer).Replace("[Code]", "chinesesimp.AppLanguage=zh-CN\nchinesesimp.Size=大小：%1\n[Code]"));
        TranslationExchange.Export(_root, "zh-CN", Csv);
        Assert.Equal(0, TranslationExchange.Check(_root, Csv, true).Errors);
    }

    [Fact]
    public void Tokens_PreserveRepetitionsAndInnoConstants()
    {
        Assert.Equal(new[] { "{0}", "{0}" }, TranslationTokens.Read("app", "{0} {0}"));
        Assert.Equal(new[] { "%1", "%n", "{app}" }, TranslationTokens.Read("installer", "%1%n{app}{#InstallerSpace}"));
    }

    private void Change(Action<List<string[]>> change)
    {
        var rows = CsvFile.Read(Csv).ToList(); change(rows); File.Delete(Csv); CsvFile.Write(Csv, rows);
    }
    private byte[] SourceBytes() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Where(p => p != Csv)
        .Order(StringComparer.Ordinal).SelectMany(File.ReadAllBytes).ToArray();
    public void Dispose() => Directory.Delete(_root, true);
}
