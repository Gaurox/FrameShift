# FrameShift 1.20.1

Published on 3 October 2026 after owner authorization: [GitHub release 1.20.1](https://github.com/Gaurox/FrameShift/releases/tag/1.20.1). This maintenance release addresses the four P1 findings from the [security audit](SECURITY_AUDIT_2026-10-03.md). Build evidence, verified installer download and remaining qualification are recorded in [the release report](RELEASE_QUALIFICATION_1.20.1.md).

## GitHub release title

`FrameShift 1.20.1`

## GitHub release body

```markdown
### Security and reliability fixes

- Hardened the installer by removing registry-based external uninstaller execution.
- Updated ImageSharp and the bundled .NET runtime with security fixes.
- Protected media outputs against concurrent filename collisions and accidental deletion during cancellation or failure.
- Improved output handling for long Windows paths, subtitle diagnostics and multi-file audio exports.

Download `FrameShift_1.20.1_Setup.exe` below — Windows 10/11 x64, no separate .NET installation required.
```

## Publication record

- Annotated tag `1.20.1` points to the exact build source commit `0b31a16329eba739cd47e725b287362684389c47`; code and release documents are pushed to `main`.
- The owner authorized publication after receiving the build results and remaining installed-qualification limit: “ok parfait push et publie”. Unexecuted scenarios from the [P1 remediation plan](SECURITY_P1_REMEDIATION_PLAN_2026-10-03.md) remain unverified.
- Published the tested `FrameShift_1.20.1_Setup.exe` and its SHA-256 checksum file. No licence key, build secret, test asset or temporary payload is attached.
- Anonymous public download verified: **171,550,754 bytes**, SHA-256 **`E606E5A30B4F7AC5E9ECD9685C261FE3964641321F301933296BE64C764B8991`**. README's latest published version is updated after that verification.
