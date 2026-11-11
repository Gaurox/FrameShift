# FrameShift 1.24.0 — Balance volume

Local installer candidate prepared on 11 November 2026. Not yet published.

## More comfortable listening

**Balance volume** reduces the difference between quiet dialogue and loud action
or music, helping you watch a film without constantly adjusting the volume.
It works across the whole audio mix and is also available for standalone audio.

Choose from three intensities:

- **Light**: gentle balancing that preserves more dynamics.
- **Moderate** (default): recommended for everyday listening.
- **Strong / Night mode**: stronger balancing for listening at low volume.

Processing runs locally in a **single FFmpeg pass**. Only the audio is re-encoded;
video is copied without re-encoding. Multiple audio tracks, stereo/surround
layouts, subtitles, chapters and metadata are retained.

The action is available in the main window and through the selected Explorer
context-menu components. Outputs are saved next to the original with unique
names. The original file is preserved.

All new controls and installer entries are available in the five existing
languages: English, Simplified Chinese, Spanish, Brazilian Portuguese and Russian.

This feature balances loudness; it does not separate voices from background music.
Processing time depends on the audio tracks and disk speed, since the video still
needs to be copied.
