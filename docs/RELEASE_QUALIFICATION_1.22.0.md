# FrameShift 1.22.0 multilingual preparation

Prepared on 1 November 2026. This is the English/simplified Chinese/Spanish candidate,
based on published 1.21.1 and the six general correction lots. It contains no Find
Loop code. The former private loop candidate is preserved separately; see the
[branch plan](RELEASE_PLAN_1.22_1.23.md). Nothing is pushed, installed or published
as part of this preparation.

## Scope and initial bilingual verification

- Source version is 1.22.0. App ID, component identifiers, processing options,
  output naming, packages and pinned runtime/tool versions remain unchanged.
- Resources retain English and adapt the Chinese fork at `f078b0c`; the subtitle
  worker shares resources and receives only a display-language environment setting.
- Settings saves the user's next-launch language while preserving existing values.
  Installer language selects its own pages/components, Explorer captions and the
  installation's default application language. See [language behavior](LOCALIZATION.md).
- Debug compilation passes without warnings/errors. The final targeted localization
  run passes **11 tests, 0 failures, 0 skips**, including complete resource/format
  coverage, Unicode paths and action identifiers, language persistence, both Settings
  languages and two narrow Chinese layout cases. Hidden renders were inspected.
- The first full Debug attempt inside the sandbox could not connect to testhost;
  it executed **zero tests** and is recorded as aborted, not as a passing suite.
  Targeted tests execute successfully outside that isolation.
- Inno Setup 6.7.1 accepts both the vendored Chinese language file and all custom
  script messages. This is a syntax check, not installed acceptance.

Evidence is under ignored `scratch/release-1.22-*` paths.

## Initial bilingual candidate (archived)

The clean canonical `build_installer.ps1` completed successfully on 1 November 2026
from commit `a4bfb65405267d57f28a26e0b14cba54d22fd9c9`, without `-AllowDirty` or
`-RunInstaller`. All three locked restores, Release compilation, mandatory tests,
self-contained win-x64 publication, runtime/tool/notice checks and Inno Setup 6.7.1
compilation pass. No compiler warnings/errors are reported. An earlier attempt
failed on the existing test fixture's NuGet path concatenation; the successful run
uses a trailing separator in the local `NUGET_PACKAGES` path, without changing the
fixture, dependencies or release checks.

The complete Release suite passes **995 tests, 0 failures, 17 conditional skips,
1,012 total**, in **4 minutes 20 seconds**. Skips comprise six OCR scenarios requiring
explicit local-model integration, six real-model AI integrations and five subtitle
cases requiring their local models/corpora. They are not silently treated as passed.

| Artifact | Size/version | SHA-256 |
| --- | --- | --- |
| Archived `a4bfb65` installer | 175,343,311 bytes; 1.22.0 | `DFAFDE644E78EAC6AB56FD9D9965915E13EE9C84AD8807D8C57870A6F35734F6` |
| Historical app DLL (`a4bfb65` payload) | 1.22.0.0 | `26C55EEDEC78B152CF1294322760711B5C822F70FC65EB5ECA536600B12F7B16` |
| Application Chinese satellite | 260,608 bytes | `CBA4154FF8CE68E815359CA6CDF335E27F2D7F35DA711433F372A5895590C2F3` |
| Subtitle worker Chinese satellite | 260,608 bytes | `3B148DF0B015D99209265ECA99199C66C3789FC010A8B2CBBDDF77BDF2AA010F` |

The app product version identifies `1.22.0+a4bfb65405267d57f28a26e0b14cba54d22fd9c9`.
The unchanged worker's numeric version remains 1.18.1.0. Both payload runtimes are
8.0.31. Independent inspection finds **802 files**, including the app and worker
`zh-CN` satellites, with no private `.lic`, model `.onnx`, debug `.pdb`/`.dbg` or test
assemblies. Managed metadata confirms the expected English/Chinese resource names
and no `VideoLoop*` or `LoopVideoPlayer` types. Both resources have **1,790 matching
keys**; format specifiers and file-filter wildcards match between languages.

This first bilingual installer, its checksum, qualification and artifact report
are preserved under `builds/release-1.22-bilingual-a4bfb65/`. The adjacent installer
checksum identifies the latest candidate. Neither archived candidate was
installed or publicly released. Documentation/checksum commits after a clean
build do not change its binaries.

Detailed evidence: `scratch/release-1.22-bilingual-canonical-final.log`,
`scratch/release-1.22-bilingual-tests/localization-final.trx`,
`scratch/release-1.22-screenshots/`, and
`builds/release-1.22-bilingual-a4bfb65/artifact-verification.json`.

## Extensible language catalog candidate (archived)

On 1 November 2026 the clean canonical `build_installer.ps1` completed from
`86b78c33ba9f0433bbd3291680e3cd48afff5df5`, without `-AllowDirty` or `-RunInstaller`.
Locked restores, Release compilation, mandatory tests, self-contained publishing,
payload/runtime/tool/notice validation and Inno Setup 6.7.1 compilation pass.

- **1,006 passed, 0 failures, 17 conditional skips, 1,023 total**, in **4 min 18 s**.
  The same six OCR, six real-model AI and five subtitle cases retain their existing
  activation/model/corpus requirements.
