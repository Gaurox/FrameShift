# Changelog

## Unreleased

## 1.0.10

- Added multi-file support for `Compress Video`, `Compress Audio`, and `Compress Image`: when several files are selected in Windows Explorer, the parallel instances are coalesced (mutex + named pipe + debounce) and a choice popup appears — **Use same settings for all files** opens one compression window and applies the result to the whole queue; **Configure each file separately** opens the compression window once per file in sequence, each file carrying its own settings; **Cancel** exits cleanly. Single-file behavior and headless CLI mode (`--profile`) are unchanged.
- Compress form headers now display the source filename (without path) in the subtitle line, making the current file immediately identifiable when configuring files one by one.
- Translated the `Media Info` action output to English for all three media kinds (video, audio, image): all section headers, field labels, units (`GiB`/`MiB`/`KiB`/`bytes`, `fps`, `channel(s)`), and compression values, plus the window labels (`File:`, `Information`, `Copy`, `Close`). No French text remains in the action.
- Added **LaMa 2025 (Fast)** as a second inpainting model in `Remove Object`: 93 MB, Apache-2.0, opencv/inpainting_lama Jan 2025 — identical API to LaMa FP32, smaller download, selectable from the ComboBox. Renamed existing model to **LaMa FP32 (Quality)**.
- Added `ForceCpu` per-model flag to `ObjectRemovalModelDefinition` to allow future models to opt into DirectML without engine changes.
- Added configurable AI models folder: user preferences are persisted in `%LOCALAPPDATA%\FrameShift\config\settings.json`; if `ModelsDirectory` is absent, empty, or not writable, the application automatically falls back to the default path (`%LOCALAPPDATA%\FrameShift\AI\Models`).
- Added "AI models folder" section to `MainForm` with Browse, Reset to default, and Open folder actions; the displayed path updates immediately on change.
- Added AI models folder choice to the Inno Setup installer: a dedicated page lets users pick a custom directory at install time; the choice is saved to `settings.json` and respected on uninstall.
- Fixed `Remove Background`: DirectML failures that occur during inference (`session.Run`) now trigger an automatic CPU retry instead of a hard crash; the progress bar shows a clean status message without internal ONNX Runtime build paths.
- Fixed AI error messages: `OnnxRuntimeException` details (including internal build paths such as `E:\_work\...`) are now written only to the diagnostic log; the UI displays a short, readable message.
- Centralized ONNX session creation (DirectML → CPU fallback) for `Remove Background`, `Remove Object`, and `RIFE Interpolate Video` into `OnnxProviderHelper`.

## 1.0.9

- Added `Remove Object (Image)` as a local AI inpainting action: visual editor with zoom/pan canvas, red-overlay brush mask, eraser, adjustable brush size, and fit-to-window; model catalogue with ComboBox selector (extensible for future models); LaMa FP32 as the default model (CPU-only — FFC/FFT operators unsupported by DirectML); on-demand model download with SHA-256 integrity check; output as `_cleaned.png` adjacent to the source; Explorer context-menu integration under `FrameShift AI → Remove object` for common image formats.

## 1.0.8

- Added `Remove Noise (Video)` as a local AI action with dedicated picker UI and adjacent output workflow.
- Expanded the shared AI model download flow so local AI actions use a consistent preflight/downloader UX.
- Standardized AI window and Explorer icons around the active assets in `Assets\Icons\ai`.
- Fixed AI engine lifecycle: `BackgroundRemovalEngine` and `RemoveNoiseEngine` are now created once per batch and reused across all files, then disposed at the end of the batch, eliminating the per-file ONNX session load cost.
- Fixed batch routing: replaced four redundant per-action wrapper methods with a single dispatch entry using a shared action-id registry, removing duplicate CLI paths for AI batch actions.
- Added unit tests for `FfmpegRunner.TryParseFfmpegTime`, `FfmpegProgressState` phase transitions, `ProgramCli.TryParseArguments`, and `ConversionActionHelper` error classification.

## 1.0.7

