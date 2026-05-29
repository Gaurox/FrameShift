"""
Phase 1 — RIFE FP16 conversion v3.
Converts initializers + Constant node attributes + inserts Cast boundaries.
No shape inference needed — operates purely on initializer/constant dtypes.
"""
import time, sys, os
import numpy as np
import onnx
from onnx import TensorProto, numpy_helper, helper, AttributeProto

MODEL_FP32 = os.path.join(os.environ.get("LOCALAPPDATA", ""), r"FrameShift\AI\Models\rife\rife_v426_x2.onnx")
MODEL_FP16 = os.path.join(os.environ.get("LOCALAPPDATA", ""), r"FrameShift\AI\Models\rife\rife_v426_x2_fp16_v3.onnx")

t0 = time.perf_counter()
print("Loading model...", flush=True)
model = onnx.load(MODEL_FP32)
graph = model.graph

# ------------------------------------------------------------------
# 1. Convert all initializer weights FP32 -> FP16
# ------------------------------------------------------------------
n_init = 0
for init in graph.initializer:
    if init.data_type == TensorProto.FLOAT:
        arr = numpy_helper.to_array(init).astype(np.float16)
        fp16 = numpy_helper.from_array(arr, name=init.name)
        init.CopyFrom(fp16)
        n_init += 1
print(f"  Initializers converted: {n_init}", flush=True)

# ------------------------------------------------------------------
# 2. Convert Constant node float tensors FP32 -> FP16
#    (inline scalar/tensor constants, not shape-related integers)
# ------------------------------------------------------------------
n_const = 0
for node in graph.node:
    if node.op_type != "Constant":
        continue
    for attr in node.attribute:
        if attr.type == AttributeProto.TENSOR and attr.t.data_type == TensorProto.FLOAT:
            arr = numpy_helper.to_array(attr.t).astype(np.float16)
            fp16_t = numpy_helper.from_array(arr)
            attr.t.CopyFrom(fp16_t)
            n_const += 1
print(f"  Constant nodes converted: {n_const}", flush=True)

# ------------------------------------------------------------------
# 3. Insert Cast FP32->FP16 at model inputs, FP16->FP32 at output
# ------------------------------------------------------------------
input_names = [inp.name for inp in graph.input]
output_names = [out.name for out in graph.output]

new_prefix_nodes = []
cast_map = {}
for inp_name in input_names:
    cast_out = f"_fp16_in_{inp_name}"
    cast_map[inp_name] = cast_out
    new_prefix_nodes.append(helper.make_node(
        "Cast", inputs=[inp_name], outputs=[cast_out],
        to=int(TensorProto.FLOAT16), name=f"Cast_in_{inp_name}"))

# Rewire: replace inp_name -> cast_out in all non-Cast graph nodes
for node in graph.node:
    for i, inp in enumerate(node.input):
        if inp in cast_map:
            node.input[i] = cast_map[inp]

new_suffix_nodes = []
for out_name in output_names:
    fp16_intermediate = f"_fp16_out_{out_name}"
    # Find producing node and rename its output
    for node in graph.node:
        for i, o in enumerate(node.output):
            if o == out_name:
                node.output[i] = fp16_intermediate
                break
    new_suffix_nodes.append(helper.make_node(
        "Cast", inputs=[fp16_intermediate], outputs=[out_name],
        to=int(TensorProto.FLOAT), name=f"Cast_out_{out_name}"))

for n in reversed(new_prefix_nodes):
    graph.node.insert(0, n)
for n in new_suffix_nodes:
    graph.node.append(n)

print(f"  Cast nodes added: {len(new_prefix_nodes)} input, {len(new_suffix_nodes)} output", flush=True)

# ------------------------------------------------------------------
# 4. Save
# ------------------------------------------------------------------
print(f"Saving {MODEL_FP16} ...", flush=True)
onnx.save(model, MODEL_FP16)

fp32_mb = os.path.getsize(MODEL_FP32)/1024/1024
fp16_mb = os.path.getsize(MODEL_FP16)/1024/1024
print(f"Size: FP32={fp32_mb:.2f} MB  FP16_v3={fp16_mb:.2f} MB  ratio={fp16_mb/fp32_mb:.2f}x", flush=True)
print(f"Done in {time.perf_counter()-t0:.2f}s", flush=True)
