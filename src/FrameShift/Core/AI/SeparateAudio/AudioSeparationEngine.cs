using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.AI;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FrameShift.Core.AI.SeparateAudio;

internal sealed class AudioSeparationEngine : IDisposable
{
    private const int StemCount = 4;
    private const int DrumsStemIndex = 0;
    private const int BassStemIndex = 1;
    private const int OtherStemIndex = 2;
    private const int VocalsStemIndex = 3;

    private readonly SemaphoreSlim _initGate = new(1, 1);

    private InferenceSession? _session;
    private HostSpectro? _hostSpectro;
    private bool _sessionReady;
    private bool _usesSplitPipeline;
    private string _mixInputName = "mix";
    private string _specInputName = "spec";
    private string _stemsOutputName = "stems";
    private string _maskSpecOutputName = "mask_spec";
    private string _stemsTimeOutputName = "stems_time";

    public string Provider { get; private set; } = "—";

    public async Task SeparateAsync(
        string inputPath,
        StemSelection stems,
        bool preferGpu,
        IProgress<AudioSeparationProgress> progress,
        CancellationToken cancellationToken)
    {
        await Task.Run(
            () => SeparateCore(inputPath, stems, preferGpu, progress, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private void SeparateCore(
        string inputPath,
        StemSelection stems,
        bool preferGpu,
        IProgress<AudioSeparationProgress> progress,
        CancellationToken cancellationToken)
    {
        ValidateInput(inputPath);

        progress.Report(new AudioSeparationProgress(1, 0, 0, "Loading model..."));
        EnsureSession(preferGpu, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new AudioSeparationProgress(5, 0, 0, "Loading audio..."));

        using var reader = new AudioChunkReader(inputPath, cancellationToken);
        var estimatedChunkTotal = reader.EstimatedChunkCount;
        using var writers = OutputWriters.Create(inputPath, stems);
        var ring = new OverlapAddRing(StemCount);

        AppLogger.LogStatic(
            $"AudioSeparationEngine: starting. provider={Provider}, split={_usesSplitPipeline}, chunks~={estimatedChunkTotal}");

        try
        {
            var chunkTotal = 0;
            for (var chunkIndex = 0; ; chunkIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var startSample = (long)chunkIndex * OverlapAddRing.ChunkHop;
                var mixChunk = reader.ReadChunk(startSample, OverlapAddRing.ChunkLen, cancellationToken);
                var stemChunk = RunChunk(mixChunk, cancellationToken);

                ring.Accumulate(chunkIndex, stemChunk);

                var isLastChunk = reader.IsLastChunk;
                var drained = ring.Drain(isLastChunk, isLastChunk ? reader.TotalSamplesPerChannel : 0);
                WriteOutputs(drained, stems, writers);

                var completedChunks = chunkIndex + 1;
                var displayChunkTotal = isLastChunk
                    ? completedChunks
                    : Math.Max(estimatedChunkTotal, completedChunks + 1);
                var percent = isLastChunk
                    ? 99
                    : 10 + Math.Min(89, completedChunks * 89 / displayChunkTotal);
                var status = $"Processing chunk {completedChunks}/{displayChunkTotal} ({Provider})";
                progress.Report(new AudioSeparationProgress(percent, completedChunks, displayChunkTotal, status));

                if (isLastChunk)
                {
                    chunkTotal = completedChunks;
                    break;
                }
            }

            progress.Report(new AudioSeparationProgress(100, chunkTotal, chunkTotal, "Done."));
            writers.MarkCompleted();
        }
        catch
        {
            writers.DeletePartialOutputs();
            throw;
        }
    }

    private void EnsureSession(bool preferGpu, CancellationToken cancellationToken)
    {
        if (_sessionReady)
            return;

        _initGate.Wait(cancellationToken);
        try
        {
            if (_sessionReady)
                return;

            if (preferGpu && ModelLocator.GpuModelExists())
            {
                try
                {
                    _session = CreateDirectMlSession(ModelLocator.GpuModelPath);
                    _usesSplitPipeline = true;
                    _hostSpectro = new HostSpectro();
                    Provider = "DirectML";
                }
                catch (Exception ex)
                {
                    AppLogger.LogStatic($"AudioSeparationEngine: DirectML init failed. {ex.Message}");
                    // Prefer V1 CPU model (STFT in-graph) over CPU-split; the split model
                    // on CPU EP is slower than V1 CPU due to host iSTFT overhead.
                    if (ModelLocator.CpuModelExists())
                    {
                        AppLogger.LogStatic("AudioSeparationEngine: falling back to V1 CPU model.");
                        _session = CreateCpuSession(ModelLocator.CpuModelPath);
                        _usesSplitPipeline = false;
                        Provider = "CPU";
                    }
                    else
                    {
                        AppLogger.LogStatic("AudioSeparationEngine: V1 CPU model not found, using split model on CPU (suboptimal).");
                        _session = CreateCpuSession(ModelLocator.GpuModelPath);
                        _usesSplitPipeline = true;
                        _hostSpectro = new HostSpectro();
                        Provider = "CPU (split)";
                    }
                }
            }
            else if (ModelLocator.CpuModelExists())
            {
                _session = CreateCpuSession(ModelLocator.CpuModelPath);
                _usesSplitPipeline = false;
                Provider = "CPU";
            }
            else if (ModelLocator.GpuModelExists())
            {
                // Only the split model is available; run it on CPU as last resort.
                AppLogger.LogStatic("AudioSeparationEngine: only split model found, using CPU EP (suboptimal).");
                _session = CreateCpuSession(ModelLocator.GpuModelPath);
                _usesSplitPipeline = true;
                _hostSpectro = new HostSpectro();
                Provider = "CPU (split)";
            }
            else
            {
                throw new FileNotFoundException("No audio separation model is available.");
            }

            ConfigureMetadata();
            _sessionReady = true;

            AppLogger.LogStatic(
                $"AudioSeparationEngine: session ready. provider={Provider}, split={_usesSplitPipeline}, inputs={string.Join(", ", _session.InputMetadata.Keys)}, outputs={string.Join(", ", _session.OutputMetadata.Keys)}");
        }
        finally
        {
            _initGate.Release();
        }
    }

    private void ConfigureMetadata()
    {
        if (_session is null)
            throw new InvalidOperationException("ONNX session not initialized.");

        var inputNames = _session.InputMetadata.Keys.ToList();
        _mixInputName = inputNames.FirstOrDefault(k => k.Contains("mix", StringComparison.OrdinalIgnoreCase))
            ?? inputNames.First();

        if (_usesSplitPipeline)
        {
            _specInputName = inputNames.FirstOrDefault(k => k.Contains("spec", StringComparison.OrdinalIgnoreCase))
                ?? inputNames.Last();
        }

        var outputNames = _session.OutputMetadata.Keys.ToList();
        if (_usesSplitPipeline)
        {
            _maskSpecOutputName = outputNames.FirstOrDefault(k => k.Contains("mask", StringComparison.OrdinalIgnoreCase))
                ?? outputNames.First();
            _stemsTimeOutputName = outputNames.FirstOrDefault(k => k.Contains("time", StringComparison.OrdinalIgnoreCase))
                ?? outputNames.Last();
        }
        else
        {
            _stemsOutputName = outputNames.FirstOrDefault(k => k.Contains("stem", StringComparison.OrdinalIgnoreCase))
                ?? outputNames.First();
        }
    }

    private float[] RunChunk(float[] mixChunk, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _usesSplitPipeline
            ? RunSplitChunk(mixChunk)
            : RunCpuChunk(mixChunk);
    }

    private float[] RunCpuChunk(float[] mixChunk)
    {
        using var results = _session!.Run(new[] { BuildMixInput(mixChunk) });
        var tensor = results.First(v => string.Equals(v.Name, _stemsOutputName, StringComparison.OrdinalIgnoreCase))
            .AsTensor<float>();

        return ExtractStems(tensor);
    }

    private float[] RunSplitChunk(float[] mixChunk)
    {
        var spec = _hostSpectro!.Stft(mixChunk);
        using var results = _session!.Run(new[]
        {
            BuildMixInput(mixChunk),
            BuildSpecInput(spec)
        });

        var stemsTimeTensor = results.First(v => string.Equals(v.Name, _stemsTimeOutputName, StringComparison.OrdinalIgnoreCase))
            .AsTensor<float>();
        var maskSpecTensor = results.First(v => string.Equals(v.Name, _maskSpecOutputName, StringComparison.OrdinalIgnoreCase))
            .AsTensor<float>();

        var stemsTime = ExtractStems(stemsTimeTensor);
        var maskSpec = ExtractMaskSpec(maskSpecTensor);
        var xFreq = _hostSpectro.Istft(maskSpec);

        for (var i = 0; i < stemsTime.Length; i++)
            stemsTime[i] += xFreq[i];

        return stemsTime;
    }

    private NamedOnnxValue BuildMixInput(float[] mixChunk)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 2, OverlapAddRing.ChunkLen });
        // DenseTensor buffer is contiguous row-major: [ch0[ChunkLen], ch1[ChunkLen]].
        // Deinterleave straight into the backing span — bypasses the slow multi-dim indexer.
        var span = tensor.Buffer.Span;
        var len = OverlapAddRing.ChunkLen;
        for (var sample = 0; sample < len; sample++)
        {
            span[sample] = mixChunk[sample * 2];
            span[len + sample] = mixChunk[sample * 2 + 1];
        }

        return NamedOnnxValue.CreateFromTensor(_mixInputName, tensor);
    }

