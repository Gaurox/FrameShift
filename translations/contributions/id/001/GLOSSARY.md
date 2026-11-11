# FrameShift translation glossary

This is an English source glossary, not a new interface language. A native reviewer
should agree on consistent equivalents in each existing language. Flag ambiguous
cases by Scope/Key instead of applying a global word replacement.

| Term | Meaning in FrameShift |
| --- | --- |
| FrameShift, FFmpeg, FFprobe, DirectML, ONNX, Whisper | Product/tool names; preserve spelling. |
| Frame | One video image. In window-layout discussions it may mean a UI border; check context. |
| Window | A desktop dialog, or an audio-analysis time segment in worker messages. |
| Clip | A video/audio segment, not the clipboard. |
| Track / stream | An audio, video or subtitle stream inside a container. |
| Channel | One audio channel, such as left, right or centre; not a complete language track. |
| Source / input | The original file supplied for processing. |
| Output | The new result; outputs normally stay beside the source. |
| Convert | Change the media format or encoding. |
| Re-encode | Encode decoded media again. |
| Stream copy / remux | Copy encoded streams into an output container without re-encoding them. |
| Codec / container | Encoding method / file structure. Keep codec IDs and extensions unchanged. |
| Compress audio / video / image | Reduce file size. Distinguish this from audio dynamic-range compression. |
| Balance volume | Reduce loudness differences across the entire mix; not voice isolation or file-size compression. |
| Light / Moderate / Strong | Balancing intensity. Moderate is the default; CLI IDs remain light/moderate/strong. |
| Night mode | Stronger balancing intended for listening at low volume. |
| Trim / cut | Select temporal boundaries. |
| Crop | Remove image borders or select a spatial region. |
| Resize | Change pixel dimensions. |
| Upscale | Increase resolution; may use a local AI model. |
| Interpolate | Generate intermediate video frames. |
| Frame rate / FPS | Frames per second, not resolution. |
| Bitrate | Bits per second, not file size or sample rate. |
| Sample rate | Audio samples per second. |
| Loop / junction / seam | Repeating segment / meeting point between end and start. |
| Candidate | A proposed loop to preview; quality estimates are not guarantees. |
| Forward/backward / ping-pong | Playback forward and then backward. |
| Subtitle | Timed text; translating the UI does not change transcription language. |
| Burn subtitles | Permanently render text into video images. |
| Preset | A named group of processing settings. |
| Queue / batch | Pending jobs / processing several inputs. |
| Cancel / remove | Stop work / remove a queue item; neither should imply deleting the original source. |
| Model | A local AI model, not a visual template. Preserve model IDs. |

Keep units and technical IDs accurate. `%n` is an installer line-break token;
`{0}` is a .NET variable. `CPU` and `GPU` identify execution hardware and are not
translation choices. Context and the actual operation take priority over this
short glossary when a word has multiple meanings.
