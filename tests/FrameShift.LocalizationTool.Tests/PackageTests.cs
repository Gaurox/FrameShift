using FrameShift.LocalizationTool;
using Xunit;

namespace FrameShift.LocalizationTool.Tests;

public sealed class PackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FrameShift langues paquet " + Guid.NewGuid().ToString("N"));
    private readonly string _repo;
    public PackageTests()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src/FrameShift/Core/Localization/Strings.resx"))) dir = dir.Parent;
        _repo = dir?.FullName ?? throw new IOException("Repository not found.");
        var source = Path.Combine(AppContext.BaseDirectory, "LanguagePackage");
        foreach (var path in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_root, Path.GetRelativePath(source, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(path, target);
        }
    }

    [Fact]
    public void BothComponents_ContainExactTextsForEveryRegisteredLanguage()
    {
        var report = PackageVerifier.Verify(_repo, _root);
        Assert.Empty(report.Issues); Assert.Equal(2 * TranslationCatalog.Languages.Length, report.LanguagesChecked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingSatellite_CannotBeHiddenByEnglishFallback(bool worker)
    {
        var folder = worker ? Path.Combine(_root, "Workers/CreateSubtitlesWorker") : _root;
        File.Delete(Path.Combine(folder, "es", worker ? "FrameShift.SubtitlesWorker.resources.dll" : "FrameShift.resources.dll"));
        var report = PackageVerifier.Verify(_repo, _root);
        Assert.Contains(report.Issues, i => i.Language == "es" && i.Message.Contains("satellite missing"));
    }

    [Fact]
    public void OutdatedSource_IsReportedWithoutChangingThePackage()
    {
        var repoCopy = Path.Combine(_root, "source");
        var folder = Path.Combine(repoCopy, "src/FrameShift/Core/Localization"); Directory.CreateDirectory(folder);
        foreach (var path in Directory.GetFiles(Path.Combine(_repo, "src/FrameShift/Core/Localization"), "Strings*.resx")) File.Copy(path, Path.Combine(folder, Path.GetFileName(path)));
        var source = Path.Combine(folder, "Strings.es.resx");
        var xml = System.Xml.Linq.XDocument.Load(source); xml.Root!.Elements("data").First().Element("value")!.Value = "Changed source text"; xml.Save(source);
        var before = File.ReadAllBytes(Path.Combine(_root, "FrameShift.dll"));
        var report = PackageVerifier.Verify(repoCopy, _root);
        Assert.Equal(2, report.Issues.Count(i => i.Language == "es" && i.Message.Contains("outdated")));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_root, "FrameShift.dll")));
    }
    public void Dispose() => Directory.Delete(_root, true);
}
