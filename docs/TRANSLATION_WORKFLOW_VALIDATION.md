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
