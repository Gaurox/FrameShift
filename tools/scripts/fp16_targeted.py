"""
FP16 conversion ciblée pour RIFE rife_v426_x2.onnx.

Stratégie :
1. Analyser les types réels des Constant nodes (FLOAT vs INT64 vs autre)
2. Identifier les FLOAT Constants qui peuvent être FP16 (alimentent seulement des compute ops)
3. Identifier les FLOAT Constants qui doivent rester FP32 (alimentent Resize/shape ops)
4. Convertir : initializers + safe FLOAT Constants -> FP16
5. Insérer Cast FP32->FP16 aux inputs du modèle
6. Insérer Cast FP16->FP32 aux outputs du modèle
7. Pour chaque FP32 Constant feeding un FP16 compute op -> insérer Cast FP32->FP16 sur ce chemin
"""
import time, os, warnings
warnings.filterwarnings("ignore")
import numpy as np
import onnx
from onnx import TensorProto, numpy_helper, helper, AttributeProto
from collections import defaultdict

MODEL_FP32 = os.path.join(os.environ.get("LOCALAPPDATA", ""), r"FrameShift\AI\Models\rife\rife_v426_x2.onnx")
MODEL_FP16 = os.path.join(os.environ.get("LOCALAPPDATA", ""), r"FrameShift\AI\Models\rife\rife_v426_x2_fp16_targeted.onnx")

t0 = time.perf_counter()
print("Loading model...", flush=True)
model = onnx.load(MODEL_FP32)
graph = model.graph
print(f"  {len(graph.initializer)} initializers, {len(graph.node)} nodes", flush=True)

# -----------------------------------------------------------------------
# Step 0: Build graph topology helpers
# -----------------------------------------------------------------------
output_to_node = {}
for node in graph.node:
    for out in node.output:
        output_to_node[out] = node

input_names = {inp.name for inp in graph.input}
output_names = {out.name for out in graph.output}

# ops where the MAIN data inputs (slot 0) can be FP16
COMPUTE_OPS = {"Conv", "ConvTranspose", "Mul", "Add", "Sub", "Div",
               "LeakyRelu", "Sigmoid", "DepthToSpace", "Clip",
               "GridSample",  # both inputs (data + grid) can be FP16
               "Concat",      # data concat — can be FP16 IF all inputs are FP16
               }

# ops where aux inputs MUST NOT be FP16 (they carry indices/shapes/scales)
# format: op_type -> set of slot indices that must stay FLOAT/INT64 (never FP16)
SAFE_AUX_SLOTS = {
    "Resize":   {1, 2},  # roi, scales must be FLOAT
    "Slice":    {1, 2, 3, 4},  # starts, ends, axes, steps must be INT64
    "Gather":   {1},      # indices must be INT64
    "Unsqueeze": {1},     # axes must be INT64 in opset 13+ (but it's an attribute in some)
    "Range":    {0, 1, 2},  # can be FLOAT or INT64, don't convert
    "Reshape":  {1},      # target shape must be INT64
    "Expand":   {1},      # shape must be INT64
}

# -----------------------------------------------------------------------
# Step 1: Classify each Constant node
# -----------------------------------------------------------------------
const_dtype = {}   # const_output_name -> TensorProto dtype
const_value = {}   # const_output_name -> numpy array (for debug)

for node in graph.node:
    if node.op_type != "Constant":
        continue
    out_name = node.output[0] if node.output else None
    if out_name is None:
        continue
    for attr in node.attribute:
        if attr.type == AttributeProto.TENSOR:
            const_dtype[out_name] = attr.t.data_type
            break
        elif attr.type == AttributeProto.FLOAT:
            const_dtype[out_name] = TensorProto.FLOAT
            break
        elif attr.type == AttributeProto.INT:
            const_dtype[out_name] = TensorProto.INT64
            break

float_consts = {k for k, v in const_dtype.items() if v == TensorProto.FLOAT}
int_consts   = {k for k, v in const_dtype.items() if v == TensorProto.INT64}
other_consts = {k for k, v in const_dtype.items() if v not in (TensorProto.FLOAT, TensorProto.INT64)}

print(f"\nConstant nodes: {len(const_dtype)} total", flush=True)
print(f"  FLOAT: {len(float_consts)}", flush=True)
print(f"  INT64: {len(int_consts)}", flush=True)
print(f"  other: {len(other_consts)}", flush=True)

# -----------------------------------------------------------------------
# Step 2: Identify FLOAT Constants that MUST stay FP32 (aux slots)
# -----------------------------------------------------------------------
must_stay_fp32_consts = set()

for node in graph.node:
    aux_slots = SAFE_AUX_SLOTS.get(node.op_type, set())
    if not aux_slots:
        continue
    for slot_idx, inp in enumerate(node.input):
        if not inp or slot_idx not in aux_slots:
            continue
        if inp in float_consts:
            must_stay_fp32_consts.add(inp)

safe_to_fp16_consts = float_consts - must_stay_fp32_consts

print(f"\nFLOAT Constant analysis:", flush=True)
print(f"  Must stay FP32 (aux slots): {len(must_stay_fp32_consts)}", flush=True)
print(f"  Safe to convert to FP16:    {len(safe_to_fp16_consts)}", flush=True)

# -----------------------------------------------------------------------
# Step 3: Convert initializers FP32 -> FP16
# -----------------------------------------------------------------------
n_init = 0
for init in graph.initializer:
    if init.data_type == TensorProto.FLOAT:
        arr = numpy_helper.to_array(init).astype(np.float16)
        fp16 = numpy_helper.from_array(arr, name=init.name)
        init.CopyFrom(fp16)
        n_init += 1
