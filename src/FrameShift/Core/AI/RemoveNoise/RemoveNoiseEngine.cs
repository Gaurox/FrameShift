using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FrameShift.Core.AI.RemoveNoise;

internal sealed class RemoveNoiseEngine : IDisposable
{
    private const int Sr = 48_000;
    private const int FftSize = 960;
    private const int HopSize = 480;
    private const int NbErb = 32;
    private const int NbDf = 96;
    private const int Lookahead = 2;
    private const int SpecBins = FftSize / 2 + 1;
    private const float NormTau = 1.0f;
    private const float MeanNormInitMin = -60.0f;
    private const float MeanNormInitMax = -90.0f;
    private const float MeanNormDenom = 40.0f;
    private const float UnitNormInitMin = 0.001f;
    private const float UnitNormInitMax = 0.0001f;
    private const int MinNbErbFreqs = 2;

    private readonly SemaphoreSlim _initGate = new(1, 1);
    private InferenceSession? _encSession;
    private InferenceSession? _erbDecSession;
    private InferenceSession? _dfDecSession;
    private bool _ready;

    public async Task<string> RemoveNoiseAsync(
        string inputPath,
        IProgress<(int percent, string status)> progress,
        CancellationToken cancellationToken,
        float minGain = 0f)
    {
        return await Task.Run(
            () => RemoveNoiseCore(inputPath, progress, cancellationToken, minGain),
            cancellationToken).ConfigureAwait(false);
    }

