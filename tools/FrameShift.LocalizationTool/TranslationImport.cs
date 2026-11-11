using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace FrameShift.LocalizationTool;

internal sealed record TranslationChange(string Reference, string Before, string After);
internal sealed record ImportFile(string Path, byte[] Before, byte[] After);

internal sealed class TranslationImport
{
    internal CheckReport Report { get; private set; } = new();
    internal List<TranslationChange> Changes { get; } = [];
    internal List<ImportFile> Files { get; } = [];
    private readonly Dictionary<string, byte[]> _inputs = new(StringComparer.OrdinalIgnoreCase);
    private Action? _verifyDraft;

    internal static TranslationImport Prepare(string repo, string csv, string? draftDirectory = null)
    {
        var rows = CsvFile.Read(csv);
        var plan = new TranslationImport();
        foreach (var path in Directory.GetFiles(Path.Combine(repo, "src/FrameShift/Core/Localization"), "Strings*.resx")
            .Concat(Directory.GetFiles(Path.Combine(repo, "installer/Languages"), "FrameShift.*.iss"))
            .Append(Path.Combine(repo, "installer/FrameShift.iss"))) plan._inputs.Add(path, File.ReadAllBytes(path));
        if (draftDirectory is not null)
        {
            draftDirectory = Path.GetFullPath(draftDirectory);
            var draft = DraftLanguage.Open(repo, draftDirectory);
            plan._verifyDraft = () => DraftLanguage.Open(repo, draftDirectory);
            foreach (var path in new[] { Path.Combine(draftDirectory, "draft.json"), draft.ResourcePath(draftDirectory), draft.InstallerPath(draftDirectory) })
                plan._inputs.Add(path, File.ReadAllBytes(path));
        }
        plan.Report = TranslationExchange.CheckRows(repo, rows, false, draftDirectory);
        if (plan.Report.Errors != 0) return plan;
        if (plan.Report.Language == "en")
        {
            plan.Error("", "Import cannot modify the English source catalog.");
            return plan;
        }
        var entries = TranslationCatalog.Load(repo, plan.Report.Language, draftDirectory).ToDictionary(e => e.Identity);
        var originalHashes = rows.Skip(1).ToDictionary(row => row[1] + ":" + row[2], row => row[8], StringComparer.Ordinal);
        var edits = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows.Skip(1))
        {
            var identity = row[1] + ":" + row[2];
            if (!string.IsNullOrWhiteSpace(row[4]) && row[4] != entries[identity].Translation
                && !TranslationEntry.Hash(row[4]).Equals(row[8], StringComparison.OrdinalIgnoreCase)) edits.Add(identity, row[4]);
        }

