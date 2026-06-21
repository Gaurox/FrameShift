# FrameShift website asset note

The `screenshots/` folder in this repository is the single source of truth for FrameShift visuals used in:

- this repository README
- the public page at `https://gaurox.dev/frameshift/`

Upscale Video assets:

- `screenshots/Gif_demos/demo_upscale_video.gif` — README/web demonstration;
- `screenshots/Video_demos/demo_upscale_video.mp4` — source video demonstration.

Do not maintain a separate edited screenshot set inside the website repo.

## Sync target

Source:

- `E:\AI\FrameShift_V1\screenshots`

Website copy:

- `E:\AI\Gaurox_Website\frameshift\screenshots`

## Sync method

Run the shared manual sync script:

- `E:\AI\sync-gaurox-website-screenshots.ps1`

The script clears the website screenshot copy for FrameShift and recopies it from this repository.

## Rule for agents

- Update screenshots here first.
- If you modify the FrameShift website page or its screenshot usage, always run `E:\AI\sync-gaurox-website-screenshots.ps1` before finishing the task.
- Then run the sync script.
- Do not edit `E:\AI\Gaurox_Website\frameshift\screenshots` by hand unless there is a one-off emergency fix.