    private string RemoveNoiseCore(
        string inputPath,
        IProgress<(int percent, string status)> progress,
        CancellationToken cancellationToken,
        float minGain)
    {
        string? outputPath = null;

        cancellationToken.ThrowIfCancellationRequested();
        EnsureSessions(progress, cancellationToken);

        progress.Report((5, "Loading audio..."));
        float[] samples = LoadMonoFloat48k(inputPath, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report((15, "Analyzing audio..."));

        int[] erbBandWidths = BuildOfficialErbBandWidths(Sr, FftSize, NbErb, MinNbErbFreqs);
        var fft = new BluesteinFft(FftSize);
        var analysisState = new OfficialLibDfState(FftSize, HopSize, fft);
        var synthesisState = new OfficialLibDfState(FftSize, HopSize, fft);

        float[] analysisAudio = new float[samples.Length + FftSize];
        Array.Copy(samples, analysisAudio, samples.Length);
        int totalFrames = (int)Math.Ceiling(analysisAudio.Length / (double)HopSize);

        var analyzedRe = new float[totalFrames][];
        var analyzedIm = new float[totalFrames][];
        for (int i = 0; i < totalFrames; i++)
        {
            analyzedRe[i] = new float[SpecBins];
            analyzedIm[i] = new float[SpecBins];
        }

        var analysisChunk = new float[HopSize];
        for (int frameIdx = 0; frameIdx < totalFrames; frameIdx++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int chunkOffset = frameIdx * HopSize;
            Array.Clear(analysisChunk, 0, analysisChunk.Length);
            int copyLen = Math.Min(HopSize, analysisAudio.Length - chunkOffset);
            if (copyLen > 0)
                Array.Copy(analysisAudio, chunkOffset, analysisChunk, 0, copyLen);

            analysisState.AnalysisFrame(analysisChunk, analyzedRe[frameIdx], analyzedIm[frameIdx]);
        }

        progress.Report((30, "Building model features..."));
        float[] erbFramesAll = new float[totalFrames * NbErb];
        float[] specFeatAll = new float[2 * totalFrames * NbDf];
        float normAlpha = MathF.Exp(-(float)HopSize / Sr / NormTau);
        float oneMinusAlpha = 1f - normAlpha;
        float[] erbNormState = MakeLinearRamp(MeanNormInitMin, MeanNormInitMax, NbErb);
        float[] specNormState = MakeLinearRamp(UnitNormInitMin, UnitNormInitMax, NbDf);

        for (int frameIdx = 0; frameIdx < totalFrames; frameIdx++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            float[] specRe = analyzedRe[frameIdx];
            float[] specIm = analyzedIm[frameIdx];

            int erbBase = frameIdx * NbErb;
            int erbBinOffset = 0;
            for (int b = 0; b < NbErb; b++)
            {
                float weightedPower = 0f;
                int bandWidth = erbBandWidths[b];
                for (int j = 0; j < bandWidth; j++)
                {
                    int k = erbBinOffset + j;
                    weightedPower += specRe[k] * specRe[k] + specIm[k] * specIm[k];
                }

                float xDb = bandWidth > 0
                    ? 10f * MathF.Log10(MathF.Max(weightedPower / bandWidth, 1e-10f))
                    : -100f;

                erbNormState[b] = normAlpha * erbNormState[b] + oneMinusAlpha * xDb;
                erbFramesAll[erbBase + b] = (xDb - erbNormState[b]) / MeanNormDenom;
                erbBinOffset += bandWidth;
            }

            int specReBase = frameIdx * NbDf;
            int specImBase = totalFrames * NbDf + frameIdx * NbDf;
            for (int k = 0; k < NbDf; k++)
            {
                float mag = MathF.Sqrt(specRe[k] * specRe[k] + specIm[k] * specIm[k]);
                specNormState[k] = normAlpha * specNormState[k] + oneMinusAlpha * mag;
                float inv = 1f / MathF.Sqrt(MathF.Max(specNormState[k], 1e-10f));
                specFeatAll[specReBase + k] = specRe[k] * inv;
                specFeatAll[specImBase + k] = specIm[k] * inv;
            }
        }

        progress.Report((45, "Running AI inference..."));
        float[] featErbModel = ShiftFeatureTime(erbFramesAll, 1, totalFrames, NbErb, Lookahead);
        float[] featSpecModel = ShiftFeatureTime(specFeatAll, 2, totalFrames, NbDf, Lookahead);

        var emptyHidden = new Dictionary<string, DenseTensor<float>>(StringComparer.Ordinal);
        var encInputs = BuildInputs(_encSession!, emptyHidden,
            ("feat_erb", featErbModel, new[] { 1, 1, totalFrames, NbErb }),
            ("feat_spec", featSpecModel, new[] { 1, 2, totalFrames, NbDf }));

        Dictionary<string, (float[] data, int[] shape)> encOutputs;
        using (var res = _encSession!.Run(encInputs))
        {
            encOutputs = ExtractAllDataOutputs(res);
        }

        float[] rawErbGainsAll;
        int[] erbGainsShape;
        var erbDecInputs = BuildInputsFromDict(_erbDecSession!, emptyHidden, encOutputs);
        using (var res = _erbDecSession!.Run(erbDecInputs))
        {
            (rawErbGainsAll, erbGainsShape) = GetFirstDataOutput(res);
        }

        float[] rawDfCoeffsAll;
        int[] dfCoeffsShape;
        var dfDecInputs = BuildInputsFromDict(_dfDecSession!, emptyHidden, encOutputs);
        using (var res = _dfDecSession!.Run(dfDecInputs))
        {
            var dfOutputs = ExtractAllDataOutputs(res);
            if (!dfOutputs.TryGetValue("coefs", out var coefsOut))
                throw new InvalidOperationException($"df_dec output 'coefs' not found. Have: [{string.Join(", ", dfOutputs.Keys)}]");
            rawDfCoeffsAll = coefsOut.data;
            dfCoeffsShape = coefsOut.shape;
        }

        int dfOrder = rawDfCoeffsAll.Length / (NbDf * 2 * totalFrames);
        if (dfOrder != 5)
        {
            throw new InvalidOperationException(
                $"Unexpected DF order inferred from runtime output: {dfOrder}. Runtime shape: [{string.Join(",", dfCoeffsShape)}].");
        }

        if (dfCoeffsShape.Length != 4 || dfCoeffsShape[1] != totalFrames || dfCoeffsShape[2] != NbDf || dfCoeffsShape[3] != dfOrder * 2)
        {
            throw new InvalidOperationException(
                $"Unexpected df_dec runtime shape: [{string.Join(",", dfCoeffsShape)}]. " +
                $"Expected [1,{totalFrames},{NbDf},{dfOrder * 2}] for full-sequence inference.");
        }

        progress.Report((70, "Reconstructing cleaned audio..."));
        var processedRe = new float[totalFrames][];
        var processedIm = new float[totalFrames][];
        for (int i = 0; i < totalFrames; i++)
        {
            processedRe[i] = new float[SpecBins];
            processedIm[i] = new float[SpecBins];
        }

        for (int frameIdx = 0; frameIdx < totalFrames; frameIdx++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            float[] specRe = new float[SpecBins];
            float[] specIm = new float[SpecBins];
            Array.Copy(analyzedRe[frameIdx], specRe, SpecBins);
            Array.Copy(analyzedIm[frameIdx], specIm, SpecBins);

            float[] erbGains = ExtractFrameSlice(rawErbGainsAll, erbGainsShape, 2, frameIdx);
            float[] rawDfCoeffs = ExtractFrameSlice(rawDfCoeffsAll, dfCoeffsShape, 1, frameIdx);

            var specMaskedRe = new float[SpecBins];
            var specMaskedIm = new float[SpecBins];
            Array.Copy(specRe, specMaskedRe, SpecBins);
            Array.Copy(specIm, specMaskedIm, SpecBins);
            ApplyOfficialErbBandGains(specMaskedRe, specMaskedIm, erbGains, erbBandWidths, minGain);

            var dfOutputRe = new float[SpecBins];
            var dfOutputIm = new float[SpecBins];
            Array.Copy(specRe, dfOutputRe, SpecBins);
            Array.Copy(specIm, dfOutputIm, SpecBins);

            ApplyOfficialMfDfFullSequence(
                analyzedRe,
                analyzedIm,
                rawDfCoeffs,
                dfOrder,
                Lookahead,
                frameIdx,
                NbDf,
                dfOutputRe,
                dfOutputIm);

            // apply attenuation floor to DF bins (blend with original)
            if (minGain > 0f)
            {
                for (int k = 0; k < NbDf; k++)
                {
                    dfOutputRe[k] += minGain * (specRe[k] - dfOutputRe[k]);
                    dfOutputIm[k] += minGain * (specIm[k] - dfOutputIm[k]);
                }
            }

            for (int k = NbDf; k < SpecBins; k++)
            {
                dfOutputRe[k] = specMaskedRe[k];
                dfOutputIm[k] = specMaskedIm[k];
            }

            Array.Copy(dfOutputRe, processedRe[frameIdx], SpecBins);
            Array.Copy(dfOutputIm, processedIm[frameIdx], SpecBins);
        }

        float[] synthAudio = new float[totalFrames * HopSize];
        var synthChunk = new float[HopSize];
        for (int frameIdx = 0; frameIdx < totalFrames; frameIdx++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            synthesisState.SynthesisFrame(processedRe[frameIdx], processedIm[frameIdx], synthChunk);
            Array.Copy(synthChunk, 0, synthAudio, frameIdx * HopSize, HopSize);
        }

        float[] clean = new float[samples.Length];
        int delay = FftSize - HopSize;
        int synthCopyLen = Math.Min(samples.Length, synthAudio.Length - delay);
        if (synthCopyLen > 0)
            Array.Copy(synthAudio, delay, clean, 0, synthCopyLen);

        progress.Report((92, "Saving cleaned file..."));
        outputPath = OutputPathHelper.CreateUniqueOutputPath(inputPath, "_clean", ".wav");
        try
        {
            WriteWavPcm16(outputPath, clean, Sr);
        }
        catch
        {
            DeletePartialOutput(outputPath);
            throw;
        }

        progress.Report((100, "Done."));
        return outputPath;
    }

    private void EnsureSessions(IProgress<(int percent, string status)> progress, CancellationToken cancellationToken)
    {
        if (_ready)
            return;

        _initGate.Wait(cancellationToken);
        try
        {
            if (_ready)
                return;

            progress.Report((2, "Loading AI models..."));
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount)
            };

            _encSession = new InferenceSession(DeepFilterNetModelLocator.EncPath, options);
            _erbDecSession = new InferenceSession(DeepFilterNetModelLocator.ErbDecPath, options);
            _dfDecSession = new InferenceSession(DeepFilterNetModelLocator.DfDecPath, options);
            _ready = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private static float[] LoadMonoFloat48k(string path, CancellationToken cancellationToken)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider source = reader;
        if (reader.WaveFormat.SampleRate != Sr)
            source = new WdlResamplingSampleProvider(source, Sr);
        if (source.WaveFormat.Channels > 1)
            source = source.ToMono();

        var samples = new List<float>(Sr * 60);
        var buffer = new float[8192];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            samples.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        return samples.ToArray();
    }

