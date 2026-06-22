# Add Subtitles to Video

Active notes for `add-subtitles-video`.

## Active scope

- single product action
- input video stays `InputPath`
- external subtitle file is provided through options (`subtitle-file`)
- supported modes:
  - `SelectableTrack`
  - `BurnIntoVideo`
- supported external subtitle inputs:
  - `SelectableTrack` -> `.srt`
  - `BurnIntoVideo` -> `.srt`, `.ass`, `.frameshift-subtitles.json`
- no subtitle text / timeline editor in this lot
- `BurnIntoVideo` now includes a visual editor with fixed-frame preview and short animated preview

## Current behavior

The action now supports two execution paths.

### `SelectableTrack`

Adds an external SRT file as a selectable subtitle track while preserving existing video and
audio streams without re-encoding in the normal case.

Container strategy:

- source `.mkv` -> output `.mkv`, new subtitle track encoded as `subrip`
- source `.mp4`, `.mov`, `.m4v` -> same container with `mov_text` when existing streams stay compatible
- fallback `.mkv` -> used when the source container is not retained for this workflow or when
  existing subtitle / data / attachment streams cannot be preserved cleanly in `MP4/MOV`

## Stream preservation rules

The action maps:

- all source streams from input `0`
- the new subtitle track from input `1:0`

Then it applies:

- `-c copy` for existing streams
- subtitle override only for the added track in `MKV`
- `-c:s mov_text` for `MP4/MOV` outputs so existing text subtitle streams and the new SRT track
  converge to a compatible format

Metadata and chapters are preserved with:

- `-map_metadata 0`
- `-map_chapters 0`

### `BurnIntoVideo`

Burns subtitles directly into the video image with video re-encoding.

Subtitle input strategy:

- `.ass` -> style passthrough, copied to a temporary working `.ass` path before FFmpeg so Windows paths with spaces / accents / special characters stay reliable
- `.srt` -> parsed into `SubtitleProject`, then converted to a temporary `.ass`
- `.frameshift-subtitles.json` -> deserialized with `CreateSubtitlesProjectSerializer`, then converted to a temporary `.ass`

Temporary ASS generation:

- reuses the shared `SubtitleProject` model and `CreateSubtitlesAssFormatter`
- adapts `PlayResX`, `PlayResY`, font size and margins to the probed display resolution, including rotation metadata
- accepts visual overrides for font, size, text color, highlight color, outline color, shadow color, outline thickness, shadow depth, vertical position and vertical margin
- supports the shared ASS presets `Classic`, `Word Highlight`, `Progressive Reveal` for `SRT` and `FrameShift` project inputs
- the shared dynamic presets respect the refined shared cue display start: nothing is shown during the preceding silence; `Word Highlight` shows the full sentence immediately at the first useful word; `Progressive Reveal` stays progressive
- reads `.srt` text with UTF-8 first and falls back to the local Windows code page when needed
- does not change the default `Create Subtitles` export behavior

Burn pipeline:

- output container stays the same as the source video
- video is re-encoded with the existing shared conversion defaults for that container
- audio is copied when the existing codec is cleanly compatible with the output container
- otherwise audio is re-encoded with the shared container default
- compatible subtitle streams already present in the source are still preserved according to the container rules from `VideoConversionPlanner`
- generated temporary `.ass` files are always deleted after the run

### Burn editor UI

The burn path now opens a dedicated DPI-safe editor window built with shared FrameShift helpers.

Available in this lot:

- real frame preview rendered from the current video through `FFmpeg` / `libass`
- simple time navigation with a slider
- short animated preview loop rendered from a temporary burn clip around the current position
- font selection
- font size
- text, highlight, outline and shadow colors
- outline and shadow thickness
- vertical position and vertical margin
- ASS preset selection for `SRT` and `FrameShift` project inputs
- debounced preview refresh with cancellation of obsolete renders
- cleanup of obsolete preview files, canceled preview renders and temporary clip / GIF artifacts
- compatibility warning when the selected font is missing locally
- compatibility warning when the source looks HDR because burn-in re-encoding may alter colorimetry

Behavior with external `.ass`:

- preview still uses the external file
- the existing ASS style is preserved as-is
- style controls and preset selection are disabled with an explicit message
- the working copy still goes through a temporary `.ass` path so FFmpeg/libass are insulated from the original Windows path quirks

## CLI

```text
FrameShift.exe --action add-subtitles-video --subtitle-file "C:\path\track.srt" "C:\path\video.mp4"
FrameShift.exe --action add-subtitles-video --subtitle-mode burn --subtitle-file "C:\path\track.ass" "C:\path\video.mp4"
```

Accepted aliases:

- `--subtitle-file`
- `--subtitle-path`
- `--srt-file`
- `--subtitle-mode`
- `--subtitles-mode`

Additional burn styling flags:

- `--subtitle-font`
- `--subtitle-size`
- `--subtitle-color`
- `--subtitle-highlight-color`
- `--subtitle-outline-color`
- `--subtitle-shadow-color`
- `--subtitle-outline`
- `--subtitle-shadow`
- `--subtitle-position`
- `--subtitle-margin-v`

Mode values:

- `selectable`
- `selectable-track`
- `burn`
- `burn-into-video`

If the required subtitle options are not provided, the launcher opens a minimal DPI-safe picker to
choose both the mode and the subtitle file.

## Product integration

- registered as a standard video action in `ActionRegistry`
- launched through `FrameShift.exe --action add-subtitles-video`
- uses the shared launcher fallback path in `Program` / `ProgramPickersSubtitles`
- available from the Explorer video context menu as `Add subtitles to video`
- shipped through a dedicated installer component under video actions
- installs the required bundled `ffmpeg.exe` and `ffprobe.exe` dependencies with that component

## Output rules

- output is created next to the source video
- no overwrite
- shared unique naming via `_subtitled`, `_subtitled_burned`, `_001`, `_002`, ...
- partial outputs are deleted on failure or cancellation

## Explicit non-goals for this lot

- advanced ASS styling editor beyond the current burn controls
- subtitle text / timeline editing
