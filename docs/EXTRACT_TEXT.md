# Extract Text

Implemented in FrameShift 1.21.0. The application and its dialogs use English.

## Workflow

Right-click an image or PDF and choose **FrameShift AI → Extract text...**, or add images/PDFs to the hub and choose **Extract Text**. Select **Small — Higher accuracy** (default) or **Tiny — Faster processing**, choose an output format, and press **Extract Text**. One settings dialog applies to the initial selection. Explorer invocations arriving during processing join the same queue; explicit CLI settings remain attached to their own items.

Supported inputs: PNG, JPG/JPEG, WebP, BMP and PDF. Image EXIF orientation is respected; transparent pixels are composited onto white. Animated images use their first frame.

Models download on demand through the existing model-download dialog, with a visible download size and Apache-2.0 licence. A connection is needed only to download or repair a missing/corrupt package. Input files stay on the computer. OCR, PDF rendering and export then run locally using C#/.NET, ONNX Runtime and PDFium; no Python, Paddle installation or online OCR service is required.

## Outputs

| Format | Result |
| --- | --- |
| Plain text (.txt) | UTF-8 without BOM, reading order, original line breaks or paragraph grouping. Optional `[Page N]` markers for PDF sources. |
| Searchable PDF (.pdf) | Original appearance with an invisible Unicode text layer for searching and copying. An image becomes one PDF page. A PDF preserves every original page; the selected range controls where OCR is added. |
| Structured data (.json) | Page number, dimensions, coordinate units, detected lines, polygons, source (`native` or `ocr`) and OCR confidence. |

Outputs are created next to each input: `name_text.txt`, `name_searchable.pdf` or `name_ocr.json`. Existing files are preserved; collisions use `_001`, `_002`, etc. Work happens in an exclusively created adjacent workspace. Failure/cancellation cleans unpublished output. Completed queue items keep their files.

PDF export preserves layout through the source document. It does not reflow the document into editable paragraphs or reconstruct tables. Modifying a signed PDF changes its signed content; an OCR copy is a new document.

## Options

| Option | Behavior |
| --- | --- |
| Model | Small uses a larger recognizer/dictionary; Tiny prioritizes download size and processing speed. The selected detector and recognizer always come from the same verified package. |
| Selected pages | Hidden for image-only selections. Empty selection processes all PDF pages. `1-3,5` is one-based, ordered and deduplicated. The same range applies to each PDF; out-of-range pages fail that item with a readable error. |
| Text layout | TXT only: keep line breaks (default), or group nearby lines into paragraphs using geometry. Column reading order is inferred; complex layouts may require review. |
| Include page markers | PDF → TXT only. Enabled by default. |
| PDF text source | Automatic (default) reuses existing native text and OCRs pages that contain images or have no usable text. Overlapping OCR/native lines are deduplicated. OCR only ignores native text for TXT/JSON. Searchable PDF always preserves native text. |
| PDF rendering quality | Standard 200 DPI (default), or High 300 DPI. Image sources retain their decoded resolution. |
| Recognition rotation | As stored, 90° clockwise, 180°, or 270° clockwise. Rotates the recognition input and maps text coordinates back to the original source. It does not visibly rotate the PDF output. |
| Processing | Automatic prefers DirectML and falls back to CPU on unsupported providers/inference. CPU can be selected explicitly. |

Advanced options start collapsed. The picker remembers model, output format, TXT paragraph preference and rendering DPI. Page ranges, recognition rotation, OCR-only mode and CPU forcing are deliberately reset for the next invocation.

Reading order is inferred automatically from whitespace and line geometry. Narrow column gutters and the margins around full-width introductions/headings/footers are measured relative to text height, so ordinary two-column pages are read down the left column and then down the right column. This applies to both image pixels and PDF points without a separate model or manual column setting. Unusual layouts, tables and skewed scans whose line bounds overlap the gutter can still require review.

Native-only PDFs need no downloaded OCR model. Mixed PDFs are evaluated page by page; a small native header does not cause scanned body text to be skipped. A password-protected PDF requires an unlocked copy. Corrupt files fail individually; later queue items can continue. Empty recognition still creates the selected output and reports that no text was found. Low-confidence text is reported for review.

## Model packages and licences