    private static float FreqToErb(float hz) => 9.265f * MathF.Log(1f + hz / (24.7f * 9.265f));

    private static float ErbToFreq(float erb) => 24.7f * 9.265f * (MathF.Exp(erb / 9.265f) - 1f);

    private static int[] BuildOfficialErbBandWidths(int sr, int fftSize, int nbBands, int minNbFreqs)
    {
        int nyqFreq = sr / 2;
        float freqWidth = sr / (float)fftSize;
        float erbLow = FreqToErb(0f);
        float erbHigh = FreqToErb(nyqFreq);
        float step = (erbHigh - erbLow) / nbBands;
        var erb = new int[nbBands];
        int prevFreq = 0;
        int freqOver = 0;

        for (int i = 1; i < nbBands + 1; i++)
        {
            float f = ErbToFreq(erbLow + i * step);
            int fb = (int)MathF.Round(f / freqWidth);
            int nbFreqs = fb - prevFreq - freqOver;
            if (nbFreqs < minNbFreqs)
            {
                freqOver = minNbFreqs - nbFreqs;
                nbFreqs = minNbFreqs;
            }
            else
            {
                freqOver = 0;
            }

            erb[i - 1] = nbFreqs;
            prevFreq = fb;
        }

        erb[nbBands - 1] += 1;
        int tooLarge = erb.Sum() - (fftSize / 2 + 1);
        if (tooLarge > 0)
            erb[nbBands - 1] -= tooLarge;

        return erb;
    }

