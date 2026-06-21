# Upscale Video — Implementation Reference

Status: **implemented in FrameShift 1.14.0**, optimized in **1.15.0**.

## User flow

- Action: `upscale-video`
- Explorer: `FrameShift AI → Upscale Video 4x`
- Picker: model + x2 / x3 / x4 or custom aspect-locked size
- Output: adjacent, unique, original container
- CLI:

```text
FrameShift.exe --action upscale-video --upscale-model <id> --upscale-scale 2|3|4 <video>
```

## Models

Hosted in `Gaurox/frameshift-models/upscale-video-onnx/`:

| ID | Model | SHA256 |
|---|---|---|
| `realesr-general-x4v3` | Real-ESRGAN General v3 (default) | `DBB0561758E0727C76B7BF6B539A988D3B3050D51F01DCF60C842DE6E6ADD349` |
| `realesr-animevideov3` | Real-ESRGAN AnimeVideo v3 | `6CB9454787F6B0948CB1C25BE0C7DA797AD83EC2696FC005CA4C967217B9CD77` |
| `realesr-animevideov3-x2` | Real-ESRGAN AnimeVideo v3 (FrameShift x2 execution variant) | `B3C1B93492C7BE8CA2C7B2EEADCA311BF1ADC709C296AA3AD14ABFA7890E4A44` |
| `realesr-animevideov3-x3` | Real-ESRGAN AnimeVideo v3 (FrameShift x3 execution variant) | `D3268B927AD1AEA8DBE24790CB6044714FFE3CA7D70C3A1D204063CDBA994B92` |
| `realesrgan-x4plus-video` | Real-ESRGAN x4plus Quality | `37651C96722D0156AAEE27404F31FBB62E93B4AB4AB9E9DB07DA2200500232AC` |

All five use BSD-3-Clause. The HF folder contains its own README and licence.

Image models remain isolated in `upscale-image-onnx/`. The former `upscale-onnx/` path is retained
only for compatibility with FrameShift 1.0.12/1.0.13. Valid local legacy files are copied into the
new action-specific folders.

## Architecture

- Shared with Upscale Image: catalogue, SHA256 downloader, ONNX frame processor, adaptive tiling,
  x4 native inference and Lanczos resizing.
- Video-specific: `UpscaleVideoAction`, `UpscaleVideoSettings`, `VideoUpscaleEngine`, picker and batch
  definition.
- Anime-specific optimization: one visible `AnimeVideo v3` picker entry, but x2/x3 requests are
  auto-routed to dedicated ONNX execution variants so the old CPU-side downscale step is skipped.
- ONNX: DirectML first, CPU fallback; one session reused for all frames, with reusable inputs/buffers in `UpscaleFrameProcessor`.
- FFmpeg: default in-memory `rawvideo` decode → process → encode path; BMP extraction/reconstruction retained as automatic fallback.
- Encoding: NVENC → CPU fallback; incompatible audio is transcoded.
- Cancellation: FFmpeg termination, partial-output deletion and temporary-directory cleanup.

## Assets

- `Assets/Icons/ai/upscale_video.ico`
- `Assets/Icons/ai/upscale_video.png`
- `screenshots/Gif_demos/demo_upscale_video.gif`
- `screenshots/Video_demos/demo_upscale_video.mp4`

## Validation baseline

- paths with spaces and accents;
- source FPS, duration and audio preserved;
- x2 / x3 / x4 and unique naming;
- DirectML and CPU fallback;
- cancellation and temporary cleanup;
- MP4, MKV, WebM and AVI;
- multi-file batch and Explorer picker.
- optimized path validated on the default `rawvideo` pipeline, with automatic fallback kept for compatibility.
