"""
Phase 1 — FP16 conversion v5.
Block Constant + all shape ops. Only Conv/ConvTranspose/Mul/Add/LeakyRelu/etc run FP16.
onnxconverter inserts Cast boundaries at Constant->compute and compute->blocked transitions.
disable_shape_infer=True for speed; boundary Casts handled by edge-type analysis.
"""
import warnings; warnings.filterwarnings("ignore")
import time, os
import onnx
from onnxconverter_common import float16

MODEL_FP32 = os.path.join(os.environ.get("LOCALAPPDATA", ""), r"FrameShift\AI\Models\rife\rife_v426_x2.onnx")
MODEL_FP16 = os.path.join(os.environ.get("LOCALAPPDATA", ""), r"FrameShift\AI\Models\rife\rife_v426_x2_fp16_v5.onnx")

# Block everything that must stay FP32 (shape ops, index ops, aux inputs)
BLOCK = {
    # shape/index
    "Cast", "Shape", "Gather", "Unsqueeze", "Squeeze", "Concat",
    "Reshape", "Expand", "Range", "ConstantOfShape", "Equal", "Where",
    "Slice", "Tile", "Transpose",
    # ops with FP32-required aux inputs
    "Resize",       # scales, roi must be FLOAT
    # inline float constants — keep FP32 so Cast boundaries are inserted correctly
    "Constant",
}

t0 = time.perf_counter()
print(f"Loading model...", flush=True)
model = onnx.load(MODEL_FP32)
print(f"  {len(model.graph.initializer)} initializers, {len(model.graph.node)} nodes", flush=True)

print(f"Converting with {len(BLOCK)}-op block list, disable_shape_infer=True...", flush=True)
t1 = time.perf_counter()
model_fp16 = float16.convert_float_to_float16(
    model,
    keep_io_types=True,
    disable_shape_infer=True,
    op_block_list=BLOCK,
)
print(f"  Converted in {time.perf_counter()-t1:.2f}s", flush=True)

print("Validating...", flush=True)
try:
    onnx.checker.check_model(model_fp16)
    print("  PASSED", flush=True)
except Exception as e:
    print(f"  WARNING: {e}", flush=True)

onnx.save(model_fp16, MODEL_FP16)
fp32_mb = os.path.getsize(MODEL_FP32)/1024/1024
fp16_mb = os.path.getsize(MODEL_FP16)/1024/1024
print(f"Size: FP32={fp32_mb:.2f} MB  FP16={fp16_mb:.2f} MB  ({fp16_mb/fp32_mb:.2f}x)", flush=True)

from onnx import TensorProto
from collections import Counter
d = Counter(TensorProto.DataType.Name(i.data_type) for i in model_fp16.graph.initializer)
print(f"Weights: {dict(d)}", flush=True)
print(f"Total: {time.perf_counter()-t0:.2f}s", flush=True)