    private static float[] MakeLinearRamp(float min, float max, int count)
    {
        var result = new float[count];
        if (count == 1)
        {
            result[0] = min;
            return result;
        }

        float step = (max - min) / (count - 1);
        for (int i = 0; i < count; i++)
            result[i] = min + i * step;
        return result;
    }

    private static float[] ShiftFeatureTime(float[] src, int channels, int frames, int bins, int lookahead)
    {
        var dst = new float[src.Length];
        for (int c = 0; c < channels; c++)
        {
            int channelBase = c * frames * bins;
            for (int t = 0; t < frames; t++)
            {
                int srcT = t + lookahead;
                if (srcT >= frames)
                    continue;

                Array.Copy(src, channelBase + srcT * bins, dst, channelBase + t * bins, bins);
            }
        }

        return dst;
    }

    private static float[] ExtractFrameSlice(float[] data, int[] shape, int timeDim, int frameIndex)
    {
        int outer = 1;
        for (int i = 0; i < timeDim; i++)
            outer *= shape[i];

        int inner = 1;
        for (int i = timeDim + 1; i < shape.Length; i++)
            inner *= shape[i];

        var result = new float[outer * inner];
        int timeStride = shape[timeDim] * inner;
        for (int o = 0; o < outer; o++)
        {
            int srcOffset = o * timeStride + frameIndex * inner;
            Array.Copy(data, srcOffset, result, o * inner, inner);
        }

        return result;
    }

