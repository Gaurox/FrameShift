# Release Checklist

Use this checklist before pushing a release-oriented update or publishing a new installer.

## Version Numbering

FrameShift uses `1.<feature release>.<patch>` starting with `1.14.0`:

- increment the middle number for a feature release, resetting the final number to `0`;
- increment only the final number for small fixes and hotfixes (`1.14.1`, `1.14.2`, etc.);
- keep `<Version>` in `src/FrameShift/FrameShift.csproj` and the changelog synchronized; `build_installer.ps1` reads that version and injects it into `installer/FrameShift.iss` as `MyAppVersion`.

## Scope Check

- Confirm the change belongs to the active FrameShift project tree.
- Do not commit local-only tooling state, temporary outputs, or historical reference folders.
- Keep `index.html` and `style.css` in sync if the GitHub project page is updated.

## Pre-Push Checks

- Re-read `README.md` if the user-facing surface changed.
- Ensure `docs/CHANGELOG.md` has a `## {version}` section matching the version in `src/FrameShift/FrameShift.csproj`.
- Re-check version consistency across:
  - `src/FrameShift/FrameShift.csproj`
  - installer compilation argument `/DMyAppVersion=<csproj Version>`
  - generated `installer/FrameShift_<version>_Setup.exe`
  - `docs/CHANGELOG.md`

## Release Build

The following are **automated** by `build_installer.ps1` and do not require manual action:

- **Git cleanliness check** — warns (with prompt to continue) if the working tree has uncommitted changes.
- **CHANGELOG consistency check** — aborts if `docs/CHANGELOG.md` has no `## {version}` section for the current csproj version.
- **Test gate** — runs `dotnet test` in Release mode and aborts on any failure.

Manual steps after the script completes:

- Run `.\build_installer.ps1`
- Confirm the expected installer is produced in `installer/`
- Confirm the publish payload and installer do not include unnecessary debug files
- For AI-model uninstall safety: install with a shared custom models folder containing a foreign file and a FrameShift-marked model folder containing an extra foreign file. Confirm uninstall removes only explicitly known FrameShift artifacts, preserves the shared root and foreign content, and leaves the marked folder in place when it is not empty.

### Inno Setup compiler (ISCC) location

`build_installer.ps1` locates the Inno Setup compiler automatically. Its search order is:

1. `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`  ← **install location on the dev machine** (per-user install; resolves to `C:\Users\Adrien\AppData\Local\Programs\Inno Setup 6\ISCC.exe`)
2. `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`
3. `C:\Program Files\Inno Setup 6\ISCC.exe`
4. The Inno Setup 5 equivalents of (2) and (3)
5. `ISCC.exe` on `PATH`

If none are found the script prints the `.iss` path so it can be compiled manually:

```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" "/DMyAppVersion=<version>" installer\FrameShift.iss
```

## Git Check

- Review staged files before committing
- Use a clear commit message
- Push only when the repository state is intentional and clean

## Publishing Notes

- `LICENSE` must remain the project license source of truth
- Third-party notices must stay up to date in `THIRD_PARTY_NOTICES.md`
- Do not publish old `references/` content as active project material
