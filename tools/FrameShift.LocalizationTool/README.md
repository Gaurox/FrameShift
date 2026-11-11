# Translation export, checks and import — lots 1–3

Offline .NET 8 development tool, separate from the distributed application. No
language registration or runtime package dependency. Import modifies only the
selected existing translation resources and separated installer custom messages. The existing language catalog is linked at compile time.

## Maintainer workflow

From the repository root, using the SDK pinned in `global.json`:

```powershell
dotnet restore tools/FrameShift.LocalizationTool/FrameShift.LocalizationTool.csproj --locked-mode
dotnet build tools/FrameShift.LocalizationTool/FrameShift.LocalizationTool.csproj --no-restore
New-Item -ItemType Directory -Path scratch/translation-review -Force
dotnet tools/FrameShift.LocalizationTool/bin/Debug/net8.0/FrameShift.LocalizationTool.dll export --language es --output scratch/translation-review/es.csv
dotnet tools/FrameShift.LocalizationTool/bin/Debug/net8.0/FrameShift.LocalizationTool.dll check --file scratch/translation-review/es.csv --report scratch/translation-review/es-report.json
```

The repository's locally prepared SDK, when present, can be used instead of
`dotnet`: `./.buildcheck/sdk/8.0.427/dotnet.exe`. This does not alter `global.json`.
Use `--repo <path>` when running outside the repository. Output directories must
exist. CSV exports and JSON reports must use **new filenames**; existing files,
including contributor work, are never overwritten.

Supported languages are the five registered codes: `en`, `zh-CN`, `es`, `pt-BR`,
`ru`. The tool deliberately refuses new languages, including `fr`, `ja` and `id`.

Exports contain the English source and current translation for application/worker
resources and the installer's FrameShift-specific custom messages. Export and check only read the installer. Its `AppLanguage` configuration and standard upstream `.isl` messages
are excluded. Current totals: 2,024 app resources + 143 custom messages = 2,167 rows.
Explorer captions remain separate CSV rows for compatibility with lot 1 exports.
During import, changing either linked caption updates both destinations. You do
not have to edit both rows; incompatible edits to both are rejected. The explicit
`ExplorerMessageLinks.json` correspondence is checked against the real menu paths
and installer caption keys by the application tests.

## Translator workflow

1. Open/import the supplied CSV in your spreadsheet editor: **UTF-8**, separator
   **semicolon**, quote character `"`. Set **all columns to Text**, not automatic
   numbers/formulas/dates. Prefer the text/CSV import dialog to double-clicking.
2. Edit **Translation** only. Keep the header, references, English source, language
   and source hashes unchanged. Sorting complete rows is allowed.
3. Preserve variables exactly, for example `{0}`, `{1:0.0}`, `%1`, `%n`, `{app}`.
   They may move within a sentence but must retain their formats and repetitions.
   The existing Inno spacing macro `{#InstallerSpace}` may be omitted; other
   variables/constants must remain intact. Preserve significant whitespace and
   intentional line breaks. Never translate internal CLI values or product names.
4. Keep every row. Leave unfinished translations blank rather than removing rows.
5. Save a new **UTF-8 semicolon-separated CSV**, not an XLSX renamed to CSV, and
   return it to the maintainer. CSV does not protect cells from accidental editing.

Columns: `Language`, `Scope`, `Key`, `Source`, `Translation`, `Context`, `Tokens`,
`SourceHash`. `app` covers the app and shared worker resources; `installer` covers
FrameShift's custom installation messages. If no English resource comment exists,
Context only identifies the application area and explicitly says no note exists.
Most current entries still need human-written context; this tool does not invent it.

## Validation and results

`check` reads the contribution and compares it with the **current** English
catalog, not with edited CSV reference columns. It detects:

- missing/unknown/duplicate references, mixed languages, bad headers and columns;
- changed English source or source hashes requiring a fresh export and review;
- missing/repeated/changed variables and numeric formats, malformed .NET braces;
- blank translations, with the exact reference and CSV record number.

Record numbers include the header; they are not physical text line numbers when
cells contain line breaks. Do not rely on the editable Tokens column for checks:
the checker derives expected variables from the repository's English source.

Default mode permits blank translations but reports them as incomplete work.
Add `--require-complete` to make blanks errors. Missing rows are always errors.
Values identical to English are counted for review, not rejected: product names
and technical terms may legitimately remain unchanged.
The report is technical validation, not a certificate of linguistic correctness.

