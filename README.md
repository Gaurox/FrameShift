# FrameShift

<p align="center">
  Windows-first offline media processing built for fast right-click workflows.
</p>

<p align="center">
  FFmpeg and FFprobe powered. Local only. Lightweight WinForms UI. Clean output handling.
</p>

<p align="center">
  <img src="screenshots/Gif_demos/demo_menus_gif.gif" alt="FrameShift context menu workflow demo" width="900" />
</p>

## Overview

FrameShift is a desktop utility for fast video, audio, image, and AI-assisted media tasks on Windows.  
Its main goal is simple: let you launch useful actions directly from Explorer context menus, make the right adjustments quickly, and save the result next to the source file with safe unique naming.

Remove backgrounds locally with a focused AI workflow directly launched from Explorer.

<p align="center">
  <img src="screenshots/Gif_demos/demo_remove_bg_gif.gif" alt="FrameShift remove background demo" width="900" />
</p>

Build image-based PDF documents with a visual layout workflow designed for quick adjustments.

<p align="center">
  <img src="screenshots/Gif_demos/Demo_image.to.pdf.gif" alt="FrameShift image to PDF demo" width="900" />
</p>

## Why FrameShift

- Right-click driven workflow for everyday media tasks
- Offline processing with local tools only
- No overwrite behavior, outputs stay next to source files
- FFmpeg and FFprobe at the core
- Focused WinForms interface built for speed and clarity
- Clean cancellation and practical batch-friendly flows

## Quick Workflow

1. Right-click a file in Explorer.
2. Choose a FrameShift action from the context menu.
3. Adjust only the settings you need.
4. Start processing and get a clean output beside the original file.

## Actions

### Video Actions

| Action | Short Description | Screenshot |
| --- | --- | --- |
| Convert Video | Convert video files to common formats with a fast, focused workflow. | <img src="screenshots/Video_actions/convert_video.png" alt="Convert Video" width="320" /> |
| Remove Audio | Create a silent video version without changing the basic workflow. | <img src="screenshots/Video_actions/convert_video.png" alt="Remove Audio" width="320" /> |
| Extract Frames | Export video frames as image sequences for review, editing, or assets. | <img src="screenshots/Video_actions/Create_gif.png" alt="Extract Frames" width="320" /> |
| Create GIF | Turn a video segment into a GIF with preview-oriented controls. | <img src="screenshots/Video_actions/Create_gif.png" alt="Create GIF" width="320" /> |
| Extract Audio | Pull the audio track from a video into a standalone file. | <img src="screenshots/Video_actions/Extract_audio.png" alt="Extract Audio" width="320" /> |
| Cut Video | Trim a video to the exact segment you want. | <img src="screenshots/Video_actions/Cut_video.png" alt="Cut Video" width="320" /> |
| Crop Video | Remove unwanted borders or focus on a specific video area. | <img src="screenshots/Video_actions/Crop_video.png" alt="Crop Video" width="320" /> |
| Rotate / Flip Video | Fix orientation issues or mirror video content quickly. | <img src="screenshots/Video_actions/Resize_video.png" alt="Rotate or Flip Video" width="320" /> |
| Resize Video | Change video dimensions for sharing, compatibility, or lighter exports. | <img src="screenshots/Video_actions/Resize_video.png" alt="Resize Video" width="320" /> |
| Compress Video | Reduce file size with practical quality-oriented presets. | <img src="screenshots/Video_actions/compress_video.png" alt="Compress Video" width="320" /> |
| Change Video Speed | Speed up or slow down a video with a simple adjustment flow. | <img src="screenshots/Video_actions/Change_video_speed.png" alt="Change Video Speed" width="320" /> |
| Interpolate Video | Generate smoother motion and higher frame-rate playback. | <img src="screenshots/Video_actions/Interpolate_video.png" alt="Interpolate Video" width="320" /> |
| Media Info | Inspect technical media details directly from the FrameShift workflow. | <img src="screenshots/Video_actions/Media_info_video.png" alt="Media Info" width="320" /> |

### Audio Actions

