# Balance volume

Balances quiet and loud passages across the entire audio mix. Available for
video and audio files from the main window and the Explorer context menu
(after installing a build with the corresponding component selected).

- **Light**: gentle balancing, preserving more dynamics.
- **Moderate** (default): recommended for everyday listening.
- **Strong / Night mode**: stronger balancing for listening at low volume.

The picker applies one intensity to all selected files. Every audio track is
processed; channel layouts remain intact. The feature does not isolate speech.

## Processing

One FFmpeg processing pass, following the usual FFprobe metadata check. A linked
compressor attenuates loud passages, a dynamic normalizer raises quieter passages
with a capped gain and a silence threshold, and a limiter controls output peaks.
No preliminary loudness scan, intermediate audio file, or video encoding is needed.
Only audio is re-encoded; video, subtitles, attachments, chapters and metadata are
copied. Audio codecs are selected for compatibility with the original container:
AAC for MP4/MKV/MOV/M4V/M4A/AAC, Opus for WebM, AC-3 for AVI (including 5.1), MP3 for MP3, Vorbis for OGG,
FLAC for FLAC, PCM 16-bit for WAV, and WMA v2 for WMA.

Output: `<source>_balanced_moderate.<extension>` beside the source, with `_001`,
`_002`, etc. on collision. Cancellation or failure removes the unpublished output
workspace through the standard output lifecycle. Processing speed depends on the
audio tracks, codecs and disk throughput; the video is still read and copied.

## Command line

```powershell
FrameShift.exe --action balance-volume --volume-intensity moderate "C:\Films\Film été.mkv"
```

Intensity values: `light`, `moderate`, `strong`. Omitting the option opens the
picker; the core action defaults to `moderate` when called without options.

Automated validation covers measured reduction of dynamic range across the three
presets, video packet hashes, track and chapter preservation, stereo/5.1 channels,
container compatibility, accented paths with spaces, collisions, failures, and
global/current-item cancellation. Listening on representative films remains useful
for judging the preferred intensity and subjective sound quality.
