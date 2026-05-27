"""Quick load+inference test for FP16 v3 model."""
import time, numpy as np, onnxruntime as ort

for label, path in [
    ("FP32",    r"C:\Users\Adrien\AppData\Local\FrameShift\AI\Models\rife\rife_v426_x2.onnx"),
    ("FP16_v3", r"C:\Users\Adrien\AppData\Local\FrameShift\AI\Models\rife\rife_v426_x2_fp16_v3.onnx"),
]:
    print(f"\n=== {label} ===", flush=True)
    opts = ort.SessionOptions()
    opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    try:
        sess = ort.InferenceSession(path, opts,
            providers=[("DmlExecutionProvider", {}), "CPUExecutionProvider"])
        prov = sess.get_providers()[0]
        print(f"  Provider: {prov}", flush=True)

        # run 720p_padded
        H, W = 768, 1280
        img0 = np.random.rand(1,3,H,W).astype(np.float32)
        img1 = np.random.rand(1,3,H,W).astype(np.float32)
        feeds = {"img0": img0, "img1": img1}

        # warmup
        for _ in range(2):
            out = sess.run(None, feeds)

        # bench 5 runs
        times = []
        for _ in range(5):
            t0 = time.perf_counter()
            out = sess.run(None, feeds)
            times.append((time.perf_counter()-t0)*1000)

        import numpy as np2
        arr = np2.array(times)
        print(f"  720p: mean={arr.mean():.1f}ms min={arr.min():.1f}ms  out_shape={out[0].shape}", flush=True)

        # run 1080p_padded
        H2, W2 = 1088, 1920
        img0b = np.random.rand(1,3,H2,W2).astype(np.float32)
        img1b = np.random.rand(1,3,H2,W2).astype(np.float32)
        for _ in range(2):
            out2 = sess.run(None, {"img0": img0b, "img1": img1b})
        times2 = []
        for _ in range(5):
            t0 = time.perf_counter()
            out2 = sess.run(None, {"img0": img0b, "img1": img1b})
            times2.append((time.perf_counter()-t0)*1000)
        arr2 = np2.array(times2)
        print(f"  1080p: mean={arr2.mean():.1f}ms min={arr2.min():.1f}ms  out_shape={out2[0].shape}", flush=True)

    except Exception as e:
        print(f"  FAILED: {e}", flush=True)

print("\nDone.", flush=True)