    private static void ApplyOfficialErbBandGains(float[] specRe, float[] specIm, float[] erbGains, int[] erbBandWidths, float minGain = 0f)
    {
        int offset = 0;
        for (int b = 0; b < erbBandWidths.Length; b++)
        {
            float gain = MathF.Max(erbGains[b], minGain);
            int bandWidth = erbBandWidths[b];
            for (int j = 0; j < bandWidth; j++)
            {
                int k = offset + j;
                specRe[k] *= gain;
                specIm[k] *= gain;
            }

            offset += bandWidth;
        }
    }

    private static void ApplyOfficialMfDfFullSequence(
        float[][] analyzedRe,
        float[][] analyzedIm,
        float[] rawDfCoeffsFrame,
        int dfOrder,
        int lookahead,
        int frameIdx,
        int nbDf,
        float[] outRe,
        float[] outIm)
    {
        int prePad = dfOrder - 1 - lookahead;
        for (int k = 0; k < nbDf; k++)
        {
            float sumRe = 0f;
            float sumIm = 0f;
            int coefBase = k * (dfOrder * 2);
            for (int n = 0; n < dfOrder; n++)
            {
                int srcFrame = frameIdx - prePad + n;
                if ((uint)srcFrame >= (uint)analyzedRe.Length)
                    continue;

                float xr = analyzedRe[srcFrame][k];
                float xi = analyzedIm[srcFrame][k];
                float cr = rawDfCoeffsFrame[coefBase + n * 2];
                float ci = rawDfCoeffsFrame[coefBase + n * 2 + 1];
                sumRe += xr * cr - xi * ci;
                sumIm += xr * ci + xi * cr;
            }

            outRe[k] = sumRe;
            outIm[k] = sumIm;
        }
    }

    private static NamedOnnxValue[] BuildInputs(
        InferenceSession session,
        Dictionary<string, DenseTensor<float>> hidden,
        params (string name, float[] data, int[] shape)[] dataInputs)
    {
        var list = new List<NamedOnnxValue>();
        foreach (var inputName in session.InputMetadata.Keys)
        {
            if (IsHidden(inputName))
            {
                if (hidden.TryGetValue(inputName, out var hs))
                    list.Add(NamedOnnxValue.CreateFromTensor(inputName, hs));
                continue;
            }

            var match = dataInputs.FirstOrDefault(d =>
                string.Equals(d.name, inputName, StringComparison.OrdinalIgnoreCase));
            if (match != default)
            {
                var tensor = new DenseTensor<float>(match.shape);
                match.data.AsSpan(0, Math.Min(match.data.Length, (int)tensor.Length)).CopyTo(tensor.Buffer.Span);
                list.Add(NamedOnnxValue.CreateFromTensor(inputName, tensor));
            }
        }

        return list.ToArray();
    }

    private static Dictionary<string, (float[] data, int[] shape)> ExtractAllDataOutputs(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        var outputs = new Dictionary<string, (float[] data, int[] shape)>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            if (IsHidden(result.Name))
                continue;

            var tensor = result.AsTensor<float>();
            outputs[result.Name] = (tensor.ToArray(), tensor.Dimensions.ToArray());
        }

