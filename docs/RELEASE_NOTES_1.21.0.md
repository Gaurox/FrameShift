# FrameShift 1.21.0

Published on 5 October 2026: [GitHub release 1.21.0](https://github.com/Gaurox/FrameShift/releases/tag/1.21.0). The installer and checksum were verified by anonymous public download; the updated website is deployed.

## Extract Text from images and PDFs

Right-click a supported image or PDF and choose **Extract text**, or launch the action from the FrameShift hub. Choose **PP-OCRv6 Small** for higher accuracy or **Tiny** for faster processing, then save **UTF-8 text**, **structured JSON** or a **searchable PDF** that retains the original document appearance.

- Supports PNG, JPG/JPEG, WebP, BMP and PDF, including scanned and mixed native/scanned documents.
- PDF options include page ranges, native-text reuse, 200/300 DPI rendering, recognition rotation, line breaks or paragraph grouping, and optional text page markers.
- Reads detected columns in sequence. Oversized PDF pages automatically use a safe rendering resolution and report the quality adjustment instead of failing the document.
- Runs locally with CPU or DirectML. Small (approximately 31.7 MB) and Tiny (approximately 6.7 MB) download on demand with dictionaries, Apache-2.0 notices and SHA-256 verification; subsequent processing works offline.
- Creates a uniquely named output beside each source and preserves original files. Searchable PDF output retains every source page, including pages outside a selected OCR range.

Enable **Extract text (images and PDF)** in the installer to add the Explorer menus.

Download **FrameShift_1.21.0_Setup.exe** from [release 1.21.0](https://github.com/Gaurox/FrameShift/releases/tag/1.21.0): Windows 10/11 x64, self-contained, no separate .NET installation required. The release also includes a SHA-256 checksum file.

Validation: **754 tests passed, 0 failed, 5 skipped** because their Whisper test assets are unavailable. OCR tests executed on both CPU and DirectML. The owner accepted the image/PDF workflow, column correction and oversized-page correction. Complex or skewed layouts can still require reviewing the extracted text. See the [feature guide](EXTRACT_TEXT.md) and [qualification report](RELEASE_QUALIFICATION_1.21.0.md) for details and remaining installed-qualification limits.
