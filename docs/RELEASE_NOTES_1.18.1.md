# FrameShift 1.18.1

## GitHub release title

`FrameShift 1.18.1 — Stability and release hardening`

## GitHub release body

```markdown
## Highlights

- **Reliable queues and batches:** Whole-queue scope, compression batching, duplicate inputs, late requests, and removal of pending items now behave predictably.
- **Robust AI video workflows:** RIFE interpolation and Upscale Video respect the video's display orientation. Cancellation and shutdown now cleanly stop FFmpeg, FFprobe, raw-video, ONNX, and Cut Audio work.
- **Lower, bounded resource use:** Separate Audio streams chunks sequentially, and video/noise workflows include memory and disk preflight safeguards.
- **Validated Windows release:** The self-contained `win-x64` installer bundles FFmpeg 9.0 with pinned hashes and required licence notices. The canonical release script validates restore, Release tests, payload, and Inno Setup output.

## Download

- `FrameShift_1.18.1_Setup.exe` — Windows 10/11 x64 installer
- SHA-256: `08731D192B975B7C4206322B2D2F803FE3C758E2B637085F8B178BFFE57A0082`

FrameShift remains self-contained and offline-first. Optional AI models download only when needed.
```

## Publication checklist

- Attach `installer\FrameShift_1.18.1_Setup.exe` to the GitHub release.
- Use tag `v1.18.1` and the release title above.
- Publish only after manual validation of the priority scenarios in the release checklist.
