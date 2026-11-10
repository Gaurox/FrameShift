# FrameShift 1.23.0 — Find Loop and Explorer menu fix

Release date: 10 November 2026.

This release adds video loop creation in **Cut Video** and fixes context menu display in Windows Explorer.

### Find Loop

- Search for natural loops within a video, with a list of candidates and an indication of estimated seam quality.
- Preview the junction or the entire loop silently before exporting.
- Create forward/backward (*ping-pong*) loops, with playback speed and repetition options.
- Adjust the minimum duration and search sensitivity.
- Export one loop or all candidates as MP4 files next to the source, with unique filenames.
- Use native frame timestamps to respect the selected boundaries, including for variable-frame-rate videos.
- Convert supported HDR sources to SDR BT.709 for loop export.

Loop detection runs locally. Suggested seams are estimates: preview the results before exporting.

### Context menu fix

The previous video context menu could hide installed actions: **Remove Audio**, **Resize Video**, and **Rotate / Flip Video**.

The fix now displays all installed video actions in the classic FrameShift menu without truncation. First-frame, last-frame, and keyframe extraction are also directly accessible.

The new features are available in the five existing languages: **English, Simplified Chinese, Spanish, Brazilian Portuguese, and Russian**.

*The classic context menu fix applies to Windows x64. After an update, you may need to restart Windows Explorer or Windows to refresh the menus.*
