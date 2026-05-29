"""Phase 1 — RIFE ONNX FP16 conversion and validation."""
import warnings
warnings.filterwarnings("ignore")

from onnxconverter_common import float16
import onnx
import os

MODEL_FP32 = os.path.join(os.environ.get("LOCALAPPDATA", ""), r"FrameShift\AI\Models\rife\rife_v426_x2.onnx")
MODEL_FP16 = os.path.join(os.environ.get("LOCALAPPDATA", ""), r"FrameShift\AI\Models\rife\rife_v426_x2_fp16.onnx")

# Ops that must stay FP32: shape logic, index ops, control flow
BLOCK = {
    "Cast", "Shape", "Gather", "Unsqueeze", "Squeeze", "Concat",
    "Reshape", "Expand", "Range", "ConstantOfShape", "Equal", "Where",
    "Slice", "Tile", "Transpose",
}

print("Loading FP32 model...")
model_fp32 = onnx.load(MODEL_FP32)

print("Converting FP32 -> FP16 (keep_io_types=True, blocking shape ops)...")
model_fp16 = float16.convert_float_to_float16(
    model_fp32,
    keep_io_types=True,
    disable_shape_infer=False,
    op_block_list=BLOCK,
)

print("Validating model...")
try:
    onnx.checker.check_model(model_fp16)
    print("  check_model: PASSED")
except Exception as e:
    print(f"  check_model: FAILED — {e}")

print("Saving...")
onnx.save(model_fp16, MODEL_FP16)

fp32_mb = os.path.getsize(MODEL_FP32) / 1024 / 1024
fp16_mb = os.path.getsize(MODEL_FP16) / 1024 / 1024
print(f"\nSizes: FP32={fp32_mb:.2f} MB  FP16={fp16_mb:.2f} MB  (ratio {fp16_mb/fp32_mb:.2f}x)")

from onnx import TensorProto
from collections import Counter

dtypes_count = Counter()
dtypes_params = Counter()
for init in model_fp16.graph.initializer:
    name = TensorProto.DataType.Name(init.data_type)
    n = 1
    for d in init.dims:
        n *= d
    dtypes_count[name] += 1
    dtypes_params[name] += n

print("\nWeight dtype distribution:")
for k in sorted(dtypes_params, key=lambda x: -dtypes_params[x]):
    print(f"  {k}: {dtypes_params[k]:,} params ({dtypes_count[k]} tensors)")
