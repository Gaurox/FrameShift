# FrameShift 1.17.0

## GitHub release title

`FrameShift 1.17.0 — System / Light / Dark appearance`

## GitHub release body

```markdown
## Highlights

- **System / Light / Dark appearance:** Open Settings from the main window to follow the Windows apps theme, force the light palette, or force the dark palette. The choice is stored locally and applies immediately.
- **Consistent WinForms appearance:** The main window, progress window, action dialogs, pickers, AI screens, editors, menus, grids, and standard title bars now use the same central palette. Windows dark title bars are used when supported by the installed Windows version.
- **Media rendering remains functional:** Video and image previews, PDF pages, crop overlays, transparency indicators, and subtitle colors chosen by the user keep their meaningful colors.
- **Installer startup fix:** The AI-model folder safety check no longer uses an unsupported Inno Setup constant, and it defers validation of the chosen installation directory until that directory is initialized.

## Notes

- `System` is the default. If the UI settings file is absent, unreadable, or contains an unknown value, FrameShift safely falls back to `System`.
- The preference is stored separately from AI settings in `%LOCALAPPDATA%\FrameShift\config\ui-settings.json`.
- Some native Windows controls and system dialogs may retain the Windows theme when it differs from the selected FrameShift palette; readable native rendering is intentionally preserved.

## Downloads

- `FrameShift_1.17.0_Setup.exe` — Windows 10/11 x64 installer
- SHA-256: `40D5966BDCB8846871BDFFABADAC60470A45AD91AE21741B49D23F9E26D655F2`

FrameShift remains self-contained and offline-first. Optional AI models download only when needed.
```

## Publication checklist

- Attach `installer\FrameShift_1.17.0_Setup.exe` to the GitHub release.
- Use tag `v1.17.0` and the release title above.
- Paste the body without the surrounding fence and keep the SHA-256 value exactly as shown above.
- Publish only after the manual DPI, context-menu, long-running progress, and native-dialog checks listed in `DARK_LIGHT_THEME_IMPLEMENTATION.md` pass on the supported Windows versions.
