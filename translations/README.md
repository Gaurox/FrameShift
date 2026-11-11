# New language preparation

These files prepare French, Japanese and Indonesian for contribution. None of
these languages is enabled in FrameShift or included in the installer yet.
The initial `001` supply folders remain empty historical references. The French
pilot has returned a complete CSV and populated its draft. Five action descriptions
have since been corrected in the English source and supplied in Japanese and
Indonesian drafts; the remaining translations in those two languages are pending.

## Current contribution folders — revision 002

Use **revision 002** for future contributions. It has the corrected English source
and current translation hashes. The five changed descriptions have been compared
with all eight language catalogs; existing Spanish, Portuguese, Russian, Chinese
and French wording was already correct and was preserved. Resource keys stay
unchanged so existing references remain valid.

| Language | Current contributor folder | Filled records | Remaining |
| --- | --- | ---: | ---: |
| Français | [French — 002](contributions/fr/002/) | 2,167 | 0 |
| 日本語 | [Japanese — 002](contributions/ja/002/) | 5 | 2,162 |
| Bahasa Indonesia | [Indonesian — 002](contributions/id/002/) | 5 | 2,162 |

Each folder includes `review.json` identifying the five English source changes.
No language has been enabled and no installer inputs have changed. The standard
upstream Inno messages remain separate future work. Japanese and Indonesian
native proofreading, and all visual acceptance, are still pending.

Keep `001` and `001-returned` as history. Their CSV sources/hashes deliberately
retain the old supplied texts; the checker now refuses five stale English sources.
Do not edit those references or replace hashes to force an old contribution in.

French pilot history: [returned CSV](contributions/fr/001-returned/translation.fr.csv),
[contributor feedback](contributions/fr/001-returned/FEEDBACK.txt),
[technical check](contributions/fr/001-returned/technical-check.json), and
[process assessment](../docs/TRANSLATION_FRENCH_PILOT.md). That report records the
check at the time of the pilot; use the French 002 CSV for current work. All 2,167
French target cells are filled, with no technical errors in revision 002. This is not approval for activation;
real spreadsheet use, independent proofreading and visual acceptance remain open.

## Initial contribution folders — historical references

English source reference: commit `e57903b`, prepared on 11 November 2026.
Each folder contains **2,167 translation records**: 2,024 application/worker
resources and 143 FrameShift-specific installer messages. The three folders
contain 6,501 records in total. This count excludes technical `AppLanguage` markers
and the standard upstream Inno Setup `.isl` messages.

| Language | Code | Contributor folder | Maintainer draft |
| --- | --- | --- | --- |
| Français | `fr` | [French — 001](contributions/fr/001/) | [French draft](drafts/fr/) |
| 日本語 | `ja` | [Japanese — 001](contributions/ja/001/) | [Japanese draft](drafts/ja/) |
| Bahasa Indonesia | `id` | [Indonesian — 001](contributions/id/001/) | [Indonesian draft](drafts/id/) |

Give a contributor a **copy of their complete contributor folder**, not the
maintainer draft. Start with `START_HERE.txt`. Each folder contains:

- `translation.csv`: UTF-8 with BOM, semicolon-separated, nine columns. Edit only
  `Translation`; import all columns as Text and preserve every record.
- `START_HERE.txt` and `GLOSSARY.md`: the current shared instructions and English
  terminology guide. Language-specific wording remains to be agreed during review.
- `FEEDBACK.txt`: editor/version, spreadsheet round-trip results and questions.
- `snapshot.json`: the supplied English source, context and empty target values.
  This is a supply record, not an accepted translation baseline.
- `READY.txt`: confirms that the folder is ready to give to a contributor. It
  explicitly identifies the language as a draft; it does not approve activation.

Keep the original `001` folder as the supply reference. Contributors work on a
copy and return a new CSV with their feedback. The French trial is recorded
separately in `001-returned`; Japanese and Indonesian now have only the five
action descriptions translated. The French trial used text/CSV tools only, without a real spreadsheet
session or PC control.

## Maintainer files and checks

Each draft was created with `draft.json`, an empty `Strings.<code>.resx`, and a
`FrameShift.<code>.iss` containing only its technical language marker. These files
stay under `translations/drafts`; they are not runtime resources or installer
inputs. The French draft now contains its imported contribution; Japanese and
Indonesian drafts each contain only the five action descriptions. Do not move
draft files into `src` or `installer` until a separate activation step.

The initial `001` exports have zero technical errors and 2,167 blank warnings each.
Strict completeness checks deliberately fail for these empty drafts. Import
previews against the initial empty drafts produced zero changes. Japanese and
Indonesian now have 2,162 blanks each. The French pilot's final strict check had
zero blanks, errors or warnings, and reimport is a no-op. The initial French
snapshot now reports 2,167 translation changes and five subsequent source changes
against its populated draft. No independent proofreading, real spreadsheet
round-trip or UI acceptance has occurred.

Build the tool as described in [its README](../tools/FrameShift.LocalizationTool/README.md).
From the repository root, a returned French contribution can be checked and
previewed with the following commands; substitute `ja` or `id` for other drafts:

```powershell
dotnet tools/FrameShift.LocalizationTool/bin/Release/net8.0/FrameShift.LocalizationTool.dll check --file <returned.csv> --draft translations/drafts/fr
dotnet tools/FrameShift.LocalizationTool/bin/Release/net8.0/FrameShift.LocalizationTool.dll import --file <returned.csv> --draft translations/drafts/fr
```

Applying an accepted contribution requires the existing explicit `--apply
--backup-dir <new-directory>` options. Review the preview, feedback and diff;
import success does not approve the language. Linked Explorer captions are
synchronized within the draft by the importer.

Keep the supplied snapshots and CSV reference columns unchanged. If the English
source changes, compare it with the previous supply snapshot and generate a new
numbered contributor folder instead of overwriting an existing supply. The next
revision after the current `002` would be `003`:

```powershell
dotnet tools/FrameShift.LocalizationTool/bin/Release/net8.0/FrameShift.LocalizationTool.dll bundle --language fr --draft translations/drafts/fr --baseline translations/contributions/fr/002/snapshot.json --output translations/contributions/fr/003
```

The new folder will include a review report. Deliberate edits from an older CSV
must be transferred with review; do not replace source hashes to bypass checks.
See [translation maintenance](../docs/TRANSLATION_MAINTENANCE.md) for backups,
concurrent-edit protection and the separate human acceptance requirements.