    private NamedOnnxValue BuildSpecInput(float[] spec)
    {
        // spec is already laid out [channel, freq, time, complex] row-major, which is
        // exactly the [1, 2, FKeep, Le, 2] tensor buffer order → a direct copy.
        var tensor = new DenseTensor<float>(new[] { 1, 2, HostSpectro.FKeep, HostSpectro.Le, 2 });
        spec.AsSpan(0, tensor.Buffer.Length).CopyTo(tensor.Buffer.Span);
        return NamedOnnxValue.CreateFromTensor(_specInputName, tensor);
    }

    private static float[] ExtractStems(Tensor<float> tensor)
    {
        // Tensor [1, 4, 2, ChunkLen] row-major == target layout [stem, channel, sample] → memcpy.
        var stemData = new float[StemCount * 2 * OverlapAddRing.ChunkLen];
        AsSpan(tensor).CopyTo(stemData);
        return stemData;
    }

    private static float[] ExtractMaskSpec(Tensor<float> tensor)
    {
        // Tensor [1, 4, 2, FKeep, Le, 2] row-major == target layout [stem, channel, freq, time, complex] → memcpy.
        var result = new float[StemCount * 2 * HostSpectro.FKeep * HostSpectro.Le * 2];
        AsSpan(tensor).CopyTo(result);
        return result;
    }

