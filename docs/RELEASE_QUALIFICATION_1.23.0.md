# FrameShift 1.23.0 — Private development candidate

## Status

First prepared on 9 November 2026; the 10 November candidates add complete video
menus, then adjacent classic/AI menus for all supported file families.
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