- The targeted localization run passes **22 tests, 0 failures, 0 skips**. Resource
  completeness tests now cover every registered translation automatically. Tests also
  cover catalog/installer consistency, regional and installer aliases, invalid-value
  fallback, numeric-culture preservation and catalog-driven Settings choices.
- An isolated temporary third-language fixture, entirely under ignored `scratch/`,
  uses the actual worker compile/resource items and copies of the catalog/loader.
  Registering a synthetic French entry and adding its resource verifies regional
  resolution, installer aliases, worker satellite inclusion and English fallback.
  **No French catalog entry or resource was added to the application or installer.**
- Independent payload inspection finds **802 files**, only the English neutral and
  simplified Chinese app/worker resources, no deferred loop types, and no private
  licences, model weights, debug or test assemblies. The 1,790 translated keys are
  unchanged. Both runtimes remain 8.0.31; the worker version remains 1.18.1.0.

| Historical artifact (`86b78c3`) | Size/version | SHA-256 |
| --- | --- | --- |
| Archived `86b78c3` installer | 175,326,882 bytes; 1.22.0 | `941E63236098347491DC601727B23234961F2255F33185A7B031951891DD495C` |
| Historical app DLL | 1.22.0.0 | `C242D13621FD8D686FA337A8BC0C648D4AC497AD94950079AB9D997BDD8CE4F2` |
| Application Chinese satellite | 260,608 bytes | `FA5E68B8F2012E0D975936904CD372E98D5164747CED818BFE5FC093A5A6D1B1` |
| Subtitle worker Chinese satellite | 260,608 bytes | `87C700BC15599A9504C41EF092D3F0EB9829DE1DBBB56C05D0C5EF9D51E5ED48` |

App product version: `1.22.0+86b78c33ba9f0433bbd3291680e3cd48afff5df5`. The installer
was **not run**, and nothing was pushed or published. Evidence:
`scratch/release-1.22-language-catalog-canonical.log`,
`scratch/release-1.22-language-catalog/localization-catalog.trx`,
`scratch/language-extension-probe/`, and
`builds/release-1.22-bilingual-86b78c3/artifact-verification.json`.
The installer, checksum, qualification and report are archived in that same folder.
The [translation procedure](LOCALIZATION.md#adding-a-future-language) documents the
remaining explicit installer registration and translation/layout acceptance work.

## Spanish addition

- The catalog now registers neutral Spanish (`es`), including regional preferences
  such as `es-ES`, `es-MX` and `es-AR`, with no new architecture or dependencies.
- All **1,790 application resources** are translated. Their composite format
  specifiers, arguments and file-filter wildcards match English. The worker includes
  the Spanish resource automatically. English and Chinese resources are unchanged.
- All **139 installer custom-message keys** exist in each of the three languages.
  The installer uses the vendored official Spanish translation and stores `es` as
  its application default when Spanish is selected. Original upstream credits and
  the Inno Setup licence are retained in `installer/Languages`.
- **35 targeted Release tests pass, 0 failures, 0 skips**: full resource coverage,
  installer/catalog/message consistency, aliases, numeric culture, stable action IDs,
  language persistence, three Settings renders and four long-caption layout cases.
- A separate hidden WinForms process initializes Spanish before the action catalog
  and checks **43 translated actions at 360 and 580 pixels**. All action buttons and
  search bounds remain inside their containers. The 360-pixel render was inspected;
  Settings was also rendered and inspected in Spanish. These probes do not write
  user preferences or install the application.
- `README.es.md` covers installation, usage, features, local AI, development and
  packaging. All three READMEs link to each other; local links and images resolve.
  Credits specifically retain the Chinese translation, README and narrow-window
  contribution from zhazhayu01. Spanish linguistic review by a native speaker and
  installed acceptance remain pending.

Evidence: `scratch/release-1.22-spanish-targeted.log`,
`scratch/release-1.22-spanish/localization-spanish.trx`,
`scratch/release-1.22-spanish-screenshots/`, and
`scratch/release-1.22-spanish-ui-probe.log`.
The isolated UI probe's restore reports NU1900 because the sandbox cannot reach the
NuGet vulnerability feed. It is not a completed vulnerability audit; the canonical
release build below performs its own locked restores and required checks.

## Work before a public release

- Qualify the dependency/runtime maintenance identified by the retained audit work:
  D01/D03 together and evaluation of D02. Current .NET 8.0.31, SDK 8.0.425 and
  ImageSharp 4.1.2 pins are unchanged by the localization work. Recheck appropriate
  versions and the transitive vulnerability scan before public distribution.
- Validate a fresh install, upgrade from published 1.21.1, switching the installer
  language, Explorer component selection and uninstall in all three languages. In particular,
  foreign model files and existing user preferences must survive.
- Check actual Windows font scaling/DPI, keyboard/accessibility, both themes and
  native menu rendering. Hidden render/layout tests do not replace this acceptance.
- Exercise the installed general corrections with actual media/models and cancellation.
- Obtain acceptance of the exact candidate before any publication. The README's
  public release remains 1.21.1.
