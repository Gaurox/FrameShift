using System;

namespace FrameShift.Core.AI.SeparateAudio;

// Minimal in-place radix-2 Cooley–Tukey FFT specialized for N=4096 (n_fft de HTDemucs).
// Pas de dépendance externe ; precomputed twiddle table + bit-reverse table.
internal static class FftRadix2
{
    public const int N = 4096;
    private const int LogN = 12;

    private static readonly float[] CosTwiddle = new float[N / 2];
    private static readonly float[] SinTwiddle = new float[N / 2];
    private static readonly int[] BitRev = new int[N];

    static FftRadix2()
    {
        for (var i = 0; i < N / 2; i++)
        {
            var theta = -2.0 * Math.PI * i / N;
            CosTwiddle[i] = (float)Math.Cos(theta);
            SinTwiddle[i] = (float)Math.Sin(theta);
        }
        for (var i = 0; i < N; i++)
        {
            var x = i;
            var j = 0;
            for (var b = 0; b < LogN; b++)
            {
                j = (j << 1) | (x & 1);
                x >>= 1;
            }
            BitRev[i] = j;
        }
    }

    // In-place forward DFT. Caller passes two float[N] (real, imag).
    public static void Forward(float[] real, float[] imag)
    {
        for (var i = 0; i < N; i++)
        {
            var j = BitRev[i];
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        for (var len = 2; len <= N; len <<= 1)
        {
            var half = len >> 1;
            var step = N / len;
            for (var i = 0; i < N; i += len)
            {
                for (var j = 0; j < half; j++)
                {
                    var wIdx = j * step;
                    var wr = CosTwiddle[wIdx];
                    var wi = SinTwiddle[wIdx];
                    var u = i + j;
                    var v = u + half;
                    var tr = real[v] * wr - imag[v] * wi;
                    var ti = real[v] * wi + imag[v] * wr;
                    real[v] = real[u] - tr;
                    imag[v] = imag[u] - ti;
                    real[u] += tr;
                    imag[u] += ti;
                }
            }
        }
    }

    // In-place inverse DFT via conjugate trick. Output divided by N.
    public static void Inverse(float[] real, float[] imag)
    {
        for (var i = 0; i < N; i++)
            imag[i] = -imag[i];
        Forward(real, imag);
        var inv = 1f / N;
        for (var i = 0; i < N; i++)
        {
            real[i] *= inv;
            imag[i] = -imag[i] * inv;
        }
    }
}
