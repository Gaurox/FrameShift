"""
Patch the targeted FP16 RIFE model by inserting FLOAT->FLOAT16 Casts on
mixed Add/Sub/Mul/Div inputs that still consume FLOAT coordinate tensors.

This is intentionally minimal:
- start from the already-small targeted model
- do not reconvert weights/constants globally
- only patch edges that still violate ORT type unification
"""
import os
import re
import time
import warnings
from collections import Counter

import onnx
import onnxruntime as ort
from onnx import AttributeProto, TensorProto, helper

warnings.filterwarnings("ignore")

MODEL_IN = r"C:\Users\Adrien\AppData\Local\FrameShift\AI\Models\rife\rife_v426_x2_fp16_targeted.onnx"
MODEL_OUT = r"C:\Users\Adrien\AppData\Local\FrameShift\AI\Models\rife\rife_v426_x2_fp16_targeted_fix1.onnx"

PATCH_OPS = {"Add", "Sub", "Mul", "Div"}
ERROR_RE = re.compile(
    r"Optype \((?P<op>[A-Za-z0-9_]+)\).*node \((?P<name>[^)]+)\)",
    re.DOTALL,
)


def infer_tensor_types(model: onnx.ModelProto) -> dict[str, int]:
    graph = model.graph
    dtypes: dict[str, int] = {}

    for vi in list(graph.input) + list(graph.value_info) + list(graph.output):
        elem_type = vi.type.tensor_type.elem_type
        if elem_type:
            dtypes[vi.name] = elem_type

    for init in graph.initializer:
        dtypes[init.name] = init.data_type

    changed = True
    while changed:
        changed = False
        for node in graph.node:
            if node.op_type == "Constant":
                out = node.output[0]
                if out in dtypes:
                    continue
                dtype = None
                for attr in node.attribute:
                    if attr.type == AttributeProto.TENSOR:
                        dtype = attr.t.data_type
                        break
                    if attr.type == AttributeProto.FLOAT:
                        dtype = TensorProto.FLOAT
                        break
                    if attr.type == AttributeProto.INT:
                        dtype = TensorProto.INT64
                        break
                if dtype is not None:
                    dtypes[out] = dtype
                    changed = True
                continue

            if node.op_type == "Cast":
                out = node.output[0]
                if out in dtypes:
                    continue
                for attr in node.attribute:
                    if attr.name == "to":
                        dtypes[out] = attr.i
                        changed = True
                        break
                continue

            if node.op_type == "Shape":
                for out in node.output:
                    if out not in dtypes:
                        dtypes[out] = TensorProto.INT64
                        changed = True
                continue

            if node.op_type in {"Gather", "Slice", "Reshape", "Expand", "Unsqueeze", "Tile"}:
                if node.input and node.input[0] in dtypes:
                    src_type = dtypes[node.input[0]]
                    for out in node.output:
                        if out not in dtypes:
                            dtypes[out] = src_type
                            changed = True
                continue

            if node.op_type in {
                "Add",
                "Sub",
                "Mul",
                "Div",
                "Conv",
                "ConvTranspose",
                "LeakyRelu",
                "Sigmoid",
                "Clip",
                "GridSample",
                "DepthToSpace",
                "Concat",
                "Transpose",
                "Resize",
            }:
                for src in node.input:
                    if src and src in dtypes:
                        src_type = dtypes[src]
                        for out in node.output:
                            if out not in dtypes:
                                dtypes[out] = src_type
                                changed = True
                        break
                continue

            if node.op_type == "Equal":
                for out in node.output:
                    if out not in dtypes:
                        dtypes[out] = TensorProto.BOOL
                        changed = True
                continue

            if node.op_type == "Where":
                candidate_types = [dtypes.get(src) for src in node.input[1:] if src]
                value_type = next((dt for dt in candidate_types if dt is not None), None)
                if value_type is not None:
                    for out in node.output:
                        if out not in dtypes:
                            dtypes[out] = value_type
                            changed = True
                continue

            if node.op_type == "Range":
                candidate_types = [dtypes.get(src) for src in node.input if src]
                value_type = next((dt for dt in candidate_types if dt is not None), None)
                if value_type is not None:
                    for out in node.output:
                        if out not in dtypes:
                            dtypes[out] = value_type
                            changed = True
                continue

            if node.op_type == "ConstantOfShape":
                for out in node.output:
                    if out not in dtypes:
                        dtypes[out] = TensorProto.FLOAT
                        changed = True

    return dtypes


