# Translation contribution and maintenance

The maintenance workflow is offline. By default it uses the eight registered
languages; an explicit draft mode prepares unregistered languages separately. It
does not register new languages, install the app, or build an installer by itself.
Build the tool as documented in [its README](../tools/FrameShift.LocalizationTool/README.md).
The commands below run from the repository root, after creating a scratch folder.

Executed automated checks and outstanding human acceptance are recorded in
[the workflow validation](TRANSLATION_WORKFLOW_VALIDATION.md).

## Prepare a new language without enabling it

Run `prepare --language <code> --output <new-directory>` to create empty maintainer
files outside `src` and `installer`, for example in `translations/drafts/<code>`.
The directory must be new. No translated values or English fallback values are
inserted. Keep the generated metadata, resource and custom-message files together.

Then add `--draft <directory>` to export, check, import, snapshot, review and bundle.
All these commands use the current English catalog and the same contribution
format/protections as registered languages. A returned contribution is previewed
and applied with backups to the draft files only. `check --require-complete` is
the completeness gate; native proofreading and visual acceptance are still separate.
Do not put unfinished resources in the application's resource directory: the SDK
could distribute their satellite assemblies even before registration.

See the [tool's draft workflow](../tools/FrameShift.LocalizationTool/README.md#prepare-an-unregistered-language-maintainer)
for runnable examples. Draft snapshots require the same `--draft` selection when
reviewed; they are comparison points, not approvals. Generating a contributor
bundle does not activate the language. App registration, upstream Inno messages,
installer inclusion, UI testing and eventual packaging remain separate work.

The initial French, Japanese and Indonesian drafts and contributor folders are
listed in [new language preparation](../translations/README.md). All three target
catalogs started empty; their supplied snapshots are not reviewed baselines.
The [French pilot](TRANSLATION_FRENCH_PILOT.md) has since populated the French
draft and records the contribution checks, observed difficulties and remaining
human acceptance. This paragraph records the historical pilot state.
Revision `002` replaced five French source descriptions with English and checked
all existing target translations. At that point only those five descriptions
had been filled in the Japanese/Indonesian drafts.
Older supplies remain historical records and are rejected as stale sources;
request the current bundle rather than rewriting their reference columns.
Japanese has since been completed through the [Japanese pilot](TRANSLATION_JAPANESE_PILOT.md);
its draft review folder is `003`. Indonesian has since been completed in
[its pilot](TRANSLATION_INDONESIAN_PILOT.md), with draft supply `003` as well.
All three languages are now activated for the owner's local 1.24.0 test candidate.
Use normal commands without `--draft` for `fr`, `ja` and `id`; their runtime
resources and custom installer files are authoritative. Historical drafts and
READY markers describe supply-time status and are no longer import destinations.
Registered languages are deliberately refused by draft mode. Generate a fresh
numbered active bundle for further work. Native proofreading, actual spreadsheet
use and visual acceptance remain open.

## Prepare and receive a contribution

```powershell
dotnet tools/FrameShift.LocalizationTool/bin/Debug/net8.0/FrameShift.LocalizationTool.dll snapshot --language es --output scratch/es-reviewed-001.json
dotnet tools/FrameShift.LocalizationTool/bin/Debug/net8.0/FrameShift.LocalizationTool.dll bundle --language es --baseline scratch/es-reviewed-001.json --output scratch/es-contributor-001
```

A bundle contains the CSV, plain-text start guide, glossary, feedback form,
snapshot and completion marker. With a baseline it also includes a detailed
review report. Without a baseline, READY explicitly says no comparison was made.
Output directories/files must be new. An I/O failure can leave an incomplete
folder without READY; do not distribute it. Generated bundles belong in scratch
or another chosen exchange location, not in runtime assets or the installer.

Each CSV now carries the original translation hash as well as the English source
hash. Keep these exported references unchanged. Unedited old cells preserve current
project corrections; incompatible concurrent edits block the import, including
linked Explorer counterparts. Missing destination entries are created on import.
Older eight-column CSVs require a fresh export; transfer only intentional edits
with human review, never synthesize current hashes onto an old file.

The repository marks translation CSVs as `-text` in `.gitattributes`. Their
embedded cell line endings must survive Git unchanged, including Windows
`core.autocrlf`, because source and original-translation hashes use exact text.
Do not normalize supplied CSV whitespace/line endings to silence Git warnings.

When the folder returns: run `check`, review the wording and feedback, preview the
import, then apply with a new backup directory. Review the Git diff, especially
automatic Explorer caption synchronization. Retain the agreed native-language
wording and proofreader notes. Do not treat import success as translation approval.

## Track changes between contributions

```powershell
dotnet tools/FrameShift.LocalizationTool/bin/Debug/net8.0/FrameShift.LocalizationTool.dll review --baseline scratch/es-reviewed-001.json --report scratch/es-review-002.json
```

Reports distinguish new, removed, source-changed, context-changed,
translation-changed and blank entries. One reference can have multiple statuses.
This is a comparison against a specific snapshot, not permanent approval tracking.
Never silently replace the reviewed baseline before reviewing changed English.
After acceptance, create a new snapshot with a new filename and retain the old
one with the contribution record. Snapshot creation itself certifies nothing.
Renamed keys appear as removed + new; no automatic fuzzy match is attempted.

When adding/changing English text, keep keys stable when possible, document what
each variable represents in the English resource comment, run the review command
for existing languages, and request native review for source-changed items even
when placeholders have not changed. Update the glossary when terminology changes.
Do not use blanket text replacements or introduce translated CLI/model IDs.

## Validate the published language resources

```powershell
dotnet tools/FrameShift.LocalizationTool/bin/Debug/net8.0/FrameShift.LocalizationTool.dll verify-package --package-dir publish/FrameShift-win-x64 --report scratch/package-languages-001.json
```

This read-only command checks every registered language in both the app and the
subtitle worker. It requires each exact language satellite and compares every key
and value with the current repository resources without parent/English fallback.
English must exist in the main assembly. Runtime/framework satellites alone do
not satisfy the check. Missing, extra or outdated texts block validation.

The canonical release script now restores/tests the maintenance tool and runs
this check against the fresh publish payload before Inno Setup. Neither editing
that workflow nor running verify-package builds a new installer. An older package
can legitimately fail after source translations change; rebuild only when asked.

## Human acceptance record — still required

For each language, record contributor/reviewer (with consent), snapshot, editor
and version, date, original/returned files, check results, preview/diff and backup
location. Ask a non-developer to perform the guide without coding help: import as
text, edit a multiline/variable-bearing cell, save, reopen and return the folder.
Record where they needed help. Verify the returned CSV with the tool.

Test wording and real windows at supported DPI/text sizes, narrow widths, themes,
keyboard focus and Explorer menus. Actual Excel/LibreOffice use, accessibility,
native proofreading and installer lifecycle acceptance must be marked **not run**
until observed. Automated CSV/resource tests do not establish those outcomes.
