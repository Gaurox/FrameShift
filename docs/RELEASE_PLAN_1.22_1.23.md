# Separating 1.22 and 1.23

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
historical and must not be confused with the new bilingual candidate.

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
The later Spanish addition uses the same catalog and resource mechanism; the final
1.22 candidate includes English, simplified Chinese and Spanish, with no loop code.
