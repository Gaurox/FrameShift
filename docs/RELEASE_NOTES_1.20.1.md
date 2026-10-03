# FrameShift 1.20.1

Prepared for publication; not yet published. This maintenance release addresses the four P1 findings from the [security audit](SECURITY_AUDIT_2026-10-03.md). Build evidence, installer identity and remaining qualification are recorded in [the candidate report](RELEASE_QUALIFICATION_1.20.1.md).

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

## Publication checklist

- Use the exact installer and source commit identified in the candidate report; prepare the annotated tag `1.20.1` on that source commit.
- Complete the remaining installed qualification from the [P1 remediation plan](SECURITY_P1_REMEDIATION_PLAN_2026-10-03.md), or record the owner's explicit acceptance of its remaining limits. Unexecuted scenarios remain unverified.
- Obtain the owner's final authorization before pushing and publishing. Local preparation does not publish the GitHub release.
- Attach the tested `FrameShift_1.20.1_Setup.exe` and its SHA-256 checksum file. Never attach `sixlabors.lic`, build secrets, test assets or temporary payloads.
- After publication, verify the downloaded asset's checksum and only then update README's latest published version and the publication status in the changelog and these notes.