    // ONNX Runtime returns DenseTensor<float> for float outputs; use its contiguous buffer
    // directly. Falls back to ToArray() only if a non-dense tensor is ever encountered.
    private static ReadOnlySpan<float> AsSpan(Tensor<float> tensor)
        => tensor is DenseTensor<float> dense ? dense.Buffer.Span : tensor.ToArray();

    private static void WriteOutputs(
        OverlapAddRing.DrainedChunk drained,
        StemSelection stems,
        OutputWriters writers)
    {
        if (drained.FramesPerChannel <= 0)
            return;

        if (stems.Drums && writers.Drums is not null)
            WriteStem(drained, DrumsStemIndex, writers.Drums);

        if (stems.Bass && writers.Bass is not null)
            WriteStem(drained, BassStemIndex, writers.Bass);

        if (stems.Other && writers.Other is not null)
            WriteStem(drained, OtherStemIndex, writers.Other);

        if (stems.Vocals && writers.Vocals is not null)
            WriteStem(drained, VocalsStemIndex, writers.Vocals);

        if (stems.Instrumental && writers.Instrumental is not null)
            WriteInstrumental(drained, writers.Instrumental);
    }

    private static void WriteStem(OverlapAddRing.DrainedChunk drained, int stemIndex, AudioStemWriter writer)
    {
        var interleaved = new float[drained.FramesPerChannel * 2];
        var baseIndex = stemIndex * 2 * drained.FramesPerChannel;
        for (var frame = 0; frame < drained.FramesPerChannel; frame++)
        {
            interleaved[frame * 2] = drained.StemData[baseIndex + frame];
            interleaved[frame * 2 + 1] = drained.StemData[baseIndex + drained.FramesPerChannel + frame];
        }

        writer.WriteSamples(interleaved, interleaved.Length);
    }

