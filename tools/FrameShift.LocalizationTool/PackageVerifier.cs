using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Runtime.Loader;
using System.Xml.Linq;

namespace FrameShift.LocalizationTool;

internal sealed record PackageIssue(string Component, string Language, string Reference, string Message);
internal sealed class PackageReport
{
    public List<PackageIssue> Issues { get; } = [];
    public int LanguagesChecked { get; set; }
    public int Errors => Issues.Count;
}

internal static class PackageVerifier
{
    internal static PackageReport Verify(string repo, string package)
    {
        var report = new PackageReport();
        foreach (var (component, assemblyName, folder) in new[]
        {
            ("app", "FrameShift", package),
            ("worker", "FrameShift.SubtitlesWorker", Path.Combine(package, "Workers/CreateSubtitlesWorker"))
        })
        {
            var main = Path.GetFullPath(Path.Combine(folder, assemblyName + ".dll"));
            if (!File.Exists(main)) { report.Issues.Add(new(component, "", "", "Main assembly missing: " + main)); continue; }
            var context = new AssemblyLoadContext("FrameShift language audit " + Guid.NewGuid(), isCollectible: true);
            context.Resolving += (_, name) =>
            {
                if (name.Name != assemblyName + ".resources" || !TranslationCatalog.Languages.Contains(name.CultureName)) return null;
                var satellite = Path.GetFullPath(Path.Combine(folder, name.CultureName!, name.Name + ".dll"));
                return File.Exists(satellite) ? context.LoadFromStream(new MemoryStream(File.ReadAllBytes(satellite))) : null;
            };
            ResourceManager? manager = null;
            try
            {
                // Read from memory so auditing never locks the published files on Windows.
                var assembly = context.LoadFromStream(new MemoryStream(File.ReadAllBytes(main)));
                manager = new ResourceManager("FrameShift.Core.Localization.Strings", assembly);
                foreach (var language in TranslationCatalog.Languages)
                {
                    report.LanguagesChecked++;
                    var culture = language == "en" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(language);
                    if (language != "en" && !File.Exists(Path.Combine(folder, language, assemblyName + ".resources.dll")))
                    { report.Issues.Add(new(component, language, "", "Language satellite missing.")); continue; }
                    try
                    {
                        var resources = manager.GetResourceSet(culture, true, false);
                        if (resources is null) { report.Issues.Add(new(component, language, "", "Resources unavailable without language fallback.")); continue; }
                        var suffix = language == "en" ? "" : "." + language;
                        var expected = XDocument.Load(Path.Combine(repo, $"src/FrameShift/Core/Localization/Strings{suffix}.resx")).Root!.Elements("data")
                            .ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Element("value")!);
                        var found = resources.Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => e.Value as string);
                        foreach (var entry in expected)
                            // ResX compilation on Windows preserves CRLF while XML readers
                            // normalize physical line endings to LF. Compare their text meaning.
                            if (!found.TryGetValue(entry.Key, out var value) || value?.Replace("\r\n", "\n") != entry.Value.Replace("\r\n", "\n"))
                                report.Issues.Add(new(component, language, entry.Key, "Missing or outdated packaged text."));
                        foreach (var extra in found.Keys.Except(expected.Keys))
                            report.Issues.Add(new(component, language, extra, "Unknown packaged text."));
                    }
                    catch (Exception ex) when (ex is MissingManifestResourceException or MissingSatelliteAssemblyException or FileLoadException or BadImageFormatException)
                    { report.Issues.Add(new(component, language, "", ex.Message)); }
                }
            }
            catch (Exception ex) when (ex is FileLoadException or BadImageFormatException)
            { report.Issues.Add(new(component, "", "", ex.Message)); }
            finally { manager?.ReleaseAllResources(); context.Unload(); }
        }
        return report;
    }
}
