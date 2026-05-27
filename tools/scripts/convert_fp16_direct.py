"""
Direct FP32->FP16 converter for RIFE ONNX model.
Converts weight initializers only, without shape inference.
Inputs/outputs stay FLOAT (keep_io_types=True equivalent).
"""
import sys
import time
import numpy as np
import onnx
from onnx import TensorProto, numpy_helper
import os

MODEL_FP32 = r"C:\Users\Adrien\AppData\Local\FrameShift\AI\Models\rife\rife_v426_x2.onnx"
MODEL_FP16 = r"C:\Users\Adrien\AppData\Local\FrameShift\AI\Models\rife\rife_v426_x2_fp16_v2.onnx"

t0 = time.perf_counter()
print(f"Loading {MODEL_FP32} ...", flush=True)
model = onnx.load(MODEL_FP32)
print(f"  Loaded in {time.perf_counter()-t0:.2f}s", flush=True)

graph = model.graph

# Step 1: convert Float initializers to Float16
n_converted = 0
n_skipped = 0
for init in graph.initializer:
    if init.data_type == TensorProto.FLOAT:
        arr = numpy_helper.to_array(init).astype(np.float16)
        fp16_init = numpy_helper.from_array(arr, name=init.name)
        init.CopyFrom(fp16_init)
        n_converted += 1
    else:
        n_skipped += 1

print(f"  Converted {n_converted} initializers to FP16, skipped {n_skipped}", flush=True)

# Step 2: insert Cast FP16->FP32 after inputs (keep external interface FLOAT)
#   inputs are FLOAT, but Conv/Mul expect FP16 after conversion
#   We insert Cast nodes: input_fp32 -> cast_fp16 -> <uses>
from onnx import helper

# Collect input names (img0, img1 — both FLOAT)
input_names = {inp.name for inp in graph.input}
print(f"  Model inputs (FLOAT): {input_names}", flush=True)

# Find all nodes that consume model inputs
new_nodes = []
cast_map = {}  # input_name -> cast_fp16_name

for input_name in input_names:
    cast_name = f"_cast_fp16_{input_name}"
    cast_node = helper.make_node(
        "Cast",
        inputs=[input_name],
        outputs=[cast_name],
        to=int(TensorProto.FLOAT16),
        name=f"Cast_input_{input_name}_to_fp16"
    )
    new_nodes.append(cast_node)
    cast_map[input_name] = cast_name

# Step 3: rewrite all edges from model inputs to use the FP16 cast outputs
for node in graph.node:
    for i, inp in enumerate(node.input):
        if inp in cast_map:
            node.input[i] = cast_map[inp]

# Step 4: insert Cast FP16->FP32 before model output
output_names = {out.name for out in graph.output}
print(f"  Model outputs (FLOAT): {output_names}", flush=True)

# Find node that produces model output and insert a cast
output_cast_nodes = []
for output_name in output_names:
    fp16_output_name = f"_fp16_pre_output_{output_name}"
    # rewrite the node that produces output_name
    for node in graph.node:
        for i, out in enumerate(node.output):
            if out == output_name:
                node.output[i] = fp16_output_name
                break
    # add cast fp16->fp32
    cast_node = helper.make_node(
        "Cast",
        inputs=[fp16_output_name],
        outputs=[output_name],
        to=int(TensorProto.FLOAT),
        name=f"Cast_output_{output_name}_to_fp32"
    )
    output_cast_nodes.append(cast_node)

# Step 5: update node dtype annotations for Conv/Mul/Add layers
# For Conv nodes: their weight initializers are now FP16, but the node
# itself needs to receive FP16 data — already handled by Cast nodes above.

# Step 6: insert all new nodes at the beginning/end
for n in reversed(new_nodes):
    graph.node.insert(0, n)
for n in output_cast_nodes:
    graph.node.append(n)

print(f"  Added {len(new_nodes)} input cast nodes, {len(output_cast_nodes)} output cast nodes", flush=True)

# Step 7: save
print(f"Saving to {MODEL_FP16} ...", flush=True)
onnx.save(model, MODEL_FP16)

fp32_mb = os.path.getsize(MODEL_FP32)/1024/1024
fp16_mb = os.path.getsize(MODEL_FP16)/1024/1024
print(f"FP32: {fp32_mb:.2f} MB  FP16: {fp16_mb:.2f} MB  ratio={fp16_mb/fp32_mb:.2f}x", flush=True)
print(f"Done in {time.perf_counter()-t0:.2f}s", flush=True)
