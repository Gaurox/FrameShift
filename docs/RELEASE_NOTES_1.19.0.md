# FrameShift 1.19.0

## GitHub release title

`FrameShift 1.19.0 — Join Videos`

## GitHub release body

```markdown
## Highlights

- **Join Videos:** Arrange multiple clips on a visual timeline — thumbnails, duration-proportional widths, and flexible sorting (received order, natural filename, dates, or custom drag order). Drop files from Explorer straight onto the timeline at the position you want, reorder with `Ctrl+Left`/`Ctrl+Right`, and a per-clip tooltip flags a resolution or audio mismatch before you run the join.
- **Safe media strategy:** FrameShift copies streams without re-encoding when FFprobe finds a strict compatible signature; otherwise SDR clips normalize automatically to H.264/AAC MP4, matching the first clip's display geometry with aspect-preserving padding and generated silence for clips without audio. Mixed HDR/SDR or HDR needing normalization is refused clearly in this first version.
- **Explorer integration:** A dedicated `Join videos` video menu entry with its own icon. CLI mode: `--join-mode auto|copy|normalize`.

## Download

- `FrameShift_1.19.0_Setup.exe` — Windows 10/11 x64 installer
- SHA-256: `BBE622D35AB86203ED9DE3AFA2C32E2BFBC8A27FA8D43039ACEC58257C2958D4`

FrameShift remains self-contained and offline-first. Optional AI models download only when needed.
```

## Publication checklist

- Attach `installer\FrameShift_1.19.0_Setup.exe` to the GitHub release.
- Use tag `v1.19.0` and the release title above.
- Publish only after manual validation of Join Videos across direct-copy and normalize paths (matching and differing resolution/codec/audio), Explorer multi-select aggregation, and cancellation cleanup.
