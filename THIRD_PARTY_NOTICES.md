# Third-Party Notices

This project is licensed under the GNU General Public License v3.0.  
See [LICENSE](LICENSE) for the full project license text.

Third-party components keep their own licenses. The main project license does not replace or rewrite those upstream licenses.

## Remove Background Model

FrameShift's optional `Remove Background` feature downloads the following model at runtime:

- Model: `BiRefNet Lite FP16`
- Source URL used by the application:
  `https://huggingface.co/onnx-community/BiRefNet_lite-ONNX/resolve/main/onnx/model_fp16.onnx`
- License string currently exposed by the application:
  `MIT License — Free for commercial and non-commercial use`

This model is not stored in the repository and is downloaded on demand into the local user profile.

## Runtime Dependency

FrameShift also uses ONNX Runtime DirectML through the NuGet package:

- `Microsoft.ML.OnnxRuntime.DirectML`

Any redistribution of that dependency remains subject to its upstream license and notices.

## Recommendation

Before public release, verify the current upstream license pages for:

- the BiRefNet Lite ONNX model distribution;
- ONNX Runtime DirectML.

If either upstream project requires attribution, notice reproduction, or bundled license text, keep those notices in this file or in a dedicated `licenses/` folder.
