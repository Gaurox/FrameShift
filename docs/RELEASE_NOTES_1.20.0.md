# FrameShift 1.20.0

Candidate for manual installed-build testing. Publication requires the owner's final approval and the qualification results below.

## GitHub release title

`FrameShift 1.20.0 — UI/DPI improvements`

## GitHub release body

```markdown
## Highlights

- **A consistent Windows interface:** refreshed action dialogs and editors with shared headers, margins, spacing and action buttons. Compact dialogs fit their content, while larger editors keep their commands accessible and allow options to scroll when needed.
- **Improved display scaling:** PerMonitorV2 handling across the application, adaptive editor panels, and monitor-aware window bounds. Media selection, crop and brush coordinates remain in source units.
- **Practical layouts:** clearer compression and interpolation options, aligned resize and PDF fields, a compact subtitle picker, and Cut Video selection controls beneath the preview.
- **Readable progress and errors:** a sober file list and a multiline message area below the queue, with full details available to read and copy.
- **Keyboard and stability:** improved focus, keyboard navigation, theme states and metadata access; corrected icon ownership and editor closure during preview loading.

## Download

- `FrameShift_1.20.0_Setup.exe` — Windows 10/11 x64 installer
- SHA-256: pending candidate build

FrameShift remains self-contained and offline-first. Optional AI models download only when needed.
```

## Publication checklist

- Complete [installed qualification](UI_PHASE_G_MANUAL_TESTS.md) and record the accepted scope in [the candidate report](RELEASE_QUALIFICATION_1.20.0.md).
- Obtain explicit final publication approval from the owner.
- Use tag `v1.20.0` on the source commit recorded for the tested installer; attach that exact installer and its SHA-256.
- Confirm GitHub publication and the downloaded asset's checksum before changing README's latest published version to 1.20.0.
- Check the external website's download link separately; this repository's `index.html` redirects to `gaurox.dev/frameshift/` and does not contain that site's release content.