        return outputs;
    }

    private static NamedOnnxValue[] BuildInputsFromDict(
        InferenceSession session,
        Dictionary<string, DenseTensor<float>> hidden,
        Dictionary<string, (float[] data, int[] shape)> available)
    {
        var list = new List<NamedOnnxValue>();
        foreach (var inputName in session.InputMetadata.Keys)
        {
            if (IsHidden(inputName))
            {
                if (hidden.TryGetValue(inputName, out var hs))
                    list.Add(NamedOnnxValue.CreateFromTensor(inputName, hs));
                continue;
            }

            if (!available.TryGetValue(inputName, out var entry))
            {
                throw new InvalidOperationException(
                    $"Required input '{inputName}' not found in available outputs. Have: [{string.Join(", ", available.Keys)}]");
            }

            var tensor = new DenseTensor<float>(entry.shape);
            entry.data.AsSpan(0, Math.Min(entry.data.Length, (int)tensor.Length)).CopyTo(tensor.Buffer.Span);
            list.Add(NamedOnnxValue.CreateFromTensor(inputName, tensor));
        }

        return list.ToArray();
    }

    private static (float[] data, int[] shape) GetFirstDataOutput(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        foreach (var result in results)
        {
            if (IsHidden(result.Name))
                continue;

            var tensor = result.AsTensor<float>();
            return (tensor.ToArray(), tensor.Dimensions.ToArray());
        }

        throw new InvalidOperationException("No data output found.");
    }

    private static bool IsHidden(string name) =>
        name.StartsWith("h_", StringComparison.Ordinal) ||
        name.StartsWith("c_", StringComparison.Ordinal) ||
        name.StartsWith("state", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("hidden", StringComparison.OrdinalIgnoreCase);

    private static void WriteWavPcm16(string path, float[] samples, int sr)
    {
        var format = new WaveFormat(sr, 16, 1);
        using var writer = new WaveFileWriter(path, format);
        foreach (var sample in samples)
            writer.WriteSample(Math.Clamp(sample, -1f, 1f));
    }

    private static void DeletePartialOutput(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _encSession?.Dispose();
        _erbDecSession?.Dispose();
        _dfDecSession?.Dispose();
        _initGate.Dispose();
    }

    private sealed class OfficialLibDfState
    {
        private readonly int _fftSize;
        private readonly int _hopSize;
        private readonly int _specBins;
        private readonly BluesteinFft _fft;
        private readonly float[] _window;
        private readonly float _wnorm;
        private readonly float[] _analysisMem;
        private readonly float[] _synthesisMem;
        private readonly Complex[] _fftBuffer;
        private readonly float[] _timeBuffer;

        public OfficialLibDfState(int fftSize, int hopSize, BluesteinFft fft)
        {
            _fftSize = fftSize;
            _hopSize = hopSize;
            _specBins = fftSize / 2 + 1;
            _fft = fft;
            _window = BuildVorbisWindow(fftSize);
            _wnorm = 1f / ((fftSize * fftSize) / (2f * hopSize));
            _analysisMem = new float[fftSize - hopSize];
            _synthesisMem = new float[fftSize - hopSize];
            _fftBuffer = new Complex[fftSize];
            _timeBuffer = new float[fftSize];
        }

        public void AnalysisFrame(float[] inputHop, float[] outRe, float[] outIm)
        {
            int firstLen = _fftSize - _hopSize;
            int analysisSplit = _analysisMem.Length - _hopSize;

            for (int i = 0; i < firstLen; i++)
                _timeBuffer[i] = _analysisMem[i] * _window[i];
            for (int i = 0; i < _hopSize; i++)
                _timeBuffer[firstLen + i] = inputHop[i] * _window[firstLen + i];

            if (analysisSplit > 0)
                Array.Copy(_analysisMem, _hopSize, _analysisMem, 0, analysisSplit);
            Array.Copy(inputHop, 0, _analysisMem, analysisSplit, _hopSize);

            for (int i = 0; i < _fftSize; i++)
                _fftBuffer[i] = new Complex(_timeBuffer[i], 0.0);
            _fft.Forward(_fftBuffer);

            for (int k = 0; k < _specBins; k++)
            {
                outRe[k] = (float)_fftBuffer[k].Real * _wnorm;
                outIm[k] = (float)_fftBuffer[k].Imaginary * _wnorm;
            }
        }

        public void SynthesisFrame(float[] specRe, float[] specIm, float[] outHop)
        {
            for (int k = 0; k < _specBins; k++)
                _fftBuffer[k] = new Complex(specRe[k], specIm[k]);
            for (int k = _specBins; k < _fftSize; k++)
                _fftBuffer[k] = Complex.Conjugate(_fftBuffer[_fftSize - k]);

            _fft.Inverse(_fftBuffer);

            for (int i = 0; i < _fftSize; i++)
                _timeBuffer[i] = (float)_fftBuffer[i].Real * _window[i];

            for (int i = 0; i < _hopSize; i++)
                outHop[i] = _timeBuffer[i] + _synthesisMem[i];

            int split = _synthesisMem.Length - _hopSize;
            if (split > 0)
                Array.Copy(_synthesisMem, _hopSize, _synthesisMem, 0, split);

            for (int i = 0; i < split; i++)
                _synthesisMem[i] += _timeBuffer[_hopSize + i];
            for (int i = 0; i < _hopSize; i++)
                _synthesisMem[split + i] = _timeBuffer[_hopSize + split + i];
        }

        private static float[] BuildVorbisWindow(int fftSize)
        {
            int windowHalf = fftSize / 2;
            double pi = Math.PI;
            var window = new float[fftSize];
            for (int i = 0; i < fftSize; i++)
            {
                double sin = Math.Sin(0.5 * pi * (i + 0.5) / windowHalf);
                window[i] = (float)Math.Sin(0.5 * pi * sin * sin);
            }

            return window;
        }
    }

    private sealed class BluesteinFft
    {
        private readonly int _n;
        private readonly int _m;
        private readonly Complex[] _w;
        private readonly Complex[] _bFft;

        public BluesteinFft(int n)
        {
            _n = n;
            _m = 1;
            while (_m < 2 * n - 1)
                _m <<= 1;

            _w = new Complex[n];
            for (int k = 0; k < n; k++)
            {
                double angle = -Math.PI * (double)k * k / n;
                _w[k] = new Complex(Math.Cos(angle), Math.Sin(angle));
            }

            var b = new Complex[_m];
            b[0] = Complex.Conjugate(_w[0]);
            for (int k = 1; k < n; k++)
            {
                var c = Complex.Conjugate(_w[k]);
                b[k] = c;
                b[_m - k] = c;
            }

            _bFft = (Complex[])b.Clone();
            Radix2Forward(_bFft);
        }

        public void Forward(Complex[] x)
        {
            if (x.Length != _n)
                throw new ArgumentException($"Expected length {_n}");

            var a = new Complex[_m];
            for (int k = 0; k < _n; k++)
                a[k] = x[k] * _w[k];

            Radix2Forward(a);
            for (int k = 0; k < _m; k++)
                a[k] *= _bFft[k];
            Radix2Inverse(a);

            for (int k = 0; k < _n; k++)
                x[k] = a[k] * _w[k];
        }

        public void Inverse(Complex[] x)
        {
            for (int i = 0; i < x.Length; i++)
                x[i] = Complex.Conjugate(x[i]);
            Forward(x);
            for (int i = 0; i < x.Length; i++)
                x[i] = Complex.Conjugate(x[i]);
        }

        private static void Radix2Forward(Complex[] x)
        {
            int n = x.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1)
                    j ^= bit;
                j ^= bit;
                if (i < j)
                    (x[i], x[j]) = (x[j], x[i]);
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2.0 * Math.PI / len;
                var wn = new Complex(Math.Cos(ang), Math.Sin(ang));
                int half = len >> 1;
                for (int i = 0; i < n; i += len)
                {
                    var w = Complex.One;
                    for (int k = 0; k < half; k++)
                    {
                        var u = x[i + k];
                        var v = x[i + k + half] * w;
                        x[i + k] = u + v;
                        x[i + k + half] = u - v;
                        w *= wn;
                    }
                }
            }
        }

        private static void Radix2Inverse(Complex[] x)
        {
            int n = x.Length;
            for (int i = 0; i < n; i++)
                x[i] = Complex.Conjugate(x[i]);
            Radix2Forward(x);
            double inv = 1.0 / n;
            for (int i = 0; i < n; i++)
                x[i] = Complex.Conjugate(x[i]) * inv;
        }
    }
}
