using System;
using System.Threading.Tasks;

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
//
// Perf : la STFT (frames indépendantes) et l'iSTFT (4 stems × 2 canaux indépendants) sont
// parallélisées sur des buffers de travail distincts. La math par unité est inchangée
// (mêmes opérations float, même ordre d'accumulation) → sortie bit-à-bit identique.
// L'enveloppe OLA est data-indépendante : précalculée une fois au lieu de 8× par chunk.
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
    private const int StemCount = 4;
    private const int IstftUnits = StemCount * 2;   // 8 independent (stem, channel) reconstructions

    private static readonly float[] HannWindow = BuildHannWindow();
    private static readonly float SqrtN = (float)Math.Sqrt(NFft);
    private static readonly float InvSqrtN = 1f / SqrtN;

    // Data-independent precomputations (identical for every stem/channel/chunk).
    private static readonly float[] Envelope = BuildEnvelope();          // OLA window-weight accumulator
    private static readonly float[] ScaledWindow = BuildScaledWindow();  // SqrtN * window[n]

    // STFT runs one channel at a time; _padded holds the doubly-reflected mono channel.
    private readonly float[] _padded = new float[PaddedLen];

    // Pre-allocated per-unit scratch for the parallel iSTFT — unit u always uses _istftScratch[u],
    // so the 8 units never share a buffer and no per-chunk allocation occurs.
    private readonly IstftScratch[] _istftScratch = new IstftScratch[IstftUnits];

    public HostSpectro()
    {
        for (var i = 0; i < _istftScratch.Length; i++)
            _istftScratch[i] = new IstftScratch();
    }

    // mix: float[2 * ChunkLen] interleaved stereo (L, R, L, R, ...).
    // Returns spec layout [channel, freq=2048, time=336, complex=2], length 2*2048*336*2.
    public float[] Stft(float[] mixInterleaved)
    {
        var spec = new float[2 * FKeep * Le * 2];

        for (var ch = 0; ch < 2; ch++)
        {
            BuildReflectPadded(mixInterleaved, ch, _padded);
            var channel = ch;

            // Frames are independent; only frames [2, 2+Le) contribute to spec, the rest are
            // window+FFT'd then discarded in the serial version — skip them outright (same output).
            Parallel.For(
                2,
                2 + Le,
                () => (real: new float[NFft], imag: new float[NFft]),
                (t, _, buf) =>
                {
                    var real = buf.real;
                    var imag = buf.imag;
                    var srcOffset = t * Hop;

                    for (var n = 0; n < NFft; n++)
                    {
                        real[n] = _padded[srcOffset + n] * HannWindow[n];
                        imag[n] = 0f;
                    }

                    FftRadix2.Forward(real, imag);

                    var outT = t - 2;
                    for (var f = 0; f < FKeep; f++)
                    {
                        var idx = ((channel * FKeep + f) * Le + outT) * 2;
                        spec[idx]     = real[f] * InvSqrtN;
                        spec[idx + 1] = imag[f] * InvSqrtN;
                    }

                    return buf;
                },
                _ => { });
        }

        return spec;
    }

    // maskSpec layout [stem=4, channel=2, freq=2048, time=336, complex=2].
    // Returns x_freq layout [stem=4, channel=2, sample=ChunkLen].
    public float[] Istft(float[] maskSpec)
    {
        var result = new float[StemCount * 2 * ChunkLen];

        // The 8 (stem, channel) reconstructions are fully independent: each reads a disjoint
        // slice of maskSpec and writes a disjoint slice of result. Per-unit math is identical
        // to the serial version, so the output is bit-for-bit the same.
        Parallel.For(0, IstftUnits, unit =>
        {
            var stem = unit >> 1;
            var ch = unit & 1;
            var scratch = _istftScratch[unit];
            var real = scratch.Real;
            var imag = scratch.Imag;
            var ola = scratch.Ola;

            Array.Clear(ola, 0, OlaLen);

            for (var t = 0; t < TFrames; t++)
            {
                var tInMask = t - 2;
                if (tInMask < 0 || tInMask >= Le)
                {
                    Array.Clear(real, 0, NFft);
                    Array.Clear(imag, 0, NFft);
                }
                else
                {
                    for (var f = 0; f < FKeep; f++)
                    {
                        var idx = (((stem * 2 + ch) * FKeep + f) * Le + tInMask) * 2;
                        real[f] = maskSpec[idx];
                        imag[f] = maskSpec[idx + 1];
                    }
                    // Pad freq +1 : bin FKeep=2048 = 0
                    real[FKeep] = 0f;
                    imag[FKeep] = 0f;
                    // Hermitian mirror : bins (NFft - f) = conj(bin f) for f in [1, FKeep)
                    for (var f = 1; f < FKeep; f++)
                    {
                        real[NFft - f] =  real[f];
                        imag[NFft - f] = -imag[f];
                    }
                }

                FftRadix2.Inverse(real, imag);

                // OLA numerator: y[m] += sqrt(N) * window[n] * irfft[n].
                // ScaledWindow[n] == SqrtN * window[n] (same float product as the serial path).
                var ofs = t * Hop;
                for (var n = 0; n < NFft; n++)
                    ola[ofs + n] += ScaledWindow[n] * real[n];
            }

            // Combined crop : internal pad InnerPad + outer pad OuterPad. Divide by the
            // precomputed window envelope (identical to the per-unit env in the serial version).
            const int cropStart = InnerPad + OuterPad;  // 3584
            var outBase = (stem * 2 + ch) * ChunkLen;
            for (var n = 0; n < ChunkLen; n++)
            {
                var e = Envelope[cropStart + n];
                var y = ola[cropStart + n];
                result[outBase + n] = e > 1e-11f ? y / e : 0f;
            }
        });

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

    // env[m] = Σ_t window[n]^2 over all frames, exactly as accumulated in the serial iSTFT loop.
    private static float[] BuildEnvelope()
    {
        var env = new float[OlaLen];
        for (var t = 0; t < TFrames; t++)
        {
            var ofs = t * Hop;
            for (var n = 0; n < NFft; n++)
            {
                var w = HannWindow[n];
                env[ofs + n] += w * w;
            }
        }
        return env;
    }

    private static float[] BuildScaledWindow()
    {
        var sw = new float[NFft];
        for (var n = 0; n < NFft; n++)
            sw[n] = SqrtN * HannWindow[n];
        return sw;
    }

    private sealed class IstftScratch
    {
        public readonly float[] Real = new float[NFft];
        public readonly float[] Imag = new float[NFft];
        public readonly float[] Ola = new float[OlaLen];
    }
}
