using System.Text.Json;

namespace FrameShift.LocalizationTool;

internal sealed record CatalogSnapshot(int Version, string Language, TranslationEntry[] Entries);
internal sealed record ReviewItem(string Reference, string Status, string PreviousSource, string CurrentSource);
internal sealed record ReviewReport(string Language, int CurrentEntries, ReviewItem[] Items);

internal static class TranslationMaintenance
{
    internal static void WriteJson<T>(string path, T value)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
    }

    internal static void Snapshot(string repo, string language, string path, string? draftDirectory = null) =>
        WriteJson(path, new CatalogSnapshot(1, language, TranslationCatalog.Load(repo, language, draftDirectory).ToArray()));

    internal static ReviewReport Review(string repo, string baseline, string? draftDirectory = null)
    {
        var snapshot = JsonSerializer.Deserialize<CatalogSnapshot>(File.ReadAllText(baseline))
            ?? throw new FormatException("Empty snapshot.");
        if (snapshot.Version != 1 || snapshot.Entries is null || !TranslationCatalog.Accepts(repo, snapshot.Language, draftDirectory))
            throw new FormatException("Unsupported snapshot version or language.");
        if (snapshot.Entries.Any(e => e is null || e.Source is null || e.Translation is null || e.Context is null || string.IsNullOrEmpty(e.Key) || e.Scope is not ("app" or "installer")))
            throw new FormatException("Invalid snapshot entry.");
        var previous = snapshot.Entries.ToDictionary(e => e.Identity, StringComparer.Ordinal);
        var current = TranslationCatalog.Load(repo, snapshot.Language, draftDirectory).ToDictionary(e => e.Identity, StringComparer.Ordinal);
        var items = new List<ReviewItem>();
        foreach (var entry in current.Values)
        {
            if (!previous.TryGetValue(entry.Identity, out var old)) items.Add(new(entry.Identity, "new", "", entry.Source));
            else
            {
                if (old.Source != entry.Source) items.Add(new(entry.Identity, "source-changed", old.Source, entry.Source));
                if (old.Context != entry.Context) items.Add(new(entry.Identity, "context-changed", old.Source, entry.Source));
                if (old.Translation != entry.Translation) items.Add(new(entry.Identity, "translation-changed", old.Source, entry.Source));
            }
            if (string.IsNullOrWhiteSpace(entry.Translation)) items.Add(new(entry.Identity, "blank", "", entry.Source));
        }
        foreach (var old in previous.Values.Where(e => !current.ContainsKey(e.Identity)))
            items.Add(new(old.Identity, "removed", old.Source, ""));
        return new(snapshot.Language, current.Count, items.OrderBy(i => i.Reference, StringComparer.Ordinal).ThenBy(i => i.Status, StringComparer.Ordinal).ToArray());
    }

    internal static void Bundle(string repo, string language, string directory, string? baseline, string? draftDirectory = null)
    {
        if (language == "en") throw new ArgumentException("Contributor bundles cannot target the English source.");
        var entries = TranslationCatalog.Load(repo, language, draftDirectory);
        var review = baseline is null ? null : Review(repo, baseline, draftDirectory);
        if (review is not null && review.Language != language) throw new ArgumentException("Baseline language differs from the requested bundle.");
        var guide = File.ReadAllText(Path.Combine(repo, "docs/TRANSLATING.md"));
        var glossary = File.ReadAllText(Path.Combine(repo, "docs/TRANSLATION_GLOSSARY.md"));
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Bundle directory must be new.");
        Directory.CreateDirectory(directory);
        // The final READY marker is written only after all bundle files succeed.
        TranslationExchange.Export(repo, language, Path.Combine(directory, "translation.csv"), draftDirectory);
        File.WriteAllText(Path.Combine(directory, "START_HERE.txt"), guide);
        File.WriteAllText(Path.Combine(directory, "GLOSSARY.md"), glossary);
        File.WriteAllText(Path.Combine(directory, "FEEDBACK.txt"), "Language: " + language + "\nSpreadsheet application/version:\nOpened as UTF-8, semicolon separator, all columns Text:\nAccents/characters and multiline cells preserved:\nSaved and reopened successfully:\nUncertain references/terms:\nNotes for the maintainer:\n");
        if (review is not null) WriteJson(Path.Combine(directory, "review.json"), review);
        WriteJson(Path.Combine(directory, "snapshot.json"), new CatalogSnapshot(1, language, entries.ToArray()));
        File.WriteAllText(Path.Combine(directory, "READY.txt"), $"Language: {language}\nDraft (not enabled in FrameShift): {draftDirectory is not null}\nEntries: {entries.Count}\nBaseline compared: {baseline is not null}\nTechnical validation and human review are still required.\n");
    }
}
