# BiRefNet High Resolution Decision Note

Decision note for a possible future `Remove Background (High Resolution)` mode in FrameShift.

Date:
- 2026-06-02

Scope:
- study `BiRefNet_HR`
- study `BiRefNet_HR-matting`
- study `BiRefNet_dynamic`
- check ONNX availability
- check ONNX export viability
- check ONNX Runtime DirectML compatibility
- check real high-resolution behavior
- check tile inference options
- check license and redistribution

Non-goal:
- no code change
- no model download into this repository
- no installer change

## Executive Summary

Best future quality candidate:
- `BiRefNet_HR-matting`

Best conservative fallback candidate:
- `BiRefNet_HR`

Not recommended for the next FrameShift integration round:
- `BiRefNet_dynamic`

Main reason:
- the HR family is promising for quality, but the current FrameShift runtime stack depends on ONNX Runtime DirectML, and the BiRefNet ONNX path still collides with `DeformConv` support limits on DirectML.

Conclusion:
- `BiRefNet_HR-matting` is the best product candidate for a future high-resolution mode.
- It is not yet a clean `go` for implementation in FrameShift until Windows validation proves that the ONNX artifact behaves acceptably with CPU fallback and, ideally, with DirectML.

## FrameShift Verdict

### `BiRefNet_HR`

Verdict:
- `Conditional Go`

Why:
- official ONNX artifact exists
- official weights are public
- MIT license is compatible
- model is explicitly trained for 2048x2048 inputs
- less ambitious than the dynamic line

Why not full go:
- no primary-source proof was found that this model runs correctly on ONNX Runtime DirectML
- DirectML documentation still lists `DeformConv` as unsupported
- model size is very large for a Windows utility workflow

Best use if selected later:
- binary-mask high-resolution mode

### `BiRefNet_HR-matting`

Verdict:
- `Preferred Conditional Go`

Why:
- official ONNX artifact exists
- official weights are public
- MIT license is compatible
- trained at 2048x2048
- specifically positioned for image matting with transparency
- best match for a future premium-quality output in FrameShift

Why not full go:
- same DirectML uncertainty as `BiRefNet_HR`
- large ONNX footprint
- no official tile-inference workflow was identified

Best use if selected later:
- future `Remove Background (High Resolution)` mode with better edge transparency

### `BiRefNet_dynamic`

Verdict:
- `No-Go for next integration round`

Why:
- model is attractive on paper because it was trained on arbitrary shapes from 256x256 to 2304x2304
- however, no official ONNX artifact was confirmed in the current official release assets
- the ONNX export path for dynamic shapes is exactly where the project discussion reports ONNX Runtime friction around `DeformConv`

Best use if revisited later:
- R&D only, after ONNX Runtime and DirectML support become clearly usable for this operator path

## Findings

### 1. ONNX availability

Confirmed official ONNX artifacts in the latest official GitHub release:
- `BiRefNet_HR-general-epoch_130.onnx`
- `BiRefNet_HR-matting-epoch_135.onnx`

Not confirmed as an official ONNX release asset in the same release:
- `BiRefNet_dynamic`

Confirmed official release assets via GitHub API:
- `BiRefNet_HR-general-epoch_130.onnx` size `1,098,928,953` bytes
- `BiRefNet_HR-matting-epoch_135.onnx` size `1,098,928,867` bytes
- `BiRefNet_dynamic-general-epoch_174.pth` exists, but no matching ONNX asset was confirmed

Practical takeaway:
- only the HR and HR-matting lines are realistic candidates for a near-term FrameShift prototype.

### 2. ONNX export viability

Current state:
- official BiRefNet repo documents ONNX conversion and publishes ONNX files for multiple variants
- fixed-resolution exports are viable enough to publish and use
- dynamic-shape ONNX export is still where the upstream discussion becomes fragile

Important upstream signal:
- the BiRefNet repo and PR discussion show active work around ONNX export using `DeformConv`
- the discussion explicitly reports ONNX Runtime execution issues for this path

Practical takeaway:
- fixed HR export looks viable
- dynamic export is not mature enough for a Windows-first utility that prioritizes stability

### 3. DirectML compatibility

Current state:
- ONNX Runtime DirectML documentation says DirectML supports up to ONNX opset 20, with exceptions including `DeformConv`
- this is the critical risk for BiRefNet HR and Dynamic ONNX deployment in FrameShift

Implication for FrameShift:
- DirectML compatibility is not proven
- a CPU fallback path would almost certainly be required for any prototype
- shipping a new mode without validating this on real Windows hardware would be high-risk

Practical takeaway:
- DirectML is currently the blocker, not licensing and not model access

### 4. Real maximum resolution

`BiRefNet_HR`:
- trained at `2048x2048`

`BiRefNet_HR-matting`:
- trained at `2048x2048`

`BiRefNet_dynamic`:
- trained on arbitrary shapes in the range `256x256` to `2304x2304`

Interpretation:
- `HR` and `HR-matting` are explicit 2K-class models
- `dynamic` is more flexible on shape, but its training range still does not mean unlimited practical inference size

### 5. High-resolution behavior

`BiRefNet_HR`:
- intended for higher-resolution foreground/background segmentation
- model card shows better results at 2048x2048 than the standard general model at 2048x2048

`BiRefNet_HR-matting`:
- intended for transparency-aware matting at high resolution
- best fit when the product goal is cleaner hair, fur, semi-transparent edges, and soft transitions

