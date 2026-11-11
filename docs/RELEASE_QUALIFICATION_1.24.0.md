# FrameShift 1.24.0 — Accepted local installer

Prepared on 11 November 2026 at the owner's request. This preparation authorizes
building a local installer, without installing it or publishing a release.

Owner acceptance recorded on 11 November 2026: **"tout est validé committe tout !"**
after delivery of the language-upgrade fix and Balance volume icon. The current
installer below is accepted for the tested scope. Publication has not been requested.

## Latest language-upgrade fix and icon rebuild — 11 November 2026

This is the owner-accepted local installer. Each installation writes a fresh
runtime identifier and its language together in `{app}/language-installation.txt`.
At the next launch, the application applies this choice once to the launching
user's UI preferences, including an older saved English preference. Later personal
language changes remain effective until the next installation. Reinstalling the
same version also creates a new identifier. The existing `language.txt` remains
for compatibility; uninstall removes both installer-owned language files.

The application saves the acknowledgement in the launching user's account rather
than writing UI preferences from the elevated installer. Existing theme, OCR and
sort settings are preserved. Missing/invalid/unreadable markers keep the previous
fallback behavior. A failed preference save uses the chosen language in memory
and retries on the next launch. No new runtime dependency was added.

- **162 targeted tests passed**, including 24 new startup/upgrade cases: all eight
  language choices, French Settings title/selection after saved English, later
  user choices, reinstall of the same version, independent user settings, legacy
  fallback, malformed/locked markers and retry after a read-only settings file.
- An Inno syntax-only check passed before the canonical build. A preliminary
  formatting issue (a line starting with `[` parsed as a section) was corrected
  before this successful check and the complete build.
- Canonical `build_installer.ps1 -AllowDirty` completed with exit code 0 using
  SDK **8.0.427**, self-contained runtime **8.0.33**, and Inno Setup **6.7.1**.
- Complete Release suite: **1,561 passed, 0 failed, 18 conditional skips, 1,579
  total**, in **13 minutes 27 seconds**. No filter was applied.
- Translation maintenance: **88 passed**, no failures/skips. Native menu probe:
  **29 passed**, no failures. Exact app/worker resource audit: **16 language
  component checks**, zero errors.
- Dedicated Balance volume icon connected to the audio/video Explorer menus and
  action dialog. ICO: 11 transparent 32-bit frames at **16, 20, 24, 28, 32, 40,
  48, 64, 96, 128 and 256 px**, individually verified with the Windows loader.
  The 32px drawing matches the approved preview exactly. Published ICO, PNG and
  editable SVG hashes match the source assets.
- Current installer: `installer/FrameShift_1.24.0_Setup.exe`, **180,142,126 bytes**.
  SHA-256:
  `731AF09C5964317F37E71F2F73C4A17BADE20751C63D9DF7BCFFA629E545E04A`.
  The adjacent `.exe.sha256` file matches. App version remains **1.24.0.0**;
  installer version remains **1.24.0**.
- Retained candidate: `builds/release-1.24-private/20261111T180752Z/`. Payload:
  **821 files**. Base revision `a585e4e` plus intentional uncommitted work is
  identified by archived source hashes, tracked patch and new files. Every source
  manifest entry was rechecked unchanged after building, before updating the
  checksum and this report. The previous Setup is also saved in
  `scratch/language-upgrade-1.24/previous-FrameShift_1.24.0_Setup.exe`.

### Owner's upgrade test — accepted

The owner reported complete validation after receiving this candidate and the
following test protocol, then requested that all changes be committed.

1. Close FrameShift, install 1.23 in English, and ensure English is saved in its
   language settings. Close all FrameShift windows again.
2. Install this rebuilt 1.24.0 and select a different language, such as French.
3. Launch FrameShift and an action from Explorer: verify translated windows and
   menu captions, plus the Balance volume icon. Restart and verify persistence.
4. Optionally change language in Settings and restart; confirm the personal
   choice persists. Reinstalling 1.24.0 with another language should apply the
   installation choice again on the next launch.

The installer was **not executed by the agent and has not been published**.
Automated tests use isolated settings and installation-marker files; Windows
acceptance is reported by the owner. The owner's overall acceptance is recorded
without adding unobserved per-scenario test results. Earlier qualification limits
and conditional skips remain documented. This evidence was written after the build.

## Previous multilingual rebuild — retained history

This earlier test candidate and the five-language build below are retained
as history. French, Japanese and Indonesian became selectable in the app and
installer, with matching subtitle-worker resources and Explorer captions.
Each new catalog has 2,167 filled records, with strict technical checks passing.
Standard Inno translations are vendored with upstream credits and pinned hashes.

- Canonical `build_installer.ps1 -AllowDirty` completed with exit code 0, using
  SDK **8.0.427**, self-contained runtime **8.0.33**, and Inno Setup **6.7.1**.
- Complete Release suite: **1,537 passed, 0 failed, 18 conditional skips, 1,555
  total**, in **11 minutes 47 seconds**. No filter was applied.
- Translation maintenance suite: **88 passed**, no failures or skips.
- Isolated native menu probe: **29 checks passed**, including all eight languages.
- Exact packaged app/worker resources: **16 component/language checks**, zero
  errors, without accepting English fallback. All eight active CSV exports pass
  strict checks; the three completed contribution imports are no-ops.
- App file/assembly version **1.24.0.0**; installer version **1.24.0**.
  App informational version:
  `1.24.0+a585e4ec3a1ba2d5ffbae4aa9073514e81c5646c`.
  The working tree intentionally includes the new translation/integration work;
  archived input hashes, tracked patch and new source files identify that state.
