using System;

namespace FrameShift.Core.AI.SeparateAudio;

// Host-side STFT / iSTFT for the HTDemucs V2 GPU pipeline.
//
// Port direct de stft_onnx.py + wrapper HostSpectro de separate_streaming_gpu.py.
// Constantes : n_fft=4096, hop=1024, hann window, normalized=True, center=True, reflect.
//
// STFT:  mix (2, LEN) → spec (1, 2, 2048, 336, 2)
// iSTFT: mask_spec (1, 4, 2, 2048, 336, 2) → x_freq (1, 4, 2, LEN)
//
// Invariants critiques (cf. plan §HostSpectro) :
//   - le = ceil(L / hop)              (jamais L // hop)
//   - y = y_real + y_imag             (kernel_imag porte déjà -sin)
//   - reflect-pad 1536 outer + 2048 inner gauche / (1620 + 2048) droite
//   - scale_k[0]=scale_k[-1]=1, scale_k[1:-1]=2  (via hermitian-extension côté C#)
internal sealed class HostSpectro
{
    public const int NFft = 4096;
    public const int Hop = 1024;
    public const int FBins = NFft / 2 + 1;   // 2049
    public const int FKeep = NFft / 2;        // 2048
    public const int Le = 336;                 // ceil(ChunkLen / Hop)
    public const int ChunkLen = OverlapAddRing.ChunkLen;  // 343_980
    public const int OuterPad = Hop / 2 * 3;  // 1536
    public const int InnerPad = NFft / 2;      // 2048
    public const int TFrames = Le + 4;         // 340  (pad time +2/+2)

    private const int OuterPadRight = OuterPad + Le * Hop - ChunkLen;  // 1620
    private const int OuterLen = ChunkLen + OuterPad + OuterPadRight;   // 347_136
    private const int PaddedLen = OuterLen + 2 * InnerPad;              // 351_232
    private const int OlaLen = (TFrames - 1) * Hop + NFft;              // 351_232

    private static readonly float[] HannWindow = BuildHannWindow();
    private static readonly float SqrtN = (float)Math.Sqrt(NFft);
    private static readonly float InvSqrtN = 1f / SqrtN;

    // Reusable per-instance buffers — STFT/iSTFT run sequentially per chunk.
    private readonly float[] _fftReal = new float[NFft];
    private readonly float[] _fftImag = new float[NFft];
    private readonly float[] _padded = new float[PaddedLen];
    private readonly float[] _olaBuf = new float[OlaLen];
    private readonly float[] _envBuf = new float[OlaLen];

    // mix: float[2 * ChunkLen] interleaved stereo (L, R, L, R, ...).
    // Returns spec layout [channel, freq=2048, time=336, complex=2], length 2*2048*336*2.
    public float[] Stft(float[] mixInterleaved)
    {
        var spec = new float[2 * FKeep * Le * 2];

        for (var ch = 0; ch < 2; ch++)
        {
            BuildReflectPadded(mixInterleaved, ch, _padded);

            for (var t = 0; t < TFrames; t++)
            {
                var srcOffset = t * Hop;

                for (var n = 0; n < NFft; n++)
                {
                    _fftReal[n] = _padded[srcOffset + n] * HannWindow[n];
                    _fftImag[n] = 0f;
                }

                FftRadix2.Forward(_fftReal, _fftImag);

                // Time crop : keep frames [2 : 2+Le]. Skip the +2/+2 padding zone.
                if (t < 2 || t >= 2 + Le)
                    continue;

                var outT = t - 2;
                // Drop top freq : keep bins [0, FKeep).
                for (var f = 0; f < FKeep; f++)
                {
                    var idx = ((ch * FKeep + f) * Le + outT) * 2;
                    spec[idx]     = _fftReal[f] * InvSqrtN;
                    spec[idx + 1] = _fftImag[f] * InvSqrtN;
                }
            }
        }

        return spec;
    }

