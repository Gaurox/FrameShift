# Separating 1.22 and 1.23

## Published — 10 November 2026

[FrameShift 1.23.0 is public](https://github.com/Gaurox/FrameShift/releases/tag/v1.23.0),
with the owner-approved English description, qualified installer and checksum.
Tag `v1.23.0` points to `b1a7f98`; the unchanged installer uses clean application
source `5b60911`. Main and the integration branch contain the completed work.
See [release qualification](RELEASE_QUALIFICATION_1.23.0.md) for asset identity
and the accepted validation scope. The sections below preserve integration history.

## Historical integration — 8 November 2026

Multilingual **1.22.0 is published**, tagged `v1.22.0`, at `6d230c6`
(`origin/main`). The local integration branch **`codex/integrate-find-loop-1.23`**
starts at that exact commit. `codex/feature-loop-1.23` remains untouched at
`137311e`; it is the historical backup, not the branch to release.

The integration selectively ports `c906026`, `f9c0444`, the final `VideoLoop*`
sources and the two timing-collector tests omitted from multilingual 1.22.
It restores natural search, forward/backward loops, native frame timing,
silent junction/full previews, color policy, bounded resources and queued
candidate exports inside Cut Video. Shared queue/progress, probe, preview and
output helpers are merged with the published code rather than replaced.

All 1,843 existing entries in each of the five display-resource files are
unchanged. Existing localized Cut Video, preview and queue messages remain in
use. Three translated footer commands (Export Selection / Loop / SDR Loop) are
added in the initial integration, with a context-switch regression test. The
9 November localization follow-up adds the remaining 160 Find Loop resources
in all five languages: **163 Find Loop keys, 2,006 total entries per catalog**.
Controls, candidate assessments/columns, accessibility instructions, progress,
preview descriptions, HDR/color messages, resource/error messages and CLI errors
now use the existing `L.Get` / `L.Format` mechanism. CLI values, candidate IDs,
FFmpeg parameters, filenames and raw diagnostics retain their existing contracts.

The published installer script, dependency locks, SDK/runtime pins, verified
FFmpeg archive, release notes, checksums and translated READMEs are preserved.
The 9 November private installer is built but not installed; no branch, tag,
release or website has been published. The private packaging preparation advances
application metadata to **1.23.0**. It is a development candidate, with work still
continuing on the integration branch. The [private qualification report](RELEASE_QUALIFICATION_1.23.0.md)
identifies its build and artifacts; this does not authorize public release.

The restored [feature guide](FIND_LOOP.md), [improvement plan](FIND_LOOP_IMPROVEMENT_PLAN.md)
and [validation history](FIND_LOOP_VALIDATION.md) are explicitly marked historical.
Their old installer results do not qualify this new integration.

Local validation uses `.buildcheck/sdk/8.0.427/dotnet.exe`, matching `global.json`,
with Release configuration and the existing locked dependencies. The first targeted
Find Loop/bounded-capture/queue run passes **334 tests, 0 failures, 1 skip**
(the opt-in search benchmark). It exercises generated media and automated WinForms
workflows; it does not establish manual visual/DPI acceptance. Its local TRX is
`scratch/loop-1.23-tests/find-loop-integration.trx`.

The integration baseline's complete Release suite finishes on **9 November 2026** with **1,409
passed, 0 failures, 18 conditional skips, 1,427 total** in **9 m 7 s**. All five
new multilingual context-switch cases pass. Skips require opt-in settings or
unavailable OCR/AI fixtures, plus the optional loop benchmark. The final TRX is
`scratch/loop-1.23-tests/full-release.trx`; the resource-preservation comparison
is `scratch/loop-1.23-translation-preservation.json`. Compilation and Git
whitespace checks pass. This is source/build/automated-test validation, with no
installer or manual Windows acceptance claimed.

The **9 November localization follow-up** builds successfully in Release with the
same SDK and locked dependencies. Its final targeted run passes **229 tests,
0 failures, 0 skips** in **3 m 55 s**, covering registered-resource coverage and
format arguments, existing language/Explorer behavior, all Find Loop WinForms
tests, color policy, resource limits and loop contracts. Five new cases exercise
localized candidates and batch descriptions under a comma-decimal numeric culture,
preserving exact bounds, candidate IDs and CLI values. The local evidence is
`scratch/loop-1.23-localization-tests/localization-final.trx` and `resources.json`
in that directory. All 1,843 published entries per language remain unchanged.
The full-suite result above predates this text-only localization follow-up;
the complete suite has not been rerun for this step. Interactive visual/DPI
and installer acceptance remain pending.

Next work before a 1.23 candidate:

1. Visually review translated long labels, candidate columns, keyboard access and
   narrow layouts in the interactive Windows acceptance; resource translation is complete.
2. Qualify the integrated feature with the serviced FFmpeg/.NET payload; retain
   historical limits for real HDR footage and perceptual loop quality.
3. Complete interactive Windows acceptance (DPI/text scaling, themes, actual
   Explorer launch and monitor changes where available).
4. Qualify each private 1.23.0 candidate, including fresh install/upgrade/uninstall.
   Keep dated artifacts while development continues. Publication needs a later
   owner instruction.

The first private 1.23.0 installer builds successfully from `df0fae2`. Its canonical
suite passes **1,414 tests, 0 failures, 18 conditional skips**. The exact payload's
five-language resources and natural/forward-backward exports pass additional smoke
checks. The dated installer, SHA-256, source identity and remaining acceptance are
recorded in [private qualification](RELEASE_QUALIFICATION_1.23.0.md). This is a working
snapshot; the branch remains open for further development, without publication.

## Integrated Explorer menu correction — 10 November 2026

The owner requested an integration study for the complete video context menu in
the future 1.23.0 alongside Find Loop. The [integration study](EXPLORER_MENU_1.23_INTEGRATION_PLAN.md)
led to an authorized implementation of a small C#/.NET 8 Native AOT Shell
component, retaining Find Loop through the existing Cut Video action. The new
component and packaging are implemented in the working tree. Canonical
qualification succeeds: **1,437 tests passed, 0 failures, 18 conditional skips**,
followed by 15 native checks and Inno Setup compilation. The native checks cover
five languages, synthetic COM selection, captured action dispatch,
component subsets and bounded resources. The new private candidate is retained
under `builds/release-1.23-private/20261110T174906Z-menus/`; its dirty-source
snapshot, exact hashes and limits are recorded in the
[private qualification](RELEASE_QUALIFICATION_1.23.0.md).
Registered Explorer rendering and
Native AOT upgrade/uninstall acceptance remain pending; no installation or
desktop control is performed. The retained 9 November private snapshot remains
unchanged and does not contain this correction.

### Follow-up: adjacent classic and AI roots

After trying the video-only candidate, the owner confirms that all video actions
are visible on MP4 and reports the classic and AI roots at different positions.
The follow-up implementation uses one handler for both roots across video,
audio, images and PDF OCR. It inserts FrameShift then FrameShift AI together,
omits empty roots, and preserves fixed AI models and actual supported formats.
The previous video CLSID is cleaned up; a new unified CLSID and metadata schema
2 prevent a still-loaded video-only handler from rendering a duplicate root.
The complete canonical build succeeds: **1,443 tests passed, 0 failures,
18 conditional skips**, plus **23 native checks** and Inno compilation.
The new candidate is retained under
`builds/release-1.23-private/20261110T182134Z-unified-menus/`; see the
[qualification record](RELEASE_QUALIFICATION_1.23.0.md) for hashes and source
identity. Installed acceptance for this follow-up remains open. The agent
neither runs the installer nor controls the desktop.

### Cut / Find Loop window follow-up

After the menu checkpoint `33c0df4`, the owner requested a taller Cut/Loop
window, automatic height adjustment when advanced options expand/collapse, and
stable candidate selection. The first UI candidate used a 900-logical-pixel base
plus advanced-panel height. After trying it, the owner confirms the ordinary Cut
size and requests content fitting in standard Find Loop as well. The working
tree now fits the complete active loop view at load, context change, first
nonempty result list, advanced toggle and DPI change. Cut keeps its existing
base size. If content cannot fit the monitor working area, the window uses that
height and options scroll. Advanced open/close can consequently keep the same
window height when both states already need the screen maximum.
Ordinary selection, incremental result updates, preview text and resize do not trigger this fit. The window
is bounded by the monitor working area; a preview floor and scrollable options
keep the footer reachable on smaller screens. Other timeline editors retain
their previous split by default.

Reserved normal selection/preview messages, buffered controls, grouped selection
updates, idempotent re-selection, stable stop/progress slots and atomic bitmap
replacement address movement/flicker without changing the search/export engine.
The shared viewport avoids snapping a partly visible candidate grid on focus,
while explicit reveal and normal keyboard scrolling remain available. The new
hidden-control regressions cover geometry, five languages, narrow windows and
font stress. Real OS DPI, monitor transitions and perceived flicker still need
installed acceptance; hidden bitmap rendering omits some native children and
must not be treated as proof of final desktop appearance.

The canonical private candidate succeeds with **1,457 tests passed, 0 failures,
18 conditional skips**, **23 native checks** and Inno compilation. The dated
installer is retained under
`builds/release-1.23-private/20261110T190523Z-cut-loop-layout/`; exact source,
hashes and remaining acceptance are recorded in the
[qualification](RELEASE_QUALIFICATION_1.23.0.md). The agent does not install it,
control the desktop or commit this new UI lot.

The owner's subsequent height feedback supersedes the advanced-only sizing
policy of that first UI candidate. The standard-loop content-fit candidate
succeeds through the canonical workflow with **1,458 passed, 0 failed,
18 conditional skips**, **23 native checks**, and Inno compilation. It is retained
under `builds/release-1.23-private/20261110T194553Z-loop-content-fit/`; its 15
targeted geometry regressions include full standard-loop fit on a large screen
and both advanced states capped on 1080p. It remains private and uninstalled by
the agent; see the appended qualification record for its exact identity.

## Historical split preparation

Prepared on 1 November 2026 at the owner's request. No remote branch, tag, GitHub
release or website is published by this preparation.

## Preserved loop work

`codex/feature-loop-1.23` preserves the complete former 1.22 work at `137311e`,
including the private installer qualification recorded on 29 October. The original
`codex/security-p1` is unchanged. The code was not reset, rebased or deleted.

`c906026` contains the initial Find Loop integration; `f9c0444` adds the extended
search, previews, timing, HDR handling and queue export. Their common-code changes
and the timing collector/tests from `3a21ade` belong to future 1.23 integration.
The retained branch's version and old private artifacts still say 1.22.0; they are
historical and must not be confused with the new multilingual candidate.

The 29 October loop installer is archived locally under
`builds/loop-1.23-preserved/FrameShift_1.22.0_Setup.exe`, alongside its qualification
and checksum. Repackaging its untouched payload/script reproduced the exact original
SHA-256 `D342D2CE8215ADE229AAE3D1C66E49703DDA079E1AAFC5FCE860C5A90FDF28D1`.

After multilingual 1.22 is finalized, create the actual 1.23 integration branch from
that final commit and selectively port the loop feature. Preserve its translations,
general fixes and installer rather than replacing files from the old branch.

## Multilingual 1.22

`codex/release-1.22` starts at published `main` commit `b777b55`, above tag 1.21.1.
It retains six general correction lots without Find Loop:

| Source commit | New commit | Retained scope |
| --- | --- | --- |
| `c6f5ddb` | `bc5f22b` | Media integrity, transparent upscale, safe write probes, FFmpeg/native cancellation |
| `10ae52c` | `ae2d52b` | Bounded PDF/image/audio memory and streamed subtitle windows |
| `bb70d8a` | `29fca70` | Batched queues, cancellable imports and successful tile reuse |
| `2d45a83` | `18be3a6` | Protected model paths, bounded/coordinated downloads and safe installer cleanup |
| `3a21ade` | `cd518be` | Restricted local pipes and bounded media captures/logs |

The last transfer excludes all `VideoLoop*` files and the two collector-specific
tests in `BoundedMediaCaptureTests`. Common FFmpeg observers remain available,
without a dependency on any loop type. Historical release notes, checksums and
loop documentation were not copied into the new release.

The Chinese contribution is pinned at `f078b0c`; its resource-based integration
and language behavior are described in [LOCALIZATION.md](LOCALIZATION.md).
The later Spanish, Brazilian Portuguese and Russian additions use the same catalog
and resource mechanism; the final 1.22 candidate includes English, simplified Chinese,
Spanish, Brazilian Portuguese and Russian, with no loop code.