`BiRefNet_dynamic`:
- strongest story for “any shape” robustness
- weaker story for FrameShift because the runtime path is less settled

Product reading:
- if FrameShift wants a visible user-facing improvement over `Fast`, `HR-matting` is the strongest candidate.

### 6. Tile inference

Confirmed:
- no official upstream tile-inference workflow was found for these models
- the upstream issue discussion raises tiling or chunking as a question for very high-resolution inputs

Inference from sources:
- tile inference is possible as an application-level strategy, but it would be our own engineering work
- stitching, overlap handling, and seam control would need validation

Practical takeaway:
- tile inference should be treated as a future optimization layer, not as a prerequisite for choosing the model

### 7. License and redistribution

`BiRefNet_HR`:
- Hugging Face model card says `License: mit`

`BiRefNet_HR-matting`:
- Hugging Face model card says `License: mit`

`BiRefNet_dynamic`:
- Hugging Face model card says `License: mit`

Practical takeaway:
- license and redistribution are not the blocking issue here
- from a project-policy perspective, this family is much cleaner than BRIA

## Decision

Recommended future model for `Remove Background (High Resolution)`:
- `BiRefNet_HR-matting`

Reasoning:
- best fit for transparent PNG output quality
- strongest edge-detail promise
- official public weights
- official public ONNX artifact
- MIT license

Fallback if matting complexity becomes too costly:
- `BiRefNet_HR`

Do not choose next:
- `BiRefNet_dynamic`

Reason:
- it is the most interesting research direction, but currently the least dependable path for FrameShift's ONNX Runtime DirectML deployment target

## Minimal Windows Validation Plan

Goal:
- determine whether `BiRefNet_HR-matting` is implementable in FrameShift without violating stability and responsiveness priorities

### Phase A - artifact validation

1. Download the official `BiRefNet_HR-matting-epoch_135.onnx`.
2. Record exact SHA256.
3. Confirm the model loads in a standalone ONNX Runtime test harness on Windows.
4. Log input metadata, output metadata, and tensor element types.

Pass criteria:
- reproducible local load
- stable metadata
- no custom runtime dependency beyond ONNX Runtime

### Phase B - provider validation

1. Try session creation with DirectML.
2. Run inference on a simple 2048x2048 test image.
3. If DirectML fails, retry on CPU.
4. Record:
- session creation result
- first inference result
- fallback behavior
- user-visible error cleanliness

Pass criteria:
- either DirectML works reliably, or CPU fallback is clean and acceptable for a future optional mode

Fail criteria:
- model cannot run at all under the current FrameShift ONNX Runtime stack
- DirectML fails in a way that is not recoverable with clean CPU fallback

### Phase C - quality validation

Use at least:
- hair / fur subject
- soft transparent edge subject
- product photo with clean boundaries
- difficult mixed background

Compare:
- current Fast mode
- `BiRefNet_HR`
- `BiRefNet_HR-matting`

Judge:
- edge fidelity
- haloing
- transparency realism
- false cutouts
- seam quality after resize-back to source resolution

Pass criteria:
- `HR-matting` produces a visible user-facing gain over Fast on difficult edges

### Phase D - performance validation

Run on representative Windows hardware:
- one integrated GPU machine
- one mid-range DirectX 12 GPU machine if available
- CPU-only fallback case

Measure:
- cold load time
- first inference time
- repeated inference time
- peak RAM
- peak VRAM if measurable
- cancellation behavior

Pass criteria:
- no crash
- no orphan resources
- acceptable responsiveness for a clearly labeled high-resolution mode

### Phase E - product fit validation

Questions to answer:
- is the download size acceptable for on-demand delivery
- is CPU fallback still useful for real users
- should the mode be labeled `High Resolution`, `High Quality`, or both
- does this need a separate warning about slower processing

## Shipping Recommendation

Current recommendation:
- do not start implementation directly from this note
- first run a standalone Windows validation spike with `BiRefNet_HR-matting`

If the spike succeeds:
- proceed with a future FrameShift integration using:
- `Fast` = current model
- `High Resolution` = `BiRefNet_HR-matting`

If the spike fails due to DirectML:
- test whether CPU-only fallback is still acceptable
- if CPU-only is too slow, stop and keep the current Fast/Quality roadmap on the fixed 1024 family

## Sources

- Official BiRefNet repository:
  - https://github.com/ZhengPeng7/BiRefNet
- Official BiRefNet release assets:
  - https://github.com/ZhengPeng7/BiRefNet/releases
- `BiRefNet_HR` model card:
  - https://huggingface.co/ZhengPeng7/BiRefNet_HR
- `BiRefNet_HR-matting` model card:
  - https://huggingface.co/ZhengPeng7/BiRefNet_HR-matting
- `BiRefNet_dynamic` model card:
  - https://huggingface.co/ZhengPeng7/BiRefNet_dynamic
- BiRefNet paper:
  - https://arxiv.org/abs/2401.03407
- ONNX Runtime DirectML execution provider documentation:
  - https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html
- BiRefNet ONNX export / dynamic-shape discussion:
  - https://github.com/ZhengPeng7/BiRefNet/pull/167
- Upstream issue discussing very high-resolution usage strategies:
  - https://github.com/ZhengPeng7/BiRefNet/issues/247

