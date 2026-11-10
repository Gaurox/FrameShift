# FrameShift 1.23.0 — Release qualification

## Owner acceptance and publication authorization — 10 November 2026

After validating the Cut / Find Loop work, requesting the final hint removal,
and receiving the refreshed installer, the owner approves the release text and
explicitly authorizes pushing the work and publishing 1.23.0 with that description
translated into English. This supersedes the earlier non-publication status.

Publish the unchanged qualified installer with SHA-256
`1571BBAD6A5BFBDF7FD5A2568A729382E952F23861832C69751544B20F63045C`, built from
clean application source `5b60911264802966900489dcbdde28b307e17836`. Later release
documentation does not change the payload. The owner accepts the tested scope;
this authorization does not mark the remaining OS DPI/monitor, installer/Explorer
lifecycle, broader HDR or conditional-model scenarios as executed.

The sections below retain the historical private-candidate preparation and exact
build evidence. Their non-publication/uncommitted statements describe those
earlier stages. The agent does not install the application or control the desktop.

Immediately before publication, fresh-cache NuGet advisory checks for the
application, subtitle worker and tests report **zero vulnerable packages**,
including transitive dependencies, with no report warnings. Local evidence:
`scratch/release-1.23-publication/{app,worker,tests}-vulnerabilities.json`.

## Publication verified — 10 November 2026