def main() -> None:
    t0 = time.perf_counter()
    print(f"Loading {MODEL_IN}...", flush=True)
    model = onnx.load(MODEL_IN)
    graph = model.graph
    total_inserted = 0
    summary = Counter()

    for iteration in range(1, 201):
        tmp_path = MODEL_OUT + ".tmp"
        onnx.save(model, tmp_path)
        try:
            opts = ort.SessionOptions()
            opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
            ort.InferenceSession(
                tmp_path,
                opts,
                providers=[("DmlExecutionProvider", {}), "CPUExecutionProvider"],
            )
            print(f"ORT load succeeded after {iteration - 1} patch iterations.", flush=True)
            os.replace(tmp_path, MODEL_OUT)
            break
        except Exception as exc:
            message = str(exc)
            match = ERROR_RE.search(message)
            if not match:
                raise

            node_name = match.group("name")
            op_type = match.group("op")
            if op_type not in PATCH_OPS:
                raise RuntimeError(f"Unhandled failing op {op_type} on {node_name}") from exc

            dtypes = infer_tensor_types(model)
            node_index = None
            node = None
            for idx, candidate in enumerate(graph.node):
                if candidate.name == node_name:
                    node_index = idx
                    node = candidate
                    break

            if node_index is None or node is None:
                raise RuntimeError(f"Failed to find node {node_name} in graph") from exc

            casts_for_node = []
            for slot, inp in enumerate(node.input):
                if not inp:
                    continue
                if dtypes.get(inp) != TensorProto.FLOAT:
                    continue
                cast_out = f"_autocast_fp16_{node.name.replace('/', '_')}_{slot}_{iteration}"
                node.input[slot] = cast_out
                casts_for_node.append(
                    helper.make_node(
                        "Cast",
                        inputs=[inp],
                        outputs=[cast_out],
                        to=int(TensorProto.FLOAT16),
                        name=f"AutoCast_{node.name.replace('/', '_')}_{slot}_{iteration}",
                    )
                )

            if not casts_for_node:
                raise RuntimeError(f"No FLOAT input found to patch for {node_name}") from exc

            original_nodes = list(graph.node)
            del graph.node[:]
            for idx, existing in enumerate(original_nodes):
                if idx == node_index:
                    for cast_node in casts_for_node:
                        graph.node.append(cast_node)
                graph.node.append(existing)

            total_inserted += len(casts_for_node)
            summary[op_type] += len(casts_for_node)
            print(
                f"[{iteration}] patched {node_name} ({op_type}) with {len(casts_for_node)} Cast(s); "
                f"retrying ORT load...",
                flush=True,
            )
    else:
        raise RuntimeError("Reached patch iteration limit without a loadable model")

    print(f"Inserted Cast nodes total: {total_inserted}", flush=True)
    print(f"By op: {dict(summary)}", flush=True)

    print("Running onnx.checker.check_model...", flush=True)
    onnx.checker.check_model(model)
    print("  check_model: PASSED", flush=True)

    print(f"Saving {MODEL_OUT}...", flush=True)
    print(f"Saved. Size={os.path.getsize(MODEL_OUT)/1024/1024:.2f} MB", flush=True)
    print(f"Done in {time.perf_counter()-t0:.2f}s", flush=True)


if __name__ == "__main__":
    main()
