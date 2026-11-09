# FrameShift 1.23.0 — Private development candidate

## Status

Prepared on 9 November 2026 at the owner's request. **Not published, not final,
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
- Existing installer languages, components, model handling and Explorer registration
  remain in place. Find Loop is accessed through Cut Video.

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
