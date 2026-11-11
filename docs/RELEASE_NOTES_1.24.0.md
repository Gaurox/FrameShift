# FrameShift 1.24.0 — Balance volume

Released on 11 November 2026 after owner acceptance.
Publication: [v1.24.0](https://github.com/Gaurox/FrameShift/releases/tag/v1.24.0).
The [published English announcement](RELEASE_ANNOUNCEMENT_1.24.0.md) follows the owner's approved release text.

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

The application, subtitle worker, Explorer captions and installer offer eight
languages: English, Simplified Chinese, Spanish, Brazilian Portuguese, Russian,
French, Japanese and Indonesian. All three new catalogs are technically complete;
the owner accepted the tested installer scope, as recorded in the qualification report.

The language selected during installation or an update now applies to the
application windows on the next launch, including when an older language was
saved in Settings. Each installation choice is applied once per Windows user;
later changes in Settings remain available. Other UI preferences are preserved.
Balance volume also has its own transparent Windows icon at multiple sizes.

This feature balances loudness; it does not separate voices from background music.
Processing time depends on the audio tracks and disk speed, since the video still
needs to be copied.
