using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace FrameShift.Core.AI.VideoInterpolation;

/// <summary>
/// AVX2 / SSSE3-accelerated conversions between RGB24 byte frames and float32 planar tensors.
/// Falls back to scalar automatically when AVX2 is unavailable.
/// </summary>
internal static class RifeTensorConversionSimd
{
    // Evaluated once at class load; RyuJIT devirtualises the branch in the hot paths.
    private static readonly bool s_avx2 = Avx2.IsSupported;

    // ── Fill: extract R / G / B channels from stride-3 RGB24 layout ──────────
    //
    // A 16-byte load covers: [R0 G0 B0 | R1 G1 B1 | R2 G2 B2 | R3 G3 B3 | R4 G4 B4 | R5]
    // Each mask picks one channel per pixel into an int32 slot (0x80 → inserts zero byte).
    //
    //   R mask: bytes at positions 0, 3, 6, 9  → int32 lanes [R0, R1, R2, R3]
    //   G mask: bytes at positions 1, 4, 7, 10 → int32 lanes [G0, G1, G2, G3]
    //   B mask: bytes at positions 2, 5, 8, 11 → int32 lanes [B0, B1, B2, B3]

    private static readonly Vector128<byte> s_rMask = Vector128.Create(
        (byte) 0, 0x80, 0x80, 0x80,  3, 0x80, 0x80, 0x80,
                6, 0x80, 0x80, 0x80,  9, 0x80, 0x80, 0x80);

    private static readonly Vector128<byte> s_gMask = Vector128.Create(
        (byte) 1, 0x80, 0x80, 0x80,  4, 0x80, 0x80, 0x80,
                7, 0x80, 0x80, 0x80, 10, 0x80, 0x80, 0x80);

    private static readonly Vector128<byte> s_bMask = Vector128.Create(
        (byte) 2, 0x80, 0x80, 0x80,  5, 0x80, 0x80, 0x80,
                8, 0x80, 0x80, 0x80, 11, 0x80, 0x80, 0x80);

    private static readonly Vector256<float> s_normScale256 = Vector256.Create(1f / 255f);

    // ── Write: pack float32 → uint8 and interleave into RGB24 ────────────────
    //
    // After float→int32→uint8, each channel is a byte vector [C0 C1 C2 C3 0 0 0 0 …].
    // To build [R0 G0 B0 | R1 G1 B1 | R2 G2 B2 | R3 G3 B3] in 12 bytes:
    //
    //   rg = UnpackLow(R,G) = [R0 G0 R1 G1 R2 G2 R3 G3 | 0 0 0 0 0 0 0 0]
    //   s_rgInterleave → [R0 G0 __ | R1 G1 __ | R2 G2 __ | R3 G3 __ | …]
    //   s_bInterleave  → [__ __ B0 | __ __ B1 | __ __ B2 | __ __ B3 | …]
    //   OR both → [R0 G0 B0 | R1 G1 B1 | R2 G2 B2 | R3 G3 B3 | 0 0 0 0]

    private static readonly Vector128<byte> s_rgInterleave = Vector128.Create(
        (byte)0, 1, 0x80, 2, 3, 0x80, 4, 5, 0x80, 6, 7, 0x80, 0x80, 0x80, 0x80, 0x80);

    private static readonly Vector128<byte> s_bInterleave = Vector128.Create(
        (byte)0x80, 0x80, 0, 0x80, 0x80, 1, 0x80, 0x80, 2, 0x80, 0x80, 3, 0x80, 0x80, 0x80, 0x80);

