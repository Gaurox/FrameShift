# French, Japanese and Indonesian contributions

All three catalogs contain 2,167 translated records (2,024 app/worker resources
and 143 custom installer messages). They are now registered for the owner's
local 1.24.0 installer test, alongside the original five languages.

## Current maintenance

Active resources live in `src/FrameShift/Core/Localization/Strings.<code>.resx`
and `installer/Languages/FrameShift.<code>.iss`. Use normal tool commands without
`--draft` for `fr`, `ja` and `id`. Historical `translations/drafts` and their READY
markers describe supply-time status; registered languages are deliberately
refused by draft mode. Do not import into those historical copies.

| Language | Last draft supply | Complete return |
| --- | --- | --- |
| Français | [002](contributions/fr/002/) | [French pilot](contributions/fr/001-returned/) |
| 日本語 | [003](contributions/ja/003/) | [Japanese return](contributions/ja/002-returned/) |
| Bahasa Indonesia | [003](contributions/id/003/) | [Indonesian return](contributions/id/002-returned/) |

The complete returned filenames are `translation.fr.csv`, `translation.ja.csv`
and `translation.id.csv`. Original `translation.csv` files and snapshots are
retained as supplied references. French 002 contains the corrected English source;
the original French pilot and 001 supplies have five stale English descriptions.
Never rewrite old hashes/reference cells to bypass source or concurrent-edit checks.

For the next contribution, generate a fresh active bundle into a new numbered
directory, retaining history. For example, from the repository root:

```powershell
dotnet tools/FrameShift.LocalizationTool/bin/Release/net8.0/FrameShift.LocalizationTool.dll bundle --language id --baseline translations/contributions/id/003/snapshot.json --output translations/contributions/id/004

dotnet tools/FrameShift.LocalizationTool/bin/Release/net8.0/FrameShift.LocalizationTool.dll check --file <returned.csv> --require-complete

dotnet tools/FrameShift.LocalizationTool/bin/Release/net8.0/FrameShift.LocalizationTool.dll import --file <returned.csv>
```

Import previews by default. Applying requires `--apply --backup-dir <new-directory>`.
Only Translation is edited by contributors; metadata and source columns remain
unchanged. Technical success does not certify wording. See the
[maintainer workflow](../docs/TRANSLATION_MAINTENANCE.md) and
[translator guide](../docs/TRANSLATING.md).

## Checks and limits

The [French](../docs/TRANSLATION_FRENCH_PILOT.md),
[Japanese](../docs/TRANSLATION_JAPANESE_PILOT.md) and
[Indonesian](../docs/TRANSLATION_INDONESIAN_PILOT.md) reports record their checks
at receipt. Exact CSV round-trips, strict checks, backup checks, Explorer links
and no-op reimports pass. These trials used text/CSV tools without PC control.
Independent native proofreading, a real Excel/LibreOffice session and visual
UI/installer acceptance remain pending for the local test candidate.

Translation CSVs are marked `-text` in `.gitattributes`. Git must preserve embedded
cell line endings because reference hashes use exact text. Do not normalize
meaningful whitespace or historical CSV bytes to silence Git warnings.

Standard upstream Inno `.isl` sources and provenance are recorded in
[installer languages](../installer/Languages/README.md), separately from the
FrameShift custom texts in the contribution CSV.
