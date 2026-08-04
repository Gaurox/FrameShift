using System;
using FrameShift.Core.AI.SeparateAudio;
using Microsoft.ML.OnnxRuntime.Tensors;
using Xunit;

namespace FrameShift.Tests;

// Lightweight numerical-behavior guards for the optimized (parallel) separate-audio host path.
// No ONNX model, no large/real audio, no benchmark — small deterministic synthetic signals only.
// Golden values were captured from the validated build and are frozen here so any change to the
// STFT / iSTFT / host reconstruction / tensor-marshalling numerics is caught.
public sealed class SeparateAudioHostSpectroTests
{
    // Frozen from the validated build (bit-identical, deterministic on win-x64).
    private const double StftSum = -0.40779349505552454;
    private const double StftAbsSum = 11538.79943297987;
    private const double IstftSum = -11.640532907540333;
    private const double IstftAbsSum = 390.59166198248494;

    [Fact]
    public void Stft_IsDeterministic_AndMatchesGolden()
    {
        var spectro = new HostSpectro();
        var mix = BuildSine(HostSpectro.ChunkLen, 440.0, 0.5, 220.0, 0.3);

        var a = spectro.Stft(mix);
        var b = spectro.Stft(mix);

        // Parallel STFT must be deterministic (disjoint outputs, no cross-thread reduction).
        Assert.Equal(a, b);

        var (sum, absSum) = Sum(a);
        Assert.Equal(StftSum, sum, 3);                          // ~1e-3 absolute
        Assert.True(Math.Abs(absSum - StftAbsSum) / StftAbsSum < 1e-6, $"STFT absSum drift: {absSum}");
        Assert.Equal(0.21267381f, a[0], 5);
        Assert.Equal(0.25545532f, a[a.Length / 2], 5);
    }

    [Fact]
    public void Istft_IsDeterministic_AndMatchesGolden()
    {
        var spectro = new HostSpectro();
        var mask = BuildMask();

        var a = spectro.Istft(mask);
        var b = spectro.Istft(mask);

        // 8 (stem, channel) units run in parallel on disjoint buffers → deterministic.
        Assert.Equal(a, b);

        var (sum, absSum) = Sum(a);
        Assert.Equal(IstftSum, sum, 3);
        Assert.True(Math.Abs(absSum - IstftAbsSum) / IstftAbsSum < 1e-6, $"iSTFT absSum drift: {absSum}");
        Assert.Equal(1.0171824E-05f, a[0], 9);
        Assert.Equal(-8.368405E-05f, a[^1], 9);
    }

    [Fact]
    public void StftThenIstft_ReconstructsInterior_WithinStrictTolerance()
    {
        var spectro = new HostSpectro();
        // Low-frequency sine → negligible Nyquist content dropped by the FKeep truncation,
        // so an identity mask must reconstruct the signal almost exactly in the interior.
        var mix = BuildSine(HostSpectro.ChunkLen, 100.0, 0.6, 100.0, 0.6);
        var spec = spectro.Stft(mix);

        var mask = new float[4 * 2 * HostSpectro.FKeep * HostSpectro.Le * 2];
        Array.Copy(spec, 0, mask, 0, spec.Length); // stem 0 (ch0+ch1) = spec; other stems = 0

        var rt = spectro.Istft(mask);

        double maxAbs = 0;
        for (var s = 50_000; s < HostSpectro.ChunkLen - 50_000; s++)
        {
            var d = Math.Abs(rt[s] - mix[s * 2]); // stem0/ch0 vs original left channel
            if (d > maxAbs) maxAbs = d;
        }

        Assert.True(maxAbs < 1e-5, $"Round-trip interior reconstruction error too large: {maxAbs}");
    }

    [Fact]
    public void DenseTensorBuffer_IsRowMajor_MatchingMarshallingAssumption()
    {
        // The optimized marshalling treats DenseTensor.Buffer as contiguous row-major and relies on
        // its layout matching the engine's flat arrays. Freeze that contract for every shape used:
        AssertRowMajor(new[] { 1, 2, 3, 4, 2 });     // BuildSpecInput  [1,2,FKeep,Le,2]
        AssertRowMajor(new[] { 1, 4, 2, 3, 4, 2 });  // ExtractMaskSpec [1,4,2,FKeep,Le,2]
        AssertRowMajor(new[] { 1, 4, 2, 7 });        // ExtractStems    [1,4,2,ChunkLen]
        AssertRowMajor(new[] { 1, 2, 7 });           // BuildMixInput   [1,2,ChunkLen]

        // BuildMixInput deinterleave: span[s]=L, span[len+s]=R must match the [0,ch,s] indexer view.
        const int len = 6;
        var interleaved = new float[len * 2];
        for (var i = 0; i < interleaved.Length; i++) interleaved[i] = i + 1;
        var t = new DenseTensor<float>(new[] { 1, 2, len });
        var span = t.Buffer.Span;
        for (var s = 0; s < len; s++)
        {
            span[s] = interleaved[s * 2];
            span[len + s] = interleaved[s * 2 + 1];
        }
        for (var s = 0; s < len; s++)
        {
            Assert.Equal(interleaved[s * 2], t[0, 0, s]);
            Assert.Equal(interleaved[s * 2 + 1], t[0, 1, s]);
        }
    }

    private static void AssertRowMajor(int[] dims)
    {
        var t = new DenseTensor<float>(dims);
        var span = t.Buffer.Span;
        for (var i = 0; i < span.Length; i++) span[i] = i;

        var idx = new int[dims.Length];
        for (var flat = 0; flat < span.Length; flat++)
        {
            Assert.Equal((float)flat, t[idx]); // multi-dim indexer must read buffer in row-major order
            for (var d = dims.Length - 1; d >= 0; d--)
            {
                if (++idx[d] < dims[d]) break;
                idx[d] = 0;
            }
        }
    }

    private static float[] BuildSine(int frames, double fL, double aL, double fR, double aR)
    {
        const double sr = 44100.0;
        var mix = new float[frames * 2];
        for (var s = 0; s < frames; s++)
        {
            mix[s * 2] = (float)(aL * Math.Sin(2 * Math.PI * fL * s / sr));
            mix[s * 2 + 1] = (float)(aR * Math.Sin(2 * Math.PI * fR * s / sr));
        }
        return mix;
    }

    private static float[] BuildMask()
    {
        var mask = new float[4 * 2 * HostSpectro.FKeep * HostSpectro.Le * 2];
        for (var i = 0; i < mask.Length; i++)
            mask[i] = (float)(0.01 * Math.Sin(i * 0.000123));
        return mask;
    }

    private static (double sum, double absSum) Sum(float[] a)
    {
        double s = 0, abs = 0;
        for (var i = 0; i < a.Length; i++) { s += a[i]; abs += Math.Abs(a[i]); }
        return (s, abs);
    }
}
