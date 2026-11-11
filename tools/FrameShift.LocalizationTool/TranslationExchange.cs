namespace FrameShift.LocalizationTool;

internal sealed record CheckIssue(string Severity, int? Record, string Reference, string Message);
internal sealed class CheckReport
{
    public string Language { get; set; } = "";
    public int CatalogEntries { get; set; }
    public int Records { get; set; }
    public int BlankTranslations { get; set; }
    public int SameAsEnglish { get; set; }
    public List<CheckIssue> Issues { get; } = [];
    public int Errors => Issues.Count(i => i.Severity == "error");
    public int Warnings => Issues.Count(i => i.Severity == "warning");
}

internal static class TranslationExchange
{
    internal static readonly string[] Header = ["Language", "Scope", "Key", "Source", "Translation", "Context", "Tokens", "SourceHash"];

    internal static void Export(string repo, string language, string output)
    {
        var rows = TranslationCatalog.Load(repo, language).Select(e => new[]
        {
            language, e.Scope, e.Key, e.Source, e.Translation, e.Context,
            string.Join(" | ", TranslationTokens.Read(e.Scope, e.Source)), e.SourceHash
        });
        CsvFile.Write(output, new[] { Header }.Concat(rows));
    }

    internal static CheckReport Check(string repo, string file, bool requireComplete)
    {
        return CheckRows(repo, CsvFile.Read(file), requireComplete);
    }

    internal static CheckReport CheckRows(string repo, string[][] rows, bool requireComplete)
    {
        var report = new CheckReport();
        void Issue(string severity, int? record, string reference, string message) => report.Issues.Add(new(severity, record, reference, message));
        if (rows.Length == 0 || !rows[0].SequenceEqual(Header))
        {
            Issue("error", 1, "", "CSV header differs from the exported format. Use a semicolon separator and UTF-8.");
            return report;
        }
        if (rows.Length > 1 && rows[1].Length != Header.Length)
        {
            Issue("error", 2, "", "Wrong number of columns in the first data record.");
            return report;
        }
        if (rows.Length == 1 || !TranslationCatalog.Languages.Contains(rows[1][0]))
        {
            Issue("error", 2, "", "Missing data or unsupported language. Export an existing language first.");
            return report;
        }
        report.Language = rows[1][0];
        var catalog = TranslationCatalog.Load(repo, report.Language).ToDictionary(e => e.Identity);
        report.CatalogEntries = catalog.Count;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 1; index < rows.Length; index++)
        {
            var row = rows[index]; var record = index + 1;
            report.Records++;
            if (row.Length != Header.Length) { Issue("error", record, "", "Wrong number of columns."); continue; }
            var reference = row[1] + ":" + row[2];
            if (row[0] != report.Language) Issue("error", record, reference, "Language differs from the rest of the file.");
            if (!seen.Add(reference)) Issue("error", record, reference, "Duplicate reference.");
            if (!catalog.TryGetValue(reference, out var entry)) { Issue("error", record, reference, "Unknown or removed reference."); continue; }
            if (row[3] != entry.Source || row[7] != entry.SourceHash)
                Issue("error", record, reference, "English source changed or was edited. Request a fresh export before reviewing this translation.");
            if (string.IsNullOrWhiteSpace(row[4]))
            {
                report.BlankTranslations++;
                Issue(requireComplete ? "error" : "warning", record, reference, "Translation is blank; work is incomplete.");
                continue;
            }
            if (entry.Scope == "installer" && row[4].IndexOfAny(['\r', '\n']) >= 0)
                Issue("error", record, reference, "Installer messages must use %n for line breaks, not physical newlines.");
            if (row[4] == entry.Source) report.SameAsEnglish++;
            try
            {
                if (!TranslationTokens.Read(entry.Scope, entry.Source).SequenceEqual(TranslationTokens.Read(entry.Scope, row[4])))
                    Issue("error", record, reference, "Variables or their formats differ from the English source (including repetitions).");
            }
            catch (FormatException)
            {
                Issue("error", record, reference, "Invalid .NET format string: check braces and variables.");
            }
        }
        foreach (var missing in catalog.Keys.Except(seen).Order(StringComparer.Ordinal))
            Issue("error", null, missing, "Reference missing from the CSV. Keep all rows, leaving unfinished translations blank.");
        return report;
    }
}