- Security and compliance fixes: completed third-party license notices, removed developer paths from scripts, added log rotation, added NuGet package lock files, added SECURITY.md.

## 1.0.6

- Added `Interpolate Video (RIFE)` as a local AI action with integrated model support, Explorer integration, and a dedicated picker UI.
- Added the shared RIFE model preflight/download flow so the model is validated before launch.
- Updated product and installer metadata to version `1.0.6`.

## 1.0.5

- Fixed `Audio Separation` DML fallback: on DirectML initialization failure the engine now falls back to the V1 CPU model (`htdemucs.onnx`) instead of running the split model on CPU, which had no benefit over the full in-graph model on that execution provider.
- Fixed `Audio Separation` ONNX session lifecycle: the engine is now created once per queue batch and reused across all files, eliminating the per-file load cost for large batches.
- Fixed `Audio Separation` picker radio buttons not rendering their selection circles on certain system themes; the engine radio buttons now use `BackColor = Surface` with visual-style rendering enabled so the indicator is always visible.
- Fixed layout overlap in the `Audio Separation` picker engine row that caused the GPU radio button circle to be painted over by the adjacent Automatic button's background.
- Added SHA256 integrity verification to `Remove Background` model download, consistent with the existing pattern in `Audio Separation`.
- Fixed `AppLogger` concurrent write safety: file append is now serialized across threads, preventing silent log line loss during parallel batch processing.
- Fixed `FfmpegRunner` cancellation: orphan processes are now force-killed on wait timeout, and the exit code is no longer accessed on a process that may still be running.
- Fixed installer FFmpeg/FFprobe source path to use the publish output folder instead of the source tree, ensuring the packaged binaries always match the built payload.
- Fixed installer context menu registry writes to target only HKLM on admin installs, eliminating duplicate Explorer entries.
- Fixed installer upgrade flow: the existing uninstaller now completes fully before the setup exits, preventing registry and file race conditions.
- Added `SHChangeNotify` call after installer registry writes so Explorer reflects context menu changes immediately without requiring a logoff.
- Hardened `build_installer.ps1` with a git cleanliness check, a changelog consistency gate, and an automated test run before packaging.
- Replaced the `MainForm` placeholder with a structured landing screen showing action categories and usage hint.
- Fixed `ProgressForm` GDI font leak: per-instance `Font` allocations replaced with shared static instances.

## 1.0.4

- Upgraded `Crop Image` with automatic border-aware crop suggestions, mouse-wheel zoom, fit-to-view, and drag navigation for faster visual adjustments.
- Brought the same interactive improvements to `Crop Video`, including frame-based auto crop that applies the selected rectangle to the full video.
- Tightened video crop normalization so even-dimension adjustments prefer trimming inward when needed, helping remove residual border pixels more cleanly.
- Refreshed the public README, site copy, and updated crop screenshots for the improved crop workflow.

## 1.0.3

- Added `Remove Noise (Audio)` picker UI with strength selection (Light / Normal / Strong / Maximum), optional stereo processing (two independent L+R passes), and 8-second audio preview.
- Added stereo processing pipeline to `Remove Noise` audio action: FFmpeg channel extraction via `pan` filter, two sequential DeepFilterNet3 passes, and FFmpeg stereo merge in the source container format.
- Updated `Remove Noise` audio action to respect the `noise-strength` option (previously always applied maximum denoising).

## 1.0.2

- Added `Remove Noise` as a local AI action for offline speech denoising via DeepFilterNet3, with adjacent unique output naming and Explorer context-menu integration.
- Simplified the `Remove Noise` experience to a direct action without profiles or extra UI settings.
- Updated the installer and release metadata to version `1.0.2`.

## 1.0.1

- Added `Audio Separation` as a local AI action with CPU and DirectML GPU paths, multi-stem export, and adjacent unique output naming.
- Added the `Audio Separation` picker UI and Explorer context-menu integration with a dedicated icon.
- Fixed CLI option routing for `--stems` and `--separate-engine` through the shared batch flow.
- Fixed `Audio Separation` progress reporting, model preflight selection, and installer packaging for the new action.

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
