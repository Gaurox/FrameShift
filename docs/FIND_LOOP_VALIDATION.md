> Historical Find Loop work preserved at `137311e` (formerly labelled 1.22).
> Carried into the unpublished 1.23 integration branch on 8 November 2026.
> Installer, version and qualification results below describe the old candidate,
> not the multilingual 1.22.0 release or acceptance of the new 1.23 build.
> See [the integration plan](RELEASE_PLAN_1.22_1.23.md) for current status.

# Find Loop validation

Current candidate: the canonical **29 October 2026** build from clean commit `f9c0444bf52d94830588bd83ee45211b24b109cb`, including improvement lots 0–9 and lot 10 qualification coverage. The complete Release suite passes **1,302 tests, 0 failures, 18 conditional skips, 1,320 total**. The installer was rebuilt and the existing installation successfully replaced; installed/published application hashes match. See [lot 10](#lot-10-candidate-qualification-on-29-october-2026) for exact artifacts, measurements and the Windows acceptance still blocked by an unrelated security dialog. Earlier sections describe historical source/build states, not this candidate.

## Initial qualification on 13 and 14 October 2026

Version under test: **1.22.0**, local uncommitted working tree. Official SDK **8.0.425**, located at `.buildcheck/sdk/8.0.425`; Windows/.NET 8/WinForms. This report distinguishes automated validation from manual visual acceptance.

## Results

- Final application build: **0 warnings, 0 errors**. The official ImageSharp licence validator accepts the owner's Community licence.
- Canonical `build_installer.ps1 -AllowDirty`: **826 passed, 0 failed, 17 skipped, 843 total**, full Release suite in **5 minutes 43 seconds**, without an exclusion filter. Skips concern the opt-in/local-model AI integrations and Whisper corpora, not Find Loop.
- The feature has **41 automated cases**, including spatial indexing of long cycles with constant global color mean, native timestamp precision, one/three-cycle VFR end duration, 10/23.976 FPS multi-chunk reversal and delayed audio. The final canonical suite includes them all.
- Self-contained `win-x64` publish passed the canonical payload/runtime/tool/notice checks. Inno Setup compilation succeeded. Installer: `installer/FrameShift_1.22.0_Setup.exe`, **175,087,099 bytes**, SHA-256 `76C1C9FC6404ACE813DED873D78A70B7270CC3F1D4F7B6353B7D694B139608DC`. No `.lic` file is present in the published payload. Nothing was installed or published to GitHub.
- Real WinForms end-to-end check at **96 DPI (100%), PerMonitorV2, 1120×760**, Light appearance: Find Loop produced three visible candidates, the continuous preview played all **90 frames**, manual boundary editing invalidated the old candidate, and the Export Loop button produced the edited **29-frame** segment through `CutVideoAction`. Capture, JSON results and videos are in `scratch/loop-validation/ui-e2e/`.
- VFR duration check: **16 frames / 2.2 seconds** for one cycle, **48 frames / 6.6 seconds** for three. The last frame's duration is corrected without introducing a duplicate; exported EOF now matches the internal repeated seams.
- Two available private clips (`video.mp4`, `image exemples/ComfyUI_00723_.mp4`) were analyzed. Neither produced a satisfactory natural candidate at Normal/1 s; both produced three-cycle Ping-Pong previews. Decoded junction strips were inspected. Random-motion/evolving-light/foreground rejection cases are synthetic; real flames/water acceptance remains manual.
- The private licence was verified, moved from the Desktop to `%USERPROFILE%\Documents\FrameShift\Licences\sixlabors.lic`, and located persistently through the Windows user `SixLaborsLicenseFile` variable. It expires on **1 January 2028**; renewal instructions are beside it and in README. Its content is neither tracked nor packaged.

Evidence: `scratch/loop-validation/*.trx`, `canonical-build.log`, `ui-e2e/result.json`, `ui-e2e/export-result.json`, `vfr-end/` and local footage previews/analysis in `scratch/loop-validation/real/`. This ignored folder contains local evidence, not runtime resources or distributable test fixtures.

## Manual acceptance still required

- Watch the real-footage exports across both junctions and assess perceptual quality, particularly chaotic flames/water and moving traffic. Automated checks do not certify a convincing seam for every video.
- Test Cut Video with all loop controls expanded at actual Windows 100/150/200/300% DPI, both themes, enlarged text and moving between different-DPI monitors. Hidden layout tests/reflows do not certify real DPI transitions.
- Test the installed binary and Explorer Cut Video entry with a long/UNC path. Packaging verification does not imply installation acceptance.
- Confirm that a candidate can be adjusted, previewed again and exported through the shared queue with the desired audio option.

Limitations are documented in [FIND_LOOP](FIND_LOOP.md): heuristic bounded search, silent video preview, SDR MP4 export, VFR normalization in Ping-Pong and temporary-work budget.

## Implementation foundation on 29 October 2026

The improvement plan has started with the search reference corpus and the first contracts/color changes. The source tree remains based on `a55498e` with local modifications; the already-built 24 October installer is unchanged and does not contain this work.

- Before changes: **55 passed, 0 failed, 0 skipped**, covering existing VideoLoop and BoundedMediaCapture classes, in **47 seconds**.
- Final application Release build with SDK **8.0.425**: **0 warnings, 0 errors**.
- Final targeted regressions: **121 passed, 0 failed, 1 skipped, 122 total**, in **50 seconds**. The single skip is the explicitly enabled long-corpus measurement theory. The selection includes VideoLoop, BoundedMediaCapture, JoinVideosPlanner and AddSubtitlesToVideoPlanner tests; this is not a complete release-suite run.
- The measurement theory was separately enabled during an earlier combined pass: **126 passed, 0 failed, 0 skipped**, including its **nine** scenarios, in **1 minute 46 seconds**. Four additional contract/color edge cases were added afterward and are included in the final targeted regressions. The detector's sampling, seed ordering, quality rules and ranking stayed unchanged between these passes.
- Real exports of tagged **SDR 10-bit and 12-bit** FFV1 sources now reach adjacent H.264 output instead of the historical HDR rejection. Tests check codec, pixel format, duration and publication cleanup. PQ/HLG and ambiguous metadata are rejected before decoder/output work. UI tests check the preflight placement and disabled loop commands while ordinary Cut Video stays enabled.
- The corpus tests establish an exact whole-source cycle, four distinct cyclic regions and an exact four-second cycle with an imperfect one-second alternative. The first source still obtains no automatic candidate: this records a search limitation for later lots, not successful detection. The longer exact candidate still appears behind shorter approximate candidates in the current ranking.

Evidence: `scratch/loop-validation/improvements/lot0-2026-10-29/{before,lot0-lot1-final,lot0-lot1-regressions,color-media}.trx`. Initial sandbox launches could not connect VSTest to testhost over localhost (socket error 10013); the executed validation runs used an unrestricted local test process.

Search measurements used deterministic **64×48, 15 FPS, FFV1** sources on Windows build **26200**, **.NET 8.0.31**, with **24 logical processors**. Source generation precedes the measured analysis. Each JSON report includes source and assembly SHA-256 hashes, candidates, limits and phase diagnostics under `scratch/loop-validation/improvements/baseline/20261029T063830985Z-6df02d7930b14b8aba01ac1a1639f38d/`.

| Source scenario | Duration | Analysis wall time | Sampled frames | Candidates | Largest RGB temporary file |
|---|---:|---:|---:|---:|---:|
| Periodic | 5 s | 1.21 s | 40 | 3 | 0.35 MiB |
| Periodic | 30 s | 1.92 s | 240 | 3 | 2.11 MiB |
| Periodic | 50 s | 1.76 s | 400 | 3 | 3.52 MiB |
| Periodic | 1 min | 1.90 s | 480 | 3 | 4.22 MiB |
| Periodic | 5 min | 3.05 s | 2,400 | 3 | 21.09 MiB |
| Periodic | 1 h | 4.64 s | 3,600 | 3 | 31.64 MiB |
| Whole-source cycle | 5 s | 0.06 s | 40 | 0 | 0.35 MiB |
| Four cyclic regions | 12 s | 1.72 s | 96 | 3 | 0.84 MiB |
| Longer exact cycle | 12 s | 1.95 s | 96 | 3 | 0.84 MiB |

These low-resolution measurements establish search references, not HD/4K performance targets. Test-host working-set peaks range from about **131 to 270 MiB** and exclude FFmpeg child processes; allocation totals include test-host background work. Temporary-file measurements cover the largest sequential RGB analysis file and exclude source generation, preview and export intermediates. HD/4K resource qualification, private user footage, HDR conversion, the simplified UI and perceptual acceptance remain in later plan lots.

To repeat the long corpus explicitly, set `FRAMESHIFT_LOOP_BENCHMARK=1` and run the `VideoLoopPerformanceTests` filter with the configured SDK. Ordinary regression runs leave that theory skipped; required portable corpus correctness tests still run. No installation or publication was performed.

## Lot 2 contextual interface on 29 October 2026

Source-only follow-up on the same local 1.22.0 working tree; SDK **8.0.425**, Windows/.NET 8/WinForms. The 24 October installer has not been rebuilt, installed or published for this lot.

The interface now has explicit Cut, Natural and Forward/backward contexts and one persistent primary footer command. Back to Cut restores ordinary export. Candidates use a native read-only grid, keyboard selection and a secondary duration sort. Manual bounds are labelled Custom selection without mutating the candidate; new analysis retains the user's choice. Advanced options start collapsed, minimum duration is optional (Auto keeps the existing one-second micro-loop guard), and speed is contextual to forward/backward. Stop search and Stop preview are separate from Cancel. Replacement searches and context transitions cancel and await obsolete work; progress callbacks are scoped to their current cancellation token. The existing full three-cycle preview remains until lot 3, and the detector's three-result limit/ranking remain until later lots.

- Application Release build: **0 warnings, 0 errors**.
- Expanded Release regressions before the final scroll/selection refinements: **279 passed, 0 failed, 1 skipped, 280 total**, in **3 minutes 38 seconds**. Selection: VideoLoop, BoundedMediaCapture, UiFoundation, UiEditor, UiKeyboard, UiResourceLifetime, ModalClosingAudit and ProgramCli classes. The skip is the opt-in long-corpus measurement; no exclusion was added to bypass a failure. This is targeted regression coverage, not the complete canonical release suite.
- UI workflow Release pass after the viewport adjustment: **45 passed, 0 failed, 0 skipped**, in **1 minute 52 seconds**. A subsequent exact-selection refinement retains native timestamps even when the chosen candidate disappears from new results, instead of rounding through average-FPS fields. The affected selection/list regressions were rerun on that final source: **5 passed, 0 failed, 0 skipped**, in **4 seconds**, including explicit `0.215`/`2.275`-second export bounds.
- Actual modal keyboard tests cover Enter in Cut/Natural/Forward-backward and Escape in Loop. Tests also cover zero/one/seven list results, untouched candidate restoration after manual edits, duration sorting, Auto/invalid minimums, candidates longer than 600 seconds, reanalysis, cancellation, repeated close and pending-work cleanup.
- A local review harness displayed the form with three injected candidate records and a synthetic still preview, captured it through WinForms `DrawToBitmap`, and inspected **Light and Dark at 96 DPI**, with **1120×760 and 480×340 client areas**. Footer actions remain visible; the loop body scrolls at reduced size. The post-search viewport was adjusted to reveal Full loop preview. These captures check layout, not media detection, playback quality or desktop input routing.

Evidence under `scratch/loop-validation/improvements/lot2-2026-10-29/`: `lot2-release-regressions.trx`, `lot2-ui-final.trx`, `lot2-selection-final.trx`, focused selection/modal reports, `ui-{Light,Dark}-{normal,compact}.png`, `ui-review.txt` and the local `UiReview/` harness. Scratch files are review evidence, not test or runtime dependencies.

Remaining acceptance: actual **150/200/300% DPI**, enlarged Windows text, monitor transitions and the installed/Explorer flow. HDR conversion, the short junction preview, broader candidate search and batch export are outside lot 2. No new installer or publication was performed.

## Lot 3 junction proxies on 29 October 2026

Source-only continuation of the local 1.22.0 candidate with SDK **8.0.425**. The 24 October installer is unchanged; this work has not been installed or published.

- Final application Release build: **0 warnings, 0 errors**.
- Expanded Release regression pass: **310 passed, 0 failed, 1 skipped, 311 total**, in **4 minutes 3 seconds**. Selection: VideoLoop, BoundedMediaCapture, UiFoundation, UiEditor, UiKeyboard, UiResourceLifetime, ModalClosingAudit and ProgramCli classes. The skip remains the explicitly enabled long-corpus measurement. This is targeted coverage, not a complete canonical installer qualification.
- Lot 3 adds **30 cases**: **27** preview plan/media cases and **3** UI cache/lifetime cases. The decoded tests compare source-frame order and the actual last→first images with a full-resolution reference export. Natural junction artifacts have one internal seam; full three-cycle proxies have exactly two cycle seams. Preview audio is absent even when the export retains audio and seven repetitions were requested; the reference export still contains its audio.
- Real synthetic **36-second 1920×1080/8 FPS** and **90-second 3840×2160/2 FPS** sources produce **3-second 960×540** junction proxies. The 4K source's whole-selection historical estimate exceeds 8 GiB, while the new excerpt-based estimate permits its junction. A full 36-second HD proxy has **864 frames / 108 seconds**, without audio. A one-hour full-proxy request is rejected before decoding with an explicit suggestion to use Preview junction. These are functional/resource-scope checks, not comprehensive HD/4K performance targets or a speedup claim.
- VFR tests cover variable 100/200 ms intervals, the final held image across the internal seam, an image held throughout the requested tail window, and a one-frame selection. A microsecond encoder/MP4 time base preserves window boundaries falling between nominal frame intervals. The streaming collector now uses integer-tick timings instead of rounded `pts_time` text. The shared forward renderer seeks just before selection and trims against original video timestamps; a separate video/container offset case passes alongside the existing delayed-audio regressions.
- Forward/backward tests at **1×, 0.75× and 0.5×** inspect both separate turning-point clips and their endpoint order/duration. Reverse intermediates retain the bounded chunk path. Short selections use two continuous cycles to expose both direction changes. Separate clips are labelled and played independently; they are not concatenated to fabricate a continuous seam between unrelated excerpts.
- UI tests prove reuse of the same session bundle on replay, independence from export audio/repetition changes, decoder termination before obsolete-file deletion, repeated modal closure cleanup, and retained selection/retry after rendering failure. Cache identity includes source path/size/write time, bounds, mode/speed, preview kind, color/geometry/timeline metadata and render version. Only one bundle is retained per session (one file, or two distinct turn files).
- Actual layout review at **96 DPI**, Light/Dark, **1120×760 and 480×340**, covers Preview junction and the secondary Full loop preview under Advanced options. The WinForms `DrawToBitmap` captures use injected candidates and a synthetic still image; they certify the displayed controls/footer/scroll layout only. Real proxy rendering and playback lifetime are covered separately by media/modal tests. No real user-footage perceptual acceptance is claimed.

Evidence: `scratch/loop-validation/improvements/lot3-2026-10-29/lot3-release-regressions.trx`, earlier focused render/cache/VFR reports, `ui-{Light,Dark}-{normal,compact}*.png`, `ui-review.txt` and the local `UiReview/` harness. Early development failures exposed a nominal-frame-time-base rounding of half-frame windows and an unbounded synthetic fixture input; both were corrected before the final green Release pass. Scratch evidence is not a runtime or portable fixture dependency.

At completion of lot 3, lot 4 was still pending: candidate still captures, native VFR navigation and the lazy index had not been delivered. That historical limitation is addressed by the lot 4 work below. Sparse VFR tail lookup can still require multiple seeks. Actual high-DPI/enlarged-text/multi-monitor acceptance, private-footage quality review and installed Explorer qualification remain open. No new installer or publication was performed.

## Lot 4 native navigation on 29 October 2026

Source-only continuation of the local 1.22.0 candidate, using SDK **8.0.425**. The 24 October installer remains unchanged; no installation or publication was performed.

- Final application Release build: **0 warnings, 0 errors**.
- Expanded Release regressions: **333 passed, 0 failed, 1 skipped, 334 total**, in **4 minutes 11 seconds**. The selected classes are VideoLoop, BoundedMediaCapture, UiFoundation, UiEditor, UiKeyboard, UiResourceLifetime, ModalClosingAudit and ProgramCli. The skip remains the opt-in long-corpus measurement. This is targeted coverage rather than complete canonical installer qualification.
- Lot 4 adds **23 cases**: **16** timing/index/media tests and **7** UI timing/lifetime tests. Existing media/export, color, proxy/cache, CLI, keyboard and modal-close regressions remain green.
- Native captures use original integer PTS and their time base. Tests compare the first/last preview pixels with decoded exports at **23.976, 59.94 and 240 FPS**, including three-second synthetic sources shifted to native timestamps near **40,000 seconds**. These timestamp-shifted cases test precision at large PTS; they do not represent decoding eleven hours of footage. Additional MP4 tests verify that unaligned analysis seeks retain original native timestamps rather than adding rounded seek-relative `pts_time` text. Video/container offset, first/last frames, one-frame natural export and irregular VFR neighbors/exclusive ends pass.
- UI tests verify first included Start and last included End images, exact native export after frame/time edits, queued arrow clicks, preservation of the untouched boundary, restoration of the original candidate, rejection of obsolete captures, cancellation/repeated close during indexing, and source-change invalidation of candidates, index, bitmap/proxy state and export commands.
- The ordinal index is built only when a frame-number mapping is needed. It stores a committed prefix in a temporary binary file at **24 bytes per frame**, capped at **256 MiB**. An extension uses a temporary transaction chunk; cancellation discards it and retains the old prefix. Up to eight windows of 4,096 native timestamps bound the separate in-memory time cache. A request past the index budget fails before decoding with a suggestion to use time bounds. The first far ordinal request still requires decoding the necessary prefix; the UI reports that work and supports Stop preview/Cancel.

The Release reuse measurement used a synthetic **160×96 FFV1, 30 FPS, 60-second** source on Windows build **26200**, **24 logical processors**, **.NET 8.0.31**. Generation precedes these measured requests; the TRX output records the source SHA-256 and environment.

| Request | Wall time | Additional decoded/indexed frames | Decode calls |
|---|---:|---:|---:|
| First ordinal selection, frames 1500–1600 | 1,790.03 ms | 1,729, including bounded lookahead | 1 |
| Nearby edit, frames 1501–1601 | 0.93 ms | 0 | 0 |

A later extension reaches the actual 1,800-frame EOF with one further decode, leaving a **43,200-byte** index. The repeat proves reuse, not a universal speedup: source resolution, codec/GOP, VFR distribution and concurrent work affect the first decode. Hour-long/high-resolution ordinal-index performance is not qualified by this one-minute low-resolution case.

Actual UI review used a generated seven-frame irregular VFR source and the real PNG capture path at **96 DPI**, Light/Dark, **1120×760 and 480×340**. End visibly shows the **2.000-second** frame while the exclusive end stays **4.000 seconds**. An ordinal Start edit to frame **4** resolves to **0.800 seconds**, preserves that end and displays the known ordinal range **4–5** after indexing. Captures cover approximate labels before indexing, resolved labels afterward, footer visibility and scrolling. They do not certify actual 150/200/300% DPI, enlarged text or monitor transitions.

Evidence: `scratch/loop-validation/improvements/lot4-2026-10-29/lot4-release-regressions.trx`, focused timing/UI reports, `ui-{Light,Dark}-{normal,compact}-{native-end,indexed}.png`, `ui-review.json` and the local `UiReview/` harness. Early focused tests exposed a debounce timer superseding an explicit capture and native preview invalidation cancelling its own queued steps; both were corrected before the final green pass. Scratch evidence is not a runtime dependency.

At completion of lot 4, broader search coverage remained pending; lot 5 below addresses it. Perceptual calibration, later export/HDR/batch lots, high-DPI/multi-monitor and installed/Explorer acceptance, and private-footage perceptual review remain open. Source changes are detected through path/size/write/creation metadata before operations and a 750 ms timer while shown; the editor invalidates stale state and asks to reopen rather than silently reusing old probe metadata. No installer was rebuilt or published.

## Lot 5 search coverage on 29 October 2026

Source-only continuation of the local 1.22.0 candidate with SDK **8.0.425**. The 24 October installer has not been rebuilt, installed or published.

- Final application Release build: **0 warnings, 0 errors**.
- Final expanded Release regressions with the long-corpus measurement enabled: **360 passed, 0 failed, 0 skipped**, in **5 minutes 56 seconds**. Selection: VideoLoop, BoundedMediaCapture, UiFoundation, UiEditor, UiKeyboard, UiResourceLifetime, ModalClosingAudit and ProgramCli. This includes all **nine** measurement scenarios and is targeted coverage, not complete canonical installer qualification.
- Lot 5 adds **18 cases**: **15** search/coverage cases, **2** UI search cases and **1** native endpoint case. They cover more than three results, twenty cyclic regions, tracks beyond the former first sixteen, deterministic bounded comparisons, a 120-second minimum on repeated signatures, matches 600 seconds apart, a one-hour continuous sampling plan and a four-hour plan with explicitly reported gaps. Real searches check whole-source cycles, minimum duration equal to source duration, immutable progressive batches, cancellation during sampling/selection/refinement and source-change rejection.
- Sampling uses blocks of at most **256 RGB frames**, retaining only compact signatures across blocks. Up to **57,600 signatures** permit continuous temporal coverage through two hours at 8 FPS; longer sources use distributed windows and report unsampled gaps. Appearance/motion matches are distributed across up to **24 temporal regions** and duration families, with **192 refinement tracks** and **512 retained results** as explicit budgets. Sampling remains a search limitation even when windows cover the full timeline. These budgets do not prove exhaustive discovery.
- The source's first/last seam is checked separately against native video endpoints. Focused failures exposed a millisecond-rounded CFR endpoint and a decode window containing the actual last frame despite ending just before its held interval ended. The final implementation uses the known native endpoint when that last frame is present, and bounds video-only CFR reconciliation to one native clock tick; longer audio/subtitle timelines are not used for this reconciliation. The final green pass includes these corrections and missing-packet-duration guards.
- The candidate list accepts selection during search and preserves bounds, native capture state, duration sorting and scroll position as batches arrive. Search status distinguishes in-progress results and unsampled temporal gaps. Same-bound quality updates reuse the preview cache. Candidates remain labelled **To review**, and shortest-first ranking remains pending lot 6 perceptual calibration.

The final measurements use the same deterministic scenario definitions as lot 0: **64×48, 15 FPS, FFV1**, Windows build **26200**, **24 logical processors**, **.NET 8.0.31**. Generation precedes measurement. Per-report source/assembly SHA-256 hashes, full candidates, limits and phase diagnostics are recorded under `scratch/loop-validation/improvements/baseline/20261029T094429011Z-3e36f948558a42b49d9a25af8624454d/`.

| Source scenario | Duration | Analysis wall time | Signatures | Blocks | Tracks | Candidates | Largest RGB temporary file |
|---|---:|---:|---:|---:|---:|---:|---:|
| Periodic | 5 s | 0.88 s | 40 | 1 | 9 | 7 | 0.35 MiB |
| Periodic | 30 s | 6.76 s | 240 | 1 | 84 | 71 | 2.11 MiB |
| Periodic | 50 s | 12.77 s | 400 | 2 | 166 | 159 | 2.25 MiB |
| Periodic | 1 min | 14.76 s | 480 | 2 | 192 | 178 | 2.25 MiB |
| Periodic | 5 min | 16.90 s | 2,400 | 10 | 192 | 192 | 2.25 MiB |
| Periodic | 1 h | 33.82 s | 28,800 | 113 | 192 | 193 | 2.25 MiB |
| Whole-source cycle | 5 s | 0.39 s | 40 | 1 | 2 | 1 | 0.35 MiB |
| Four cyclic regions | 12 s | 1.41 s | 96 | 1 | 16 | 12 | 0.84 MiB |
| Longer exact cycle | 12 s | 1.93 s | 96 | 1 | 23 | 22 | 0.84 MiB |

The one-hour reference now samples **28,800** frames instead of **3,600**, retains **193** candidates instead of **3**, and bounds its largest RGB temporary file to **2.25 MiB** instead of **31.64 MiB**. It takes **33.82 seconds**, compared with the historical **4.64 seconds**: broader coverage and refinement cost more time, so this is not a speedup claim or a controlled identical-binary comparison. Signature/time payload for that source is **4.17 MiB**, excluding managed-object overhead. Test-host working-set peaks across the nine scenarios range from about **169 to 305 MiB**, excluding FFmpeg children and including other test-host work. RGB temporary-file measurements exclude source generation, timing-index files, preview and export intermediates. These synthetic low-resolution measurements do not qualify HD/4K search speed or perceptual quality.

Actual UI review ran the real four-region search and native PNG capture path, producing **12 candidates** in Light/Dark at **96 DPI**, with **1120×760 and 480×340 client areas**. Captures show the duration-sorted list, bounded scrolling and visible footer commands; the compact layout uses horizontal and vertical scrolling. Media detection is real in this harness, but these captures do not certify desktop input routing, higher DPI or perceptual acceptance.

Evidence under `scratch/loop-validation/improvements/lot5-2026-10-29/`: `lot5-release-final.trx`, focused search/UI/endpoint reports, `ui-{Light,Dark}-{normal,compact}-results.png`, `ui-review.json` and the local `UiReview/` harness. The final check found no running FFmpeg/FFprobe processes. Scratch evidence is not a runtime or portable test dependency.

At completion of lot 5, perceptual refinement and ranking were still pending. Lot 6 below supersedes that limitation. Actual 150/200/300% DPI, enlarged text, monitor transitions, installed/Explorer flows and private-footage review remain open. No installer or publication was performed.

## Lot 6 perceptual refinement on 29 October 2026

Source-only continuation of the local 1.22.0 candidate, using the repository's **SDK 8.0.425**. The 24 October installer is unchanged; no installation or publication was performed. The working tree retains the preceding lots' local changes.

- Final application Release build: **0 warnings, 0 errors**.
- Final expanded Release regressions, with the long-corpus measurement enabled: **399 passed, 0 failed, 0 skipped**, in **6 minutes 55 seconds**. Selection: VideoLoop, BoundedMediaCapture, UiFoundation, UiEditor, UiKeyboard, UiResourceLifetime, ModalClosingAudit and ProgramCli. All **nine** benchmark scenarios are included. This is targeted regression coverage, not the complete canonical installer qualification. Evidence: `lot6-release-final.trx`.
- Lot 6 adds **39 cases**: **32** perceptual/unit cases, **6** media cases and **1** UI assessment/update case. Before implementation, the initial detector/corpus/search selection passed **37/37** in **10 seconds**; the final focused quality/native-FPS/animation/UI pass passed **41/41** in **8 seconds**. Updated existing tests enforce quality-first semantics and native-time floating point tolerance without excluding any class or failing scenario.

The coarse pass remains at **64×48**. Native refinement now preserves display geometry, including rotation and pixel aspect ratio, at up to **192 pixels** on the longest side. Each window holds at most **384 frames**; previous windows are cleared before reading the next track. Signed, time-normalized movement and the actual last-to-first transition participate before the best **24 pairs per shortlist** are checked at full analysis resolution. Boundary verification has its own bounded shortlist. No source-wide native all-pairs search is introduced.

The evaluator records appearance, local lighting, motion, seam continuity and scene change separately. Context adapts between **120 and 350 ms**, with at most **five** aligned image comparisons rather than five fixed neighboring frames. Incoming, seam and outgoing velocities use native time intervals. Static content has an explicit zero-motion path. At EOF, missing exterior context is not invented; irregular acceleration contributes uncertainty instead of cancelling every seam error.

The initial corpus calibration uses total error `max(appearance, scene) + 0.25 × lighting + 0.5 × motion + 0.75 × continuity`. Acceptance remains Strict **0.025**, Normal **0.055**, Permissive **0.095**. Visible assessments use fixed thresholds at every sensitivity: **Promising ≤ 0.012**, **Approximate ≤ 0.04**, otherwise **Needs review**. Missing measurements also mean Needs review. These labels are heuristic estimates, not probabilities or user-accepted perceptual grades. The numeric ranking score is monotonic in error and is not shown as a percentage.

Candidates now rank by quality before duration, with deterministic duration/start tie-breaking and near-duplicate removal. Equivalent static candidates are grouped by duration family. Rounding score/duration sort keys to six decimal places avoids reordering equivalent cycles due to floating point noise; their native boundaries are unchanged. Existing animation/rotation regressions now require their known two-second cycle to remain available without requiring it to outrank a better longer seam. Benchmark boundary assertions allow **1e-7 seconds** of floating point comparison tolerance, far below the native fixture clock; the implementation does not round candidate times to satisfy them.

Auto retains **one second** on sources at least two seconds long. Shorter sources use half the available video duration, aiming for three native frames, capped by availability with a 0.1-second floor. A nullable analysis minimum represents Auto and is resolved again after native video EOF, so longer audio does not exclude a short video cycle. Explicit advanced values keep their existing meaning.

Quality evidence:

- The deterministic longer-better source has a true **four-second** period with imperfect one-second alternatives. The best retained four-second seam is **Promising**, with error effectively zero; a one-second alternative has error about **0.01766**, **Approximate**, and follows the better long candidates. Both are exported as three continuous cycles and decoded across both internal transitions. Tests compare expected source images and account for the same image's H.264/color roundtrip error; they do not mistake that encoding loss for seam failure. `quality-exports.json`, both MP4s and their junction contact sheets retain the measurements.
- A real **1920×1080** FFV1 fixture changes a **20×20** detail on a static background. At 64×48 its error is about **0.00958**, which would be Promising; the **192×108** refinement rejects the same mismatch at Strict sensitivity. `fine-detail.json` records both outcomes. This qualifies that controlled detail, not arbitrary full-resolution details.
- Mandatory cases cover portrait/wide/rotated/anamorphic dimensions and descriptor mapping, small changed foreground, opposite movement, local illumination, skipped phase, duplicated pose/pause, noisy unrelated endpoints, equivalent CFR and irregular VFR motion, 10/24/60/240 FPS bounded context, static grouping, score/assessment boundaries, deterministic ties and cancellation. Real media searches exercise **240 FPS** and **24000/1001 FPS**, plus a short **0.8-second** source with and without longer audio.
- The two available optional local five-second clips were analyzed and their fallback forward/backward exports inspected by the existing media tests. Both Normal searches retained zero natural candidates. This is bounded-search evidence, not proof that those clips lack a loop or an annotated before/after perceptual acceptance. Private-footage quality review and the user's annotated 36-second example remain open.

The UI keeps the engine's quality order until the user sorts by duration. Labels can update without resetting selected native bounds or rebuilding the silent preview cache. The Junction column has a DPI-scaled minimum width so compact layouts can reveal complete labels using horizontal scrolling. A local real-search/native-PNG harness captures **41 candidates** in Light/Dark at **96 DPI**, with **1120×760 and 480×340** client areas. The compact captures deliberately scroll horizontally to reveal Junction; the footer stays visible. These captures do not certify real keyboard routing, higher DPI, enlarged text or monitor transitions.

The nine measurement scenarios were run again with `FRAMESHIFT_LOOP_BENCHMARK=1`, after the UI harness finished, using the same **64×48 / 15 FPS / FFV1** definitions as lot 0. Reports, source hashes, assembly hash and full candidate metrics are under `scratch/loop-validation/improvements/baseline/20261029T105526019Z-06e1dcd343d44e7a98ef1c643cc64925/`.

| Source scenario | Duration | Search wall time | Candidates | Largest RGB file |
|---|---:|---:|---:|---:|
| Periodic | 5 s | 1.05 s | 14 | 0.35 MiB |
| Periodic | 30 s | 7.29 s | 181 | 2.11 MiB |
| Periodic | 50 s | 14.47 s | 372 | 2.25 MiB |
| Periodic | 1 min | 17.98 s | 437 | 2.25 MiB |
| Periodic | 5 min | 18.76 s | 512 | 2.25 MiB |
| Periodic | 1 h | 35.84 s | 512 | 2.25 MiB |
| Whole-source cycle | 5 s | 0.39 s | 1 | 0.35 MiB |
| Four cyclic regions | 12 s | 1.65 s | 13 | 0.84 MiB |
| Longer exact cycle | 12 s | 2.31 s | 38 | 0.84 MiB |

The hour retains **28,800** compact signatures, **192** examined tracks and **7,324** native refinement frames across bounded windows. The 512-result guard is reached on the five-minute/hour periodic fixtures and is reported explicitly. Test-host working-set peaks across the scenarios are about **175–319 MiB**, excluding FFmpeg children and including other test-host work. RGB figures exclude source generation, timing indices, proxies and export intermediates. These sources are already low resolution, so their refinement remains 64×48; these figures do **not** qualify HD/4K or 240 FPS search speed, nor universal perceptual quality. They are not a controlled identical-binary speed comparison with earlier lots.

Evidence is under `scratch/loop-validation/improvements/lot6-2026-10-29/`: before/focused/expanded TRX reports, `build-release.log`, decoded comparison reports/MP4/contact sheets, four UI captures, `ui-review.json` and the local `UiReview/` harness. Scratch files are not runtime or portable test dependencies.

At completion of lot 6, export resource work was still pending; lot 7 below supersedes that limitation. The perceptual thresholds need further annotated real-footage review; no claim of universal seamlessness or exhaustive discovery is made.

## Lot 7 natural export resources on 29 October 2026

Source-only continuation of the local 1.22.0 candidate with **SDK 8.0.425**. The 24 October installer remains unchanged. No installation or publication was performed.

- Final application Release build: **0 warnings, 0 errors**.
- Final expanded Release regressions: **457 passed, 0 failed, 0 skipped**, in **6 minutes 51 seconds**. Selection: VideoLoop, BoundedMediaCapture, UiFoundation, UiEditor, UiKeyboard, UiResourceLifetime, ModalClosingAudit, ProgramCli, OutputOperation, OutputPathHelper and ActionQueueRemoval. All **nine** opt-in long-corpus scenarios were enabled. This is targeted coverage, not the complete canonical installer qualification. Evidence: `lot7-release-complete.trx`.
- Final resource/stop-barrier follow-up: **40 passed, 0 failed, 0 skipped**, in **50 seconds**, including both boundary/encoding refused-stop cases, preview bundle retention and the pre-decode preview budget. Evidence: `lot7-boundary-final.trx`.

The natural production path now encodes **one H.264 cycle**, with PCM audio when retained, into a temporary MOV. A concat list repeats those encoded video packets, and one final AAC encode processes the complete audio timeline. The cycle and final output both retain the final packet's explicit hold; presentation-ordered video uses a microsecond clock. A non-native requested start is resolved to the first included source image before trimming audio and computing the effective cycle duration. Pixel dimensions/SAR/rotation, odd-size 4:4:4 output and the source color preflight retain their previous contracts. Previews and forward/backward processing continue using FFV1.

The resource plan separates a natural compact cycle, proxy/preview intermediates and forward/reverse/concatenated lossless artifacts. The natural temporary estimate is independent of repetition count and includes PCM when present. It is an estimate, not a promised compression ratio. A **250 ms** guard checks actual owned temporary bytes against **8 GiB** and available space while every render process runs. Space is checked again before assembly, using measured cycle size plus an AAC allowance. Windows free-space queries support extended local and UNC path syntax and use the caller's available quota. Final outputs can exceed the temporary budget when destination space permits. Actual external UNC/server acceptance remains manual.

Natural rendering uses **two decoder threads**, **one filter thread**, **four encoder threads** and fixed x264 lookahead settings (**8 rate-control / 4 synchronization frames**). Buffers remain independent of selected duration; this is not a fixed total-memory guarantee for every frame size. The backward path keeps its estimated 128 MiB decoded chunks and uses the actual **N + (N − 2)** cycle frame count for final duration/progress, including the three speeds. Queue reporting/cancellation uses the original source throughout internal stages, and Cut Video preserves CurrentItem versus global cancellation scope.

Cleanup checks process termination before interpreting cancellation. A failed OS stop raises an error and retains both owned render and adjacent output workspaces; it does not report a successful cancellation or delete still-used files. Normal failures/cancellation remove owned partials after exit. Destination reservation uses CreateNew, publication retains OutputOperation's move without replacement, and both an existing destination and a collision introduced after encoding preserve the foreign file. Source changes before publication reject the stale render.

### Compared alternatives and measurements

A local harness compared the pre-lot-7 FFV1 reference, an encoded-once compact cycle and a decoded-frame loop graph. Generation precedes measurement. All execution goes through the existing FFmpeg/FFprobe runners. The decoded graph produced the correct VFR frame count/total duration but collapsed its two internal **200 ms** seam intervals to about **98 microseconds**. Its estimated decoded buffers also exceed the 128 MiB graph policy on HD/4K. It was rejected; there is no graph-loop production option.

The first unrestricted compact prototype retained high encoder memory, so the bounded settings above were measured and selected. The table compares the **actual production renderer** against the legacy reference on deterministic **testsrc2 / FFV1 / YUV420P** sources, with **three repetitions**, Windows build 26200, 24 logical processors and .NET 8.0.31. The legacy 4K measurement disables only its obsolete uniform estimate rejection in the scratch snapshot so the old pipeline can be measured; production has no budget bypass.

| Source | Reference / new wall time | Reference / new temporary bytes | Reference / new peak FFmpeg working set |
|---|---:|---:|---:|
| 36 s, 1920×1080, 8 FPS | 3.80 / 3.17 s | 30.23 / 16.62 MiB | 1,048 / 234 MiB |
| 90 s, 3840×2160, 2 FPS | 7.17 / 7.63 s | 68.18 / 50.16 MiB | 4,025 / 828 MiB |

The HD case reduces temporary storage by about **45%** and measured FFmpeg peak memory by about **78%**. The 4K case reduces them by about **26%** and **79%**, while taking slightly longer under the bounded settings. These measurements do not establish a universal speedup. FFmpeg memory is sampled every 30 ms for newly started FFmpeg processes on the standalone harness; it excludes the .NET host and may miss short peaks. Temporary figures include cycle/list artifacts and exclude source/final output. Final output sizes depend on content and repetition count. Counts/durations remain **864 frames / 108 s** and **540 frames / 270 s**, with **125 ms** and **500 ms** seam intervals respectively. VFR production retains **48 frames / 6.6 s** and **200 ms** seams.

The runnable harness, source/reference snapshot, all alternatives and production reports live under `scratch/loop-validation/improvements/lot7-2026-10-29/Prototype/` and `prototype-results.json`. They are local evidence, never runtime, installer or portable test dependencies. The portable `VideoLoopNaturalReference` is a test-only snapshot of the former natural FFV1/PCM strategy for decoded regression comparisons.

### Validation scope

- Initial pre-change selection: **48 passed, 0 failed, 0 skipped**, in **14 seconds** (existing media/preview/color/OutputOperation cases).
- Expanded media/resource selection before the stop-barrier follow-ups: **108 passed, 0 failed, 0 skipped**, in **49 seconds**, including native timing and shared output behavior.
- **40 new cases** cover phase plans, real/live loss of disk space, source changes, existing/late collisions, cancellation during boundary resolution/encoding/assembly/reversal, current-item identity/scope, extended-path space queries, packet hashes through **1/3/10** repetitions, CFR/VFR/odd/SAR decoded reference parity, exact sparse/single-frame final holds, native-start resolution, continuous delayed/earlier/muted audio and the failed-OS-stop barrier. One case exercises OutputOperation retention directly; the others are VideoLoop resource cases.
- Packet SHA-256 identities match the corresponding first-cycle packets in every repetition. Sparse fixtures assert their integer-PTS input timeline before exporting; no duplicate last image is inserted to manufacture the held duration. Reference comparisons test decoded images and timing, rather than treating lossy encoder bytes as equivalent.
- Mandatory long exports include **36 seconds at 1080p/30 FPS** and **90 seconds at 4K/2 FPS**, each repeated three times. Both exceed the former raw estimate yet export with correct packet count, geometry and duration. These functional cases are deliberately highly compressible blue sources; their tiny files are not presented as general content performance. Separate motion-source measurements appear above.
- Delayed audio continues beyond video EOF in the fixture. Every cycle through ten repetitions retains the original half-second silence and tone onset, without accumulating independently encoded AAC priming. Earlier audio uses the offset video timeline; mute removes the audio track. Audio seamlessness is still not analyzed.
- The failed-stop test suspends its own FFmpeg process and makes both OS kill attempts fail through the runner's existing test hook. It runs at the initial boundary read, cycle encoding and preview assembly; the preview builder also retains its bundle. After confirming retained workspaces/no published output, its finally block kills that owned process and cleans the retained test artifacts. The normal path is not changed to silently accept an unconfirmed stop.

Evidence is under `scratch/loop-validation/improvements/lot7-2026-10-29/`: before/focused/final TRX reports, `build-release.log`, prototype/production measurements and HD/4K functional reports. No runtime/package dependency was added. Existing private-footage evidence exports now choose unique filenames so repeated test runs obey the renderer's no-overwrite contract.

At completion of lot 7, HDR conversion remained pending; lot 8 below supplies the software SDR path. Installed/Explorer flows, high-DPI/multi-monitor acceptance, real external UNC/storage failure scenarios and broader annotated real-footage perceptual acceptance remain open.

## Lot 8 HDR-to-SDR conversion on 29 October 2026

Source-only implementation, .NET SDK **8.0.425** from `.buildcheck/sdk/8.0.425`, Windows/.NET 8/WinForms and the existing bundled FFmpeg. No dependency, tool binary, version or installer was changed for this lot.

`VideoLoopColorPolicy` now owns conversion and SDR tags. Required `zscale`, `tonemap`, `sidedata` and `setparams` filters are checked through `FfmpegRunner`, cached by executable path/length/write time, and explicitly diagnosed if absent. The parser accepts the bundled FFmpeg 9 two-flag inventory as well as older three-flag listings. Cancellation and failed captures are not cached as successful capabilities.

Qualified HDR input requires PQ or HLG transfer, BT.2020 primaries and a BT.2020 non-constant matrix. Missing/inconsistent transfer or gamut remains blocked. Explicit full/limited range is honored; missing range assumes limited. PQ uses the largest valid mastering/MaxCLL peak in 100–10,000 nits, with 1000 nits as the nominal fallback. FFprobe reads at most 32 initial video packets to recover frame SEI metadata where stream metadata is absent. HLG uses the 1000-nit zimg reference display. Dynamic HDR and Dolby Vision interpretation are outside this qualification.

The conversion linearizes with `zscale` at a 100-nit SDR reference, works in float RGB, converts primaries, maps highlights with Hable, and writes dithered limited-range BT.709. BT.709 luma coefficients are explicitly set before tone mapping: the initial ramp test exposed overbright desaturation when the RGB matrix was left as GBR, and the corrected chain is now checked against an independent linear Hable curve. SDR BT.2020/10-bit conversion omits HDR tone mapping. Frame side data is removed and SDR tags are written before final publication. Existing runners, output ownership and cancellation cleanup remain in use.

Evidence under `scratch/loop-validation/improvements/lot8/` includes `ffmpeg-filters.txt`, `lot8-hdr-color-ui.trx`, ramp JSON metrics, SDR MP4s and PNG reference images. The final **51-case focused run passes**, covering policy decisions, filter absence, PQ/HLG search, native stills, full/junction previews, reverse export, conversion cancellation and temporary cleanup, HDR10 SEI/side-data removal, metadata conflicts/invalid peaks, SDR 10/12-bit and HDR footer commands. Ramp PNGs were inspected: grey steps remain distinct through the high end and colored patches retain their hue.

The broader Find Loop, bounded capture, Join Videos and Add Subtitles run passed **368 tests, 0 failures, 1 opt-in performance skip, 369 total** in **3 min 44 s** (`lot8-regressions.trx`). Final native-still/temp-cleanup assertions and three invalid-peak cases were then verified in the focused run above. The final application build has **0 warnings / 0 errors** and `git diff --check` passes. These source checks do not replace canonical Release/installer qualification.

| Deterministic VFR fixture | Max mean RGB error: analysis vs export (0–255) | Max mean RGB error: preview vs export |
|---|---:|---:|
| PQ, limited range | 0.769 | 0.240 |
| PQ, full range | 0.722 | 0.160 |
| HLG, limited range | 0.683 | 0.223 |
| SDR BT.2020 (`bt2020-10`, `bt2020-12`, `bt709` transfer) | 0.711 | 0.106 |

Each two-second source has **15 native VFR images**; the three-cycle MP4 and full preview retain **45 images / 6.000 seconds**. All repeated image times match within 2 ms. Output is H.264/yuv420p with BT.709 primaries/transfer/matrix and limited range. The HDR10 test reads a 4000-nit MaxCLL over a 1000-nit mastering peak and verifies that neither mastering nor content-light frame side data survives SDR export. Cancellation during PQ/HLG encoding leaves no adjacent output workspace/file and releases the source for exclusive access. The native still path uses the same policy, and preview identity now includes range/peak/render version.

Remaining acceptance: **real HDR footage** viewed across the junctions on an SDR display (including missing/static peak metadata and subjective HLG rendering). Synthetic patches and decoded metrics do not replace that acceptance. High-DPI/multi-monitor and installed/Explorer validation remain open. No new installer was built or installed. Lot 9 below implements batch candidate export.

## Lot 9 batch candidate export on 29 October 2026

Source-only implementation using the existing .NET **8.0.425** SDK, WinForms helpers, FFmpeg/FFprobe runners and shared sequential queue. No dependency, version, tool binary or installer was changed for this lot.

`Export All Candidates (N)` appears in the Natural footer when the list contains validated candidates. It captures the displayed order, deduplicates equal native bounds and stores immutable selections with repeat/audio settings and source identity. Custom selection edits do not replace candidates in this snapshot. The optional command is detached outside this context, preserving ordinary Cut's two-button footer measurement. It is disabled during search/render/native navigation; the primary selected/custom export and CLI route remain separate.

The picker returns either the existing single export options or an independent batch. `CreateLoopBatchRequests` gives each candidate its own read-only option dictionary and real source path, then `RunQueuedRequestsWithProgressForm` runs them through `ActionQueueRunner`. A short `QueueItemProgressReporter` adapts the action's calls to occurrence IDs for row state, progress, removal and current cancellation. New interface overloads default to the legacy methods for existing reporters. Candidate number/duration/exclusive bounds remain visible in labels/details, separately from the execution path. Terminal rows ignore late processing updates; a removed occurrence never falls back to another duplicate row.

Automated cases verify:

- Three same-source candidates export **8 / 12 / 14 decoded images**, with **0.8 / 1.2 / 1.4 seconds** of video. The last candidate repeats its seven-frame cycle twice; every decoded image is compared to its own source segment. Audio remains present/absent/present according to the three independent requests. The same batch also runs on a PQ-tagged fixture and produces BT.709 SDR output. These are deterministic low-resolution fixtures; physical HDR ramp qualification remains in lot 8.
- A pre-existing `_loop.mp4` collision is preserved and three distinct adjacent outputs are created. An invalid middle candidate keeps its failure cause while earlier and later successful files survive. A source write-time change after the first output rejects the remaining saved selections.
- Current-item cancellation targets only the first occurrence and lets the next two complete. Global cancellation during encoding removes partial output and stops all pending candidates; cancellation after the first completion preserves that file. Sources reopen exclusively after cancellation and no adjacent owned workspace remains.
- Removing the second duplicate leaves the first and third independent. ProgressForm tests route cancellation, progress and terminal states by row ID, preserve failed details, ignore late progress and mark each pending duplicate on global cancellation.
- UI tests cover zero/five candidates, deduplication, duration sorting, custom edits, immutable snapshots, disabled commands during work, single-export compatibility, source invalidation, both themes and **480×340 / 760×540 / 1120×760** layout sizes.
- A real modal `EnsureCutVideoOptions` picker returns two batch requests, including source stamps, then opens the actual shared progress window through `RunQueuedRequestsWithProgressForm`. It produces **0.800 / 1.200 seconds**, exit code **0**, with separate labelled queue IDs. Rendered window captures and result JSON are saved locally.

Evidence: `scratch/loop-validation/improvements/lot9/lot9-focused.trx`, `lot9-media.trx`, `lot9-ui.trx`, `lot9-full-suite.trx`, `lot9-final-targeted.trx`, `combined-test-results.json`, `batch-picker.png`, `batch-progress.png`, `batch-modal-result.json`. The two window captures were inspected: the candidate list and three footer commands remain readable, and each queue line displays its own candidate number/duration/bounds while details retain the actual source path.

The full Debug suite ran **1314 cases in 8 min 28 s**: **1294 passed, 18 conditional skips, 2 failures**. Both failures were an obsolete pre-lot-9 assertion requiring the batch button to be absent with one/seven candidates. That test was updated to require the new count/context behavior. The final focused replay passes **29/29**, including those cases, modal routing, decoded media, cancellation, queue IDs and footer layout. No application code changed after the full run. Across the full run plus this replay, the latest outcomes cover **1296 unique passed cases, 18 conditional skips, no unresolved failure**; this is a combined result, not a second full-suite run. Skips concern the existing opt-in AI/model integrations and the long performance campaign. The application build has **0 warnings / 0 errors** and `git diff --check` passes.

Remaining acceptance belongs to **lot 10 complete qualification**: actual higher DPI/enlarged text/monitor transitions, real HDR and other annotated footage, and the exact installed/Explorer candidate after a canonical installer rebuild. No installer was rebuilt or installed in lot 9.

## Lot 10 candidate qualification on 29 October 2026

### Candidate and complete regression run

The implementation and qualification tests were committed as `f9c0444bf52d94830588bd83ee45211b24b109cb`. The canonical `.\build_installer.ps1` ran from that clean commit, without `-AllowDirty`, an exclusion filter or modified test parallelism. All locked restores, mandatory Release tests, self-contained application/worker publishing, runtime/tool/notice checks and Inno compilation succeeded. The suite passed **1,302 tests**, with **0 failures**, **18 conditional skips**, **1,320 total**, in **10 min 3 s**. Conditional skips retain existing local-model/integration activation requirements and the long benchmark; the latter was separately enabled and all **nine** scenarios passed in **2 min 43 s**. The six new path/failure cases also passed independently, **6/6**. Application builds have **0 warnings / 0 errors**.

| Artifact | Size or version | SHA-256 |
|---|---|---|
| `installer/FrameShift_1.22.0_Setup.exe` | 175,297,685 bytes; 1.22.0.0 | `D342D2CE8215ADE229AAE3D1C66E49703DDA079E1AAFC5FCE860C5A90FDF28D1` |
| Published and installed `FrameShift.dll` | `1.22.0+f9c0444bf52d94830588bd83ee45211b24b109cb` | `7C870C24B1E50D51D23548C72CBCC7549A80709C99413CF81BC6DCA7448EFB7B` |

The payload has **800 files**, with no `.pdb`, `.dbg`, private `.lic`, model `.onnx`, test or UI-sample assembly. Independent installed-file comparison finds **790 matching SHA-256 values, zero differing files**; the other ten published debug/dump/import artifacts are deliberately excluded by the ISS (`createdump`, `mscordaccore*`, `mscordbi`, `DirectML.Debug.dll`, `onnxruntime.lib`, including worker copies). The application and worker retain their pinned .NET **8.0.31** runtime and SDK **8.0.425**; no dependency, tool or version was changed by lot 10. Existing dependency maintenance gates in the release report remain open before public release. Inno Setup **6.7.1** compiled the installer in **76.235 s**. The adjacent checksum identifies this new candidate, replacing the 24 October artifact. Nothing was published or pushed. A final process snapshot finds **no running FFmpeg or FFprobe**.

### Corpus comparison and performance scope

Machine: Windows 11 Pro **10.0.26200**, Intel Core **i7-13700KF**, **24 logical processors**, **64 GiB RAM**, one active **2560×1440** display at **96 DPI**. Search measurements repeat the lot 0 **64×48 / 15 FPS / FFV1** definitions. Generation is outside the timers. Source hashes and the measured Debug assembly hash are recorded in each JSON; the measurements preceded the source commit and carry its parent informational revision, so the assembly SHA identifies that measured binary. The clean Release candidate was subsequently qualified by the complete suite above.

| Scenario / source duration | Lot 0 search | Current search | Junction proxy | Three-cycle export | Current candidates |
|---|---:|---:|---:|---:|---:|
| Periodic / 5 s | 1.21 s | 1.477 s | 0.140 s | 0.091 s | 14 |
| Periodic / 30 s | 1.92 s | 8.729 s | 0.147 s | 0.092 s | 181 |
| Periodic / 50 s | 1.76 s | 33.782 s | 0.647 s | 0.176 s | 372 |
| Periodic / 1 min | 1.90 s | 29.257 s | 0.140 s | 0.112 s | 437 |
| Periodic / 5 min | 3.05 s | 20.046 s | 0.155 s | 0.098 s | 512 |
| Periodic / 1 h | 4.64 s | 49.290 s | 0.151 s | 0.099 s | 512 |
| Whole-source cycle / 5 s | 0.06 s; missed | 0.472 s; found | 0.166 s | 0.107 s | 1 |
| Four cyclic regions / 12 s | 1.72 s; 3 results | 2.068 s | 0.163 s | 0.116 s | 13 |
| Longer exact cycle / 12 s | 1.95 s; shorter first | 2.691 s; exact 4 s first | 0.278 s | 0.130 s | 38 |

These measurements show the cost of broader search, native refinement and more retained candidates; **they do not show a search speedup**. Old and new search budgets differ, host load varies, and this is not a statistically controlled speed comparison. Preview/export timings use the **first ranked candidate**, a one-second cycle for periodic/four-region sources, five seconds for the whole-source case and four seconds for longer-better; they do not represent export of an entire one-hour source. Candidate limits at five minutes/one hour remain explicit. The largest analysis RGB file is **2.25 MiB**, versus **31.64 MiB** for the old hour case; test-host working-set peaks range approximately **89–267 MiB**, excluding FFmpeg children and generation. Total allocation is not peak retained memory.

Standalone resource replay used the existing lot 7 motion fixtures after the Release test host exited, while canonical publishing/packaging could still run. Native peaks sample all FFmpeg processes every 20 ms and exclude the managed host; short peaks may be missed. Timings are indicative and resolution/FPS/content specific:

| Source | Silent junction render | Export three full cycles | Peak FFmpeg working set | Largest export workspace | Output |
|---|---:|---:|---:|---:|---:|
| 1920×1080, 36 s, **8 FPS** | 0.349 s | 3.154 s | 234.46 MiB | 16.62 MiB | 108 s / 52,270,512 bytes |
| 3840×2160, 90 s, **2 FPS** | 0.412 s | 7.943 s | 827.83 MiB | 50.16 MiB | 270 s / 157,802,425 bytes |

The normal suite independently exports **1080p/30 FPS/36 s** and **4K/2 FPS/90 s**, checking decoded duration and packet count. Neither the low-FPS motion measurements nor the static 30-FPS test establishes general 4K/30–60 FPS performance. Existing hard guards remain: **8 GiB** owned render temporary space, free-space preflight plus **16 MiB** emergency reserve/live checks, 960-pixel preview dimension, bounded sample/native windows, 512 candidates and a 256-MiB timing index. Measured native memory is not an enforced total-process memory limit. Full long/large forward-backward previews may be refused with a readable shorter-selection/junction instruction.

### Media, failures and exact installed payload

- New tests perform both silent junction rendering and adjacent publication through `CutVideoAction` on spaces, accents, apostrophes and paths exceeding **300 characters**. They verify duration, retained audio, source exclusive reopening and owned workspace cleanup. Missing/exclusively locked sources and an output directory replaced by a foreign file fail readably without publishing or altering that file.
- The complete suite retains decoded CFR/VFR/final-hold and fractional/high-FPS timing, SDR 8/10/12-bit/PQ/HLG ramp conversion, delayed/continuous audio, reverse chunk order, per-candidate options/IDs, collisions, failures, source changes and cancellation/failed-stop barriers. Disk-full and OS-stop failures are deterministic injected conditions; the test machine was not filled or its permissions weakened. The failed-stop test explicitly retains owned files until its test process is killed; ordinary confirmed cancellation leaves no owned workspace or source lock.
- A real **UNC loopback SMB** export from `\\localhost\E$` with accents, apostrophes and spaces succeeds. The exact installed application repeats that CLI route with exit **0**, preserves the existing output hash and creates `_loop_001.mp4`. This checks local SMB/path handling, not a third-party server, network outage or different-account permission configuration.
- Thirty-one local video files were probed; **none is PQ/HLG HDR**. The owner confirmed no real HDR footage is available. Existing PQ/HLG patches, tags, decoded pixels and SDR coherence checks pass; subjective HDR footage acceptance is explicitly deferred, not certified by synthetic ramps.
- Three private clips (`video.mp4`, `ComfyUI_00723_.mp4`, `train.mp4`) yield **zero accepted natural candidates** at Auto/Normal. Their explicit three-cycle forward/backward fallback and two turning-point proxies render successfully. Six-image turning-point strips were visually inspected for frame continuity. These checks do not certify full-speed perceptual quality, natural flames/water seams or HDR rendering.
- The **published apphost** starts and runs Natural and forward/backward CLI exports, each exit **0**. Probing verifies respectively **45 frames / 3.000 s** and **84 frames / 5.600 s**, both silent, from a 15-frame selection repeated three times.
- Canonical installer execution (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`) returns **0** and successfully reinstalls the existing administrative **1.22.0** copy at `C:\Program Files\FrameShift`. The old informational revision `b777b55` becomes `f9c0444`; installed and published application SHA-256 match. Installed FFmpeg/FFprobe hashes match the canonical pins, selected components are retained and the registered `.mp4` Explorer Cut Video command points to this installed executable. This is a **same-version replacement**, not a tested upgrade from 1.21.1.

### Acceptance limits and remaining gate

Lot 10's **automated/media/packaging and same-version installation checks are executed**. Its complete manual Windows acceptance is **not complete**:

- An unrelated Windows Security firewall request for **UnrealGame** covers the interactive desktop. It was observed before changing settings or sending editor input and was left to the owner. The Computer Use skill prohibits acting on security/privacy requests. The new installed editor and payload editor open; their UI state can be read, but obstructed screenshots and automated modal tests are not manual visual/keyboard acceptance.
- Actual **150/200/300% DPI**, enlarged Windows text, Light/Dark manual workflow, small visible windows, long-list keyboard/scrolling and different-DPI monitor transitions remain unexecuted in this session. Only one display is currently active. No Windows scale, text or display topology setting was changed. Existing hidden layout/theme checks and real automated modal batch tests pass in the full suite; they do not replace these scenarios.
- A fresh install, upgrade from the previously published version, uninstall/reinstall and an actual Explorer right-click interaction remain to be qualified in a suitable interactive/test environment. The installed command registration and its installed CLI execution are checked, without relabelling them as an Explorer menu click. The working installation and its downloaded models were not uninstalled to manufacture a fresh-install result.
- Real HDR footage, external UNC servers/credentials and subjective acceptance on broader annotated footage remain explicit corpus/environment limits. Dependency maintenance in the release qualification remains separate and blocks public publication acceptance.

Evidence is local and ignored by Git under `scratch/loop-validation/improvements/lot10/`: `canonical-build.log`, `lot10-paths.trx`, `lot10-benchmarks.trx`, `artifacts.json`, `resources.json`, `local-media-inventory.json`, `real-footage.json`, footage/proxy/contact strips, `unc-result.json`, `published-cli.json`, `published-output-validation.json`, `install.log`, `install-result.json`, `installed-verification.json`, `installed-unc-cli.json`, `installed-payload-comparison.json`, `process-cleanup.json` and `explorer-command.json`. Benchmark JSON is under `baseline/20261029T153414832Z-adfea54b932c475f8805aa195301710c/`. Qualification harnesses and private media remain outside runtime/installer dependencies. Completion of the remaining interactive gate must update this section; this candidate is not declared fully accepted or publicly released.
