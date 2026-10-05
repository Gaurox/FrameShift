# FrameShift 1.21.0 release qualification

Prepared on 4 October 2026, updated on 5 October 2026. **Release 1.21.0 is published after the owner's explicit authorization, with an anonymous installer download verified against the accepted canonical build.** The website is deployed and verified. Independent installed qualification remains limited as described below.

## Build and tests

- Canonical command: `build_installer.ps1 -AllowDirty`, with the existing private Six Labors Community licence supplied through `SixLaborsLicenseFile`.
- SDK 8.0.425, self-contained Windows x64 application and subtitle worker, runtime 8.0.31. All three locked restores passed. Compilation reported no warnings/errors.
- Full Release suite after the oversized-page correction: **754 passed, 0 failed, 5 skipped, 759 total**, 3 minutes 59 seconds.
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

### Oversized PDF page correction (5 October 2026)

The mixed six-page sample failed on its 2160 × 2160-point second page: rendering requires 36 million pixels at 200 DPI or 81 million at 300 DPI, above the existing 32-million-pixel limit. Rendering now adapts that page to **188 DPI / 5640 × 5640 pixels**, keeping normal pages at the selected DPI. Rounded dimensions and invalid/extreme dimensions are checked before bitmap allocation. Quality reductions are reported, logged per page, and flagged in the completion message. Cancellation is checked again after reporting the adjustment and before allocation.

The focused OCR suite passed **61 tests, 0 failed, 0 skipped**. New cases cover ordinary/oversized/rotated page dimensions, pixel rounding, very narrow pages, invalid/extreme dimensions, actual bounded rendering and page coordinates, and cancellation before bitmap allocation. The owner's complete six-page PDF was processed without exclusions into TXT (Tiny, 200 DPI), JSON (Small, 300 DPI), and searchable PDF (Small, 200 DPI), on CPU. Each run recognized the large page, retained all six pages, reported exactly one adjusted page, preserved the source SHA-256 and left no working directory. JSON coordinates were checked against original page bounds. The published `FrameShift.dll` was loaded directly; its render planner returned 5640 × 5640 pixels at 188 DPI for both requested qualities.

All six searchable PDF pages were compared to the source at 54 DPI: rendered pixels and original page sizes were identical. Visual review of the complete output also passed. Evidence is retained under ignored `scratch/ocr-qa-results/Mixed oversized été */` and `scratch/ocr-oversized-release-build.log`. The local manual-test guide now tests the complete PDF instead of excluding page 2. No model or UI framework was added.

## Artifacts

| Artifact | Size/version | SHA-256 |
| --- | --- | --- |
| `installer/FrameShift_1.21.0_Setup.exe` | 175,032,307 bytes | `48D711BD8A918F9E5764F7F874E2A1050195B944C37415D4C049B44829E01281` |
| `publish/FrameShift-win-x64/FrameShift.dll` | 1.21.0 | `AB7EF9E085CD775DB59B1A8F48F9C4B07CF66FCB5E3EF5A104DE314F7601685F` |

Local evidence is retained under ignored `scratch/ocr-qa-results/`, `scratch/ocr-release-build.log`, `scratch/ocr-columns-release-build.log` and `scratch/ocr-oversized-release-build.log`. The initial integration and column correction are recorded in commit `c972919`; the accepted oversized-page correction is recorded in `d0c5f8a81c815c4035e4feb5feb0d0751348dadd`. Both commits were requested by the owner on 5 October 2026. Publication of this exact installer is now authorized.

The canonical build was performed with the oversized-page correction present as explicitly allowed working-tree changes. Its product metadata retains `1.21.0+c972919429c3e5aa682f6577ff27d07878021845`. Commit `d0c5f8a81c815c4035e4feb5feb0d0751348dadd` records the corrected build sources. The annotated release tag targets the subsequent publication commit, including the README, release documents and screenshot. Its runtime sources and packaging inputs are unchanged from `d0c5f8a`; these documentation changes do not alter the accepted installer. SHA-256, rather than the retained build metadata suffix, identifies the accepted artifact.