        // Explicit, versioned pairs keep the app and installer captions consistent.
        var linksPath = Path.Combine(repo, "tools/FrameShift.LocalizationTool/ExplorerMessageLinks.json");
        if (!File.Exists(linksPath)) throw new FormatException("ExplorerMessageLinks.json is required for import.");
        plan._inputs.Add(linksPath, File.ReadAllBytes(linksPath));
        if (File.Exists(linksPath))
        {
            var links = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(linksPath))!;
            foreach (var group in links.GroupBy(p => p.Value))
            {
                var identities = group.Select(p => "app:" + p.Key).Append("installer:" + group.Key).ToArray();
                if (identities.Any(id => !entries.ContainsKey(id)) || identities.Select(id => entries[id].Source).Distinct().Count() != 1)
                {
                    plan.Error(group.Key, "Explorer correspondence is missing or the English captions differ. Maintainer review required.");
                    continue;
                }
                var changed = identities.Where(edits.ContainsKey).Select(id => edits[id]).Distinct(StringComparer.Ordinal).ToArray();
                if (changed.Length > 1) { plan.Error(group.Key, "Conflicting app/installer translations for the same Explorer caption."); continue; }
                if (changed.Length == 0) continue;
                var value = changed[0];
                if (value.IndexOfAny(['\r', '\n']) >= 0) { plan.Error(group.Key, "Explorer captions must be single-line text."); continue; }
                foreach (var identity in identities)
                {
                    if (entries[identity].Translation != value && !entries[identity].TranslationHash.Equals(originalHashes[identity], StringComparison.OrdinalIgnoreCase))
                        plan.Error(identity, "Linked Explorer translation changed since export; synchronization would overwrite a newer correction.");
                    if (!TranslationTokens.Read(entries[identity].Scope, entries[identity].Source).SequenceEqual(TranslationTokens.Read(entries[identity].Scope, value)))
                        plan.Error(identity, "A linked Explorer caption would lose required variables.");
                    if (value != entries[identity].Translation) edits[identity] = value;
                }
            }
        }
        if (plan.Report.Errors != 0) return plan;
        foreach (var pair in edits.OrderBy(p => p.Key, StringComparer.Ordinal))
            plan.Changes.Add(new(pair.Key, entries[pair.Key].Translation, pair.Value));
        if (edits.Count == 0) { plan.VerifyInputs(); return plan; }

        var target = TranslationCatalog.Target(repo, plan.Report.Language, draftDirectory);
        var resource = target.Resource;
        var resourceBytes = File.ReadAllBytes(resource);
        var xml = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using (var reader = XmlReader.Create(new MemoryStream(resourceBytes), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) xml.Load(reader);
        var appChanged = false;
        var resourceNodes = xml.SelectNodes("/root/data")!.Cast<XmlElement>().ToDictionary(data => data.GetAttribute("name"), StringComparer.Ordinal);
        foreach (var edit in edits.Where(pair => pair.Key.StartsWith("app:", StringComparison.Ordinal)))
        {
            var key = edit.Key[4..];
            if (!resourceNodes.TryGetValue(key, out var data))
            {
                data = xml.CreateElement("data"); data.SetAttribute("name", key);
                var space = xml.CreateAttribute("xml", "space", "http://www.w3.org/XML/1998/namespace"); space.Value = "preserve"; data.Attributes.Append(space);
                data.AppendChild(xml.CreateElement("value")); xml.DocumentElement!.AppendChild(data);
            }
            data["value"]!.InnerText = edit.Value; appChanged = true;
        }
        if (appChanged)
        {
            using var output = new MemoryStream();
            using (var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(resourceBytes.Take(3).SequenceEqual(new byte[] {239,187,191})), Indent = false, NewLineHandling = NewLineHandling.Entitize })) xml.Save(writer);
            plan.Files.Add(new(resource, resourceBytes, output.ToArray()));
        }
        var installer = target.Installer;
        if (!File.Exists(installer)) throw new FormatException("Import requires separated installer custom-message files.");
        var installerBytes = File.ReadAllBytes(installer);
        var installerLanguage = target.InstallerLanguage;
        var encoding = new UTF8Encoding(false, true);
        var text = encoding.GetString(installerBytes);
        var existingInstallerKeys = new HashSet<string>(StringComparer.Ordinal);
        var replaced = Regex.Replace(text, @"(?m)^(\w+)\.(\w+)=(.*?)(\r?)$", match =>
        {
            if (match.Groups[1].Value != installerLanguage) throw new FormatException("Unexpected language in " + installer);
            existingInstallerKeys.Add(match.Groups[2].Value);
            return edits.TryGetValue("installer:" + match.Groups[2].Value, out var value)
                ? match.Groups[1].Value + "." + match.Groups[2].Value + "=" + value + match.Groups[4].Value : match.Value;
        });
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        foreach (var edit in edits.Where(pair => pair.Key.StartsWith("installer:", StringComparison.Ordinal)))
        {
            var key = edit.Key[10..];
            if (existingInstallerKeys.Contains(key)) continue;
            if (replaced.Length > 0 && !replaced.EndsWith('\n')) replaced += newline;
            replaced += installerLanguage + "." + key + "=" + edit.Value + newline;
        }
        if (text != replaced) plan.Files.Add(new(installer, installerBytes, encoding.GetBytes(replaced)));
        plan.VerifyInputs();
        return plan;
    }

    private void Error(string reference, string message) => Report.Issues.Add(new("error", null, reference, message));

    internal void Apply(string backupDirectory, Action<int>? beforeReplace = null)
    {
        if (Report.Errors > 0) throw new InvalidOperationException("Import has validation errors.");
        VerifyInputs();
        if (Files.Count == 0) return;
        foreach (var file in Files) VerifyOriginal(file);
        backupDirectory = Path.GetFullPath(backupDirectory);
        if (Directory.Exists(backupDirectory) || File.Exists(backupDirectory)) throw new IOException("Backup directory must be new.");
        Directory.CreateDirectory(backupDirectory);
        for (var i = 0; i < Files.Count; i++) File.WriteAllBytes(Path.Combine(backupDirectory, $"{i:000}.original"), Files[i].Before);
        File.WriteAllText(Path.Combine(backupDirectory, "manifest.json"), JsonSerializer.Serialize(Files.Select((f, i) => new
        {
            Target = f.Path, Backup = $"{i:000}.original", OriginalSha256 = Convert.ToHexString(SHA256.HashData(f.Before)), UpdatedSha256 = Convert.ToHexString(SHA256.HashData(f.After))
        }), new JsonSerializerOptions { WriteIndented = true }));
        var applied = new List<ImportFile>();
        try
        {
            for (var i = 0; i < Files.Count; i++)
            {
                beforeReplace?.Invoke(i);
                _verifyDraft?.Invoke();
                VerifyOriginal(Files[i]);
                Replace(Files[i].Path, Files[i].After);
                applied.Add(Files[i]);
            }
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var file in applied.AsEnumerable().Reverse())
            {
                try
                {
                    _verifyDraft?.Invoke();
                    if (!File.ReadAllBytes(file.Path).SequenceEqual(file.After)) throw new IOException("Concurrent edit detected during rollback: " + file.Path);
                    Replace(file.Path, file.Before);
                }
                catch (Exception ex) { rollbackErrors.Add(ex); }
            }
            if (rollbackErrors.Count > 0) throw new AggregateException("Import failed; recovery backups retained at " + backupDirectory, new[] { failure }.Concat(rollbackErrors));
            throw new IOException("Import failed and previous writes were restored. Backups: " + backupDirectory, failure);
        }
    }

    private static void VerifyOriginal(ImportFile file)
    {
        if (!File.ReadAllBytes(file.Path).SequenceEqual(file.Before)) throw new IOException("File changed since preview; import aborted: " + file.Path);
    }

    private void VerifyInputs()
    {
        _verifyDraft?.Invoke();
        foreach (var pair in _inputs)
            if (!File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) throw new IOException("Catalog changed since preview; export/check again: " + pair.Key);
    }

    private static void Replace(string path, byte[] content)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write)) { stream.Write(content); stream.Flush(true); }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

