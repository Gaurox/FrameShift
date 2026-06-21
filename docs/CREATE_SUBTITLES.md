# Create Subtitle File

Active V1 notes for `create-subtitles-audio` and `create-subtitles-video`.

## Scope

- local/offline only
- GPU via DirectML (Windows), automatic CPU fallback
- three selectable models: Whisper Base, Whisper Small (default), Whisper Large-v3 Turbo
- adjacent unique `.srt` output, UTF-8 no BOM
- no overwrite

## Supported extensions

Audio: `.wav` `.wave` `.mp3` `.flac` `.m4a` `.ogg` `.aac` `.wma`

Video: `.mp4` `.mkv` `.avi` `.mov` `.webm` `.m4v`

## Shared pipeline rule

The two visible actions stay separate for installer and Explorer purposes:

- `create-subtitles-audio`
- `create-subtitles-video`

But the business pipeline is shared:

- same model catalog
- same downloader
- same Whisper engine
- same windowing (29 s / overlap 1.5 s)
- same overlap / dedup logic
- same SRT segmentation
- same final writer

Video adds only one pre-step:

- extract first audio track via `FfmpegRunner` → PCM s16le WAV

Then both paths normalize to the same Whisper input:

- mono · 16 kHz · PCM s16le WAV

## GPU Acceleration

The worker uses DirectML (Windows GPU) by default with automatic CPU fallback.

- **DirectML init failure** (no compatible GPU, old driver): logged to stderr, worker continues on CPU.
- **DirectML inference failure** (e.g., VRAM OOM mid-run): the worker disposes the DirectML recognizer
  and restarts the entire transcription from scratch on CPU. No partial SRT is produced.
- The active provider (`directml` or `cpu`) is logged after the worker exits.

The DirectML provider string is `"directml"` (not `"dml"` — exact mapping in `provider.cc:StringToProvider`).

### Bundled native DLLs

Four DLLs shipped in `Workers/CreateSubtitlesWorker/` override the CPU-only DLLs from the
sherpa-onnx NuGet package. They are published as a directory (not a single-file bundle) so that
`DirectML.dll` is resolved by Windows `LoadLibrary` from the exe directory.

| File | Source | Version | SHA-256 |
|---|---|---|---|
| `sherpa-onnx-c-api.dll` | sherpa-onnx recompiled with `-DSHERPA_ONNX_ENABLE_DIRECTML=ON` | 1.13.3 | `BD41BDD1…93F2B0D8` |
| `onnxruntime.dll` | Microsoft.ML.OnnxRuntime.DirectML win-x64 | 1.24.4 | `E7EEDEC6…B97BFF40` |
| `onnxruntime_providers_shared.dll` | Microsoft.ML.OnnxRuntime.DirectML win-x64 | 1.24.4 | `265C8DAF…4CBEC3B6` |
| `DirectML.dll` | Microsoft.AI.DirectML bin/x64-win | 1.15.4 | `9C9E6D82…4E92DA1` |

Full SHA-256 hashes and license texts are in
`src/FrameShift.SubtitlesWorker/native-dml/THIRD_PARTY_NOTICES.txt`.

FeatureDim per model: Base=80, Small=80, Turbo=128. Propagated from `CreateSubtitlesModelCatalog`
through the request protocol to the worker's `OfflineRecognizerConfig.FeatConfig.FeatureDim`.

## Runtime isolation

Whisper runs through sherpa-onnx in a dedicated worker process:

- `src/FrameShift.SubtitlesWorker/`

Reason: FrameShift already ships another ONNX Runtime stack for its existing AI modules;
isolating sherpa avoids native `onnxruntime.dll` collisions and keeps the rest of the app unchanged.

The main process keeps:

- probe / validation
- FFmpeg extraction and normalization
- model preflight and download
- progress wiring
- cancellation signaling
- SRT assembly and output naming
- temp cleanup

**Hang guard**: the runner kills the worker after 6 hours if it has not exited. Safety net for
deadlocks only — even hours of audio on CPU finishes well within this window.

**Cancellation**: a cancel-signal file is written when the user cancels. The worker checks it
between transcription windows and exits cleanly. If the worker does not exit within 20 seconds,
it is killed.

**Worker crash (native exception before response is written)**: the runner reports the OS exit code
to the log and shows "Whisper worker crashed (exit code …)" with a suggestion to check the model files.

## Worker communication protocol

