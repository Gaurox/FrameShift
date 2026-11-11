# Translation export, checks, import and draft languages

Offline .NET 8 development tool, separate from the distributed application. No
language registration or runtime package dependency. Import modifies only the
selected existing translation resources and separated installer custom messages,
or explicitly selected draft files. The existing language catalog is linked at compile time.

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

When tracking contribution CSVs under `translations`, retain the repository's
`-text` Git attribute. Embedded cell line endings are part of source/translation
hashes and must not be converted by Windows `core.autocrlf`. Do not normalize
whitespace or line endings in supplied CSVs to fix a Git formatting warning.

Without `--draft`, supported languages remain the five registered codes: `en`,
`zh-CN`, `es`, `pt-BR`, `ru`. Unregistered languages require an explicitly prepared
draft directory; merely creating resource files does not enable a language.

## Prepare an unregistered language (maintainer)

Use `prepare --language <code> --output <new-directory>` to create an empty draft.
The code must be a canonical .NET culture code for a language not already
registered. The following example uses `qaa`, a local-use code for a trial, rather
than introducing a real translation:

```powershell
dotnet tools/FrameShift.LocalizationTool/bin/Debug/net8.0/FrameShift.LocalizationTool.dll prepare --language qaa --output scratch/draft-qaa
dotnet tools/FrameShift.LocalizationTool/bin/Debug/net8.0/FrameShift.LocalizationTool.dll bundle --language qaa --draft scratch/draft-qaa --output scratch/qaa-contributor-001
dotnet tools/FrameShift.LocalizationTool/bin/Debug/net8.0/FrameShift.LocalizationTool.dll check --file scratch/qaa-contributor-001/translation.csv --draft scratch/draft-qaa
dotnet tools/FrameShift.LocalizationTool/bin/Debug/net8.0/FrameShift.LocalizationTool.dll import --file scratch/qaa-contributor-001/translation.csv --draft scratch/draft-qaa
dotnet tools/FrameShift.LocalizationTool/bin/Debug/net8.0/FrameShift.LocalizationTool.dll import --file scratch/qaa-contributor-001/translation.csv --draft scratch/draft-qaa --apply --backup-dir scratch/qaa-backup-001
```

Preparation creates exactly three files: `draft.json` (schema version and language
code), `Strings.<code>.resx` (no translated entries), and `FrameShift.<code>.iss`
(only the technical `AppLanguage` marker). Metadata is written last; a directory
left incomplete by an I/O error must not be used. The installer identifier is the
code with hyphens replaced by underscores. Keep these maintainer files together;
contributors only edit their bundle's CSV.

Use `--draft <directory>` on **every** export/check/import/snapshot/review/bundle
command for that draft. The CSV/snapshot language must match the metadata. The
English source still comes from the current repository. Missing translations
export as blank cells, never as copied English. Partial imports, variable checks,
linked Explorer captions, concurrent-edit protection, backups and rollback use
the same implementation as existing languages. `--require-complete` rejects the
unfinished cells. A bundle's READY marker explicitly identifies a draft.

Choose a separate location such as `translations/drafts/<code>` or `scratch`.
Draft directories inside `src`, `installer`, `references` or repository metadata
are refused, as are symbolic links/junctions in draft paths. Imports only update
the two draft translation files; `draft.json` is not rewritten. Existing files
are never overwritten by `prepare`.

This workflow does not edit `AppLanguages`, runtime resources, installer inputs,
standard upstream `.isl` files or release scripts. `verify-package` continues to
check registered languages only and does not accept `--draft`. There is no
automatic promotion/activation command: activation and installer integration
remain a separate, reviewed step after completeness and human acceptance.

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
`SourceHash`, `OriginalTranslationHash`. `app` covers the app and shared worker resources; `installer` covers
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

The original-translation hash is a read-only baseline for concurrent edits.
An unchanged old CSV cell preserves the current project translation. If the
contributor and maintainer both changed the same text differently, the entire
import is blocked. Matching changes already applied are accepted without a write.
Linked Explorer captions receive the same protection, including automatic updates
of a counterpart not edited by the contributor.

Old eight-column CSVs are rejected because they lack this baseline. Request a fresh
export and transfer only the contributor's deliberate edits after review. Never
add current hashes to an old contribution: that would hide concurrent changes.

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

Missing translation entries are inserted into the existing language resource or
installer message file; blank submissions do not create empty entries.
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
