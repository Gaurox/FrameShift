using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FrameShift.Core.Actions;

/// <summary>
/// One action as it applies to the current scope: how many scoped files it would
/// process (the "· N" badge) and whether it is enabled (the single-file rule, D2).
/// </summary>
public sealed record ActionAvailability(
    ActionCatalogEntry Entry,
    int MatchingFileCount,
    bool IsEnabled,
    string? DisabledReason);

/// <summary>
/// Pure logic behind the actions panel. Turns the queue + selection + type filter into
/// the effective scope, and the scope into the grouped, counted, enable-gated set of
/// applicable actions. No WinForms; unit-tested.
///
/// Defaults: no selection ⇒ scope is the whole queue; a selection ⇒ scope is the
/// selection. Actions are shown by union (an action appears if it accepts at least one
/// scoped file) and run only on the subset they accept. Single-file actions are enabled
/// only when exactly one compatible file is in scope (D2).
/// </summary>
public static class ActionScopeResolver
{
    /// <summary>Resolves the effective scope from the queue, the selection and an optional type filter.</summary>
    public static IReadOnlyList<string> ResolveScope(
        IReadOnlyList<string> allFiles,
        IReadOnlyList<string> selectedFiles,
        MediaFamily? familyFilter)
    {
        var baseScope = selectedFiles is { Count: > 0 } ? selectedFiles : allFiles;

        if (familyFilter is null)
        {
            return baseScope.ToArray();
        }

        return baseScope
            .Where(file => MediaFileClassifier.Classify(file) == familyFilter.Value)
            .ToArray();
    }

    /// <summary>
    /// Applicable actions for the given scope, in catalog order (already grouped by
    /// category). An action is included only if it accepts at least one scoped file.
    /// </summary>
    public static IReadOnlyList<ActionAvailability> Resolve(IReadOnlyList<string> scopeFiles)
    {
        var result = new List<ActionAvailability>();

        foreach (var entry in ActionCatalog.Entries)
        {
            var matching = CountMatching(entry, scopeFiles);
            if (matching == 0)
            {
                continue;
            }

            var (isEnabled, reason) = Evaluate(entry, matching);
            result.Add(new ActionAvailability(entry, matching, isEnabled, reason));
        }

        return result;
    }

    /// <summary>The scoped files a given action would actually process (those it accepts).</summary>
    public static IReadOnlyList<string> MatchingFiles(ActionCatalogEntry entry, IReadOnlyList<string> scopeFiles)
        => scopeFiles.Where(file => entry.Accepts(Path.GetExtension(file))).ToArray();

    /// <summary>Count of scoped files per <see cref="MediaFamily"/> (for the type chips).</summary>
    public static IReadOnlyDictionary<MediaFamily, int> FamilyCounts(IReadOnlyList<string> files)
    {
        var counts = new Dictionary<MediaFamily, int>();
        foreach (var file in files)
        {
            var family = MediaFileClassifier.Classify(file);
            counts[family] = counts.TryGetValue(family, out var current) ? current + 1 : 1;
        }

        return counts;
    }

    private static int CountMatching(ActionCatalogEntry entry, IReadOnlyList<string> scopeFiles)
    {
        var count = 0;
        foreach (var file in scopeFiles)
        {
            if (entry.Accepts(Path.GetExtension(file)))
            {
                count++;
            }
        }

        return count;
    }

    private static (bool IsEnabled, string? Reason) Evaluate(ActionCatalogEntry entry, int matching)
    {
        if (matching < entry.MinimumInputCount)
        {
            return (false, $"Select at least {entry.MinimumInputCount} files for this action.");
        }

        // Single-file editors accept exactly one compatible file (D2). Batch and Combine
        // run on the whole matching subset.
        if (entry.Arity == ActionArity.Single && matching > 1)
        {
            return (false, "Select a single file for this action.");
        }

        return (true, null);
    }
}
