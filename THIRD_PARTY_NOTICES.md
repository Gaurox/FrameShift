# Third-Party Notices

This project is licensed under the GNU General Public License v3.0.  
See [LICENSE](LICENSE) for the full project license text.

Third-party components keep their own licenses. The main project license does not replace or rewrite those upstream licenses.

Every release carries this document as `licenses/THIRD_PARTY_NOTICES.md`. The
same directory contains the FrameShift GPLv3 text (`licenses/LICENSE`) and the
additional static native-worker license texts identified below.

---

## FFmpeg and FFprobe

FrameShift bundles `ffmpeg.exe` and `ffprobe.exe` as part of its installer and local toolchain.

- Build: [`9.0-essentials_build-www.gyan.dev`](https://www.gyan.dev/ffmpeg/builds/) by Gyan Doshi (release build, 4 August 2026)
- Distribution archive: [`ffmpeg-release-essentials.zip`](https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip), SHA-256 `E6B54767A6065919048F1A098EB27211CA4E12B4348A05D88777A5855D0B6E71`
- Upstream project: [FFmpeg 9.0 "Lei"](https://ffmpeg.org/download.html), source commit [`d32b387f2b`](https://github.com/FFmpeg/FFmpeg/commit/d32b387f2b)
- Bundled binary SHA-256: `ffmpeg.exe` `227AF0691433B703FFC5725E47F7D06EEFC34B4A72E7870E73D30E2CDA483ECF`; `ffprobe.exe` `901F0EFE4793CBB0F017101E3427F816E8FBF9A407BD585F49DF30F4325CFD88`
- Configuration verified from both binaries: static GPLv3 build with `--enable-gpl --enable-version3`, including FrameShift-required codecs/libraries such as libx264, libx265, libvpx, libopus, libmp3lame, libwebp and libass.
- License: **GNU General Public License v3.0 or later** (build compiled with `--enable-gpl --enable-version3`)
- Source code: https://ffmpeg.org/download.html

This build includes components licensed under LGPL and GPL. Because this build was compiled with `--enable-gpl`, the binary as a whole is governed by the GPL v3+. FrameShift is itself distributed under GPL v3, which is compatible with this requirement.

In compliance with the GPL, users who wish to obtain the corresponding source code for the bundled FFmpeg build may refer to the upstream source repository listed above, or to the build provider (www.gyan.dev).

---

## .NET Runtime

The self-contained `win-x64` publish includes the .NET 8 Windows Desktop runtime.

- Source: https://github.com/dotnet/runtime and https://github.com/dotnet/wpf
- License: **MIT License**

---

## NAudio

- NuGet package: `NAudio` version 2.2.1
- Source: https://github.com/naudio/NAudio
- License: **MIT License**
- Copyright: Copyright © Mark Heath and Contributors

---

## PDFsharp

- NuGet package: `PDFsharp` version 6.2.4
- Source: https://github.com/empira/PDFsharp
- License: **MIT License**
- Copyright: Copyright © empira Software GmbH

---

## SixLabors.ImageSharp

- NuGet package: `SixLabors.ImageSharp` version 3.1.12
- Source: https://github.com/SixLabors/ImageSharp
- License: **Six Labors Split License**
  - Apache 2.0 for open-source projects (OSI-approved license)
  - Commercial license required for closed-source or commercial use
- FrameShift is distributed under GPL v3 (an OSI-approved open-source license), so the Apache 2.0 branch of the Split License applies.

Full license text: https://github.com/SixLabors/ImageSharp/blob/main/LICENSE

---

## ONNX Runtime DirectML

- NuGet package: `Microsoft.ML.OnnxRuntime.DirectML` version 1.24.4
- Source: https://github.com/microsoft/onnxruntime
- License: **MIT License**
- Copyright: Copyright © Microsoft Corporation

---

## Remove Background Models

FrameShift's optional `Remove Background` feature downloads one of the following models at runtime:

### BiRefNet Lite FP16 (Fast)

- Model: `BiRefNet Lite FP16`
- Source URL used by the application:
  `https://huggingface.co/Gaurox/frameshift-models/resolve/main/birefnet_lite-onnx/model_fp16.onnx`
- Upstream model repository: https://huggingface.co/onnx-community/BiRefNet_lite-ONNX
- License: **MIT License**

### BiRefNet HR Matting (High Resolution)

- Model: `BiRefNet_HR-matting-epoch_135.onnx`
- Source URL used by the application:
  `https://huggingface.co/Gaurox/frameshift-models/resolve/main/birefnet_hr-matting-onnx/BiRefNet_HR-matting-epoch_135.onnx`
- Upstream model card: https://huggingface.co/ZhengPeng7/BiRefNet_HR-matting
- Upstream repository and release source: https://github.com/ZhengPeng7/BiRefNet
- License: **MIT License**
- Runtime note in FrameShift `1.0.11`: this mode is currently executed on **CPU only**

### BiRefNet HR General (High Resolution)

- Model: `BiRefNet_HR-general-epoch_130.onnx`
- Source URL used by the application:
  `https://huggingface.co/Gaurox/frameshift-models/resolve/main/birefnet_hr-general-onnx/BiRefNet_HR-general-epoch_130.onnx`
- Upstream model card: https://huggingface.co/ZhengPeng7/BiRefNet_HR
- Upstream repository and release source: https://github.com/ZhengPeng7/BiRefNet
- License: **MIT License**
- Runtime note in FrameShift `1.0.11`: this mode is currently executed on **CPU only**

These models are not stored in the repository and are downloaded on demand into the local user profile.

### BRIA RMBG-2.0 (BRIA Balanced / BRIA High Quality) — user-supplied

- Models: `model_fp16.onnx` (~500 MB, "BRIA Balanced") and `model.onnx` (~1 GB, "BRIA High Quality")
- Official source the user must visit: https://huggingface.co/briaai/RMBG-2.0/tree/main
- License: **BRIA RMBG-2.0 — CC BY-NC 4.0 (non-commercial use only)**
- **FrameShift does NOT distribute, bundle, mirror, host or download these models.** They are
  user-supplied: the user must obtain the file manually from BRIA's official page above, review
  BRIA's documentation and licensing, and place it in the corresponding local model folder.
- FrameShift verifies the file against the official BRIA file's content SHA256, cross-checked
  against the exact official byte size (verified 2026-06-03). On mismatch FrameShift directs the
  user back to the official BRIA page rather than redistributing anything.
- BRIA support is optional, off by default, and intended for non-commercial use only.
  No commercial compatibility is claimed.

---

## Remove Noise Model

FrameShift's optional `Remove Noise` feature downloads the following model at runtime:

- Model: `DeepFilterNet3 ONNX` (enc, erb_dec, df_dec, config)
- Source URL used by the application:
  `https://huggingface.co/Gaurox/frameshift-models/resolve/main/deepfilternet3_onnx/`
- Upstream project: https://github.com/Rikorose/DeepFilterNet
- License: **MIT or Apache 2.0** (dual-licensed upstream)

This model is not stored in the repository and is downloaded on demand into the local user profile.

---

## Audio Separation Models

FrameShift's optional `Audio Separation` feature downloads the following models at runtime:

- CPU model: `HTDemucs V1` (`htdemucs.onnx`)
- GPU model: `HTDemucs V2 Split` (`htdemucs_split.onnx`)
- Source URL used by the application:
  `https://huggingface.co/Gaurox/frameshift-models/resolve/main/htdemucs/`
- Upstream project: https://github.com/facebookresearch/demucs
- License: **MIT License**

These models are not stored in the repository and are downloaded on demand into the local user profile.

---

## Video Interpolation Models (RIFE)

FrameShift's optional `Interpolate Video (RIFE)` feature downloads the following models at runtime:

- Model: `RIFE v4.25 Lite` (`rife_v425_lite.onnx`)
- Model: `RIFE v4.26 x2` (`rife_v426_x2.onnx`)
- Source URL used by the application:
  `https://huggingface.co/Gaurox/frameshift-models/resolve/main/rife/`
- Upstream project: https://github.com/hzwer/ECCV2022-RIFE
- License: **MIT License**

These models are not stored in the repository and are downloaded on demand into the local user profile.

---

## Remove Object Models

FrameShift's optional `Remove Object` feature downloads one of the following models at runtime (user choice via ComboBox).

> **Common data notice**: Both models below are based on LaMa weights trained on the Places2 dataset (MIT CSAIL).
> Places2 terms restrict use to *"non-commercial research and educational purposes only"*.
> The commercial status of weights derived from Places2 data is not formally settled.
> FrameShift is a free, donation-supported application and documents this honestly.

### LaMa FP32 (Quality)

- Model: `LaMa FP32 (Quality)` (`lama_fp32.onnx`, ~208 MB)
- Source URL:
  `https://huggingface.co/Gaurox/frameshift-models/resolve/main/lama-onnx/lama_fp32.onnx`
- ONNX port: [Carve/LaMa-ONNX](https://huggingface.co/Carve/LaMa-ONNX) — **Apache-2.0**
- Original model: [advimman/lama](https://github.com/advimman/lama) — **Apache-2.0**, Copyright © 2021 Samsung Research
- **Weights**: trained on Places2 — commercial use not guaranteed (see notice above)

### LaMa 2025 (Fast)

- Model: `LaMa 2025 (Fast)` (`inpainting_lama_2025jan.onnx`, ~93 MB)
- Source URL:
  `https://huggingface.co/Gaurox/frameshift-models/resolve/main/lama-opencv-onnx/inpainting_lama_2025jan.onnx`
- ONNX optimization: [opencv/inpainting_lama](https://huggingface.co/opencv/inpainting_lama) — **Apache-2.0**, January 2025
- Original model: [advimman/lama](https://github.com/advimman/lama) — **Apache-2.0**, Copyright © 2021 Samsung Research
- **Weights**: trained on Places2 — commercial use not guaranteed (see notice above)

These models are not stored in the repository and are downloaded on demand into the local user profile.

---

## Upscale Image and Video Models

FrameShift's optional `Upscale Image` and `Upscale Video` features download models at runtime. Image
models are hosted under `upscale-image-onnx/`; video models are hosted separately under
`upscale-video-onnx/`. Each directory contains its own README, checksums, provenance and applicable
license texts. The former `upscale-onnx/` folder is retained only for old-release URL compatibility;
current FrameShift builds do not reference it. No model is bundled in the installer or source repository.

### Real-ESRGAN General v3 (video default)

- Model: `realesr_general_x4v3.onnx` (4,867,430 bytes, FP32)
- Exported from the official `realesr-general-x4v3.pth` weights (SRVGGNetCompact, 32 convolution blocks).
- SHA256: `DBB0561758E0727C76B7BF6B539A988D3B3050D51F01DCF60C842DE6E6ADD349`
- Upstream: https://github.com/xinntao/Real-ESRGAN — **BSD-3-Clause**, Copyright © 2021 Xintao Wang

### Real-ESRGAN AnimeVideo v3

- Model: `realesr_animevideov3.onnx` (2,493,430 bytes, FP32)
- Exported from the official `realesr-animevideov3.pth` weights (SRVGGNetCompact, 16 convolution blocks).
- SHA256: `6CB9454787F6B0948CB1C25BE0C7DA797AD83EC2696FC005CA4C967217B9CD77`
- Upstream: https://github.com/xinntao/Real-ESRGAN — **BSD-3-Clause**, Copyright © 2021 Xintao Wang

### Real-ESRGAN AnimeVideo v3 — FrameShift x2/x3 execution variants

- Models: `realesr_animevideov3_x2.onnx` and `realesr_animevideov3_x3.onnx` (2,493,683 bytes each, FP32)
- Derived from the same official `realesr-animevideov3.pth` ONNX export by appending a final in-graph
  x0.5 / x0.75 linear resize, so FrameShift can keep one visible AnimeVideo v3 option while avoiding
  the previous CPU-side downscale step for anime x2/x3 requests.
- SHA256 x2: `B3C1B93492C7BE8CA2C7B2EEADCA311BF1ADC709C296AA3AD14ABFA7890E4A44`
- SHA256 x3: `D3268B927AD1AEA8DBE24790CB6044714FFE3CA7D70C3A1D204063CDBA994B92`
- Upstream: https://github.com/xinntao/Real-ESRGAN — **BSD-3-Clause**, Copyright © 2021 Xintao Wang

### Real-ESRGAN x4plus (image default; video quality option)

- Model: `realesrgan_x4plus_fp16.onnx` (~34 MB, FP16 weights with float32 I/O)
- Hosted as byte-identical copies in both action-specific folders; each copy has the same pinned SHA256.
- Upstream project: https://github.com/xinntao/Real-ESRGAN — **BSD-3-Clause**, Copyright © 2021 Xintao Wang
- License: **BSD-3-Clause** (covers the model weights, not just the code)

### Real-ESRGAN x4plus anime 6B (anime / illustration)

- Model: `realesrgan_x4plus_anime_6b.onnx` (~18 MB, FP32)
- Exported to ONNX from the official `RealESRGAN_x4plus_anime_6B.pth` weights (RRDBNet, 6 blocks).
- Upstream project: https://github.com/xinntao/Real-ESRGAN — **BSD-3-Clause**, Copyright © 2021 Xintao Wang
- License: **BSD-3-Clause**

### Swin2SR real-world x4 (restoration / quality)

- Model: `swin2sr_realworld_x4.onnx` (~54 MB, FP32 transformer)
- Upstream project: https://github.com/mv-lab/swin2sr — base model
  https://huggingface.co/caidas/swin2SR-realworld-sr-x4-64-bsrgan-psnr
- ONNX from https://huggingface.co/onnx-community/swin2SR-realworld-sr-x4-64-bsrgan-psnr-ONNX
- License: **Apache-2.0**

These models are not stored in the repository and are downloaded on demand into the local user profile.

---

---

## Create Subtitle File — sherpa-onnx

FrameShift's optional `Create Subtitle File` feature uses the sherpa-onnx library for local Whisper inference.

### sherpa-onnx NuGet package

- NuGet package: `org.k2fsa.sherpa.onnx` version 1.13.3
- Source: https://github.com/k2-fsa/sherpa-onnx
- License: **Apache License 2.0**
- Copyright: Copyright 2022-2025 Next-gen kaldi authors

### sherpa-onnx-c-api.dll (bundled native DLL, DirectML build)

The worker process bundles a custom build of `sherpa-onnx-c-api.dll` compiled from sherpa-onnx 1.13.3
source with `-DSHERPA_ONNX_ENABLE_DIRECTML=ON` to enable GPU acceleration on Windows.

- Source: https://github.com/k2-fsa/sherpa-onnx (tag v1.13.3)
- License: **Apache License 2.0**
- SHA-256: `BD41BDD1EE47766B11DB2D84174637F6725C8C0894004AF9880176BB93F2B0D8`
- Full notice: `src/FrameShift.SubtitlesWorker/native-dml/THIRD_PARTY_NOTICES.txt`

### onnxruntime.dll + onnxruntime_providers_shared.dll (bundled native DLLs, DirectML build)

The worker process bundles the ORT 1.24.4 DirectML binaries directly (rather than via the NuGet
package already used by the main app) to keep the two ORT stacks isolated.

- Source: Microsoft.ML.OnnxRuntime.DirectML 1.24.4 (win-x64)
- License: **MIT License** — Copyright © Microsoft Corporation
- SHA-256 (onnxruntime.dll): `E7EEDEC6A6F26DC39DC948276A75EF6D2BEE3FFF944D874CEED0BBD3B97BFF40`
- SHA-256 (onnxruntime_providers_shared.dll): `265C8DAF29637CB259CAC8BE9F08F2CD45F3883F0F0E4949CBFDDD5B4CBEC3B6`
- Full notice: `src/FrameShift.SubtitlesWorker/native-dml/THIRD_PARTY_NOTICES.txt`

### DirectML.dll (bundled native DLL)

- Source: Microsoft.AI.DirectML 1.15.4 (bin/x64-win)
- License: **Microsoft DirectML License**
- SHA-256: `9C9E6D822561C6C41B90E6994B3E8857CF1D66DBFB1E0C4C799C7C89B4E92DA1`
- Native-worker notices and the exact DirectML package terms are distributed in
  `licenses/subtitles-worker-native/`:
  `THIRD_PARTY_NOTICES.txt`, `DirectML-LICENSE.txt`, and
  `DirectML-THIRD_PARTY_NOTICES.txt`. The Apache 2.0 text for the custom
  sherpa-onnx native build is distributed there as `APACHE-2.0.txt`.

---

## Create Subtitle File — Whisper Models

FrameShift's optional `Create Subtitle File` feature downloads OpenAI Whisper ONNX models at runtime.

- Models: Whisper Base, Whisper Small, Whisper Large-v3 Turbo (ONNX exports, FP32)
- Hosted at: `https://huggingface.co/Gaurox/frameshift-models/` (subfolders `whisper-base-onnx/`,
  `whisper-small-onnx/`, `whisper-large-v3-turbo-onnx/`)
- Upstream project: https://github.com/openai/whisper
- License: **MIT License** — Copyright © 2022 OpenAI
- ONNX export toolchain: sherpa-onnx export scripts (Apache 2.0)

These models are not stored in the repository and are downloaded on demand into the local user profile.

---

## Recommendation

If public redistribution requirements change upstream, keep the corresponding license texts and attribution notes in this file or in a dedicated `licenses/` folder.
