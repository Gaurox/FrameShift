"""
Phase 1 — Benchmark FP32 vs FP16 RIFE models sur DirectML.
"""
import time
import numpy as np
import onnxruntime as ort
import sys
import os

MODELS = {
    "FP32":       os.path.join(os.environ.get("LOCALAPPDATA", ""), r"FrameShift\AI\Models\rife\rife_v426_x2.onnx"),
    "FP16_v1":    os.path.join(os.environ.get("LOCALAPPDATA", ""), r"FrameShift\AI\Models\rife\rife_v426_x2_fp16.onnx"),
    "FP16_v2":    os.path.join(os.environ.get("LOCALAPPDATA", ""), r"FrameShift\AI\Models\rife\rife_v426_x2_fp16_v2.onnx"),
}

# Test at 720p padded (same as FrameShift actual use case for 1280x720 video)
SIZES = [
    ("720p_padded",  1280, 768),
    ("1080p_padded", 1920, 1088),
]

WARMUP  = 3
REPEATS = 10


def make_session(model_path, label):
    opts = ort.SessionOptions()
    opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    try:
        providers = [("DmlExecutionProvider", {}), "CPUExecutionProvider"]
        sess = ort.InferenceSession(model_path, opts, providers=providers)
        active = sess.get_providers()[0]
        return sess, active
    except Exception as e:
        print(f"  [{label}] DML FAILED: {e}")
        return None, "FAILED"


def benchmark(sess, label, provider, width, height):
    img0 = np.random.rand(1, 3, height, width).astype(np.float32)
    img1 = np.random.rand(1, 3, height, width).astype(np.float32)
    feeds = {"img0": img0, "img1": img1}

    # warmup
    for _ in range(WARMUP):
        out = sess.run(None, feeds)

    # bench
    times = []
    for _ in range(REPEATS):
        t0 = time.perf_counter()
        out = sess.run(None, feeds)
        times.append(time.perf_counter() - t0)

    arr = np.array(times) * 1000
    return arr.mean(), arr.min(), arr.max(), out[0]


print("=" * 60)
print("RIFE FP32 vs FP16 BENCHMARK")
print("=" * 60)

sessions = {}
for label, path in MODELS.items():
    import os
    if not os.path.exists(path):
        print(f"\n[{label}] NOT FOUND: {path}")
        continue
    print(f"\n[{label}] Loading...")
    sess, prov = make_session(path, label)
    if sess is not None:
        print(f"  Provider: {prov}")
        sessions[label] = (sess, prov)

print("\n" + "=" * 60)
print("TIMING RESULTS")
print("=" * 60)

ref_output = {}
for size_label, W, H in SIZES:
    print(f"\n--- {size_label} ({W}x{H}) ---")
    for label, (sess, prov) in sessions.items():
        try:
            mean_ms, min_ms, max_ms, out = benchmark(sess, label, prov, W, H)
            print(f"  {label:12s} [{prov[:10]:10s}]  mean={mean_ms:7.1f} ms  min={min_ms:7.1f} ms  max={max_ms:7.1f} ms")
            if size_label not in ref_output:
                ref_output[size_label] = (label, out)
            else:
                ref_label, ref_out = ref_output[size_label]
                diff = np.abs(out - ref_out)
                print(f"    vs {ref_label}: max_diff={diff.max():.5f}  mean_diff={diff.mean():.5f}")
        except Exception as e:
            print(f"  {label:12s} INFERENCE FAILED: {e}")

print("\n" + "=" * 60)
print("SPEEDUP SUMMARY")
print("=" * 60)
for size_label, W, H in SIZES:
    results = {}
    for label, (sess, prov) in sessions.items():
        try:
            mean_ms, *_ , _ = benchmark(sess, label, prov, W, H)
            results[label] = mean_ms
        except:
            pass
    if "FP32" in results:
        base = results["FP32"]
        for label, ms in results.items():
            speedup = base / ms
            print(f"  {size_label} {label:12s}: {ms:7.1f} ms  speedup vs FP32: {speedup:.2f}x")

print("\nDone.")
