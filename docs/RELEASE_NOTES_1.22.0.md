# FrameShift 1.22.0 — Multilingual update

Release date: 8 November 2026. Find Loop is reserved for 1.23.

## Short release note

- Adds Simplified Chinese, Spanish, Brazilian Portuguese and Russian alongside English, with translated application interfaces, installer and READMEs.
- Special thanks to [@zhazhayu01](https://github.com/zhazhayu01/FrameShift) for the original Simplified Chinese translation, Chinese README and narrow-window layout fix, adapted and integrated into this release.
- Changing the language in Settings also updates installed Explorer context menus, requesting administrator permission when needed. The application language takes effect after restarting.
- Includes general stability fixes and security updates to bundled .NET, FFmpeg and FFprobe dependencies.

## Preparation details

- **Dependency security:** bundled .NET/Windows Desktop 8.0.33, PKCS 8.0.2, and
  FFmpeg/FFprobe from the pinned 29 October build, including the MP4 encryption
  metadata and HDR10+ fixes. The build SDK is 8.0.427.
- **English, simplified Chinese, Spanish, Brazilian Portuguese and Russian:** one application and installer, translated
  settings, tools, progress and errors, with optional Explorer menus in the
  installer's selected language. Settings also synchronizes installed Explorer
  captions; machine-wide changes request administrator authorization. The application
  preference applies on next launch.
  Failed preference saves leave Explorer menus unchanged and restore the saved selection.
  Chinese translation and narrow-window corrections originate from
  [zhazhayu01](https://github.com/zhazhayu01/FrameShift).
- **Future translations:** a shared language catalog supplies Settings and language
  resolution; resource files are included automatically in the subtitle worker.
  Version 1.22 ships English, simplified Chinese, neutral Spanish (`es`), Brazilian
  Portuguese (`pt-BR`) and neutral Russian (`ru`), including the installer and five
  linked READMEs. Regional Russian preferences use the Russian translation. Regional
  Spanish preferences use the neutral Spanish translation.
- **Media integrity and cancellation:** reject unsupported multichannel separation,
  preserve transparent upscale pixels and existing write-probe files, propagate
  cancellation to FFmpeg and interrupt native noise-removal inference.
- **Memory:** bound PDF image/history/clipboard residency, stream audio and subtitle
  windows, and validate image dimensions before decoding.
- **Responsiveness:** batch queue changes, cancel folder imports and reuse successful
  upscale tile sizes instead of retrying known failures.
- **Model downloads:** protect model paths, coordinate concurrent instances, enforce
  disk/size/inactivity limits, verify checksums and preserve existing files when a
  replacement fails or is cancelled.
- **Local communications:** restrict pipes to the current Windows account/session,
  bound messages and process captures, and fail oversized structured output explicitly.

See [qualification](RELEASE_QUALIFICATION_1.22.0.md) for the exact candidate and its
remaining checks, and [branch separation](RELEASE_PLAN_1.22_1.23.md) for preserved
loop work. The owner accepted the manual-test scope and authorized publication on 8 November 2026.
