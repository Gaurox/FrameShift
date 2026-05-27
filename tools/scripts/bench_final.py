"""Phase 1 — Benchmark final FP32 vs FP16_v4 sur DirectML."""
import time, numpy as np
import onnxruntime as ort
import os

MODELS = {
    "FP32":    r"C:\Users\Adrien\AppData\Local\FrameShift\AI\Models\rife\rife_v426_x2.onnx",
    "FP16_v4": r"C:\Users\Adrien\AppData\Local\FrameShift\AI\Models\rife\rife_v426_x2_fp16_v4.onnx",
}
SIZES = [("720p",  1280, 768), ("1080p", 1920, 1088)]
WARMUP = 5
RUNS   = 15

def load_sess(path):
    opts = ort.SessionOptions()
    opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    try:
        sess = ort.InferenceSession(path, opts,
            providers=[("DmlExecutionProvider",{}), "CPUExecutionProvider"])
        return sess, sess.get_providers()[0]
    except Exception as e:
        return None, str(e)

sessions = {}
for label, path in MODELS.items():
    print(f"Loading {label}...", flush=True)
    sess, prov = load_sess(path)
    if sess:
        print(f"  OK — provider={prov}", flush=True)
        sessions[label] = sess
    else:
        print(f"  FAILED: {prov}", flush=True)

results = {}
for sz_label, W, H in SIZES:
    img0 = np.random.rand(1,3,H,W).astype(np.float32)
    img1 = np.random.rand(1,3,H,W).astype(np.float32)
    feeds = {"img0": img0, "img1": img1}
    results[sz_label] = {}

    for label, sess in sessions.items():
        # warmup
        for _ in range(WARMUP):
            try:
                out = sess.run(None, feeds)
            except Exception as e:
                print(f"  {label} warmup FAIL @{sz_label}: {e}", flush=True)
                break
        else:
            times = []
            for _ in range(RUNS):
                t0 = time.perf_counter()
                out = sess.run(None, feeds)
                times.append((time.perf_counter()-t0)*1000)
            arr = np.array(times)
            results[sz_label][label] = {"mean": arr.mean(), "min": arr.min(), "out": out[0]}
            print(f"  {sz_label} {label}: mean={arr.mean():.1f}ms  min={arr.min():.1f}ms  p95={np.percentile(arr,95):.1f}ms", flush=True)

# Quality check
print("\n--- Quality check (max pixel diff) ---", flush=True)
for sz_label, W, H in SIZES:
    if "FP32" in results[sz_label] and "FP16_v4" in results[sz_label]:
        out32 = results[sz_label]["FP32"]["out"]
        out16 = results[sz_label]["FP16_v4"]["out"]
        diff = np.abs(out32.astype(np.float32) - out16.astype(np.float32))
        print(f"  {sz_label}: max_diff={diff.max():.5f}  mean_diff={diff.mean():.6f}  "
              f"range=[{out32.min():.3f},{out32.max():.3f}]", flush=True)

# Speedup summary
print("\n--- Speedup summary ---", flush=True)
for sz_label in results:
    if "FP32" in results[sz_label] and "FP16_v4" in results[sz_label]:
        fp32_ms = results[sz_label]["FP32"]["mean"]
        fp16_ms = results[sz_label]["FP16_v4"]["mean"]
        ratio = fp32_ms / fp16_ms
        print(f"  {sz_label}: FP32={fp32_ms:.1f}ms  FP16={fp16_ms:.1f}ms  "
              f"speedup={ratio:.2f}x  delta={fp32_ms-fp16_ms:+.1f}ms", flush=True)

print("\nDone.", flush=True)
