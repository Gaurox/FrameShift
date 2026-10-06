# FrameShift 1.21.1

Prepared on 7 October 2026, with GitHub publication authorized by the owner. This patch follows 1.21.0 and adds no new action.

- **Remove Noise audio/video:** Denoise, Cancel and native close no longer leave the settings window stuck on "Closing..." after idle or completed preview cleanup.
- **Noise-removal models:** FrameShift now checks and offers to download DeepFilterNet3 before opening the settings window, making the first Preview available without manually installing ONNX files.
- **Shared AI model downloader:** Closing during a download no longer leaves the window open when cancellation finishes immediately.
- **Cut Video and Crop Image:** Repeated close requests wait for preview cleanup before the window closes, including when cleanup finishes immediately.

The installer is `FrameShift_1.21.1_Setup.exe`, **175,010,494 bytes**, built through the canonical Release workflow for Windows 10/11 x64. It is self-contained and requires no separate .NET installation. A SHA-256 checksum file accompanies the installer.

The mandatory Release suite passed **795 tests**, with 11 opt-in/unavailable-asset tests skipped; a complementary OCR run passed **35 tests without skips** using the existing real models. The owner reports that Remove Noise appears corrected. Other installed checks remain open. See the [qualification report](RELEASE_QUALIFICATION_1.21.1.md) for the exact hashes and remaining acceptance checks. The [modal closing audit](MODAL_CLOSING_AUDIT_2026-10-07.md) records the reproduced failures and their corrections.
