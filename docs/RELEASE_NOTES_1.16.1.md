# FrameShift 1.16.1

## GitHub release title

`FrameShift 1.16.1 — Safer AI model uninstall`

## GitHub release body

```markdown
## Highlights

- **Safer AI model uninstall:** FrameShift never removes the selected AI-models root. Uninstall now validates custom locations and only removes explicitly known FrameShift model files from directories that FrameShift created and marked. The directory itself is removed only when it is empty; shared folders, unmarked legacy folders, and unknown files remain untouched.
- **New drop-driven main window:** Add or drag files into a simple queue, then run the relevant actions over the matching selection without Explorer's multi-file context-menu limit.
- **AI performance improvements:** Faster host-side processing for Audio Separation, lower memory use in Upscale Video, and a smaller Create Subtitle File worker without changing output behavior.

## Safety notes

Custom AI-model folders are still supported. Drive roots, profile roots, Windows, Program Files, the application folder, and their parents are rejected. During uninstall, an unknown file or subfolder prevents removal of that model folder.

## Downloads

- `FrameShift_1.16.1_Setup.exe` — Windows 10/11 x64 installer
- SHA-256: `CD7396EBE37DA79340A79332FF55EDB1ADC769001D3BAD451464644981B4AA49`

FrameShift remains self-contained and offline-first. Optional AI models download only when needed.
```

## Publication checklist

- Attach `installer\FrameShift_1.16.1_Setup.exe` to the GitHub release.
- Use tag `v1.16.1` and release title above.
- Paste the release body above without the surrounding fence.
- Publish only after the manual shared-model-folder uninstall check in `RELEASE_CHECKLIST.md` passes.
