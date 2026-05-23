# Changelog

## Unreleased

## 1.0.0

- Remove Background now uses the shared `ProgressForm`.
- Added a shared donation banner to `ProgressForm`.
- Updated shared-option batch UX for `Convert Video`, `Convert Audio`, `Convert Image`, and `Extract Audio`: the picker now opens first and the progress window appears only after explicit confirmation.
- Standardized action-window chrome so the title bar and taskbar icon now use the global FrameShift icon across action forms, while the internal function banner stays separate.
- Restructured the root Markdown documentation into a cleaner `docs/` hierarchy.
- Added a central documentation index in `docs/README.md`.
- Added and refreshed the active code file index in `docs/CODE_FILE_INDEX.md`.
- Added the video action `Extract Frames` with batch queue support, adjacent unique output folders, PNG export, cleanup on cancel/failure, and installer/context-menu integration.
- Added the audio action `Reverse Audio` with same-format output, FFprobe validation, clean cancellation, and adjacent unique naming.
- Added the audio action `Cut Audio` with single-file CLI/UI routing, same-format output, live time validation, clean cancellation, and installer/context-menu integration.
- Added the video action `Create GIF` with visual range selection, frame preview, looped GIF preview, GIF presets, adjacent unique naming, and installer/context-menu integration.
- Added the image action `Convert to Icon` with multi-size ICO generation, fit/fill and background options, dynamic previews, adjacent unique naming, and installer/context-menu integration.
- Fixed `Image to PDF` support alignment for WebP between launcher, UI flow and core validation.
- Fixed the installer component mapping so `Image to PDF` receives `ffmpeg.exe` when installed on its own.
- Merged Media Info probing into `FfprobeRunner` via a dedicated `TryProbeMediaInfoAsync(...)` path and removed the old direct-probe runner.
- Centralized the strictly equivalent `DeleteIfExists` cleanups on `ConversionActionHelper.DeleteIfExists(...)`.
- Made `build_installer.ps1` the canonical build script and turned `build_all.ps1` into a thin delegating wrapper.
- Corrected the documentation link that referenced `docs/CODE_FILE_INDEX.md`.

## Notes

- This changelog tracks notable project-level changes.
- Historical changes from legacy reference projects are intentionally excluded.
