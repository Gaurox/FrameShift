# FrameShift 1.24.0 — Local installer candidate

Prepared on 11 November 2026 at the owner's request. This preparation authorizes
building a local installer, without installing it or publishing a release.

## Scope and build contract

- Balance volume, with Light, Moderate (default), and Strong / Night mode.
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

## Completed canonical build — 11 November 2026

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
