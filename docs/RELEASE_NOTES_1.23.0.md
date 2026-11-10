# FrameShift 1.23.0 — Find Loop

**Unreleased private development candidate.** Prepared on 9 November 2026.
Development continues on `codex/integrate-find-loop-1.23`; this version number
does not indicate final acceptance or authorization to publish.

- Find natural video loops inside Cut Video, compare candidate junctions and
  preview them before export. Search coverage and resource limits are reported;
  results remain estimates requiring visual review.
- Create silent forward/backward loops, with playback speed and repetition options.
- Keep the taller Cut window, and size the standard Find Loop view to its
  contents before advanced options are opened. Refit when the first candidates
  appear and when advanced options open or close. Keep the preview and footer usable within the available
  screen area, scrolling options when necessary. Reduce movement/flicker when
  selecting candidates or updating previews; selecting the same candidate again
  preserves its current preview instead of restarting it.
- Use native frame timestamps, silent junction/full previews, bounded temporary
  storage and individual queued candidate exports. Outputs remain adjacent to
  sources with unique names.
- Convert supported HDR/wide-gamut sources to SDR BT.709 for loop output, with
  explicit color-metadata/filter validation.
- Translate all Find Loop display text into English, simplified Chinese, Spanish,
  Brazilian Portuguese and Russian, preserving the published 1.22 translations,
  language behavior and servicing fixes.
- Restore the complete classic Explorer video menu on native x64 Windows, with
  specific-frame extractions at the same level. Use the same C# Native AOT
  handler for classic and AI menus on video, audio and images, plus PDF OCR,
  inserting FrameShift then FrameShift AI together. Empty groups are omitted.
  Preserve selected installer components and all five languages; Find Loop
  remains inside Cut Video. The owner confirmed the video-only candidate's
  complete menu on MP4; the unified candidate's Explorer/upgrade/uninstall
  acceptance is pending.
  Replacing a previously loaded Shell build may require restarting its host or
  Windows. Arm64 systems retain the previous static menu implementation.

This candidate uses the existing .NET 8 self-contained win-x64 and Inno Setup
workflow. Public READMEs and the website continue to describe published 1.22.0.
See [private qualification](RELEASE_QUALIFICATION_1.23.0.md) and the
[integration plan](RELEASE_PLAN_1.22_1.23.md) for build evidence and remaining work.

Interactive Windows/DPI/keyboard and installation acceptance, perceptual review
and broader real HDR footage qualification remain open. No release date is set.
