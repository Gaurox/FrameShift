# FrameShift 1.22.0 bilingual preparation

Prepared on 1 November 2026. This is the new English/simplified Chinese candidate,
based on published 1.21.1 and the six general correction lots. It contains no Find
Loop code. The former private loop candidate is preserved separately; see the
[branch plan](RELEASE_PLAN_1.22_1.23.md). Nothing is pushed, installed or published
as part of this preparation.

## Scope and initial verification

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

## Canonical Release candidate

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
| `installer/FrameShift_1.22.0_Setup.exe` | 175,343,311 bytes; 1.22.0 | `DFAFDE644E78EAC6AB56FD9D9965915E13EE9C84AD8807D8C57870A6F35734F6` |
| `publish/FrameShift-win-x64/FrameShift.dll` | 1.22.0.0 | `26C55EEDEC78B152CF1294322760711B5C822F70FC65EB5ECA536600B12F7B16` |
| Application Chinese satellite | 260,608 bytes | `CBA4154FF8CE68E815359CA6CDF335E27F2D7F35DA711433F372A5895590C2F3` |
| Subtitle worker Chinese satellite | 260,608 bytes | `3B148DF0B015D99209265ECA99199C66C3789FC010A8B2CBBDDF77BDF2AA010F` |

The app product version identifies `1.22.0+a4bfb65405267d57f28a26e0b14cba54d22fd9c9`.
The unchanged worker's numeric version remains 1.18.1.0. Both payload runtimes are
8.0.31. Independent inspection finds **802 files**, including the app and worker
`zh-CN` satellites, with no private `.lic`, model `.onnx`, debug `.pdb`/`.dbg` or test
assemblies. Managed metadata confirms the expected English/Chinese resource names
and no `VideoLoop*` or `LoopVideoPlayer` types. Both resources have **1,790 matching
keys**; format specifiers and file-filter wildcards match between languages.

The adjacent installer checksum identifies this new bilingual candidate; it does
not identify the archived loop candidate. The installer was **not run**. Neither
GitHub nor the public website was changed. Documentation/checksum commits after
this clean build do not change its binaries.

Detailed evidence: `scratch/release-1.22-bilingual-canonical-final.log`,
`scratch/release-1.22-bilingual-tests/localization-final.trx`,
`scratch/release-1.22-screenshots/`, and
`builds/release-1.22-bilingual/artifact-verification.json`.

## Work before a public release

- Qualify the dependency/runtime maintenance identified by the retained audit work:
  D01/D03 together and evaluation of D02. Current .NET 8.0.31, SDK 8.0.425 and
  ImageSharp 4.1.2 pins are unchanged by the localization work. Recheck appropriate
  versions and the transitive vulnerability scan before public distribution.
- Validate a fresh install, upgrade from published 1.21.1, switching the installer
  language, Explorer component selection and uninstall in both languages. In particular,
  foreign model files and existing user preferences must survive.
- Check actual Windows font scaling/DPI, keyboard/accessibility, both themes and
  native menu rendering. Hidden render/layout tests do not replace this acceptance.
- Exercise the installed general corrections with actual media/models and cancellation.
- Obtain acceptance of the exact candidate before any publication. The README's
  public release remains 1.21.1.
