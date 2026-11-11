using System.Text;
using System.Text.Json;

namespace FrameShift.LocalizationTool;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args.Length == 0 || args.SequenceEqual(["--help"])) { Help(); return 0; }
            var command = args[0];
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            var complete = false;
            var apply = false;
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i] == "--require-complete" && command == "check" && !complete) { complete = true; continue; }
                if (args[i] == "--apply" && command == "import" && !apply) { apply = true; continue; }
                var allowed = command switch
                {
                    "prepare" => new[] { "--repo", "--language", "--output" },
                    "snapshot" => new[] { "--repo", "--language", "--output", "--draft" },
                    "review" => new[] { "--repo", "--baseline", "--report", "--draft" },
                    "bundle" => new[] { "--repo", "--language", "--output", "--baseline", "--draft" },
                    "verify-package" => new[] { "--repo", "--package-dir", "--report" },
                    "export" => new[] { "--repo", "--language", "--output", "--draft" },
                    "import" => new[] { "--repo", "--file", "--backup-dir", "--draft" },
                    "check" => new[] { "--repo", "--file", "--report", "--draft" },
                    _ => throw new ArgumentException("Unknown command: " + command)
                };
                if (!allowed.Contains(args[i]) || i + 1 >= args.Length || args[i + 1].StartsWith("--") || !options.TryAdd(args[i], args[i + 1]))
                    throw new ArgumentException("Unknown, duplicate or incomplete option: " + args[i]);
                i++;
            }
            string Required(string key) => options.GetValueOrDefault(key) ?? throw new ArgumentException("Missing " + key);
            var repo = options.TryGetValue("--repo", out var root) ? Path.GetFullPath(root) : FindRepository();
            var draft = options.GetValueOrDefault("--draft");
            if (command == "prepare")
            {
                DraftLanguage.Create(repo, Required("--language"), Required("--output"));
                Console.WriteLine("Empty draft created: " + Path.GetFullPath(Required("--output")));
                Console.WriteLine("Use --draft <directory> for translation commands. No language was enabled and no installer files were changed.");
                return 0;
            }
            if (command == "snapshot")
            {
                TranslationMaintenance.Snapshot(repo, Required("--language"), Required("--output"), draft);
                Console.WriteLine("Snapshot saved. This is a comparison point, not proof of human approval.");
                return 0;
            }
            if (command == "review")
            {
                var review = TranslationMaintenance.Review(repo, Required("--baseline"), draft);
                TranslationMaintenance.WriteJson(Required("--report"), review);
                Console.WriteLine($"{review.Language}: {review.Items.Length} review items across {review.CurrentEntries} current entries.");
                foreach (var group in review.Items.GroupBy(i => i.Status)) Console.WriteLine($"{group.Key}: {group.Count()}");
                return 0;
            }
            if (command == "bundle")
            {
                TranslationMaintenance.Bundle(repo, Required("--language"), Required("--output"), options.GetValueOrDefault("--baseline"), draft);
                Console.WriteLine("Contributor bundle ready: " + Path.GetFullPath(Required("--output")));
                return 0;
            }
            if (command == "verify-package")
            {
                var result = PackageVerifier.Verify(repo, Required("--package-dir"));
                if (options.TryGetValue("--report", out var reportPath)) TranslationMaintenance.WriteJson(reportPath, result);
                foreach (var issue in result.Issues) Console.WriteLine($"ERROR {issue.Component}/{issue.Language} {issue.Reference}: {issue.Message}");
                Console.WriteLine($"{result.LanguagesChecked} component/language checks; {result.Errors} errors. Package was not modified.");
                return result.Errors == 0 ? 0 : 1;
            }
            if (command == "export")
            {
                var output = Path.GetFullPath(Required("--output"));
                TranslationExchange.Export(repo, Required("--language"), output, draft);
                Console.WriteLine("Export created: " + output);
                return 0;
            }
            if (command == "import")
            {
                var plan = TranslationImport.Prepare(repo, Required("--file"), draft);
                foreach (var issue in plan.Report.Issues)
                    Console.WriteLine($"{issue.Severity.ToUpperInvariant()} {issue.Reference}: {issue.Message}");
                if (plan.Report.Errors > 0) return 1;
                foreach (var change in plan.Changes)
                    Console.WriteLine(JsonSerializer.Serialize(change));
                Console.WriteLine($"{plan.Changes.Count} text changes in {plan.Files.Count} files.");
                if (apply)
                {
                    plan.Apply(Required("--backup-dir"));
                    Console.WriteLine("Import applied. Recovery backups: " + Path.GetFullPath(Required("--backup-dir")));
                }
                else Console.WriteLine("Preview only. No project files changed. Use --apply --backup-dir <new-directory> to apply.");
                if (draft is not null) Console.WriteLine("Draft mode: only draft translation files are targeted. The language remains disabled in FrameShift.");
                return 0;
            }
            if (command != "check") throw new ArgumentException("Unknown command: " + command);
            var report = TranslationExchange.Check(repo, Required("--file"), complete, draft);
            Console.WriteLine($"{report.Language}: {report.Records}/{report.CatalogEntries} records; {report.Errors} errors; {report.Warnings} warnings; {report.SameAsEnglish} identical to English.");
            foreach (var issue in report.Issues)
                Console.WriteLine($"{issue.Severity.ToUpperInvariant()} record {issue.Record?.ToString() ?? "-"} {issue.Reference}: {issue.Message}");
            if (options.TryGetValue("--report", out var path))
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                JsonSerializer.Serialize(stream, report, new JsonSerializerOptions { WriteIndented = true });
            }
            Console.WriteLine("Technical checks only. Native-language review and visual checks are still required. No project files were modified.");
            return report.Errors == 0 ? 0 : 1;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or FormatException or System.Xml.XmlException or InvalidOperationException or AggregateException or JsonException)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 2;
        }
    }

    private static string FindRepository()
    {
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src", "FrameShift", "Core", "Localization", "Strings.resx"))
                && File.Exists(Path.Combine(dir.FullName, "installer", "FrameShift.iss"))) return dir.FullName;
        throw new ArgumentException("Repository not found. Supply --repo <path>.");
    }

    private static void Help()
    {
        Console.WriteLine("FrameShift translation export/check tool (.NET 8, offline, preview before import).");
        Console.WriteLine("prepare --language <new-code> --output <new-draft-directory> [--repo <root>]");
        Console.WriteLine("export --language <code> --output <new.csv> [--repo <root>]");
        Console.WriteLine("check --file <translation.csv> [--require-complete] [--report <new.json>] [--repo <root>]");
        Console.WriteLine("import --file <translation.csv> [--apply --backup-dir <new-directory>] [--repo <root>]");
        Console.WriteLine("snapshot --language <code> --output <new.json> [--repo <root>]");
        Console.WriteLine("review --baseline <snapshot.json> --report <new.json> [--repo <root>]");
        Console.WriteLine("bundle --language <code> --output <new-directory> [--baseline <snapshot.json>] [--repo <root>]");
        Console.WriteLine("verify-package --package-dir <publish-root> [--report <new.json>] [--repo <root>]");
        Console.WriteLine("Add --draft <directory> to export/check/import/snapshot/review/bundle for an unregistered language. Drafts never enable a language or modify the installer.");
        Console.WriteLine("CSV: UTF-8 with BOM, semicolon separator. Edit Translation only. Output files must not already exist.");
        Console.WriteLine("Exit codes: 0 = technically valid (possibly incomplete); 1 = validation errors; 2 = command/file error.");
    }
}