- Current installer: `installer/FrameShift_1.24.0_Setup.exe`, **180,126,423 bytes**.
  SHA-256:
  `8B33A0E918E8AFBDD6B2E314241D805A3F11986ACD03F16C221E9FEED04E6D98`.
  The adjacent `.exe.sha256` file matches.
- Retained candidate: `builds/release-1.24-private/20261111T173002Z/`, including
  identical Setup, source evidence, canonical log, package audit, native reports,
  per-file payload manifest and `candidate.json`. Payload: **818 files**.
- Every source-manifest entry was rechecked unchanged after the build, before
  updating the checksum and this qualification record. The old installer is
  preserved in the original candidate archive and in
  `scratch/release-1.24-languages/previous-FrameShift_1.24.0_Setup.exe`.

The first rebuild attempt was stopped before packaging after revealing test
assumptions: English assertions inherited French UI culture once French resources
became available, and the release-script fixture predated the new translation
gates. Test-only UI culture is now explicitly English, preserving numeric culture
and explicit localization cases. The fixture now covers both test suites and
package-language verification, including refusal before Inno on failure.
**203 targeted tests passed** before the successful complete canonical run.
No production culture behavior or mandatory release gate was weakened.

The installer was **not executed or published**. Independent native proofreading,
real Excel/LibreOffice use, actual Windows UI/DPI/keyboard testing and installed
upgrade/component/Explorer/uninstall acceptance remain open. This record was
updated after building and does not change the payload.

## Scope and build contract

- Balance volume, with Light, Moderate (default), and Strong / Night mode.
- Eight app/worker/Explorer/installer languages, including French, Japanese and
  Indonesian; offline translation contribution and maintenance tooling.
- One FFmpeg processing pass; video stream copy; all audio tracks processed.
- Application/file/assembly version 1.24.0 / 1.24.0.0 / 1.24.0.0.
- Shell component 1.24.0; subtitle worker retains its independent version.
- Canonical workflow: `build_installer.ps1 -AllowDirty`, with the installed Inno
  Setup compiler explicitly located. The working tree intentionally includes the
  new feature and release preparation; no source commit is implied.
- Pinned SDK 8.0.427, self-contained .NET/Windows Desktop 8.0.33, existing locked
  dependencies, and SHA-256-verified bundled FFmpeg/FFprobe.
- No runtime, dependency or model download upgrade belongs to this release.

## Validation

Feature development checks: 201 targeted Debug tests passed, including measured
dynamic-range reduction, video packet hashes, container compatibility, multiple
tracks/subtitles/chapters/metadata, stereo/5.1 channels, accented paths with spaces,
unique outputs, failures, global/current-item cancellation, picker defaults and
CLI/localization/menu integration. Those development checks used SDK 8.0.420;
the canonical release below uses the pinned 8.0.427 SDK.

Release preparation additionally checks AVI 5.1: use AC-3 audio for AVI so that
multichannel tracks are not sent to a stereo-only MP3 encoder. All 19 core
Balance volume tests pass in Release with SDK 8.0.427 after this correction.
The first complete-suite attempt was intentionally stopped before packaging to
include this correction; the canonical run below uses the corrected sources.

## Initial five-language canonical build — retained history

- Canonical command completed with exit code 0, using SDK **8.0.427** and the
  existing Inno Setup **6.7.1** compiler. All locked restores succeeded.
- Complete Release suite: **1,478 passed, 0 failed, 18 conditional skips, 1,496
  total**, in 10 minutes 37 seconds. Skips retain the existing optional AI/OCR
  models and opt-in Find Loop benchmark conditions; no test filter was applied.
- Native x64 Shell component: **23 checks passed, 0 failed**, including all
  supported languages, media families and Balance volume menu/launch routing.
- The canonical checks verified self-contained .NET/Windows Desktop **8.0.33**,
  PKCS **8.0.2**, pinned FFmpeg/FFprobe hashes, worker and distribution notices.
- Published application file/assembly version: **1.24.0.0**. Informational version:
  `1.24.0+9e7b676949cedee2178c9d5a89162a17da0c6982`. This identifies the base
  revision; the build intentionally includes uncommitted changes. The archived
  source manifest, tracked patch and new source files identify the actual inputs.
- Installer: `installer/FrameShift_1.24.0_Setup.exe`, **179,980,742 bytes**.
  Inno Setup file/product version: **1.24.0**.
- SHA-256:
  `2CA499A5C56BF3FCA8B70CA8B9FEE383EE9ECE9AB1D259A62E4B6FB86B81A96B`.
  The adjacent `.exe.sha256` file records the same checksum.
- Retained candidate: `builds/release-1.24-private/20261111T135959Z/`, with an
  identical installer, source snapshot, canonical log, per-file payload manifest,
  native component/probe reports and `candidate.json`.
- The published payload contains **812 files**. No PDB/DBG files, private licence
  files, development FFmpeg ZIP or provenance JSON are included. Source hashes
  were checked against the pre-build snapshot before archiving.

The installer was **not executed**, and this candidate was **not published**.
This completed evidence section was added after building and does not change the
installer payload.

## Remaining manual acceptance

- Listening on representative films for natural sound at all three intensities.
- The actual new picker at Windows DPI/text sizes, themes, keyboard navigation
  and multiple monitors where available.
- Fresh install/upgrade/component selection/uninstall and actual Explorer menu
  interaction, including Balance volume for video and audio.

Automated checks do not mark these manual scenarios passed. The installer is a
local test candidate until owner acceptance; the published README version remains
1.23.0 until a separately authorized public release.
