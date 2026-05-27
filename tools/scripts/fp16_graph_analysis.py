"""
Analyse précise du graphe RIFE pour déterminer quels noeuds doivent rester FP32.
Identifie les Constant nodes qui alimentent des slots d'inputs non-data (Resize scales, etc.)
"""
import onnx
from onnx import TensorProto
from collections import defaultdict

MODEL = r"C:\Users\Adrien\AppData\Local\FrameShift\AI\Models\rife\rife_v426_x2.onnx"

print("Loading model...", flush=True)
model = onnx.load(MODEL)
graph = model.graph

# Build output_name -> node map
output_to_node = {}
for node in graph.node:
    for out in node.output:
        output_to_node[out] = node

# Build initializer set
initializer_names = {init.name for init in graph.initializer}

# -----------------------------------------------------------------------
# 1. Find all Resize nodes and their problematic input slots
#    Resize inputs: [X, roi, scales, sizes]
#    - X (slot 0): can be FP16
#    - roi (slot 1): must be FLOAT or empty
#    - scales (slot 2): must be FLOAT or empty
#    - sizes (slot 3): must be INT64 or empty
# -----------------------------------------------------------------------
RESIZE_AUX_SLOTS = {1, 2}   # roi, scales — must stay FLOAT
RESIZE_INT_SLOTS = {3}       # sizes — INT64, irrelevant for FP16

resize_fp32_required = set()  # tensor names that MUST stay FLOAT (direct inputs)
resize_analysis = []

for node in graph.node:
    if node.op_type != "Resize":
        continue
    for slot_idx, inp in enumerate(node.input):
        if not inp:
            continue
        if slot_idx in RESIZE_AUX_SLOTS:
            resize_fp32_required.add(inp)
            resize_analysis.append((node.name or f"Resize@{slot_idx}", slot_idx, inp))

print(f"\n=== RESIZE AUX INPUTS (must stay FLOAT) ===")
for name, slot, inp in resize_analysis:
    src_node = output_to_node.get(inp)
    src_op = src_node.op_type if src_node else "initializer"
    print(f"  {name} slot[{slot}] <- {inp!r} (source: {src_op})")

# -----------------------------------------------------------------------
# 2. Trace these FP32-required tensors back to their Constant source nodes
# -----------------------------------------------------------------------
def trace_to_constant(tensor_name, output_to_node, depth=0):
    """Returns the set of Constant node names that produce this tensor (directly or transitively)."""
    if depth > 5:
        return set()
    node = output_to_node.get(tensor_name)
    if node is None:
        return set()
    if node.op_type == "Constant":
        return {node.name or tensor_name}
    # For intermediate ops (Slice, Gather, etc.), follow their inputs
    result = set()
    for inp in node.input:
        if inp:
            result |= trace_to_constant(inp, output_to_node, depth + 1)
    return result

constant_nodes_to_block = set()  # node names
constant_output_names_to_block = set()  # output tensor names

for tensor_name in resize_fp32_required:
    src_node = output_to_node.get(tensor_name)
    if src_node is None:
        continue
    if src_node.op_type == "Constant":
        constant_nodes_to_block.add(src_node.name or tensor_name)
        constant_output_names_to_block.add(tensor_name)
    else:
        # Could be a chain: Reshape->Constant, etc.
        transitive = trace_to_constant(tensor_name, output_to_node)
        constant_nodes_to_block |= transitive
        constant_output_names_to_block.add(tensor_name)

print(f"\n=== CONSTANT NODES FEEDING RESIZE AUX SLOTS (must stay FP32) ===")
for n in sorted(constant_nodes_to_block):
    print(f"  {n}")
print(f"Total: {len(constant_nodes_to_block)} constant nodes")

# -----------------------------------------------------------------------
# 3. Same analysis for GridSample, Slice, Where, etc.
# -----------------------------------------------------------------------
# GridSample: [input, grid] — both can be FP16 (grid is float spatial coordinates)
# Slice: [data, starts, ends, axes, steps] — slots 1-4 must be INT64 (not float)
# Where: [condition, X, Y] — condition must be BOOL, X/Y can be FP16

# Check what feeds GridSample
print(f"\n=== GRIDSAMPLE INPUTS ===")
for node in graph.node:
    if node.op_type == "GridSample":
        for i, inp in enumerate(node.input):
            src = output_to_node.get(inp, None)
            src_op = src.op_type if src else "initializer"
            print(f"  slot[{i}] <- {inp!r} (source: {src_op})")
        break  # just show one example

# -----------------------------------------------------------------------
# 4. Full Constant node analysis: how many TOTAL, how many feed compute ops
# -----------------------------------------------------------------------
constant_to_consumers = defaultdict(list)
for node in graph.node:
    for slot_idx, inp in enumerate(node.input):
        src = output_to_node.get(inp)
        if src and src.op_type == "Constant":
            constant_to_consumers[src.name or inp].append((node.op_type, slot_idx))

compute_ops = {"Conv", "ConvTranspose", "Mul", "Add", "Sub", "Div", "LeakyRelu", "Sigmoid", "DepthToSpace"}

constant_safe_to_fp16 = 0
constant_unsafe = 0
consumer_op_distribution = defaultdict(int)

for const_name, consumers in constant_to_consumers.items():
    all_safe = all(op in compute_ops for op, _ in consumers)
    for op, _ in consumers:
        consumer_op_distribution[op] += 1
    if all_safe:
        constant_safe_to_fp16 += 1
    else:
        constant_unsafe += 1

print(f"\n=== CONSTANT NODE CONSUMER DISTRIBUTION ===")
for op, cnt in sorted(consumer_op_distribution.items(), key=lambda x: -x[1]):
    print(f"  -> {op}: {cnt}")
print(f"\nConstants safe to convert to FP16: {constant_safe_to_fp16}")
print(f"Constants that MUST stay FP32: {constant_unsafe}")
print(f"Total Constant nodes: {len(constant_to_consumers)}")

# -----------------------------------------------------------------------
# 5. What ops are problematic (have non-data FP32 input requirements)?
# -----------------------------------------------------------------------
PROBLEMATIC_AUX_OPS = {
    "Resize": {1, 2},        # roi, scales must be FLOAT
    "GridSample": set(),     # both inputs can be FP16
    "Gather": {1},           # indices must be INT64 (irrelevant for float)
    "Slice": {1, 2, 3, 4},   # all aux inputs are INT64
    "Range": {0, 1, 2},      # start/limit/delta — can be float OR int
}

print(f"\n=== OP-SPECIFIC ANALYSIS ===")
for op_type, aux_slots in PROBLEMATIC_AUX_OPS.items():
    nodes = [n for n in graph.node if n.op_type == op_type]
    print(f"  {op_type}: {len(nodes)} nodes, aux_slots={aux_slots}")

print("\nDone.", flush=True)