    private static readonly Vector128<float> s_scale128 = Vector128.Create(255f);
    private static readonly Vector128<float> s_half128  = Vector128.Create(0.5f);
    private static readonly Vector128<float> s_ones128  = Vector128.Create(1f);

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts an RGB24 raw frame to a float32 planar tensor [1,3,paddedHeight,paddedWidth].
    /// Fills the active region (width × height) then replicates edge pixels into padding.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static void Fill(
        ReadOnlySpan<byte>  src,
        Span<float>         tensor,
        int width, int height, int paddedWidth, int paddedHeight)
    {
        var planeSize = paddedWidth * paddedHeight;
        const float norm = 1f / 255f;

        ref byte  srcRef    = ref MemoryMarshal.GetReference(src);
        ref float tensorRef = ref MemoryMarshal.GetReference(tensor);

        for (var y = 0; y < height; y++)
        {
            var srcRowStart = y * width * 3;
            var rowOffset   = y * paddedWidth;
            var redOff      = rowOffset;
            var greenOff    = planeSize     + rowOffset;
            var blueOff     = planeSize * 2 + rowOffset;

            int x;

            if (s_avx2)
            {
                // AVX2 fast path — 8 pixels per iteration.
                //
                // We issue two 128-bit loads per group of 8 pixels:
                //   lo = bytes [x*3 ..  x*3+15]  → covers pixels x+0 .. x+3 (+ 4 guard bytes)
                //   hi = bytes [x*3+12 .. x*3+27] → covers pixels x+4 .. x+7 (+ 4 guard bytes)
                //
                // The hi load overlaps 1 pixel with lo; the same shuffle masks apply to both
                // because the relative offsets 0,3,6,9 are identical.
                //
                // Both loads may read up to 4 bytes past their pixel group. This is safe:
                // ArrayPool.Shared rounds up to a power-of-two, so there is always headroom
                // beyond the last valid pixel.
                for (x = 0; x + 8 <= width; x += 8)
                {
                    var srcOff = srcRowStart + x * 3;

                    var lo = Vector128.LoadUnsafe(ref srcRef, (nuint)srcOff);
                    var hi = Vector128.LoadUnsafe(ref srcRef, (nuint)(srcOff + 12));

                    var r256 = Vector256.Create(
                        Ssse3.Shuffle(lo, s_rMask).AsInt32(),
                        Ssse3.Shuffle(hi, s_rMask).AsInt32());
                    var g256 = Vector256.Create(
                        Ssse3.Shuffle(lo, s_gMask).AsInt32(),
                        Ssse3.Shuffle(hi, s_gMask).AsInt32());
                    var b256 = Vector256.Create(
                        Ssse3.Shuffle(lo, s_bMask).AsInt32(),
                        Ssse3.Shuffle(hi, s_bMask).AsInt32());

                    (Avx.ConvertToVector256Single(r256) * s_normScale256)
                        .StoreUnsafe(ref tensorRef, (nuint)(redOff   + x));
                    (Avx.ConvertToVector256Single(g256) * s_normScale256)
                        .StoreUnsafe(ref tensorRef, (nuint)(greenOff + x));
                    (Avx.ConvertToVector256Single(b256) * s_normScale256)
                        .StoreUnsafe(ref tensorRef, (nuint)(blueOff  + x));
                }
            }
            else
            {
                x = 0;
            }

            // Scalar tail (0–7 pixels on AVX2, full row on non-AVX2)
            for (var s = srcRowStart + x * 3; x < width; x++, s += 3)
            {
                tensor[redOff   + x] = src[s]     * norm;
                tensor[greenOff + x] = src[s + 1] * norm;
                tensor[blueOff  + x] = src[s + 2] * norm;
            }

            // Column edge padding: replicate rightmost pixel
            if (width < paddedWidth)
            {
                var lastS = srcRowStart + (width - 1) * 3;
                var edgeR = src[lastS]     * norm;
                var edgeG = src[lastS + 1] * norm;
                var edgeB = src[lastS + 2] * norm;
                for (var px = width; px < paddedWidth; px++)
                {
                    tensor[redOff   + px] = edgeR;
                    tensor[greenOff + px] = edgeG;
                    tensor[blueOff  + px] = edgeB;
                }
            }
        }

        // Row padding: replicate bottom row into padded rows
        if (height < paddedHeight)
        {
            var lastRowOff = (height - 1) * paddedWidth;
            var rLastRow   = tensor.Slice(lastRowOff,                     paddedWidth);
            var gLastRow   = tensor.Slice(planeSize     + lastRowOff,     paddedWidth);
            var bLastRow   = tensor.Slice(planeSize * 2 + lastRowOff,     paddedWidth);

            for (var py = height; py < paddedHeight; py++)
            {
                var dstOff = py * paddedWidth;
                rLastRow.CopyTo(tensor.Slice(dstOff,                     paddedWidth));
                gLastRow.CopyTo(tensor.Slice(planeSize     + dstOff,     paddedWidth));
                bLastRow.CopyTo(tensor.Slice(planeSize * 2 + dstOff,     paddedWidth));
            }
        }
    }

    /// <summary>
    /// Converts the active region of a float32 planar tensor [1,3,paddedHeight,paddedWidth]
    /// back to an RGB24 raw frame (width × height pixels).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static void Write(
        ReadOnlySpan<float> tensor,
        int width, int height, int paddedWidth, int paddedHeight,
        Span<byte> dst)
    {
        var planeSize = paddedWidth * paddedHeight;

        ref float tensorRef = ref MemoryMarshal.GetReference(tensor);
        ref byte  dstRef    = ref MemoryMarshal.GetReference(dst);

        for (var y = 0; y < height; y++)
        {
            var rowOffset    = y * paddedWidth;
            var dstRowOffset = y * width * 3;

            int x;

            if (s_avx2)
            {
                // SSSE3 + SSE path — 4 pixels per iteration.
                //
                // float → int32 → int16 → uint8 → SSSE3 interleave → 12-byte store.
                // The 12-byte store uses one 8-byte write and one 4-byte write to avoid
                // writing the 4 trailing zero bytes of the 16-byte register into the
                // next pixel group (safe for non-final groups, but we keep it uniform).
                for (x = 0; x + 4 <= width; x += 4)
                {
                    var rF = Vector128.LoadUnsafe(ref tensorRef, (nuint)(rowOffset + x));
                    var gF = Vector128.LoadUnsafe(ref tensorRef, (nuint)(planeSize     + rowOffset + x));
                    var bF = Vector128.LoadUnsafe(ref tensorRef, (nuint)(planeSize * 2 + rowOffset + x));

                    // Clamp [0,1], scale ×255, bias +0.5 for correct rounding on truncation
                    var rS = (Sse.Min(Sse.Max(rF, Vector128<float>.Zero), s_ones128) * s_scale128) + s_half128;
                    var gS = (Sse.Min(Sse.Max(gF, Vector128<float>.Zero), s_ones128) * s_scale128) + s_half128;
                    var bS = (Sse.Min(Sse.Max(bF, Vector128<float>.Zero), s_ones128) * s_scale128) + s_half128;

                    // float → int32 (truncate toward zero; bias above makes it equivalent to round)
                    var ri32 = Sse2.ConvertToVector128Int32WithTruncation(rS);
                    var gi32 = Sse2.ConvertToVector128Int32WithTruncation(gS);
                    var bi32 = Sse2.ConvertToVector128Int32WithTruncation(bS);

                    // int32 → uint8 via two pack steps (saturating)
                    // PackSignedSaturate(a, zero): [a0,a1,a2,a3, 0,0,0,0] as int16
                    // PackUnsignedSaturate(a16, zero): [a0,a1,a2,a3, 0,0,0,0,0,...] as uint8
                    var riB = Sse2.PackUnsignedSaturate(
                        Sse2.PackSignedSaturate(ri32, Vector128<int>.Zero),
                        Vector128<short>.Zero);
                    var giB = Sse2.PackUnsignedSaturate(
                        Sse2.PackSignedSaturate(gi32, Vector128<int>.Zero),
                        Vector128<short>.Zero);
                    var biB = Sse2.PackUnsignedSaturate(
                        Sse2.PackSignedSaturate(bi32, Vector128<int>.Zero),
                        Vector128<short>.Zero);

                    // Interleave into [R0 G0 B0 | R1 G1 B1 | R2 G2 B2 | R3 G3 B3 | 0 0 0 0]
                    var rg  = Sse2.UnpackLow(riB, giB);
                    var rgb = Sse2.Or(
                        Ssse3.Shuffle(rg,  s_rgInterleave),
                        Ssse3.Shuffle(biB, s_bInterleave));

                    // Store exactly 12 bytes: 8 + 4
                    var dstOff = dstRowOffset + x * 3;
                    Unsafe.WriteUnaligned(
                        ref Unsafe.Add(ref dstRef, dstOff),
                        rgb.AsUInt64().GetElement(0));
                    Unsafe.WriteUnaligned(
                        ref Unsafe.Add(ref dstRef, dstOff + 8),
                        rgb.AsUInt32().GetElement(2));
                }
            }
            else
            {
                x = 0;
            }

            // Scalar tail
            for (; x < width; x++)
            {
                var dstOff = dstRowOffset + x * 3;
                dst[dstOff]     = ToByte(tensor[rowOffset + x]);
                dst[dstOff + 1] = ToByte(tensor[planeSize     + rowOffset + x]);
                dst[dstOff + 2] = ToByte(tensor[planeSize * 2 + rowOffset + x]);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ToByte(float value) =>
        (byte)(uint)MathF.Round(Math.Clamp(value * 255f, 0f, 255f));
}