    // maskSpec layout [stem=4, channel=2, freq=2048, time=336, complex=2].
    // Returns x_freq layout [stem=4, channel=2, sample=ChunkLen].
    public float[] Istft(float[] maskSpec)
    {
        const int stemCount = 4;
        var result = new float[stemCount * 2 * ChunkLen];

        for (var stem = 0; stem < stemCount; stem++)
        {
            for (var ch = 0; ch < 2; ch++)
            {
                Array.Clear(_olaBuf, 0, OlaLen);
                Array.Clear(_envBuf, 0, OlaLen);

                for (var t = 0; t < TFrames; t++)
                {
                    var tInMask = t - 2;
                    if (tInMask < 0 || tInMask >= Le)
                    {
                        Array.Clear(_fftReal, 0, NFft);
                        Array.Clear(_fftImag, 0, NFft);
                    }
                    else
                    {
                        for (var f = 0; f < FKeep; f++)
                        {
                            var idx = (((stem * 2 + ch) * FKeep + f) * Le + tInMask) * 2;
                            _fftReal[f] = maskSpec[idx];
                            _fftImag[f] = maskSpec[idx + 1];
                        }
                        // Pad freq +1 : bin FKeep=2048 = 0
                        _fftReal[FKeep] = 0f;
                        _fftImag[FKeep] = 0f;
                        // Hermitian mirror : bins (NFft - f) = conj(bin f) for f in [1, FKeep)
                        for (var f = 1; f < FKeep; f++)
                        {
                            _fftReal[NFft - f] =  _fftReal[f];
                            _fftImag[NFft - f] = -_fftImag[f];
                        }
                    }

                    FftRadix2.Inverse(_fftReal, _fftImag);

                    // OLA: y[m] += sqrt(N) * window[n] * irfft[n] ; env[m] += window[n]^2
                    var ofs = t * Hop;
                    for (var n = 0; n < NFft; n++)
                    {
                        var w = HannWindow[n];
                        _olaBuf[ofs + n] += SqrtN * w * _fftReal[n];
                        _envBuf[ofs + n] += w * w;
                    }
                }

                // Combined crop : internal pad InnerPad + outer pad OuterPad
                const int cropStart = InnerPad + OuterPad;  // 3584
                var outBase = (stem * 2 + ch) * ChunkLen;
                for (var n = 0; n < ChunkLen; n++)
                {
                    var e = _envBuf[cropStart + n];
                    var y = _olaBuf[cropStart + n];
                    result[outBase + n] = e > 1e-11f ? y / e : 0f;
                }
            }
        }

        return result;
    }

    // Builds the doubly-reflected mono channel: outer pad (1536, 1620) then inner pad (2048, 2048).
    private static void BuildReflectPadded(float[] interleaved, int channel, float[] dest)
    {
        // Extract mono once into the middle of `dest`, leaving room for both pads on each side.
        // dest layout : [InnerPad zeros][OuterPad zeros][mono ChunkLen][OuterPadRight zeros][InnerPad zeros]
        const int outerLeftOfs = InnerPad;
        const int monoOfs = InnerPad + OuterPad;
        const int outerRightOfs = monoOfs + ChunkLen;
        const int innerRightOfs = outerRightOfs + OuterPadRight;

        for (var i = 0; i < ChunkLen; i++)
            dest[monoOfs + i] = interleaved[i * 2 + channel];

        // Outer reflect pad — reflects against the mono boundaries (excludes the boundary samples).
        for (var i = 0; i < OuterPad; i++)
            dest[monoOfs - 1 - i] = dest[monoOfs + 1 + i];
        for (var i = 0; i < OuterPadRight; i++)
            dest[outerRightOfs + i] = dest[outerRightOfs - 2 - i];

        // Inner reflect pad — reflects against the (already outer-padded) signal boundaries.
        for (var i = 0; i < InnerPad; i++)
            dest[outerLeftOfs - 1 - i] = dest[outerLeftOfs + 1 + i];
        for (var i = 0; i < InnerPad; i++)
            dest[innerRightOfs + i] = dest[innerRightOfs - 2 - i];
    }

    // PyTorch hann_window(N) defaults to periodic=True : w[n] = 0.5*(1 - cos(2π n / N)).
    private static float[] BuildHannWindow()
    {
        var w = new float[NFft];
        for (var n = 0; n < NFft; n++)
            w[n] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * n / NFft)));
        return w;
    }
}