The application pins [Gaurox/frameshift-models revision b4f8fa6](https://huggingface.co/Gaurox/frameshift-models/tree/b4f8fa610edff25e059959fb478bd3879e26f7a7).

| Variant | Folder | Approximate complete download |
| --- | --- | --- |
| Tiny | `pp-ocrv6-tiny-onnx` | 6.7 MB |
| Small | `pp-ocrv6-small-onnx` | 31.7 MB |

Each package contains 13 verified files: root `LICENSE`, `NOTICE-export-provenance.md`, `README.md`, `SHA256SUMS`, `source_manifest.json`, and detector/recognizer `inference.onnx`, `inference.yml`, `inference.json`, `README.md`. The embedded `Core/AI/Ocr/model-files.json` supplies exact sizes and SHA-256 values; the upstream checksum listing is preserved as provenance, not treated as an untrusted replacement for those expectations.

The packages use Apache-2.0. Licence and attribution files accompany the downloads; bundled PDFium, YamlDotNet, Clipper2 and GlyphLessFont notices are included in the installation's `licenses/ocr` folder. See [Third-Party Notices](../THIRD_PARTY_NOTICES.md).

Model storage uses the configured FrameShift AI folder. FrameShift marks directories it creates, serializes simultaneous downloads with a file lease, repairs only invalid/missing files, and leaves foreign content alone. Optional uninstall cleanup removes only known files in owned directories with the expected `det`/`rec` structure; foreign files, subdirectories and reparse points prevent cleanup.

## CLI

```powershell
FrameShift.exe --action extract-text --ocr-model tiny --ocr-format txt "C:\Docs\invoice.pdf"
FrameShift.exe --action extract-text --ocr-format pdf --ocr-pages "1-3,5" --ocr-dpi 300 "C:\Docs\scanned.pdf"
FrameShift.exe --action extract-text --ocr-format json --ocr-device cpu "C:\Images\été.png"
```

| Argument | Accepted values | Default |
| --- | --- | --- |
| `--ocr-model` | `small`, `tiny` | `small` |
| `--ocr-format` | `txt`, `pdf`, `json` | `txt` |
| `--ocr-pages` | Comma-separated page numbers/ranges | All pages |
| `--ocr-layout` | `lines`, `paragraphs` | `lines` |
| `--ocr-page-markers` | `true`, `false` | `true` |
| `--ocr-text-source` | `auto`, `ocr` | `auto` |
| `--ocr-dpi` | `200`, `300` | `200` |
| `--ocr-rotation` | `0`, `90`, `180`, `270` | `0` |
| `--ocr-device` | `auto`, `cpu` | `auto` |

Without OCR options the picker opens. Supplying any OCR option selects CLI configuration with the documented defaults; the progress window remains visible and a missing model can still prompt for download. `--ocr-format pdf --ocr-text-source ocr` is rejected because searchable PDFs retain existing text.

## JSON contract

Schema version 1 contains `source` (filename), `model`, pinned `modelRevision`, `coordinateSystem`, `pages` and actual `provider`. Each page carries `pageNumber`, `width`, `height`, `units` and `lines`. Coordinates start at the top-left of the displayed source page, accounting for image orientation and PDF rotation/crop boxes. Image coordinates use pixels; PDF coordinates use points (1/72 inch).

Each line has `text`, `confidence` (null for native text), `polygon` (four x/y points), `source`, and derived `left`, `top`, `right`, `bottom`. Confidence is the recognizer's average decoded-character probability, not a calibrated guarantee of correctness.

## Implementation and validation

The focused `Core/AI/Ocr` classes implement detector preprocessing, DB region extraction/unclipping, tiled images, rectified line crops, recognition normalization, CTC decoding, reading order and text output. DirectML uses sequential sessions with memory patterns disabled. Inference runs away from the UI thread; native PDFium access is serialized and pages are handled individually. Sessions are reused within a queue and released when it closes. Cancellation terminates active ONNX calls and is checked between pages, tiles, lines and publication.

Resource limits: decoded images at most 64 million pixels; rendered PDF pages at most 32 million pixels at the selected DPI; individual recognition crops at most four million pixels. These limits produce readable errors. Dense layouts, unusual scripts, handwriting and damaged scans can have lower accuracy; automatic table reconstruction, DOCX, hOCR, ALTO, automatic orientation detection and semantic document reconstruction are outside this feature.

Automated coverage includes page-range validation, accented/spaced paths, Unicode/emoji PDF copy text, CPU/DirectML inference for both packages, six image/PDF export combinations, native/mixed PDFs, manual rotation, PDF crop/rotation, appearance preservation, collision naming, cancellation before and during processing, queue-session reuse and conservative cache cleanup. Set `FRAMESHIFT_OCR_TEST_MODELS` to a directory containing both complete model packages to enable real-model tests. Ordinary tests do not download models.

Build through `build_installer.ps1` with the private Six Labors licence configured as documented in the repository README. Installed Explorer registration, screen-reader use and physical multi-monitor DPI transitions remain manual acceptance checks.
