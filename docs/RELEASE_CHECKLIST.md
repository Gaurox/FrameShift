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

The only official release command is:

```powershell
.\build_installer.ps1
```

`build_installer.ps1` is the canonical, blocking workflow. In order, it validates the required files, version/changelog and clean Git state; verifies the pinned SHA-256 values of the bundled FFmpeg and FFprobe binaries; restores all projects with `--locked-mode`; runs `dotnet test` in Release with `--no-restore`; clears only `publish\FrameShift-win-x64`; publishes a self-contained `win-x64` payload with `--no-restore`; verifies the application, the FFmpeg/FFprobe payload hashes and subtitle-worker payload; then compiles Inno Setup from that exact publish directory. Any required failure, including an unavailable or failing Inno compiler, returns a non-zero exit code. Use `-AllowDirty` only for an intentional local build from a dirty tree.

`build_all.ps1`, `build_publish.ps1`, and `build_publish.bat` are compatibility wrappers to the same workflow. They do not offer optional test, publish-only, or installer-skipping modes.

Manual steps after the script completes:

- Confirm the expected installer is produced in `installer/`
- Confirm the publish payload and installer do not include unnecessary debug files
- For installer runtime coherence (H-10), verify a fresh complete install and a custom `core`-only install both contain the current `ffmpeg.exe`, `ffprobe.exe`, and `Workers\CreateSubtitlesWorker` payload. The `core`-only install must create no action-specific Explorer menus.
- Verify custom installs selecting only `remove_noise_video`, only Media Info, Create Subtitles audio, and Create Subtitles video: each selected Explorer menu is present, non-selected menus are absent, and the shared runtimes remain present.
- Verify upgrades from a previous minimal install and with formerly selected components now deselected: FFmpeg, FFprobe, and the subtitles worker are replaced by the current payload while `InstallSelectedMenus` removes menus that are no longer selected. Finish with an uninstall and confirm the shared runtimes and Explorer menus are removed.
- For AI-model uninstall safety: install with a shared custom models folder containing a foreign file and a FrameShift-marked model folder containing an extra foreign file. Confirm uninstall removes only explicitly known FrameShift artifacts, preserves the shared root and foreign content, and leaves the marked folder in place when it is not empty.

### Inno Setup compiler (ISCC) location

`build_installer.ps1` locates the Inno Setup compiler automatically. Its search order is:

1. `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`  ← **install location on the dev machine** (per-user install; resolves to `C:\Users\Adrien\AppData\Local\Programs\Inno Setup 6\ISCC.exe`)
2. `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`
3. `C:\Program Files\Inno Setup 6\ISCC.exe`
4. The Inno Setup 5 equivalents of (2) and (3)
5. `ISCC.exe` on `PATH`

If none are found, the release command fails. Install Inno Setup and run the same canonical command again; do not compile the ISS as an alternate release path.

## Git Check

- Review staged files before committing
- Use a clear commit message
- Push only when the repository state is intentional and clean

## Publishing Notes

- `LICENSE` must remain the project license source of truth
- Third-party notices must stay up to date in `THIRD_PARTY_NOTICES.md`
- Do not publish old `references/` content as active project material
