# Translation workflow validation — 11 November 2026

Scope: development tooling and contribution/maintenance workflow, lots 1–3.
Existing language catalog only: English, Simplified Chinese, Spanish, Brazilian
Portuguese and Russian. No installer regeneration, installation or publication.

## Executed checks

- SDK 8.0.427; locked restore of the standalone maintenance tests and fixtures.
- **41 standalone Release tests passed**, no failures/skips. They cover CSV,
  import/rollback, snapshots, review statuses, contributor bundles, exact packaged
  resources and missing-language fallback prevention. Package fixtures are small
  resource-only test assemblies, not the production app or worker.
- **108 application localization/Explorer tests passed**, no failures/skips.
- The updated native probe builds in Release with zero warnings/errors and uses
  the shared language catalog instead of two hardcoded language lists.
  **23 unregistered native COM/menu checks passed**, using the existing production
  DLL and isolated test fixtures. No production COM registration or Explorer input.
- PowerShell syntax parsing of the updated canonical release script passed.
  The build script itself was not run. Future releases will run the maintenance
  tests and audit app/worker language resources before compiling Inno Setup.
- A real Spanish contributor bundle was generated. Its **2,167 CSV entries** pass
  strict checks; guides, glossary, feedback form, snapshot, review and READY marker
  are present. No source translations were applied during this smoke check.
- Six English Balance volume resource comments now provide real context. All
  English resource keys/values were compared before/after and remain identical.

## Existing package audit

The read-only audit checks ten component/language combinations. It correctly
reports four outdated entries in the retained 1.24.0 publish directory: the
Chinese Strong label and Portuguese balancing description, each in app and worker.
These are the two source corrections whose installer rebuild was stopped at the
owner's request. The audit does not repair or rebuild the old package.

The retained Setup SHA-256 remains:
`2CA499A5C56BF3FCA8B70CA8B9FEE383EE9ECE9AB1D259A62E4B6FB86B81A96B`.

## Not yet accepted by a human contributor

### Import corrections and second review — 11 November 2026

After commit `a9274d2`, review reproduced two import defects: an old unchanged CSV
could revert a newer translation, and missing destination entries were silently
ignored. The corrective CSV format adds `OriginalTranslationHash`; old eight-column
files are rejected and require a fresh export. Concurrent incompatible edits block
the import, unchanged cells preserve newer project values, and missing resource/
installer entries are inserted. Linked Explorer counterparts are protected too.

Validation after correction: **54 standalone Release tests and 108 application
localization/menu tests passed**. Six CLI scenarios on isolated repository copies
confirmed preservation of newer text, conflict rejection without writes, insertion
of missing app and installer entries with backups, linked-caption protection, and
legacy-format rejection. A second code/diff review found no further error in the
corrected scope. No runtime translation, language registration or installer binary
was changed by these corrective checks.

### Draft language preparation — 11 November 2026

The maintenance tool now prepares an unregistered language in a separate directory
with empty resources, a custom-message draft and versioned metadata. All exchange
and maintenance commands require an explicit `--draft` selection for that language.
No French, Japanese or Indonesian translation files were created or registered.

- **85 standalone Release tests passed**, no failures/skips (54 existing + 31 draft
  cases). They cover empty exports, partial imports, draft-only writes, backups,
  rollback, linked captions, concurrent edits, language mismatches, malformed
  metadata/messages and rejection of protected source/installer locations.
- **15 CLI scenarios passed** using the current repository's English catalog and
  the local-use code `qaa` under ignored scratch directories. All **2,167 entries**
  initially export blank; incomplete work passes partial checks and fails strict
  completeness checks. A synthetic contribution was previewed, imported with
  backup, snapshotted and compared without enabling a language. Missing draft
  selection, mismatched languages, reuse of an existing draft directory and
  draft selection for package verification were refused. A real Windows junction
  was created in scratch, refused by the tool and removed.
- Byte hashes of runtime localization sources and every installer file were
  compared before/after the CLI trial and remained identical. The retained Setup
  SHA-256 remains the value recorded above. No installer/compiler/release script
  was run. The package verifier still covers only the five registered languages.
- Final code/diff review and whitespace checks found no further error. The new
  tests build small resource-only fixtures; this step did not rebuild the app.

This validates the preparation tooling, not the wording or visual readiness of
any new language. Human acceptance below remains outstanding.

### Initial French, Japanese and Indonesian contribution folders — 11 November 2026