| Action | Short Description | Screenshot |
| --- | --- | --- |
| Convert Audio | Convert audio files to other formats with a clean minimal flow. | <img src="screenshots/Audio_actions/convert_audio.png" alt="Convert Audio" width="320" /> |
| Cut Audio | Trim audio precisely without leaving the right-click workflow. | <img src="screenshots/Audio_actions/Cut_audio.png" alt="Cut Audio" width="320" /> |
| Reverse Audio | Reverse an audio file for sound design or quick experiments. | <img src="screenshots/Audio_actions/Cut_audio.png" alt="Reverse Audio" width="320" /> |
| Compress Audio | Reduce audio size for easier sharing and storage. | <img src="screenshots/Audio_actions/Compress_audio.png" alt="Compress Audio" width="320" /> |
| Change Pitch | Shift audio pitch with a straightforward adjustment interface. | <img src="screenshots/Audio_actions/Change_pitch.png" alt="Change Pitch" width="320" /> |
| Change Audio Speed | Speed up or slow down audio while keeping the workflow simple. | <img src="screenshots/Audio_actions/Change_audio_speed.png" alt="Change Audio Speed" width="320" /> |

### Image Actions

| Action | Short Description | Screenshot |
| --- | --- | --- |
| Convert Image | Convert images between practical everyday formats. | <img src="screenshots/Image_actions/Convert_Image.png" alt="Convert Image" width="320" /> |
| Compress Image | Reduce image size while keeping the process quick and readable. | <img src="screenshots/Image_actions/Compress_image.png" alt="Compress Image" width="320" /> |
| Convert to Icon | Build `.ico` files from images with multi-size export support. | <img src="screenshots/Image_actions/ConvertToIcon.png" alt="Convert to Icon" width="320" /> |
| Crop Image | Crop images visually with direct manipulation controls. | <img src="screenshots/Image_actions/Crop_image.png" alt="Crop Image" width="320" /> |
| Resize Image | Resize images for web, sharing, or asset preparation. | <img src="screenshots/Image_actions/resize_image.png" alt="Resize Image" width="320" /> |
| Rotate / Flip Image | Correct image orientation or mirror an image in a few clicks. | <img src="screenshots/Image_actions/Rotate_image.png" alt="Rotate or Flip Image" width="320" /> |
| Image to PDF | Assemble one or more images into a PDF document. | <img src="screenshots/Image_actions/Image_to_pdf.png" alt="Image to PDF" width="320" /> |

### AI Actions

| Action | Short Description | Screenshot |
| --- | --- | --- |
| Remove Background | Remove the background from an image with the local AI workflow. | <img src="screenshots/Gif_demos/demo_remove_bg_gif.gif" alt="Remove Background" width="320" /> |
| Audio Separation | Split audio into vocals, drums, bass, other, and instrumental with the local AI workflow. | <img src="screenshots/AI_actions/Audio_separation.png" alt="Audio Separation" width="320" /> |

## Built For

- Fast everyday media conversions
- Creator and editor utility workflows
- Clean Windows Explorer integration
- Offline processing environments
- Users who want practical tools instead of heavy software

## Technology

- C#
- .NET 8 LTS
- WinForms
- FFmpeg
- FFprobe
- Optional local AI components with ONNX Runtime DirectML

## Project Structure

```text
src/FrameShift/
installer/
docs/
tests/
```

## Documentation

- [Documentation Index](docs/README.md)
- [Project Overview](docs/PRODUCT_GUIDE.md)
- [Project Rules](docs/PROJECT_RULES.md)
- [Architecture Freeze](docs/ARCHITECTURE_FREEZE.md)
- [Migration Plan](docs/MIGRATION_PLAN.md)
- [Changelog](docs/CHANGELOG.md)
- [Code File Index](docs/CODE_FILE_INDEX.md)

## Notes

- FrameShift is distributed under the GNU GPL v3.0. See [LICENSE](LICENSE).
- Third-party components and optional AI model notices are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
- `references/` is reference-only.
- Active runtime resources must stay inside the real FrameShift project tree.
- Before rebuilding the installer, publish the app so the setup packs the latest release payload.

```powershell
.\build_installer.ps1
```
