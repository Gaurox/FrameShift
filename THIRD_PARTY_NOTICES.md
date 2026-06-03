# Third-Party Notices

This project is licensed under the GNU General Public License v3.0.  
See [LICENSE](LICENSE) for the full project license text.

Third-party components keep their own licenses. The main project license does not replace or rewrite those upstream licenses.

---

## FFmpeg and FFprobe

FrameShift bundles `ffmpeg.exe` and `ffprobe.exe` as part of its installer and local toolchain.

- Build: `2025-04-17-git-7684243fbe-essentials_build` by [Gyan Doshi](https://www.gyan.dev/ffmpeg/builds/)
- Upstream project: https://ffmpeg.org
- License: **GNU General Public License v3.0 or later** (build compiled with `--enable-gpl --enable-version3`)
- Source code: https://ffmpeg.org/download.html

This build includes components licensed under LGPL and GPL. Because this build was compiled with `--enable-gpl`, the binary as a whole is governed by the GPL v3+. FrameShift is itself distributed under GPL v3, which is compatible with this requirement.

In compliance with the GPL, users who wish to obtain the corresponding source code for the bundled FFmpeg build may refer to the upstream source repository listed above, or to the build provider (www.gyan.dev).

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

## Upscale Image Models

FrameShift's optional `Upscale Image` feature downloads one of the following models at runtime
(user choice via the model picker). All are hosted under
`https://huggingface.co/Gaurox/frameshift-models/resolve/main/upscale-onnx/` with a `README.md` and
the corresponding license texts; none are bundled in the installer or repository.

### Real-ESRGAN x4plus (general, default)

- Model: `realesrgan_x4plus_fp16.onnx` (~34 MB, FP16 weights with float32 I/O)
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

## Recommendation

If public redistribution requirements change upstream, keep the corresponding license texts and attribution notes in this file or in a dedicated `licenses/` folder.
