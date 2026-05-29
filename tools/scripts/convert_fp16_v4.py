"""
Phase 1 — RIFE FP16 conversion v4.
Uses onnxconverter_common with a precise op_block_list.
Key: block Resize, GridSample, DepthToSpace + all shape/index ops.
"""
import warnings
warnings.filterwarnings("ignore")
import time, os, sys
import onnx
from onnxconverter_common import float16

MODEL_FP32 = os.path.join(os.environ.get("LOCALAPPDATA", ""), r"FrameShift\AI\Models\rife\rife_v426_x2.onnx")
MODEL_FP16 = os.path.join(os.environ.get("LOCALAPPDATA", ""), r"FrameShift\AI\Models\rife\rife_v426_x2_fp16_v4.onnx")

# Must stay FP32:
# - Cast, Shape, Gather, etc.: shape/index ops, expect INT64 or FLOAT for specific slots
# - Resize: scales/roi inputs must be FLOAT, not FP16
# - GridSample: coordinate grid may need FP32 for accuracy (optional; test both)
# - Range: start/stop/step are INT or FLOAT, not FP16
BLOCK = {
    "Cast", "Shape", "Gather", "Unsqueeze", "Squeeze",
    "Reshape", "Expand", "Range", "ConstantOfShape",
    "Equal", "Where", "Tile", "Transpose",
    "Resize",        # scales/roi must be FLOAT
    "Slice",         # indices are INT64
}

t0 = time.perf_counter()
print(f"Loading model ({os.path.getsize(MODEL_FP32)/1024/1024:.1f} MB)...", flush=True)
model = onnx.load(MODEL_FP32)
print(f"  Loaded in {time.perf_counter()-t0:.2f}s", flush=True)

print(f"Converting (op_block_list={len(BLOCK)} ops)...", flush=True)
t1 = time.perf_counter()
model_fp16 = float16.convert_float_to_float16(
    model,
    keep_io_types=True,
    disable_shape_infer=True,  # skip slow shape inference
    op_block_list=BLOCK,
)
print(f"  Converted in {time.perf_counter()-t1:.2f}s", flush=True)

print("Checking model validity...", flush=True)
try:
    onnx.checker.check_model(model_fp16)
    print("  check_model: PASSED", flush=True)
except Exception as e:
    print(f"  check_model: WARNING — {e}", flush=True)

print(f"Saving {MODEL_FP16}...", flush=True)
onnx.save(model_fp16, MODEL_FP16)

fp32_mb = os.path.getsize(MODEL_FP32)/1024/1024
fp16_mb = os.path.getsize(MODEL_FP16)/1024/1024
print(f"Size: FP32={fp32_mb:.2f} MB  FP16={fp16_mb:.2f} MB  ({fp16_mb/fp32_mb:.2f}x)", flush=True)

# Weight dtype count
from onnx import TensorProto
from collections import Counter
d = Counter(TensorProto.DataType.Name(i.data_type) for i in model_fp16.graph.initializer)
print(f"Weight dtypes: {dict(d)}", flush=True)
print(f"Total time: {time.perf_counter()-t0:.2f}s", flush=True)
