# Release Checklist

Use this checklist before pushing a release-oriented update or publishing a new installer.

## Scope Check

- Confirm the change belongs to the active FrameShift project tree.
- Do not commit local-only tooling state, temporary outputs, or historical reference folders.
- Keep `index.html` and `style.css` in sync if the GitHub project page is updated.

## Pre-Push Checks

- Run `dotnet test tests/FrameShift.Tests/FrameShift.Tests.csproj -c Release`
- Re-read `README.md` if the user-facing surface changed.
- Re-check version consistency if the release number changed:
  - `src/FrameShift/FrameShift.csproj`
  - `installer/FrameShift.iss`
  - `docs/CHANGELOG.md`

## Release Build

- Run `.\build_installer.ps1`
- Confirm the expected installer is produced in `installer/`
- Confirm the publish payload and installer do not include unnecessary debug files

## Git Check

- Review `git status`
- Review staged files before committing
- Use a clear commit message
- Push only when the repository state is intentional and clean

## Publishing Notes

- `LICENSE` must remain the project license source of truth
- Third-party notices must stay up to date in `THIRD_PARTY_NOTICES.md`
- Do not publish old `references/` content as active project material
