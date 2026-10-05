# FrameShift 1.21.0 local qualification

Prepared on 4 October 2026, updated on 5 October 2026. The feature is implemented and the installer is built locally; this document does not claim a published or installed release.

## Build and tests

- Canonical command: `build_installer.ps1 -AllowDirty`, with the existing private Six Labors Community licence supplied through `SixLaborsLicenseFile`.
- SDK 8.0.425, self-contained Windows x64 application and subtitle worker, runtime 8.0.31. All three locked restores passed. Compilation reported no warnings/errors.
- Full Release suite after the column-order correction: **742 passed, 0 failed, 5 skipped, 747 total**, 4 minutes 11 seconds.
- Real OCR tests used both complete packages downloaded through FrameShift's own downloader from the pinned HF revision. CPU and DirectML cases executed. Existing local-model security/non-regression tests used the owner's existing model cache without changing those models.
- The five skipped cases require unavailable Whisper test models/corpora: alternate subtitle formats, audio longer than 30 seconds, audio/video equivalence, Small FR/EN, and cancellation between long-audio windows. No OCR case was skipped.
- Packaging verified the runtime manifests/binaries, ImageSharp 4.1.2, FFmpeg/FFprobe hashes, OCR binaries and all 20 OCR licence/notice files. No `.onnx` weights or private `.lic` file occur in the published payload.
- Inno Setup compiled successfully. A separate `/O-` syntax check also passed without producing setup files.

## Functional evidence

Automated cases exercise image/PDF to TXT, JSON and searchable PDF, native-only PDFs without downloaded models, mixed native/scanned text without duplication, selected page ranges, Unicode/emoji copy text, manual recognition rotation, rotated/cropped PDF pages, source/render preservation, existing-output collisions, cancellation and partial-output cleanup. A 5,000-line case verifies bounded reading-order recursion on very tall pages. The same OCR action remains usable for the next item after cancellation. Queue tests verify explicit per-item settings and continuation after failed preparation.

The published `FrameShift.exe` was launched directly on a private Windows desktop with isolated AI settings and temporary paths. Three simultaneous invocations on `sample été.png` produced TXT (Tiny/CPU), JSON (Small/DirectML) and searchable PDF (Tiny/CPU), preserving each invocation's settings. A repeated TXT invocation produced `_001` and retained the earlier output. The resulting PDF was then processed as a PDF source into JSON. Source SHA-256 remained unchanged; no adjacent working directory remained.

Recognized TXT sample:

```text
FrameShift OCR 2026
Été à Paris - façade
```

Visual review covered the native WinForms picker in light/dark themes, collapsed/expanded options and a narrow window. The shared scroll body retained accessible footer commands. This review used the current desktop DPI; physical monitor transitions and screen-reader interaction were not exercised.

### Column-order correction (5 October 2026)

The owner's two-column brochure reproduced alternating left/right lines. Its detected gutter and the margins separating the full-width sections were smaller than the original reading-order thresholds. These thresholds now follow median text height in both pixels and PDF points, preserving the introduction, complete left column, complete right column, and final full-width section.

The regression fixture contains the actual 82 detected bounds without recognized text. It failed before the correction at all three coordinate scales (1, 0.36 and 1.5) and now passes. Narrow-column/full-width and single-column paragraph cases also pass. The focused OCR suite passed **49 tests, 0 failed, 0 skipped**, including real-model CPU/DirectML cases. Separate CPU recognition of the supplied brochure with Tiny and Small, at 200 and 300 DPI, retained all 82 lines and placed all 23 left-column lines before all 22 right-column lines in each run. The published `FrameShift.dll` was also loaded directly and its reading-order method returned the exact 82-line expected sequence. Evidence is under `scratch/ocr-qa-results/columns-*-after-*.json` and `.txt`.

The skewed companion brochure remains a known limitation: overlapping axis-aligned bounds close its gutter. No deskewing or layout-analysis model was added, and this correction does not claim that every complex or inclined layout is resolved.

## Artifacts

| Artifact | Size/version | SHA-256 |
| --- | --- | --- |
| `installer/FrameShift_1.21.0_Setup.exe` | 175,029,614 bytes | `76677D30518A53572FC21A53EAEA4E4070AF573DD54BD23EF321F7062725C12A` |
| `publish/FrameShift-win-x64/FrameShift.dll` | 1.21.0 | `BB5EE58FA99CC6199A8837F132590120D60193103614602D9BBF586778800B05` |

Local evidence is retained under ignored `scratch/ocr-qa-results/`, `scratch/ocr-release-build.log` and `scratch/ocr-columns-release-build.log`. Source and tests are recorded in the implementation commit requested by the owner on 5 October 2026. No push or public release was requested.

## Installed acceptance

The owner confirmed that OCR extraction works and accepted the corrected column reading order on 5 October 2026. The agent did not run the installer against the owner's existing installation. Explorer registration, update/uninstall interaction and physical multi-monitor DPI behavior have not been independently qualified. Enable the **Extract text (images and PDF)** installer component to register the new Explorer menus.

See [the feature guide](EXTRACT_TEXT.md) for formats, options, model storage and CLI usage.
