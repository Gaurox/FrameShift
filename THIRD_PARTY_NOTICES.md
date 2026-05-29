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

## Remove Background Model

FrameShift's optional `Remove Background` feature downloads the following model at runtime:

- Model: `BiRefNet Lite FP16`
- Source URL used by the application:
  `https://huggingface.co/Gaurox/frameshift-models/resolve/main/birefnet_lite-onnx/model_fp16.onnx`
- Upstream model repository: https://huggingface.co/onnx-community/BiRefNet_lite-ONNX
- License: **MIT License**

This model is not stored in the repository and is downloaded on demand into the local user profile.

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

## Recommendation

If public redistribution requirements change upstream, keep the corresponding license texts and attribution notes in this file or in a dedicated `licenses/` folder.