Exit codes: **0** = no technical errors (may still have incomplete warnings);
**1** = validation errors; **2** = invalid command, CSV syntax, encoding, or file
access. Syntax/file errors are printed to stderr; JSON is written only after the
file has been parsed and checked. The JSON report includes all issues and counts.

Export/check never apply translations. Import behavior is described below.

## Import (maintainer)

```powershell
dotnet tools/FrameShift.LocalizationTool/bin/Debug/net8.0/FrameShift.LocalizationTool.dll import --file scratch/translation-review/es.csv
dotnet tools/FrameShift.LocalizationTool/bin/Debug/net8.0/FrameShift.LocalizationTool.dll import --file scratch/translation-review/es.csv --apply --backup-dir scratch/translation-review/backup-es-001
```

Import previews changes by default. `--apply` requires a **new backup directory**.
The CSV must retain all rows; blank translations are skipped and existing values
are preserved. A conflict, stale English source, malformed variable or other
validation error blocks the entire import. English source imports are refused.
Physical newlines in installer messages are rejected; use `%n` where appropriate.
The existing language catalog and upstream Inno `.isl` files are never modified.

Before any replacement, originals and a SHA-256 manifest are saved in the backup
directory. Each destination is replaced using a temporary file in its own folder.
Inputs are rechecked against the preview snapshot to detect concurrent edits.
If a later replacement fails, previously written files are restored. Backups are
retained on success and failure. A process crash, power loss or failed rollback
may require manual recovery from the manifest; this is not a filesystem-wide
transaction. A concurrent edit detected during rollback is preserved and reported
instead of being overwritten.

Only changed values are written. No-op imports preserve bytes exactly. An XML
serializer escapes changed text safely and preserves comments/whitespace, but
may normalize the XML declaration. Review the Git diff after applying an import.
This command is a maintainer action, not part of the installed application.

## Tests and limits

```powershell
dotnet restore tests/FrameShift.LocalizationTool.Tests/FrameShift.LocalizationTool.Tests.csproj --locked-mode
dotnet test tests/FrameShift.LocalizationTool.Tests/FrameShift.LocalizationTool.Tests.csproj --no-restore
```

The standalone test project does not build FrameShift
or require the ImageSharp licence, FFmpeg, AI models or Inno Setup.

Automated tests cover CSV round-trips (Unicode, quotes, delimiters, LF/CR/CRLF and
empty cells), damaged contributions, stale source, partial work, format variables,
Chinese aliases, imports with partial work, caption conflicts, concurrent edits,
backup preservation and rollback after a failed second write. Real exports of all five
languages are also checked. Actual open/edit/save acceptance in Excel and
LibreOffice remains manual; byte-level tests do not certify spreadsheet behavior.
Visual UI checks and native-language proofreading remain necessary before shipping.

## Contribution and maintenance (lot 3)

See the [maintainer workflow](../../docs/TRANSLATION_MAINTENANCE.md),
[translator start guide](../../docs/TRANSLATING.md), and
[glossary](../../docs/TRANSLATION_GLOSSARY.md).

- `snapshot --language es --output <new.json>` records a comparison point.
- `review --baseline <snapshot.json> --report <new.json>` lists new/removed,
  source/context/translation changes and blank translations, without approving them.
- `bundle --language es --output <new-directory> [--baseline <snapshot.json>]`
  assembles a contributor folder; only Translation in the CSV needs editing.
- `verify-package --package-dir <publish-root> [--report <new.json>]` checks exact
  app/worker language resources against the repository, without English fallback.

Snapshots/reports/folders never overwrite existing work. A review report has
multiple items when a reference has several statuses; counts are not necessarily
counts of unique strings. Bundle snapshots are supply records, not reviewed
baselines. Keep a separate accepted baseline and retain contributor feedback.
Old translations remain suggestions when English changes; review those entries
explicitly before recording a new accepted baseline.

Resource text comparisons treat CRLF/LF line endings as equivalent because Windows
ResX compilation and XML readers represent physical line endings differently.
Assembly auditing reads from memory and does not lock package files. It does not
execute application entry points or regenerate/install anything. Human proofreading,
real spreadsheet use and visual acceptance remain outside automated validation.