## Installed acceptance

The owner confirmed that OCR extraction works and accepted both the corrected column reading order and the oversized-page correction on 5 October 2026. The agent did not run the installer against the owner's existing installation. Explorer registration, update/uninstall interaction and physical multi-monitor DPI behavior have not been independently qualified. Enable the **Extract text (images and PDF)** installer component to register the new Explorer menus.

## README and website verification

The owner's unmodified 602 × 554 screenshot is stored in `screenshots/AI_actions/Extract_text.png` and used in the README, website gallery and new `/frameshift/extract-text/` guide. Its SHA-256 is `AD090B543AAF0AD0563377BE8C987B13E198200894A38FB3E7F917A02687F7E4`. The prescribed shared synchronization script copied product screenshots to the website repository.

The website overview, action catalog, local-AI catalog/model table, comparison pages, Image to PDF guide, footer navigation and sitemap were reviewed and updated. The software version is 1.21.0 and the local-AI count is 10. Removed the outdated TIFF support claim from Image to PDF. Two existing mobile table overflows were corrected with keyboard-accessible scroll regions and a versioned stylesheet URL.

All **20 HTML pages**, including the 404 page and the other Gaurox product pages, were loaded in the browser at desktop size and at 390 × 844. No horizontal page overflow remains. Static validation checked internal links, anchors, assets, unique IDs, JSON-LD and the 19-page sitemap without errors. The OCR gallery lightbox opened and closed correctly. Screenshots and local verification results are retained under ignored `scratch/website-qa/` and `scratch/website-local-check.json`.

## Verified public publication

- [Stable release 1.21.0](https://github.com/Gaurox/FrameShift/releases/tag/1.21.0), release ID `404111521`, published at **2026-10-05T21:41:38Z** (23:41:38 CEST), public, not a prerelease, and confirmed as the latest release.
- FrameShift code, README, screenshot and release documents were pushed to `main`. Annotated tag `1.21.0`, object `0d13b87c8d85ff749961ea7dfbe649157bd28445`, targets publication commit `2ad8f86c994397fd51089c6c7bcbdaa2462e6412`. Runtime source and packaging inputs match the accepted correction commit `d0c5f8a`.
- [Installer](https://github.com/Gaurox/FrameShift/releases/download/1.21.0/FrameShift_1.21.0_Setup.exe), asset `613672905`, **175,032,307 bytes**. GitHub's asset digest and an anonymous public download both match **`48D711BD8A918F9E5764F7F874E2A1050195B944C37415D4C049B44829E01281`**. The [checksum file](https://github.com/Gaurox/FrameShift/releases/download/1.21.0/FrameShift_1.21.0_Setup.exe.sha256), asset `613672906`, downloaded identically to the local file. No rebuild or installer substitution occurred.
- Website commit `53db0006d1d5a5ad6b104ec25b40c9856c4a3593` was pushed to `Gaurox/gaurox-website:main`. Cloudflare Pages completed successfully at **2026-10-05T21:41:34Z**. The [OCR guide](https://gaurox.dev/frameshift/extract-text/) and overview show the new function; all 20 public pages were loaded in the browser without broken loaded images or horizontal overflow. Public mobile checks also passed for the OCR guide and the two corrected table pages.
- Separate anonymous HTTP verification passed for **20 pages and 54 unique asset URLs**. Page titles/H1s match the local sources, all FrameShift pages expose the OCR route and versioned stylesheet, and the downloaded public screenshot matches its source SHA-256 exactly. Completed at **2026-10-05T21:44:07Z**.
- Local publication evidence: ignored `builds/release-1.21.0/github-draft-metadata.json`, `github-published-metadata.json`, `installer-public-verification.json`, `public-download-verification.json`, and the verified installer in `public-download/`. Browser evidence is under `scratch/website-qa/`.

See [the feature guide](EXTRACT_TEXT.md) for formats, options, model storage and CLI usage.
