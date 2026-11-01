# FrameShift 1.22.0 — draft

Unpublished bilingual release candidate. Find Loop is reserved for 1.23.

- **English and simplified Chinese:** one application and installer, translated
  settings, tools, progress and errors, with optional Explorer menus in the
  installer's selected language. An application preference applies on next launch.
  Chinese translation and narrow-window corrections originate from
  [zhazhayu01](https://github.com/zhazhayu01/FrameShift).
- **Media integrity and cancellation:** reject unsupported multichannel separation,
  preserve transparent upscale pixels and existing write-probe files, propagate
  cancellation to FFmpeg and interrupt native noise-removal inference.
- **Memory:** bound PDF image/history/clipboard residency, stream audio and subtitle
  windows, and validate image dimensions before decoding.
- **Responsiveness:** batch queue changes, cancel folder imports and reuse successful
  upscale tile sizes instead of retrying known failures.
- **Model downloads:** protect model paths, coordinate concurrent instances, enforce
  disk/size/inactivity limits, verify checksums and preserve existing files when a
  replacement fails or is cancelled.
- **Local communications:** restrict pipes to the current Windows account/session,
  bound messages and process captures, and fail oversized structured output explicitly.

See [qualification](RELEASE_QUALIFICATION_1.22.0.md) for the exact candidate and its
remaining checks, and [branch separation](RELEASE_PLAN_1.22_1.23.md) for preserved
loop work. This draft does not authorize publication.
