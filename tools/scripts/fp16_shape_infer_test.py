"""
Teste uniquement la vitesse de onnx.shape_inference.infer_shapes sur le modele RIFE.
Si c'est rapide, onnxconverter avec disable_shape_infer=False est viable.
"""
import time, warnings
warnings.filterwarnings("ignore")
import onnx

import os
MODEL = os.path.join(os.environ.get("LOCALAPPDATA", ""), r"FrameShift\AI\Models\rife\rife_v426_x2.onnx")

print("Loading...", flush=True)
t0 = time.perf_counter()
model = onnx.load(MODEL)
print(f"  Loaded in {time.perf_counter()-t0:.3f}s", flush=True)

print("Running shape_inference.infer_shapes...", flush=True)
t1 = time.perf_counter()
try:
    inferred = onnx.shape_inference.infer_shapes(model, check_type=True, strict_mode=False)
    elapsed = time.perf_counter() - t1
    print(f"  Done in {elapsed:.3f}s", flush=True)

    # Count how many tensors got type annotations
    annotated = sum(1 for vi in inferred.graph.value_info if vi.type.tensor_type.elem_type != 0)
    print(f"  Annotated value_info entries: {annotated}", flush=True)

    # Check the Sub_6 node
    vi_map = {vi.name: vi.type.tensor_type.elem_type for vi in inferred.graph.value_info}
    from onnx import TensorProto
    for node in inferred.graph.node:
        if "_Sub_6" in (node.name or ""):
            print(f"\n  /ifnet/Sub_6 inputs:", flush=True)
            for inp in node.input:
                t = vi_map.get(inp, "unknown")
                tname = TensorProto.DataType.Name(t) if isinstance(t, int) else t
                print(f"    {inp!r}: {tname}", flush=True)
            break
except Exception as e:
    print(f"  FAILED: {e}", flush=True)

print(f"\nTotal: {time.perf_counter()-t0:.3f}s", flush=True)
