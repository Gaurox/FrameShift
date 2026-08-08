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
    private int _disposed;

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
        progress.Report((15, "Analyzing audio..."));
        var analysis = LoadAnalyzeAndBuildFeatures(inputPath, progress, cancellationToken);
        int sampleCount = analysis.SampleCount;
        int totalFrames = analysis.TotalFrames;
        int[] erbBandWidths = analysis.ErbBandWidths;
        float[][] analyzedRe = analysis.AnalyzedRe;
        float[][] analyzedIm = analysis.AnalyzedIm;

        var fft = new BluesteinFft(FftSize);
        var synthesisState = new OfficialLibDfState(FftSize, HopSize, fft);

        progress.Report((45, "Running AI inference..."));

        var emptyHidden = new Dictionary<string, DenseTensor<float>>(StringComparer.Ordinal);
        var encInputs = BuildInputs(_encSession!, emptyHidden,
            ("feat_erb", analysis.ErbFeatures, new[] { 1, 1, totalFrames, NbErb }),
            ("feat_spec", analysis.SpecFeatures, new[] { 1, 2, totalFrames, NbDf }));

        float[] rawErbGainsAll;
        int[] erbGainsShape;
        float[] rawDfCoeffsAll;
        int[] dfCoeffsShape;

        cancellationToken.ThrowIfCancellationRequested();
        using (var encoderResults = _encSession!.Run(encInputs))
        {
            var erbDecInputs = BuildInputsFromResults(_erbDecSession!, emptyHidden, encoderResults);
            cancellationToken.ThrowIfCancellationRequested();
            using (var erbResults = _erbDecSession!.Run(erbDecInputs))
            {
                (rawErbGainsAll, erbGainsShape) = GetFirstDataOutput(erbResults);
            }

            var dfDecInputs = BuildInputsFromResults(_dfDecSession!, emptyHidden, encoderResults);
            cancellationToken.ThrowIfCancellationRequested();
            using (var dfResults = _dfDecSession!.Run(dfDecInputs))
            {
                (rawDfCoeffsAll, dfCoeffsShape) = GetDataOutput(dfResults, "coefs");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

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
        var outputRe = new float[SpecBins];
        var outputIm = new float[SpecBins];
        var synthChunk = new float[HopSize];
        var clean = new float[sampleCount];
        int delay = FftSize - HopSize;

        for (int frameIdx = 0; frameIdx < totalFrames; frameIdx++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            float[] analyzedFrameRe = analyzedRe[frameIdx];
            float[] analyzedFrameIm = analyzedIm[frameIdx];
            Array.Copy(analyzedFrameRe, outputRe, SpecBins);
            Array.Copy(analyzedFrameIm, outputIm, SpecBins);

            var erbGains = GetFirstOuterFrameSlice(rawErbGainsAll, erbGainsShape, 2, frameIdx);
            var rawDfCoeffs = GetFirstOuterFrameSlice(rawDfCoeffsAll, dfCoeffsShape, 1, frameIdx);
            ApplyOfficialErbBandGains(outputRe, outputIm, erbGains, erbBandWidths, minGain);

            ApplyOfficialMfDfFullSequence(
                analyzedRe,
                analyzedIm,
                rawDfCoeffs,
                dfOrder,
                Lookahead,
                frameIdx,
                NbDf,
                outputRe,
                outputIm);

            // apply attenuation floor to DF bins (blend with original)
            if (minGain > 0f)
            {
                for (int k = 0; k < NbDf; k++)
                {
                    outputRe[k] += minGain * (analyzedFrameRe[k] - outputRe[k]);
                    outputIm[k] += minGain * (analyzedFrameIm[k] - outputIm[k]);
                }
            }

            synthesisState.SynthesisFrame(outputRe, outputIm, synthChunk);
            CopySynthesisChunkToClean(synthChunk, clean, frameIdx, delay);
        }

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
            cancellationToken.ThrowIfCancellationRequested();
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount)
            };

            _encSession = new InferenceSession(DeepFilterNetModelLocator.EncPath, options);
            cancellationToken.ThrowIfCancellationRequested();
            _erbDecSession = new InferenceSession(DeepFilterNetModelLocator.ErbDecPath, options);
            cancellationToken.ThrowIfCancellationRequested();
            _dfDecSession = new InferenceSession(DeepFilterNetModelLocator.DfDecPath, options);
            _ready = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private static RemoveNoiseAnalysis LoadAnalyzeAndBuildFeatures(
        string path,
        IProgress<(int percent, string status)> progress,
        CancellationToken cancellationToken)
    {
        var loaded = LoadMonoFloat48k(path, cancellationToken);
        int totalFrames = checked((loaded.Count + FftSize + HopSize - 1) / HopSize);
        int[] erbBandWidths = BuildOfficialErbBandWidths(Sr, FftSize, NbErb, MinNbErbFreqs);
        var analyzedRe = new float[totalFrames][];
        var analyzedIm = new float[totalFrames][];
        var fft = new BluesteinFft(FftSize);
        var analysisState = new OfficialLibDfState(FftSize, HopSize, fft);
        var analysisChunk = new float[HopSize];

        for (int frameIdx = 0; frameIdx < totalFrames; frameIdx++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            analyzedRe[frameIdx] = new float[SpecBins];
            analyzedIm[frameIdx] = new float[SpecBins];

            int chunkOffset = frameIdx * HopSize;
            Array.Clear(analysisChunk, 0, analysisChunk.Length);
            int copyLength = Math.Min(HopSize, loaded.Count - chunkOffset);
            if (copyLength > 0)
                Array.Copy(loaded.Samples, chunkOffset, analysisChunk, 0, copyLength);

            analysisState.AnalysisFrame(analysisChunk, analyzedRe[frameIdx], analyzedIm[frameIdx]);
        }

        progress.Report((30, "Building model features..."));
        var erbFeatures = new float[checked(totalFrames * NbErb)];
        var specFeatures = new float[checked(2 * totalFrames * NbDf)];
        var erbFeatureFrame = new float[NbErb];
        var specReFeatureFrame = new float[NbDf];
        var specImFeatureFrame = new float[NbDf];
        float normAlpha = MathF.Exp(-(float)HopSize / Sr / NormTau);
        float oneMinusAlpha = 1f - normAlpha;
        float[] erbNormState = MakeLinearRamp(MeanNormInitMin, MeanNormInitMax, NbErb);
        float[] specNormState = MakeLinearRamp(UnitNormInitMin, UnitNormInitMax, NbDf);

        for (int frameIdx = 0; frameIdx < totalFrames; frameIdx++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            float[] specRe = analyzedRe[frameIdx];
            float[] specIm = analyzedIm[frameIdx];

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
                erbFeatureFrame[b] = (xDb - erbNormState[b]) / MeanNormDenom;
                erbBinOffset += bandWidth;
            }

            for (int k = 0; k < NbDf; k++)
            {
                float mag = MathF.Sqrt(specRe[k] * specRe[k] + specIm[k] * specIm[k]);
                specNormState[k] = normAlpha * specNormState[k] + oneMinusAlpha * mag;
                float inv = 1f / MathF.Sqrt(MathF.Max(specNormState[k], 1e-10f));
                specReFeatureFrame[k] = specRe[k] * inv;
                specImFeatureFrame[k] = specIm[k] * inv;
            }

            StoreLookaheadShiftedFrame(erbFeatures, 1, totalFrames, NbErb, Lookahead, frameIdx, 0, erbFeatureFrame);
            StoreLookaheadShiftedFrame(specFeatures, 2, totalFrames, NbDf, Lookahead, frameIdx, 0, specReFeatureFrame);
            StoreLookaheadShiftedFrame(specFeatures, 2, totalFrames, NbDf, Lookahead, frameIdx, 1, specImFeatureFrame);
        }

        return new RemoveNoiseAnalysis(loaded.Count, totalFrames, erbBandWidths, analyzedRe, analyzedIm, erbFeatures, specFeatures);
    }

    private static LoadedMonoAudio LoadMonoFloat48k(string path, CancellationToken cancellationToken)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider source = reader;
        if (reader.WaveFormat.SampleRate != Sr)
            source = new WdlResamplingSampleProvider(source, Sr);
        if (source.WaveFormat.Channels > 1)
            source = source.ToMono();

        int initialCapacity = GetInitialSampleCapacity(reader.TotalTime);
        var samples = new float[initialCapacity];
        int sampleCount = 0;
        var buffer = new float[8192];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSampleCapacity(ref samples, checked(sampleCount + read));
            buffer.AsSpan(0, read).CopyTo(samples.AsSpan(sampleCount, read));
            sampleCount += read;
        }

        return new LoadedMonoAudio(samples, sampleCount);
    }

    private static int GetInitialSampleCapacity(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return 8192;

        double estimatedSampleCount = Math.Ceiling(duration.TotalSeconds * Sr);
        return estimatedSampleCount is > 0 and <= int.MaxValue
            ? Math.Max(8192, (int)estimatedSampleCount)
            : 8192;
    }

    private static void EnsureSampleCapacity(ref float[] samples, int requiredCount)
    {
        if (requiredCount <= samples.Length)
            return;

        int newCapacity = checked(Math.Max(requiredCount, samples.Length + samples.Length / 2));
        Array.Resize(ref samples, newCapacity);
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

    internal static void StoreLookaheadShiftedFrame(
        float[] destination,
        int channels,
        int frames,
        int bins,
        int lookahead,
        int sourceFrame,
        int channel,
        ReadOnlySpan<float> values)
    {
        if ((uint)channel >= (uint)channels || values.Length < bins)
            throw new ArgumentOutOfRangeException(nameof(channel));

        int destinationFrame = sourceFrame - lookahead;
        if ((uint)destinationFrame >= (uint)frames)
            return;

        values[..bins].CopyTo(destination.AsSpan((channel * frames + destinationFrame) * bins, bins));
    }

    internal static void CopySynthesisChunkToClean(float[] synthChunk, float[] clean, int frameIndex, int delay)
    {
        int outputOffset = checked(frameIndex * synthChunk.Length - delay);
        int sourceOffset = Math.Max(0, -outputOffset);
        int destinationOffset = Math.Max(0, outputOffset);
        int copyLength = Math.Min(synthChunk.Length - sourceOffset, clean.Length - destinationOffset);
        if (copyLength > 0)
            Array.Copy(synthChunk, sourceOffset, clean, destinationOffset, copyLength);
    }

    private static ReadOnlySpan<float> GetFirstOuterFrameSlice(float[] data, int[] shape, int timeDim, int frameIndex)
    {
        int inner = 1;
        for (int i = timeDim + 1; i < shape.Length; i++)
            inner *= shape[i];

        return data.AsSpan(checked(frameIndex * inner), inner);
    }

    private static void ApplyOfficialErbBandGains(float[] specRe, float[] specIm, ReadOnlySpan<float> erbGains, int[] erbBandWidths, float minGain = 0f)
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
        ReadOnlySpan<float> rawDfCoeffsFrame,
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
                // The feature arrays already have the ONNX row-major layout. Keep this
                // tensor as a view over them rather than allocating and copying a second
                // full-sequence feature buffer.
                var tensor = new DenseTensor<float>(match.data.AsMemory(), match.shape);
                list.Add(NamedOnnxValue.CreateFromTensor(inputName, tensor));
            }
        }

        return list.ToArray();
    }

    private static NamedOnnxValue[] BuildInputsFromResults(
        InferenceSession session,
        Dictionary<string, DenseTensor<float>> hidden,
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> available)
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

            DisposableNamedOnnxValue? match = available.FirstOrDefault(result =>
                !IsHidden(result.Name) && string.Equals(result.Name, inputName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new InvalidOperationException(
                    $"Required input '{inputName}' not found in encoder outputs. Have: [{string.Join(", ", available.Select(result => result.Name))}]");
            }

            // The encoder result remains alive while both decoders run. Passing its tensor
            // directly avoids materializing and copying every full-sequence encoder output.
            list.Add(NamedOnnxValue.CreateFromTensor(inputName, match.AsTensor<float>()));
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

    private static (float[] data, int[] shape) GetDataOutput(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        string outputName)
    {
        var result = results.FirstOrDefault(value =>
            !IsHidden(value.Name) && string.Equals(value.Name, outputName, StringComparison.OrdinalIgnoreCase));
        if (result is null)
        {
            throw new InvalidOperationException(
                $"df_dec output '{outputName}' not found. Have: [{string.Join(", ", results.Select(value => value.Name))}]");
        }

        var tensor = result.AsTensor<float>();
        return (tensor.ToArray(), tensor.Dimensions.ToArray());
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

    private readonly record struct LoadedMonoAudio(float[] Samples, int Count);

    private sealed record RemoveNoiseAnalysis(
        int SampleCount,
        int TotalFrames,
        int[] ErbBandWidths,
        float[][] AnalyzedRe,
        float[][] AnalyzedIm,
        float[] ErbFeatures,
        float[] SpecFeatures);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _encSession?.Dispose();
        _erbDecSession?.Dispose();
        _dfDecSession?.Dispose();
        _encSession = null;
        _erbDecSession = null;
        _dfDecSession = null;
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