    private static void WriteInstrumental(OverlapAddRing.DrainedChunk drained, AudioStemWriter writer)
    {
        var interleaved = new float[drained.FramesPerChannel * 2];
        var drumsBase = DrumsStemIndex * 2 * drained.FramesPerChannel;
        var bassBase = BassStemIndex * 2 * drained.FramesPerChannel;
        var otherBase = OtherStemIndex * 2 * drained.FramesPerChannel;

        for (var frame = 0; frame < drained.FramesPerChannel; frame++)
        {
            interleaved[frame * 2] =
                drained.StemData[drumsBase + frame] +
                drained.StemData[bassBase + frame] +
                drained.StemData[otherBase + frame];

            interleaved[frame * 2 + 1] =
                drained.StemData[drumsBase + drained.FramesPerChannel + frame] +
                drained.StemData[bassBase + drained.FramesPerChannel + frame] +
                drained.StemData[otherBase + drained.FramesPerChannel + frame];
        }

        writer.WriteSamples(interleaved, interleaved.Length);
    }

    private static InferenceSession CreateCpuSession(string modelPath)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount)
        };

        return new InferenceSession(modelPath, options);
    }

    private static InferenceSession CreateDirectMlSession(string modelPath)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        options.AppendExecutionProvider_DML();
        return new InferenceSession(modelPath, options);
    }

    private static void ValidateInput(string inputPath)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Input audio not found.", inputPath);
    }

    private sealed class OutputWriters : IDisposable
    {
        private bool _completed;

        private OutputWriters(
            AudioStemWriter? drums,
            AudioStemWriter? bass,
            AudioStemWriter? other,
            AudioStemWriter? vocals,
            AudioStemWriter? instrumental)
        {
            Drums = drums;
            Bass = bass;
            Other = other;
            Vocals = vocals;
            Instrumental = instrumental;
        }

        public AudioStemWriter? Drums { get; }
        public AudioStemWriter? Bass { get; }
        public AudioStemWriter? Other { get; }
        public AudioStemWriter? Vocals { get; }
        public AudioStemWriter? Instrumental { get; }

        public static OutputWriters Create(string inputPath, StemSelection stems)
        {
            var created = new List<AudioStemWriter>();

            try
            {
                AudioStemWriter? CreateWriter(bool enabled, string suffix)
                {
                    if (!enabled)
                        return null;

                    var path = OutputPathHelper.CreateUniqueOutputPath(inputPath, suffix, ".wav");
                    var writer = new AudioStemWriter(path);
                    created.Add(writer);
                    return writer;
                }

                return new OutputWriters(
                    CreateWriter(stems.Drums, "_drums"),
                    CreateWriter(stems.Bass, "_bass"),
                    CreateWriter(stems.Other, "_other"),
                    CreateWriter(stems.Vocals, "_vocals"),
                    CreateWriter(stems.Instrumental, "_instrumental"));
            }
            catch
            {
                foreach (var writer in created)
                    writer.DeletePartialOutput();
                throw;
            }
        }

        public void MarkCompleted()
        {
            _completed = true;
        }

        public void DeletePartialOutputs()
        {
            foreach (var writer in Enumerate())
                writer.DeletePartialOutput();
        }

        public void Dispose()
        {
            if (_completed)
            {
                foreach (var writer in Enumerate())
                    writer.Dispose();
            }
            else
            {
                DeletePartialOutputs();
            }
        }

        private IEnumerable<AudioStemWriter> Enumerate()
        {
            if (Drums is not null) yield return Drums;
            if (Bass is not null) yield return Bass;
            if (Other is not null) yield return Other;
            if (Vocals is not null) yield return Vocals;
            if (Instrumental is not null) yield return Instrumental;
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _initGate.Dispose();
    }
}