print(f"\nInitializers converted FP32->FP16: {n_init}", flush=True)

# -----------------------------------------------------------------------
# Step 4: Convert safe FLOAT Constant nodes -> FP16
# -----------------------------------------------------------------------
n_const_converted = 0
for node in graph.node:
    if node.op_type != "Constant":
        continue
    out_name = node.output[0] if node.output else None
    if out_name not in safe_to_fp16_consts:
        continue
    for attr in node.attribute:
        if attr.type == AttributeProto.TENSOR and attr.t.data_type == TensorProto.FLOAT:
            arr = numpy_helper.to_array(attr.t).astype(np.float16)
            fp16_t = numpy_helper.from_array(arr)
            attr.t.CopyFrom(fp16_t)
            n_const_converted += 1
            break
print(f"Safe FLOAT Constants converted FP32->FP16: {n_const_converted}", flush=True)

# -----------------------------------------------------------------------
# Step 5: Insert Cast FP32->FP16 at model inputs
# -----------------------------------------------------------------------
cast_input_map = {}  # original_name -> fp16_name
new_prefix_nodes = []

for inp in graph.input:
    t = inp.type.tensor_type
    if t.elem_type == TensorProto.FLOAT:
        fp16_name = f"_fp16_in_{inp.name}"
        cast_input_map[inp.name] = fp16_name
        new_prefix_nodes.append(helper.make_node(
            "Cast", inputs=[inp.name], outputs=[fp16_name],
            to=int(TensorProto.FLOAT16), name=f"Cast_in_{inp.name}"))

# Rewire: replace model-input references in graph nodes
for node in graph.node:
    for i, inp in enumerate(node.input):
        if inp in cast_input_map:
            node.input[i] = cast_input_map[inp]

for n in reversed(new_prefix_nodes):
    graph.node.insert(0, n)

print(f"Input Cast nodes inserted: {len(new_prefix_nodes)}", flush=True)

# -----------------------------------------------------------------------
# Step 6: Insert Cast FP16->FP32 at model outputs
# -----------------------------------------------------------------------
new_suffix_nodes = []
for out in graph.output:
    t = out.type.tensor_type
    if t.elem_type == TensorProto.FLOAT:
        fp16_intermediate = f"_fp16_pre_out_{out.name}"
        for node in graph.node:
            for i, o in enumerate(node.output):
                if o == out.name:
                    node.output[i] = fp16_intermediate
                    break
        new_suffix_nodes.append(helper.make_node(
            "Cast", inputs=[fp16_intermediate], outputs=[out.name],
            to=int(TensorProto.FLOAT), name=f"Cast_out_{out.name}"))

for n in new_suffix_nodes:
    graph.node.append(n)
print(f"Output Cast nodes inserted: {len(new_suffix_nodes)}", flush=True)

# -----------------------------------------------------------------------
# Step 7: Insert Cast FP32->FP16 where FP32 Constant feeds a FP16 node
#         (specifically: must_stay_fp32 constants feeding compute ops that
#          now expect FP16 because their other inputs are FP16)
#
# For each must_stay_fp32 constant:
#   find which compute op it feeds
#   insert Cast FP32->FP16 on that edge
# -----------------------------------------------------------------------
# Build: for each must_stay_fp32 constant, insert a Cast FP32->FP16 node
# and rewire the consumer to use the cast output.

cast_fp32_to_fp16_nodes = []
tensor_cast_map = {}  # original_tensor_name -> cast_fp16_output_name

for node in graph.node:
    if node.op_type == "Constant":
        continue
    for slot_idx, inp in enumerate(node.input):
        if not inp:
            continue
        # Is this tensor a must_stay_fp32 constant?
        if inp not in must_stay_fp32_consts:
            continue
        # Is the consumer a compute op (that now runs in FP16)?
        if node.op_type not in COMPUTE_OPS:
            continue
        # This must_stay_fp32 constant feeds a compute op in FP16 -> need Cast
        if inp not in tensor_cast_map:
            cast_out = f"_cast_fp32tofp16_{inp.replace('/', '_')}"
            tensor_cast_map[inp] = cast_out
        node.input[slot_idx] = tensor_cast_map[inp]

for orig, cast_out in tensor_cast_map.items():
    cast_fp32_to_fp16_nodes.append(helper.make_node(
        "Cast", inputs=[orig], outputs=[cast_out],
        to=int(TensorProto.FLOAT16),
        name=f"Cast_boundary_{orig.replace('/', '_')}"))

# Insert boundary Cast nodes after the input Casts but before the rest
for n in reversed(cast_fp32_to_fp16_nodes):
    graph.node.insert(len(new_prefix_nodes), n)

print(f"FP32->FP16 boundary Cast nodes: {len(cast_fp32_to_fp16_nodes)}", flush=True)

# -----------------------------------------------------------------------
# Step 8: Save and validate
# -----------------------------------------------------------------------
print(f"\nSaving to {MODEL_FP16}...", flush=True)
onnx.save(model, MODEL_FP16)

fp32_mb = os.path.getsize(MODEL_FP32)/1024/1024
fp16_mb = os.path.getsize(MODEL_FP16)/1024/1024
print(f"Size: FP32={fp32_mb:.2f} MB  FP16_targeted={fp16_mb:.2f} MB  ({fp16_mb/fp32_mb:.2f}x)", flush=True)

print("Validating...", flush=True)
try:
    onnx.checker.check_model(onnx.load(MODEL_FP16))
    print("  check_model: PASSED", flush=True)
except Exception as e:
    print(f"  check_model: WARNING — {e}", flush=True)

print(f"Done in {time.perf_counter()-t0:.2f}s", flush=True)
