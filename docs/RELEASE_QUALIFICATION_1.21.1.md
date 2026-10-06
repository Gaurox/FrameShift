# FrameShift 1.21.1 release qualification

Prepared on 7 October 2026 at the owner's request for an updated installer. Version 1.21.1 follows the repository's patch-numbering rule for fixes without new features. The owner subsequently authorized committing, pushing and publishing this exact installer on GitHub.

## Scope

Remove Noise audio/video modal closure, DeepFilterNet model preflight before Preview, inline cancellation of the shared model download dialog, and repeated close requests during Cut Video/Crop Image preview cleanup. No UI framework, model weights, package version or processing algorithm changed.

## Validation before packaging

- The eight Remove Noise modal close cases failed before correction and pass afterwards.
- The shared downloader's inline-cancellation case and repeated-close cases for Cut Video/Crop Image failed before correction and pass afterwards.
- Expanded Debug run: **146 passed, 0 failed, 0 skipped**, including 53 audit cases over 13 modal windows, lifetime/cleanup tests, OCR UI and existing real-media editor tests.
- Missing-model audio/video preflight tests verify download is offered before the settings window and that cancellation stops preparation without downloading anything.
- Details: [modal closing audit](MODAL_CLOSING_AUDIT_2026-10-07.md).

## Canonical build

The canonical command `build_installer.ps1 -AllowDirty` completed successfully on 7 October 2026, with the owner's private Six Labors licence supplied through `SixLaborsLicenseFile`. The build intentionally includes the uncommitted correction/version files. No commit, tag or public release is implied by generating this candidate.

- SDK **8.0.425**; self-contained **win-x64**, application/worker runtime **8.0.31**. All three locked restores passed. Release compilation and publishing completed without compiler warnings/errors; the official ImageSharp licence validator accepted the licence.
- Full mandatory Release suite: **795 passed, 0 failed, 11 skipped, 806 total**, in **4 minutes 18 seconds**. Six OCR theories were disabled by their opt-in environment flag; five Whisper integrations lack their required models/corpora.
- Complementary Release run with `FRAMESHIFT_OCR_TEST_MODELS` pointing at the existing local models: **35 passed, 0 failed, 0 skipped**, in **7 seconds**. This covers the OCR tests disabled in the canonical run, including real Tiny/Small recognition, document/action exports and cancellation. No model was downloaded. Existing local-model security tests were enabled in the canonical run via `FRAMESHIFT_SECURITY_AI_MODELS`.
- Canonical payload checks passed for application/worker manifests and runtime binaries, ImageSharp 4.1.2, FFmpeg/FFprobe pinned hashes, OCR binaries and distribution notices.
- **Inno Setup 6.7.1** compiled the installer successfully in **82.844 seconds**, using version 1.21.1 and the freshly published payload.
- Independent artifact checks confirm application assembly/file version and installer numeric version **1.21.1.0**, both embedded application frameworks at 8.0.31, **800 payload files**, all **20 OCR notice files**, and no `.pdb`, `.dbg`, private `.lic`, `.onnx` weights or test/UI-sample assembly in that payload. The real user's AI settings SHA-256 was unchanged across the successful build and checks.

## Artifacts

| Artifact | Size/version | SHA-256 |
| --- | --- | --- |
| `installer/FrameShift_1.21.1_Setup.exe` | 175,010,494 bytes; 1.21.1.0 | `D09AAB3E3CC1E2E3F4E6DC5968BA54293363DB34B89C139B6D01615C55E6BD10` |
| `publish/FrameShift-win-x64/FrameShift.dll` | 1.21.1.0 | `2952363614467A6AAD71B8AD3CF89725CCABD41BBA047CBFC35DD57157DE1585` |

An adjacent checksum file is saved as `installer/FrameShift_1.21.1_Setup.exe.sha256`. Build evidence is retained under ignored `scratch/release-1.21.1-build.log`, `scratch/release-1.21.1-ocr-tests.log` and `builds/release-1.21.1/artifact-verification.json`.

The application product metadata is `1.21.1+9690a9477f863447a8245a9d457ebd4b7db5b5cc`, reflecting the pre-existing HEAD during this intentionally dirty build. It does not identify a new patch commit. The SHA-256 values identify the exact generated candidate.

## Installed acceptance

The owner reports that Remove Noise appears corrected in this installer. This is preliminary feedback and does not qualify the remaining scenarios.

The canonical build does not install into the owner's current installation. Hub/Explorer entry points, real DeepFilterNet Preview followed by Denoise, actual network-download cancellation, upgrade from 1.21.0, uninstall and physical monitor/DPI changes require separate installed acceptance. Automated modal/lifetime checks do not claim native inference cancellation or installed qualification. Use the [Remove Noise recipe](UI_PHASE_D3_MANUAL_TESTS.md) and [release checklist](RELEASE_CHECKLIST.md).
