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
