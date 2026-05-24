using System;

namespace FrameShift.Core.AI.SeparateAudio;

// OLA (Overlap-Add) ring buffer for HTDemucs streaming.
//
// Layout invariant: ring position 0 always corresponds to global sample _flushOrigin.
// After each Flush call the ring is shifted left by flushCount so the invariant holds.
//
// stemData passed to Accumulate must be laid out [stem, channel, sample]
// matching the HTDemucs ONNX output shape [stems, 2, ChunkLen] (batch dim removed).
internal sealed class OverlapAddRing
{
    public readonly record struct DrainedChunk(float[] StemData, int FramesPerChannel);

    public const int ChunkLen = 343_980;
    public const int ChunkHop = 257_985;  // ChunkLen * 0.75 — 25 % overlap

    private static readonly float[] TriangularWindow = BuildWindow();

    private readonly int _stemCount;
    // Flat [stemCount, 2, ChunkLen]: weighted stem-sample accumulator
    private readonly float[] _num;
    // Flat [ChunkLen]: window-weight accumulator (shared across stems)
    private readonly float[] _den;
    // Global sample index (per channel) that maps to ring position 0
    private long _flushOrigin;

    public OverlapAddRing(int stemCount)
    {
        _stemCount = stemCount;
        _num = new float[stemCount * 2 * ChunkLen];
        _den = new float[ChunkLen];
    }

    // Returns how many chunks are needed to cover totalSamplesPerChannel.
    public static int ComputeChunkCount(long totalSamplesPerChannel)
    {
        if (totalSamplesPerChannel <= ChunkLen)
            return 1;
        return (int)Math.Ceiling((double)(totalSamplesPerChannel - ChunkLen) / ChunkHop) + 1;
    }

    // Add one chunk's model output to the ring.
    // stemData: float[stemCount * 2 * ChunkLen], layout [stem, channel, sample].
    public void Accumulate(int chunkIndex, float[] stemData)
    {
        var ringOffset = (int)((long)chunkIndex * ChunkHop - _flushOrigin);

        for (var i = 0; i < ChunkLen; i++)
        {
            var w = TriangularWindow[i];
            var ringPos = ringOffset + i;
            _den[ringPos] += w;

            for (var sc = 0; sc < _stemCount * 2; sc++)
            {
                _num[sc * ChunkLen + ringPos] += w * stemData[sc * ChunkLen + i];
            }
        }
    }

    // Normalize and return finalized samples, then advance the ring.
    // isLastChunk: flush all remaining audio rather than exactly ChunkHop frames.
    // totalSamplesPerChannel: original audio length used to clamp the last-chunk flush.
    // Returned stemData layout: [stem, channel, sample] flattened as
    // [stem0-left][stem0-right][stem1-left][stem1-right]...
    public DrainedChunk Drain(bool isLastChunk, long totalSamplesPerChannel)
    {
        var flushCount = isLastChunk
            ? (int)Math.Min(totalSamplesPerChannel - _flushOrigin, (long)ChunkLen)
            : ChunkHop;

        var drained = new float[_stemCount * 2 * flushCount];
        for (var stemIndex = 0; stemIndex < _stemCount; stemIndex++)
        {
            var stemBase = stemIndex * 2 * ChunkLen;
            var outBase = stemIndex * 2 * flushCount;
            for (var pos = 0; pos < flushCount; pos++)
            {
                var d = _den[pos];
                var scale = d > 1e-8f ? 1f / d : 0f;
                drained[outBase + pos] = _num[stemBase + pos] * scale;
                drained[outBase + flushCount + pos] = _num[stemBase + ChunkLen + pos] * scale;
            }
        }

        ShiftRing(flushCount);
        _flushOrigin += flushCount;
        return new DrainedChunk(drained, flushCount);
    }

    private void ShiftRing(int flushCount)
    {
        var keepLen = ChunkLen - flushCount;
        if (keepLen > 0)
        {
            Array.Copy(_den, flushCount, _den, 0, keepLen);
            for (var sc = 0; sc < _stemCount * 2; sc++)
            {
                var b = sc * ChunkLen;
                Array.Copy(_num, b + flushCount, _num, b, keepLen);
            }
        }
        Array.Clear(_den, keepLen, flushCount);
        for (var sc = 0; sc < _stemCount * 2; sc++)
            Array.Clear(_num, sc * ChunkLen + keepLen, flushCount);
    }

    // Symmetric triangular window: rises (i+1)/half for i < half, falls symmetrically.
    // Denominator accumulation in Flush handles perfect reconstruction regardless of shape.
    private static float[] BuildWindow()
    {
        var w = new float[ChunkLen];
        var half = ChunkLen / 2;
        for (var i = 0; i < ChunkLen; i++)
            w[i] = i < half ? (float)(i + 1) / half : (float)(ChunkLen - i) / half;
        return w;
    }
}
