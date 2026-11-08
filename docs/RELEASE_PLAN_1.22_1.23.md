# Separating 1.22 and 1.23

## Current integration — 8 November 2026

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
added in all five languages, with a context-switch regression test. The remaining
new Find Loop controls, analysis/status messages and errors still contain the
historical English wording: full localization is an explicit next step.

The published installer script, dependency locks, SDK/runtime pins, verified
FFmpeg archive, release notes, checksums and translated READMEs are preserved.
No installer has been rebuilt or installed, and no branch, tag, release or
website has been published. App/installer version metadata still says 1.22.0
until a coordinated 1.23 packaging change; a development build from this branch
must not be distributed as the published 1.22.0 payload.

The restored [feature guide](FIND_LOOP.md), [improvement plan](FIND_LOOP_IMPROVEMENT_PLAN.md)
and [validation history](FIND_LOOP_VALIDATION.md) are explicitly marked historical.
Their old installer results do not qualify this new integration.

Local validation uses `.buildcheck/sdk/8.0.427/dotnet.exe`, matching `global.json`,
with Release configuration and the existing locked dependencies. The first targeted
Find Loop/bounded-capture/queue run passes **334 tests, 0 failures, 1 skip**
(the opt-in search benchmark). It exercises generated media and automated WinForms
workflows; it does not establish manual visual/DPI acceptance. Its local TRX is
`scratch/loop-1.23-tests/find-loop-integration.trx`.

The final complete Release suite finishes on **9 November 2026** with **1,409
passed, 0 failures, 18 conditional skips, 1,427 total** in **9 m 7 s**. All five
new multilingual context-switch cases pass. Skips require opt-in settings or
unavailable OCR/AI fixtures, plus the optional loop benchmark. The final TRX is
`scratch/loop-1.23-tests/full-release.trx`; the resource-preservation comparison
is `scratch/loop-1.23-translation-preservation.json`. Compilation and Git
whitespace checks pass. This is source/build/automated-test validation, with no
installer or manual Windows acceptance claimed.

Next work before a 1.23 candidate:

1. Complete Find Loop resources in all registered languages and check long labels,
   candidate columns, keyboard access and narrow layouts.
2. Qualify the integrated feature with the serviced FFmpeg/.NET payload; retain
   historical limits for real HDR footage and perceptual loop quality.
3. Complete interactive Windows acceptance (DPI/text scaling, themes, actual
   Explorer launch and monitor changes where available).
4. Coordinate 1.23 versions, release notes and private packaging, then qualify
   fresh install/upgrade/uninstall. Publication needs a later owner instruction.

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
