"""Benchmark FP32 vs FP16_targeted : chargement DirectML, inference, qualite, timing."""
import time
import numpy as np
import onnxruntime as ort
import os

MODELS = {
    "FP32":         r"C:\Users\Adrien\AppData\Local\FrameShift\AI\Models\rife\rife_v426_x2.onnx",
    "FP16_targeted": r"C:\Users\Adrien\AppData\Local\FrameShift\AI\Models\rife\rife_v426_x2_fp16_targeted.onnx",
}
SIZES = [
    ("720p_padded",  1280, 768),
    ("1080p_padded", 1920, 1088),
]
WARMUP = 5
RUNS   = 15

def load_sess(path, label):
    opts = ort.SessionOptions()
    opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    mb = os.path.getsize(path)/1024/1024
    print(f"\nLoading {label} ({mb:.1f} MB)...", flush=True)
    try:
        sess = ort.InferenceSession(path, opts,
            providers=[("DmlExecutionProvider",{}), "CPUExecutionProvider"])
        prov = sess.get_providers()[0]
        print(f"  OK - provider={prov}", flush=True)
        return sess, prov
    except Exception as e:
        print(f"  FAILED: {e}", flush=True)
        return None, str(e)

sessions = {}
for label, path in MODELS.items():
    sess, prov = load_sess(path, label)
    if sess:
        sessions[label] = (sess, prov)

results = {}
for sz, W, H in SIZES:
    img0 = np.random.rand(1,3,H,W).astype(np.float32)
    img1 = np.random.rand(1,3,H,W).astype(np.float32)
    feeds = {"img0": img0, "img1": img1}
    results[sz] = {}

    print(f"\n{'='*50}\n{sz} ({W}x{H})", flush=True)
    for label, (sess, prov) in sessions.items():
        try:
            # warmup
            for _ in range(WARMUP):
                out = sess.run(None, feeds)
            # bench
            times = []
            for _ in range(RUNS):
                t0 = time.perf_counter()
                out = sess.run(None, feeds)
                times.append((time.perf_counter()-t0)*1000)
            arr = np.array(times)
            results[sz][label] = {"mean": arr.mean(), "min": arr.min(),
                                   "p95": np.percentile(arr, 95), "out": out[0]}
            print(f"  {label:16s}: mean={arr.mean():.1f}ms  min={arr.min():.1f}ms  "
                  f"p95={np.percentile(arr,95):.1f}ms", flush=True)
        except Exception as e:
            print(f"  {label:16s}: INFERENCE FAILED: {e}", flush=True)

print(f"\n{'='*50}\nQUALITY CHECK (real images - same pair)", flush=True)
# Use actual video frames for quality check
FRAME_DIR = r"E:\AI\FrameShift_V1\tests\input"
import glob, sys
test_bmp = sorted(glob.glob(os.path.join(FRAME_DIR, "video moyenne_frames", "*.bmp")))

if len(test_bmp) >= 2 and all(label in sessions for label in MODELS):
    from PIL import Image as PILImage

    def load_frame(path, W, H):
        img = PILImage.open(path).convert("RGB").resize((W, H))
        arr = np.array(img, dtype=np.float32) / 255.0
        return arr.transpose(2,0,1)[np.newaxis]  # 1,3,H,W

    W, H = 1280, 768
    img0 = load_frame(test_bmp[0], W, H)
    img1 = load_frame(test_bmp[1], W, H)
    feeds_real = {"img0": img0, "img1": img1}

    outs = {}
    for label, (sess, prov) in sessions.items():
        try:
            out = sess.run(None, feeds_real)[0]
            outs[label] = out
            print(f"  {label}: out range [{out.min():.4f}, {out.max():.4f}]", flush=True)
        except Exception as e:
            print(f"  {label}: FAILED {e}", flush=True)

    if "FP32" in outs and "FP16_targeted" in outs:
        diff = np.abs(outs["FP32"] - outs["FP16_targeted"])
        print(f"\n  Pixel diff FP32 vs FP16_targeted:", flush=True)
        print(f"    max  = {diff.max():.6f}  (pixel units 0-1)", flush=True)
        print(f"    mean = {diff.mean():.6f}", flush=True)
        print(f"    p99  = {np.percentile(diff, 99):.6f}", flush=True)
        # Convert to 8-bit for visual assessment
        max_8bit = diff.max() * 255
        print(f"    max_diff in 8-bit: {max_8bit:.2f} / 255  -> "
              f"{'visually negligible (<2)' if max_8bit < 2 else 'noticeable' if max_8bit < 10 else 'significant'}", flush=True)
else:
    # No real frames; use random quality check
    for sz, W, H in SIZES:
        if "FP32" not in results[sz] or "FP16_targeted" not in results[sz]:
            continue
        out32 = results[sz]["FP32"]["out"].astype(np.float32)
        out16 = results[sz]["FP16_targeted"]["out"].astype(np.float32)
        diff = np.abs(out32 - out16)
        print(f"  {sz}: max_diff={diff.max():.5f}  mean_diff={diff.mean():.6f}  "
              f"(random inputs — not representative of real image quality)", flush=True)

print(f"\n{'='*50}\nSPEEDUP SUMMARY", flush=True)
for sz in results:
    if "FP32" in results[sz] and "FP16_targeted" in results[sz]:
        fp32_ms = results[sz]["FP32"]["mean"]
        fp16_ms = results[sz]["FP16_targeted"]["mean"]
        ratio = fp32_ms / fp16_ms
        delta = fp32_ms - fp16_ms
        print(f"  {sz:18s}: FP32={fp32_ms:.1f}ms  FP16={fp16_ms:.1f}ms  "
              f"speedup={ratio:.2f}x  delta={delta:+.1f}ms", flush=True)

print("\nDone.", flush=True)
