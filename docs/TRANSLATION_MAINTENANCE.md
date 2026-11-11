# Translation contribution and maintenance

The maintenance workflow is offline and uses the existing five languages. It does
not register new languages, install the app, or build an installer by itself.
Build the tool as documented in [its README](../tools/FrameShift.LocalizationTool/README.md).
The commands below run from the repository root, after creating a scratch folder.

Executed automated checks and outstanding human acceptance are recorded in
[the workflow validation](TRANSLATION_WORKFLOW_VALIDATION.md).

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