[Release v1.23.0](https://github.com/Gaurox/FrameShift/releases/tag/v1.23.0)
was published at **2026-11-10 20:22:48 UTC**, as the latest stable release
(GitHub release ID `440866718`). The approved English body was verified before
publication. Annotated tag `v1.23.0` points to release-documentation commit
`b1a7f988a58b8e9e714a580a9ad807dcd77a6cc2`; application source remains
`5b60911264802966900489dcbdde28b307e17836`.

Published assets were checked against the qualified archive and GitHub's
SHA-256 digests:

- `FrameShift_1.23.0_Setup.exe`: **179,950,669 bytes**, SHA-256
  `1571BBAD6A5BFBDF7FD5A2568A729382E952F23861832C69751544B20F63045C`.
- `FrameShift_1.23.0_Setup.exe.sha256`: **95 bytes**, file SHA-256
  `FB10117FC346F23490E805517572D4EF2F8086A0BB5F4376354CC64832290A3F`.

No installer execution or desktop control was performed for publication.

## Historical private-candidate status

First prepared on 9 November 2026; the 10 November candidates add complete video
menus, then adjacent classic/AI menus for all supported file families.
The subsequent Cut/Loop UI follow-up is based on the committed menu checkpoint
`33c0df4`; its geometry tests and private candidate are recorded below.
**Not published, not final,
and not feature-frozen.** Work continues on `codex/integrate-find-loop-1.23`.
No push, tag, GitHub release, website update or installation is part of this build.
The historical backup `codex/feature-loop-1.23` remains at `137311e`.

## Build contract

- Application/file/assembly version: 1.23.0 / 1.23.0.0 / 1.23.0.0.
- Canonical command: `build_installer.ps1`, without `-RunInstaller`.
- SDK 8.0.427; self-contained .NET/Windows Desktop 8.0.33; PKCS 8.0.2.
- Locked dependencies and the verified bundled FFmpeg/FFprobe archive are retained
  from published 1.22.0. No dependency upgrade belongs to this packaging change.
- The subtitle worker retains its independent component version.
- Existing installer languages, components and model handling remain in place.
  The latest candidate migrates classic/AI video, audio and image menus, plus
  PDF OCR, to one x64 Native AOT Shell component; Arm64 keeps the static menus.
  Find Loop remains accessible
  through Cut Video.

The canonical workflow restores locked dependencies, runs the complete Release
suite, rebuilds the exact publish payload, verifies its runtime/tool/notice contents
and compiles Inno Setup. Build results and artifact identities are recorded below
after successful execution; the workflow itself does not publish externally.

## Artifact retention

The normal output is `installer/FrameShift_1.23.0_Setup.exe`. A successful candidate
is also copied into a new dated directory under `builds/release-1.23-private/`,
with its SHA-256, source revision, build log and payload manifest. Use that dated
copy to identify the tested candidate: later development builds may regenerate
the normal filename while retaining version 1.23.0. Earlier candidate snapshots
must remain intact. The model payload is not downloaded by this preparation.

## Remaining acceptance

- Interactive Windows workflows, actual DPI/text scaling, themes, keyboard and
  narrow/long translated layouts; monitor transitions where available.
- Fresh installation, upgrade from published 1.22.0, component changes,
  uninstall/reinstall and actual Explorer menu interaction.
- Broader subjective loop quality and real HDR footage, preserving the limits
  described in the historical Find Loop validation.
- Final scope review, release notes, dependency review and owner authorization
  before any public release.

Automated tests and packaging success do not mark these scenarios passed. The
existing installed application is not replaced by preparing this installer.

## Executed private candidate — 9 November 2026

- **Source:** `df0fae28ca69682aff69e473c498ea075eb87db4` on
  `codex/integrate-find-loop-1.23`, built from a clean working tree. The application
  informational version is `1.23.0+df0fae28ca69682aff69e473c498ea075eb87db4`;
  application assembly/apphost versions are `1.23.0.0`.
- **Canonical workflow:** exit 0. Locked restores, full Release tests,
  self-contained win-x64 publish, runtime/tool/notice checks and Inno Setup all
  succeed. The test suite reports **1,414 passed, 0 failures, 18 conditional skips,
  1,432 total**, in approximately **9 minutes**. Skips retain their normal opt-in
  or unavailable OCR/AI-fixture conditions, including the optional loop benchmark.
- **Build environment:** repository SDK 8.0.427, existing `scratch/nuget/` package
  cache (`NUGET_PACKAGES` with its trailing separator) and
  `scratch/release-1.22-serviced-http-cache`. Inno compiler:
  `C:\Users\Adrien\AppData\Local\Programs\Inno Setup 6\ISCC.exe`.
- **Installer:** `installer/FrameShift_1.23.0_Setup.exe`, **178,976,771 bytes**.
  SHA-256: `7A19A15AF4B366E7A5230E5507ADE67A1D97E45F5B2534203F351B09C263EF14`.
  The matching tracked checksum is `installer/FrameShift_1.23.0_Setup.exe.sha256`.
- **Retained candidate:**
  `builds/release-1.23-private/20261109T010854Z/FrameShift_1.23.0_Setup.exe`.
  The archived copy has the same SHA-256. That directory also contains
  `candidate.json`, `payload-manifest.json`, `canonical-build-success.log`,
  `packaged-probe.json`, its checksum and synthetic smoke-test media.
- **Payload:** 808 files, 627,148,605 bytes, with a per-file SHA-256 manifest.
  No PDB/DBG files or development FFmpeg ZIP are present. The canonical checks
  verify the retained .NET/Windows Desktop 8.0.33, PKCS 8.0.2 and pinned tools.
- **Packaged translations:** direct resource lookup from the exact published
  application and worker assemblies finds all five languages, **2,006 entries
  and 163 Find Loop keys per language**, with the expected Find Loop labels.
- **Packaged loop smoke:** the packaged assembly, FFmpeg and FFprobe generate
  synthetic media and export natural/ping-pong loops through `FfmpegRunner` /
  `FfprobeRunner`. Natural export yields **45 frames / 3.000 s**; its repeat
  creates `_loop_001.mp4` and preserves the first output hash. Forward/backward
  yields **84 frames / 5.600 s**. All are silent as requested. The source path
  contains spaces and accents, its hash is unchanged, it can be reopened
  exclusively, and no owned output workspace remains. This tests the packaged
  processing code directly, not a manual apphost/Explorer interaction.

The installer was **not launched or installed**. No branch/tag/release/website
was published. These artifacts are a private working snapshot; subsequent
development and new private 1.23.0 builds remain expected. The acceptance items
above remain open.

## Executed private candidate — 10 November 2026, video menus

- **Source:** working tree on `codex/integrate-find-loop-1.23`, based on
  `ff6ad5d884abaea7de3038d2845b6427975c1b6c`. This is a **dirty-source build**,
  not a clean committed revision. The dated archive contains `source-changes/`
  and `source-manifest.json` for the build inputs. Final documentation updates
  follow that snapshot. No Find Loop processing, Cut Video or picker code was
  changed by this menu integration.
- **Canonical workflow:** `build_installer.ps1 -AllowDirty`, without
  `-RunInstaller`, exits 0. Locked restores, the complete Release suite,
  self-contained publish, Native AOT compilation, payload verification and
  Inno Setup succeed. **1,437 passed, 0 failed, 18 conditional skips,
  1,455 total**, in 9 minutes 16 seconds. The skips retain their existing
  opt-in/fixture conditions; they are not acceptance passes.
- **Native qualification:** **15 passed, 0 failed**, against the production-source
  fixture and the exact packaged DLL. This covers 18 flat video commands in
  all five languages, synthetic COM file selection, captured action arguments,
  component subsets, multiselect rules, ownership/schema rejection, bounded
  command lines and stable USER/GDI resources over repeated menu lifetimes.
  The fixture intercepts launch; these checks do not constitute registered
  Explorer interaction or an actual installed action launch.
- **Toolchain:** SDK 8.0.427, application/worker runtime 8.0.33, Native AOT
  compiler/runtime 8.0.33; the latter's official license and third-party notices
  are included. Native projects compile without warnings. A final mandatory
  application Debug build also succeeds with 0 warnings and 0 errors.
- **Installer:** `installer/FrameShift_1.23.0_Setup.exe`, **179,927,719 bytes**.
  SHA-256: `8B174FCA6A12BB76B9CC2BDDD568CCF560B721FC0444F082BD1FC8BBAF706417`.
  The tracked checksum now identifies this candidate. The 9 November archived
  installer retains its original `7A19A15A…63EF14` SHA-256 unchanged.
- **Retained candidate:**
  `builds/release-1.23-private/20261110T174906Z-menus/` contains the installer,
  checksum, `candidate.json`, `canonical-build.log`, `payload-manifest.json`,
  source snapshot/manifests, `native-probe.json`, `shell-component.json`,
  `translation-preservation.json` and `packaged-translations.json`.
- **Payload:** **812 files, 629,808,886 bytes**. Only the production native
  component and its manifest enter `Shell/<sha256>/`; prototype and test fixture
  are excluded. The native x64 DLL is **2,580,992 bytes**, SHA-256 / directory ID
  `7c5cb962242ab85634722e6f4c7268c07878108d4b249576d49cbf49dfbcbd43`.
- **Translations:** all 2,006 previous resource entries per language are
  preserved; only three flat extraction captions are added. Resource lookup
  against the exact published assemblies verifies **2,009 entries** in English,
  simplified Chinese, Spanish, Brazilian Portuguese and Russian.
- **Installation strategy implemented, not exercised here:** content-addressed
  native DLLs, owned CLSID/metadata registration, selected-component migration
  with restoration on failure, and deferred removal of loaded files. Native AOT
  remains loaded until the host exits; real activation, upgrade and uninstall
  therefore remain mandatory acceptance gates.

The installer was **not run**, the existing installation remains **1.22.0.0**,
and the new Shell CLSID is not registered on this machine. No desktop control,
commit, push or publication was performed. The full automated suite includes
Find Loop; its actual **Explorer → Cut Video → Find Loop → preview/export** path
still requires installed acceptance. Fresh/upgrade/component-change/uninstall,
registered menu rendering, icons, keyboard and DPI checks remain open alongside
the existing subjective loop/HDR acceptance. This candidate is a private,
reviewable integration, not a final release approval.

## Executed private candidate — 10 November 2026, unified classic/AI menus

### Owner observation motivating this follow-up

The owner reports that the previous candidate works and supplies an MP4 context
menu screenshot showing the complete 18-command video list. FrameShift and
FrameShift AI are separated in the outer menu. This confirms the observed video
rendering, not every action or installer lifecycle. The agent did not perform
that installation or control Explorer. The screenshot is retained as
`user-video-only-menu.png` with the new archive.

### Exact follow-up candidate and automated results

- **Source:** dirty working tree on `codex/integrate-find-loop-1.23`, base
  `ff6ad5d884abaea7de3038d2845b6427975c1b6c`. The archive contains
  `source-changes/` and `source-manifest.json`; final qualification documentation
  follows that snapshot. Find Loop/Cut Video/picker processing sources are
  unchanged. No commit, push, tag or publication was performed.
- **Scope:** one handler inserts FrameShift then FrameShift AI consecutively at
  Explorer's supplied insertion position. It handles classic/AI video, audio,
  images and PDF OCR, omitting empty roots. With all components: video **18 + 4**,
  MP3 **7 + 3**, images **8 + 8**, PDF **0 + 1**. Narrower audio-format support,
  single/batch rules and five fixed background-removal models are preserved.
  The global placement among other applications remains Shell-controlled.
- **Migration:** new CLSID `{F6740A5A-42BC-4716-9512-531F0B254862}`,
  `FrameShift.NativeMenus` registration and metadata schema 2. The installer
  cleans the previous video CLSID/handler and migrates both static roots after
  preparing all descriptions. Still-loaded video-only handlers reject schema 2.
  Failure restoration covers both roots. Arm64 retains the static fallback.
- **Canonical workflow:** `build_installer.ps1 -AllowDirty`, without
  `-RunInstaller`, exits **0**. **1,443 passed, 0 failed, 18 conditional skips,
  1,461 total**, in **8 minutes 53 seconds**. Native AOT publish, exact payload
  verification and Inno compilation succeed; Inno reports **80.406 seconds**.
  SDK 8.0.427 and application/worker/AOT servicing 8.0.33 remain unchanged.
- **Native checks:** **23 passed, 0 failed**. The expanded production-source
  fixture checks adjacent roots and every supported extension in all five
  languages, exact IDs/captions/flat commands, every action's captured arguments
  including AI model variants, AI-only/classic-only/core-only selections, old
  schema rejection and repeated USER/GDI lifetimes. No real action process or
  registered Explorer interaction is performed by this fixture. The production
  DLL's ABI/exports and exact hash are checked separately within that probe.
- **Additional checks:** the command catalog/installer contract tests pass for
  all 46 native command variants. The mandatory application build succeeds with
  **0 warnings, 0 errors**. The five resource catalogs are byte-for-byte unchanged
  since the video-only candidate, and direct lookup in the exact published
  assemblies confirms **2,009 entries per language**.
- **Installer:** **179,950,729 bytes**, SHA-256
  `5F8911628447FC983524B74FFB0EB999BE6498D86C8CE4D0C116D12B14789037`.
  Normal output and its tracked checksum identify this new build; the previous
  dated video installer retains its original `8B174FCA…706417` checksum.
- **Archive:** `builds/release-1.23-private/20261110T182134Z-unified-menus/`.
  Contains installer/checksum, `candidate.json`, `canonical-build.log`,
  `payload-manifest.json`, source snapshot/manifests, `native-probe.json`,
  `shell-component.json`, translation preservation/packaged reports and the
  earlier user screenshot.
- **Payload:** **812 files, 629,850,482 bytes**; production Shell DLL
  **2,622,464 bytes**, SHA-256 / immutable directory ID
  `bcd0c75224c5b22ca187646735624139ecd7d8245e72fd6276b762846299990c`.
  The exact source hashes match the component manifest. Only the product DLL
  and its manifest enter `Shell/`; prototype and test binaries are excluded.

### Acceptance still required

The agent has **not run this installer**, registered the new handler or taken
desktop control. The owner still needs to confirm adjacent classic/AI groups
for video/audio/images and PDF OCR, action launch and selected-component subsets.
Include the upgrade from the already-tested video-only candidate, fresh install,
1.22 upgrade, repeated 1.23 upgrade, component removal, loaded-DLL uninstall and
visual/DPI/keyboard checks. Native AOT hosts can retain loaded code until exit;
content-addressed files and the distinct CLSID do not certify every activation
path. Cut Video → Find Loop → preview/export and the earlier loop/HDR acceptance
remain part of final Windows qualification. These open items are not marked
passed by the isolated tests or the owner's earlier video-only observation.

## Cut / Loop window follow-up — 10 November 2026

The owner requested a taller window, automatic height changes for the advanced
panel, and less movement/flicker when selecting detected candidates. This work
is based on menu checkpoint `33c0df4` on `codex/integrate-find-loop-1.23`.

- The collapsed client height is **900 logical pixels**, bounded by the current
  monitor working area. Advanced options add their measured height; closing
  them restores the collapsed height. The explicit fit runs at load, context
  change, advanced toggle and DPI change. Candidate lists and preview updates
  do not resize the form or override a manually resized window.
- Cut/Loop gives options the remaining space after a usable preview, relaxing
  that preferred preview height on very short windows so neither pane vanishes.
  The footer remains outside the scrollable options. Other timeline editors
  retain their previous 40/60 split by default.
- Normal selection/preview messages reserve stable geometry using existing
  translated resources; longer actual errors can still wrap. Reserved text
  measurements are bounded/cached and invalidated for font/DPI changes.
- Candidate selection is grouped under suspended layout, and duplicate
  SelectionChanged/CellClick or unchanged frame edits do not restart the
  preview/index work. The grid and layout containers use buffering; stop
  commands and the progress slot retain their space. A decoded bitmap replaces
  the previous image before disposing it, avoiding an intermediate blank image.
- The viewport avoids snapping a partly visible candidate grid on focus. It
  calculates descendant bounds from layout coordinates during resize/show,
  keeping explicit reveal and normal keyboard navigation. See the
  [WinForms ScrollToControl contract](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.scrollablecontrol.scrolltocontrol?view=windowsdesktop-8.0).
- The loop search/export engine, native timing implementation, CLI picker,
  output naming and five resource catalogs are unchanged by this follow-up.
  Existing unified classic/AI menus remain included in the candidate.

The final **14 targeted geometry/state regressions pass** in
`scratch/cut-loop-layout/layout-qualified.trx`: 1080p grow/shrink, repeated
advanced toggles, both loop contexts, large-font stress, short/narrow windows,
five languages, stable selection/preview geometry, duplicate-click suppression,
busy/progress state geometry and partial-grid focus scrolling. The hidden
viewport test uses the protected scroll primitive to exercise a scrolled state
without showing a window or sending desktop input. Optional DrawToBitmap
renders omit some native child controls and are **not** desktop visual QA.

### Executed candidate and retained evidence

- **Source:** dirty tree based on
  `33c0df48314e272473c336556ce559dcdb2b9e1d`. The archive contains the changed
  source snapshot and manifest; final qualification documentation follows that
  snapshot. This new UI lot is not committed or published by this build.
- **Canonical workflow:** `build_installer.ps1 -AllowDirty`, without
  `-RunInstaller`, exits **0**. **1,457 passed, 0 failed, 18 conditional skips,
  1,475 total**, in **9 minutes 45 seconds**. This includes the final UI
  regressions and the existing media, timing, cancellation and export tests.
  Skips keep their previous opt-in/fixture conditions. Native menu checks:
  **23 passed, 0 failed**. Inno compilation succeeds in **80.063 seconds**.
  The final mandatory application Debug build also passes with **0 warnings,
  0 errors**. SDK 8.0.427 and servicing 8.0.33 are unchanged.
- **Installer:** `installer/FrameShift_1.23.0_Setup.exe`, **179,956,400 bytes**,
  SHA-256 `0F8E9FDD1A3ED0DFA633C56C23B5BB31E7490C94333E4A82053A40DABCB78865`.
  The normal checksum now identifies this candidate. The previous dated
  unified-menu installer retains its `5F891162…789037` checksum unchanged.
- **Archive:** `builds/release-1.23-private/20261110T190523Z-cut-loop-layout/`.
  Contains installer/checksum, `candidate.json`, `canonical-build.log`, full
  payload manifest, source snapshot/manifest, native report/component manifest,
  `layout-qualified.trx`, `viewport-qualified.trx`, translation reports and
  `loop-preservation.json` confirming the Core/CLI-picker source preservation.
- **Payload:** **812 files, 629,862,770 bytes**. Native Shell DLL **2,622,464 bytes**,
  SHA-256 / build directory ID
  `3f3158dfe4778f229569b70663554884b16ec83fdcf1310e7cbd33710bfa8ce3`.
  Its source is unchanged by the UI lot; the binary is rebuilt from the current
  source revision. Packaged resources still contain **2,009 entries per language**,
  and all five source catalogs match the previous candidate byte-for-byte.

Real OS DPI/monitor transitions, perceived flicker, advanced-panel interaction
with the installed application and the previous Explorer/install/upgrade/uninstall
acceptance remain open. The agent has **not run this installer**, registered a
handler, controlled the user's desktop, committed this UI lot or published it.

## Standard Find Loop content fitting — 10 November 2026

The owner confirms the ordinary Cut window size, but reports that activating
Find Loop leaves the candidate list/commands too low even without advanced
options. The supplied screenshot shows 42 candidates. The preceding candidate's
900-pixel base plus advanced-only increment did not measure the standard loop
view, so its scrolling was not proof that the screen maximum had been reached.

The new policy keeps Cut's existing base and fits the complete active loop
view: measured current options, the existing fixed-height candidate grid,
header/footer and a usable preview. It caps the outer window at the current
monitor working area. If that cap is reached, options scroll and advanced
open/close may legitimately keep the same window height. The fit occurs on
context change, the first nonempty result list, advanced toggle, load and DPI
change; individual selection and subsequent nonempty result updates do not
refit. Three bounded passes allow width/wrapping to settle after screen clamping.

**15 targeted tests pass** in
`scratch/cut-loop-content-fit/layout-content-fit.trx`, including standard-loop
growth before advanced options, full control fit on a large area, both advanced
states capped at a 1080p work area, return to Cut size, unchanged grid/window
size from two to 42 candidates, plus the prior language/selection/busy/focus and
small-window regressions. Core processing, CLI picker and translations are
unchanged.

- **Canonical build:** `build_installer.ps1 -AllowDirty`, without
  `-RunInstaller`, exits **0**. **1,458 passed, 0 failed, 18 conditional skips,
  1,476 total**, in **9 minutes 41 seconds**; **23 native checks pass**. Inno
  compilation succeeds in **81.172 seconds**. The mandatory application Debug
  build passes with **0 warnings, 0 errors**. Toolchain/servicing are unchanged.
- **Source:** dirty tree based on
  `33c0df48314e272473c336556ce559dcdb2b9e1d`, with changed sources/manifests
  retained in the archive. Final qualification documentation follows that
  snapshot. The new UI lot remains uncommitted.
- **Installer:** **179,940,098 bytes**, SHA-256
  `054F8A9F624438E3D8F7FCBFF7BC889779135D07B664C4115B7F10349576B7C9`.
  The normal installer/checksum now identify this build; the previous dated
  Cut/Loop candidate retains its `0F8E9FDD…B78865` checksum unchanged.
- **Archive:** `builds/release-1.23-private/20261110T194553Z-loop-content-fit/`.
  Contains installer/checksum, candidate/log, source and payload manifests,
  `layout-content-fit.trx`, native/component reports, translation and Core/picker
  preservation reports, and the owner's preceding height screenshot.
- **Payload:** **812 files, 629,862,770 bytes**. The unchanged native Shell DLL
  retains SHA-256 `3f3158dfe4778f229569b70663554884b16ec83fdcf1310e7cbd33710bfa8ce3`.
  Packaged resources still contain 2,009 entries in each of the five languages;
  source catalogs are unchanged.

The agent has **not installed or published this candidate or controlled the
desktop**. Real-screen fitting, OS DPI/monitor transitions, perceived flicker
and the previous installer/Explorer lifecycle acceptance remain to confirm.

### Owner validation and commit request

The owner subsequently validates this latest Cut / Find Loop candidate in the
project conversation and requests that the work be committed. This records
acceptance of the delivered UI correction; it does not claim that every OS DPI,
monitor or installer/Explorer lifecycle scenario above was exercised. The
candidate remains a private build from the retained dirty-source snapshot;
committing the sources does not rebuild, install or publish that artifact.

## Clean committed rebuild — 10 November 2026, export hint removed

The owner requested an installer refresh after commit `5b60911` removed the
redundant clipped export hint at the bottom of Cut / Find Loop. This candidate
includes all previously committed menu and loop UI work plus that removal.

- **Source:** clean tree at `5b60911264802966900489dcbdde28b307e17836` on
  `codex/integrate-find-loop-1.23`. The exact published application reports
  `1.23.0+5b60911264802966900489dcbdde28b307e17836`; file version is `1.23.0.0`.
  Qualification/checksum updates follow the clean build and do not alter its
  application sources or informational version.
- **Canonical workflow:** `build_installer.ps1`, without `-AllowDirty` or
  `-RunInstaller`, exits **0**. **1,458 passed, 0 failed, 18 conditional skips,
  1,476 total**, in **9 minutes 54 seconds**; **23 native checks pass**.
  Publish/runtime/tool/notice checks pass and Inno compiles in **82.000 seconds**.
- **Installer:** **179,950,669 bytes**, SHA-256
  `1571BBAD6A5BFBDF7FD5A2568A729382E952F23861832C69751544B20F63045C`.
  The normal installer/checksum now identify this build. The preceding dated
  content-fit installer keeps its original `054F8A9F…76B7C9` checksum.
- **Archive:** `builds/release-1.23-private/20261110T200728Z-export-hint-removed/`.
  Contains installer/checksum, `candidate.json`, `source-state.json`, relevant
  source hashes, `canonical-build.log`, payload manifest, native report/component
  manifest and packaged translation report. Source state was checked clean
  before the build and again before archiving.
- **Payload:** **812 files, 629,862,770 bytes**. Shell DLL **2,622,464 bytes**,
  SHA-256 `ae4d1d39cc4c3327e9d4e328a8a1fb24b3f04767b3bb53c1500c025cff0156f4`.
  Five languages retain **2,009 entries each**; the unused hint resources are
  retained, while the UI no longer creates the hint label.

The agent has **not installed or published this candidate or controlled the
desktop**. Previous Windows DPI/monitor, perceived flicker and installer/Explorer
lifecycle acceptance limits remain recorded above.