After commit `e57903b`, the committed tool prepared the three real drafts under
`translations/drafts` and generated their `001` contributor folders under
`translations/contributions`. No tool/runtime/installer implementation changed
during this preparation. No translated text was added.

For each of `fr`, `ja` and `id`:

- The CSV contains **2,024 app/worker + 143 installer custom-message records**,
  all 2,167 target cells empty, in the current nine-column format with UTF-8 BOM.
- Partial checks pass with zero errors and 2,167 blank warnings. Strict
  completeness checks fail as expected. Import preview reports zero changes.
- Snapshot comparison reports only 2,167 blank items and no other changes.
- The complete contributor folder contains CSV, shared guide, glossary, feedback
  form, snapshot and a READY marker identifying the draft. Guide/glossary hashes
  match the source documents. Maintainer resources have no translated entries.

Hashes of the runtime localization sources, every installer file and the release
script remained identical before/after preparation. No installer was regenerated,
no language was registered, and the human acceptance statuses below remain open.
The initial supply reference and the non-developer handoff are documented in
`translations/README.md`. The next step is the French translation/pilot, not activation.

### French contribution pilot — 11 November 2026

The [French pilot report](TRANSLATION_FRENCH_PILOT.md) records the returned CSV,
terminology, feedback and role separation. Only the Translation column was
changed in the contribution; the initial supplied folder/snapshot stayed intact.
The user excluded PC control, so this was a text/CSV-assisted simulation, not a
real Excel/LibreOffice or external non-developer usability session.

Six intermediate checks passed with 813, 1,053, 1,304, 1,567, 1,832 and finally
2,167 filled records. The final strict check passes with zero errors, warnings or
blanks. All reference fields, leading/trailing whitespace and line-break counts
are preserved. The 60 source-identical values were reviewed. Two deliberate
mistakes in disposable copies (missing variable, edited source) were refused.

Import with verified recovery backups populated only the two French draft files.
All 2,167 values survive exact reexport, all 51 Explorer caption links agree, and
a second import preview is a no-op. Snapshot review reports 2,167 translation
changes only. Runtime resources, all installer files, release script, original
French supply folder and Japanese/Indonesian folders remain unchanged. The Setup
SHA-256 still matches the recorded value. The new contribution has a small final
technical report in its returned folder; detailed trial logs/backups remain in
`scratch/fr-translation`.

The report identifies 2,018 automatic-only contexts and five French descriptions
in the English pivot, plus ambiguous labels/fragmented phrases needing context.
Those source/context issues were recorded, not changed during translation.
The human acceptance statuses below still require observation; no new language
has been activated and no installer has been rebuilt.

### Five action descriptions corrected across language catalogs — 11 November 2026

The five descriptions introduced in French in the English source at `a4bfb65`
are now English. Their keys and references are unchanged. The existing Spanish,
Portuguese, Russian, Chinese and French translations were compared with the
intended meanings and retained. Only those five descriptions were added to the
Japanese and Indonesian drafts using fresh CSV export/check/preview/import with
backups; neither new language was enabled.

Contributor revision `002` was generated for French, Japanese and Indonesian,
with new English sources/hashes and a report identifying five source changes.
French remains complete (2,167 records, no blanks/errors). Japanese and Indonesian
each have five filled records and 2,162 blanks, with no technical errors. Reimport
previews of all three current bundles are no-ops. The initial `001` folders and
French returned pilot files/reports were retained as historical evidence, not
rewritten to synthesize hashes onto an old contribution.

**85 standalone Release tests passed**, with no failures or skips, including
fresh compilation/auditing of the app/worker resource-only test fixtures. Runtime
source changes are restricted to five English resource values; all installer
inputs and the retained Setup remain unchanged. No PC control or installer build
was used. Human acceptance statuses below remain open.

### Human acceptance status

| Scenario | Status |
| --- | --- |
| Non-developer completes the supplied guide without coding assistance | Not run |
| Actual Excel open/edit/save/reopen, including multiline and variables | Not run |
| Actual LibreOffice open/edit/save/reopen | Not run |
| Native-language proofreading of a returned contribution | Not run |
| Real UI/DPI/text scaling/accessibility and installed menu acceptance | Not run |

Use FEEDBACK.txt in a contributor bundle and the record described in
[the maintenance procedure](TRANSLATION_MAINTENANCE.md) for these steps. Tooling
readiness does not imply that the non-developer acceptance criterion has passed.
