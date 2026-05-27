# Third-Party Notices

This project is licensed under the GNU General Public License v3.0.  
See [LICENSE](LICENSE) for the full project license text.

Third-party components keep their own licenses. The main project license does not replace or rewrite those upstream licenses.

## Remove Background Model

FrameShift's optional `Remove Background` feature downloads the following model at runtime:

- Model: `BiRefNet Lite FP16`
- Source URL used by the application:
  `https://huggingface.co/Gaurox/frameshift-models/resolve/main/birefnet_lite-onnx/model_fp16.onnx`
- License string currently exposed by the application:
  `MIT License — Free for commercial and non-commercial use`

This model is not stored in the repository and is downloaded on demand into the local user profile.

## Runtime Dependency

FrameShift also uses ONNX Runtime DirectML through the NuGet package:

- `Microsoft.ML.OnnxRuntime.DirectML`

Any redistribution of that dependency remains subject to its upstream license and notices.

## AI Model License Summary

FrameShift's current optional AI model downloads map to these upstream license families:

- `BiRefNet Lite FP16` (`Remove Background`): MIT
- `DeepFilterNet3 ONNX` (`Remove Noise`): dual-licensed MIT or Apache-2.0 upstream
- `HTDemucs` / `HTDemucs Split` (`Separate Audio`): MIT
- `RIFE v4.26 x2 ONNX` (`Interpolate Video`): MIT

## Recommendation

If public redistribution requirements change upstream, keep the corresponding license texts and attribution notes in this file or in a dedicated `licenses/` folder.
