"""
FP16 conversion via :
1. onnx.shape_inference.infer_shapes() (0.05s - annote tous les types)
2. onnxconverter_common avec disable_shape_infer=True sur le modele pre-annote
   -> onnxconverter utilise value_info pour inserer les Cast boundaries correctement
   -> minimal block list (seulement les ops dont le comportement est incompatible FP16)

Objectif : modele mixed-precision valide sans le lent boundary analysis.
"""
import warnings; warnings.filterwarnings("ignore")
import time, os
import onnx
from onnx import shape_inference
from onnxconverter_common import float16

MODEL_FP32 = os.path.join(os.environ.get("LOCALAPPDATA", ""), r"FrameShift\AI\Models\rife\rife_v426_x2.onnx")
MODEL_FP16 = os.path.join(os.environ.get("LOCALAPPDATA", ""), r"FrameShift\AI\Models\rife\rife_v426_x2_fp16_preinf.onnx")

t0 = time.perf_counter()
print("Loading model...", flush=True)
model = onnx.load(MODEL_FP32)

print("Pre-running shape inference (annotates all tensor types)...", flush=True)
t1 = time.perf_counter()
model_inferred = shape_inference.infer_shapes(model, check_type=True, strict_mode=False)
print(f"  Done in {time.perf_counter()-t1:.3f}s", flush=True)
print(f"  value_info entries: {len(model_inferred.graph.value_info)}", flush=True)

# Minimal block list: only ops whose aux-input requirements are truly incompatible with FP16
# (Resize scales MUST be FLOAT, Shape output is INT64, etc.)
# Do NOT block "Constant" — let onnxconverter see the type boundaries and insert Casts.
BLOCK = {
    "Cast",             # keep as-is (shape casts INT64<->FLOAT)
    "Shape",            # output is always INT64
    "Gather",           # indices must be INT64
    "Slice",            # starts/ends/axes/steps must be INT64
    "Unsqueeze",        # axes attribute (opset 13+) — no problematic inputs
    "Reshape",          # shape must be INT64
    "Expand",           # shape must be INT64
    "Range",            # start/limit/delta are FLOAT or INT64 (keep FP32 for safety)
    "Equal",            # comparison ops
    "Where",            # condition is BOOL, X/Y stay as-is
    "ConstantOfShape",  # shape must be INT64
    "Tile",             # repeats must be INT64
    "Transpose",        # pure tensor layout op (no compute benefit)
    "Resize",           # scales (slot 2) must be FLOAT; block entire op for safety
}

print(f"Converting with {len(BLOCK)}-op block list, disable_shape_infer=True...", flush=True)
t2 = time.perf_counter()
model_fp16 = float16.convert_float_to_float16(
    model_inferred,
    keep_io_types=True,
    disable_shape_infer=True,   # types already in value_info
    op_block_list=BLOCK,
)
elapsed_conv = time.perf_counter() - t2
print(f"  Conversion done in {elapsed_conv:.3f}s", flush=True)

print("Validating...", flush=True)
try:
    onnx.checker.check_model(model_fp16)
    print("  check_model: PASSED", flush=True)
except Exception as e:
    print(f"  check_model: WARNING - {e}", flush=True)

print(f"Saving {MODEL_FP16}...", flush=True)
onnx.save(model_fp16, MODEL_FP16)

fp32_mb = os.path.getsize(MODEL_FP32)/1024/1024
fp16_mb = os.path.getsize(MODEL_FP16)/1024/1024
print(f"Size: FP32={fp32_mb:.2f} MB  FP16={fp16_mb:.2f} MB  ({fp16_mb/fp32_mb:.2f}x)", flush=True)

from onnx import TensorProto
from collections import Counter
d = Counter(TensorProto.DataType.Name(i.data_type) for i in model_fp16.graph.initializer)
print(f"Weights: {dict(d)}", flush=True)
print(f"Total: {time.perf_counter()-t0:.3f}s", flush=True)
