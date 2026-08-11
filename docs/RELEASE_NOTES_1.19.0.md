# FrameShift 1.19.0

## GitHub release title

`FrameShift 1.19.0 — Join Videos`

## GitHub release body

```markdown
## Highlights

- **Join Videos:** Arrange multiple clips on a visual timeline — thumbnails, duration-proportional widths, sort by received order/natural filename/dates/custom drag order, and drop files from Explorer directly onto the timeline at the exact position you want. `Delete` and `Ctrl+Left`/`Ctrl+Right` reorder without the mouse, a `Clear all` button resets the timeline, and a per-clip tooltip flags a resolution or audio mismatch against the first clip before you run the join.
- **Safe media strategy:** FrameShift copies streams without re-encoding only when FFprobe finds a strict compatible signature; otherwise SDR clips normalize automatically to H.264/AAC MP4, matching the first clip's display geometry with aspect-preserving padding and generated silence for clips without audio. Mixed HDR/SDR or HDR needing normalization is refused clearly in this first version.
- **Explorer integration:** A dedicated `Join videos` video menu entry with its own icon uses the Player multi-select model and aggregates multiple Explorer invocations before opening the editor. CLI mode: `--join-mode auto|copy|normalize`.
- **Fixed normalization on the bundled FFmpeg:** The normalization pipeline now passes its filter graph inline via `-filter_complex` instead of `-filter_complex_script`, an option the bundled FFmpeg 9 essentials build does not implement. Without this fix, joining any two clips that couldn't use direct stream copy — i.e. almost any two different source files — failed immediately with a misleading "codecs not supported" error.

## Download

- `FrameShift_1.19.0_Setup.exe` — Windows 10/11 x64 installer
- SHA-256: `BBE622D35AB86203ED9DE3AFA2C32E2BFBC8A27FA8D43039ACEC58257C2958D4`

FrameShift remains self-contained and offline-first. Optional AI models download only when needed.
```

## Publication checklist

- Attach `installer\FrameShift_1.19.0_Setup.exe` to the GitHub release.
- Use tag `v1.19.0` and the release title above.
- Publish only after manual validation of Join Videos across direct-copy and normalize paths (matching and differing resolution/codec/audio), Explorer multi-select aggregation, and cancellation cleanup.
