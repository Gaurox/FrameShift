# FrameShift 1.18.0

## GitHub release title

`FrameShift 1.18.0 — Extract specific frames`

## GitHub release body

```markdown
## Highlights

- **Extract specific frames:** The existing `Extract all frames` action remains available in one click. Explorer now also provides **First frame**, **Last frame**, and **Keyframes** under **Extract specific frames**.
- **CLI modes:** `FrameShift.exe --action extract-frames --frame-mode all|first|last|keyframes "video.mp4"`. Leaving out `--frame-mode` preserves the historical all-frames behavior.
- **Reliable media handling:** Keyframe extraction combines FFmpeg keyframe decoding with keyframe filtering for VP9/AV1. Last-frame extraction searches from the end progressively, then safely falls back to a full decode when necessary.
- **Safe outputs:** First and last frames are unique PNG files next to the source; keyframes are written to a unique adjacent folder. Partial output is removed after failure or cancellation.

## Downloads

- `FrameShift_1.18.0_Setup.exe` — Windows 10/11 x64 installer
- SHA-256: `E421BB57BA284001195A60FAEFF142FF3D75D2AA39AC616FDD9F10C6FD0267E6`

FrameShift remains self-contained and offline-first. Optional AI models download only when needed.
```

## Publication checklist

- Attach `installer\FrameShift_1.18.0_Setup.exe` to the GitHub release.
- Use tag `v1.18.0` and the release title above.
- Publish only after manual validation of Explorer menus, the four extraction modes, cancellation cleanup, and the last-frame fallback on representative media.