All files live under a per-run temp dir (`%TEMP%\FrameShift\CreateSubtitles\<guid>\`):

| File | Role |
|---|---|
| `worker-response.json.request.json` | serialized `CreateSubtitlesWorkerRequest` written before launch |
| stdout lines | `{"type":"progress","percent":N,"message":"..."}` JSON events |
| `worker-response.json` | `CreateSubtitlesWorkerResponse` written by the worker on exit |
| `cancel.signal` | created by the main process to request cancellation between windows |

Worker path resolution: `Workers/CreateSubtitlesWorker/FrameShift.SubtitlesWorker.exe` (published layout),
then Debug / Release build paths (test/dev fallbacks).

## ASCII path workaround

sherpa-onnx rejects paths with non-ASCII characters. If the configured model directory contains
non-ASCII chars, the action copies all model files to `worker-model/` under the per-run temp dir
before launching the worker. The copies are deleted in the `finally` block.

For Turbo, this includes both `turbo-encoder.onnx` and `turbo-encoder.weights` since ONNX Runtime
resolves encoder external data by relative filename from the encoder graph file.

## Models

Three models are available via `CreateSubtitlesModelCatalog`. The catalog ID is stored in
`options[SubtitlesModel]` (key `subtitles-model` on the CLI).

| Id | Display name | Download size | Artifacts |
|---|---|---|---|
| `whisper-base` | Whisper Base — Fast | ~280 MB | encoder.onnx, decoder.onnx, tokens.txt |
| `whisper-small` | Whisper Small — Recommended | ~925 MB | encoder.onnx, decoder.onnx, tokens.txt |
| `whisper-turbo` | Whisper Large-v3 Turbo — Quality | ~3.1 GB | encoder.onnx, encoder.weights, decoder.onnx, tokens.txt |

Default: `whisper-small`.

### Turbo encoder external data

The Turbo encoder exceeds the protobuf 2 GB limit. ONNX Runtime's external data format is used:

- `turbo-encoder.onnx` (~736 KB): ONNX graph, all tensor references point to offsets in `turbo-encoder.weights`
- `turbo-encoder.weights` (~2.5 GB): consolidated weight tensors

Both files must be in the same directory. `CreateSubtitlesModelCatalog` lists `turbo-encoder.weights`
as `Artifacts[3]`. The downloader verifies its SHA256 and the locator's `GetModelFiles` returns
`Artifacts[0..2]` (encoder.onnx, decoder.onnx, tokens.txt) as the sherpa paths; ORT finds the
weights file automatically via the same directory.

### Turbo language detection note

The sherpa-onnx spoken-language-identification step uses a fixed 80-mel spectrogram. Turbo requires
128 mel bins, so the language detection pass silently returns empty for Turbo. Transcription
proceeds correctly; the UI shows "Language: auto" instead of the detected language code.

## Hosted artifacts

All models are hosted at `Gaurox/frameshift-models` on Hugging Face:

- `whisper-base-onnx/`
- `whisper-small-onnx/`
- `whisper-large-v3-turbo-onnx/`

Checksums are pinned in `CreateSubtitlesModelCatalog.cs` and verified before use.

## Preflight and UI flow

1. `EnsureCreateSubtitlesOptions` in `ProgramAiPreflight.cs`:
   - validates extension
   - opens `CreateSubtitlesPickerForm` (3-model radio picker, Small selected by default)
     unless `--subtitles-model` was passed headlessly
   - writes `options[SubtitlesModel] = <selected-id>`

2. `EnsureCreateSubtitlesModelReady` in `ProgramAiPreflight.cs`:
   - checks all artifacts via SHA256
   - removes invalid files
   - opens `DownloadModelForm` if any artifact is missing or invalid

## SRT segmentation limits

Constants in `CreateSubtitlesSegmenter` (from `CreateSubtitlesSrt.cs`):

| Constant | Value |
|---|---|
| Max cue characters | 84 |
| Max cue words | 14 |
| Max cue duration | 5.8 s |
| Silence break threshold | 0.85 s (min 2 words in cue) |
| Strong punctuation break | after 1.2 s of cue time |
| Soft punctuation break | after 38+ chars |
| Single-line target | ≤ 42 chars → single line |
| Two-line break | balanced split at midpoint |

Cue end is estimated from last word start + letter-count tail (0.38–1.10 s), capped 40 ms before
the next cue start and clamped to total duration.

## Tests

`tests/FrameShift.Tests/CreateSubtitlesTests.cs` — 9 tests:

- `Segmenter_BreaksOnPunctuationAndSilence` — unit, no model required
- `Default_Model_Is_Whisper_Small` — catalog unit test
- `Turbo_Model_Has_Four_Artifacts_Including_Weights_File` — catalog unit test
- `Audio_And_Video_Actions_Produce_Equivalent_Subtitle_Text` — end-to-end, requires Base model
- `Small_Model_Produces_Subtitles_With_FR_EN_Audio` — end-to-end, requires Small export in scratch/
- `Long_Audio_Over_Thirty_Seconds_Produces_Subtitles` — windowing, requires Base + long sample
- `Long_Audio_Can_Be_Canceled_Between_Windows` — cancellation path
- `Video_Without_Audio_Track_Fails_Cleanly` — validation error
- `Corrupted_Audio_File_Fails_Cleanly` — probe failure path

Integration tests load models from `scratch/WhisperBaseOnnxSpike/export-control/` and sample
audio from `scratch/WhisperBaseOnnxSpike/samples/`.
