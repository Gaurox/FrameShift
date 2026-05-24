using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        using var reader = new AudioChunkReader(inputPath);
        var chunkTotal = OverlapAddRing.ComputeChunkCount(reader.TotalSamplesPerChannel);
        using var writers = OutputWriters.Create(inputPath, stems);
        var ring = new OverlapAddRing(StemCount);

        AppLogger.LogStatic(
            $"AudioSeparationEngine: starting. provider={Provider}, split={_usesSplitPipeline}, chunks={chunkTotal}, samples={reader.TotalSamplesPerChannel}");

        try
        {
            for (var chunkIndex = 0; chunkIndex < chunkTotal; chunkIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var startSample = (long)chunkIndex * OverlapAddRing.ChunkHop;
                var mixChunk = reader.ReadChunk(startSample, OverlapAddRing.ChunkLen);
                var stemChunk = RunChunk(mixChunk, cancellationToken);

                ring.Accumulate(chunkIndex, stemChunk);

                var isLastChunk = chunkIndex == chunkTotal - 1;
                var drained = ring.Drain(isLastChunk, reader.TotalSamplesPerChannel);
                WriteOutputs(drained, stems, writers);

                var percent = 10 + ((chunkIndex + 1) * 89 / chunkTotal);
                var status = $"Processing chunk {chunkIndex + 1}/{chunkTotal} ({Provider})";
                progress.Report(new AudioSeparationProgress(percent, chunkIndex + 1, chunkTotal, status));
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
                    AppLogger.LogStatic($"AudioSeparationEngine: DirectML init failed, falling back to CPU split model. {ex}");
                    _session = CreateCpuSession(ModelLocator.GpuModelPath);
                    _usesSplitPipeline = true;
                    _hostSpectro = new HostSpectro();
                    Provider = "CPU (split)";
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
        for (var sample = 0; sample < OverlapAddRing.ChunkLen; sample++)
        {
            tensor[0, 0, sample] = mixChunk[sample * 2];
            tensor[0, 1, sample] = mixChunk[sample * 2 + 1];
        }

        return NamedOnnxValue.CreateFromTensor(_mixInputName, tensor);
    }

    private NamedOnnxValue BuildSpecInput(float[] spec)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 2, HostSpectro.FKeep, HostSpectro.Le, 2 });
        var index = 0;
        for (var channel = 0; channel < 2; channel++)
        {
            for (var freq = 0; freq < HostSpectro.FKeep; freq++)
            {
                for (var time = 0; time < HostSpectro.Le; time++)
                {
                    tensor[0, channel, freq, time, 0] = spec[index++];
                    tensor[0, channel, freq, time, 1] = spec[index++];
                }
            }
        }

        return NamedOnnxValue.CreateFromTensor(_specInputName, tensor);
    }

    private static float[] ExtractStems(Tensor<float> tensor)
    {
        var stemData = new float[StemCount * 2 * OverlapAddRing.ChunkLen];
        for (var stem = 0; stem < StemCount; stem++)
        {
            var stemBase = stem * 2 * OverlapAddRing.ChunkLen;
            for (var sample = 0; sample < OverlapAddRing.ChunkLen; sample++)
            {
                stemData[stemBase + sample] = tensor[0, stem, 0, sample];
                stemData[stemBase + OverlapAddRing.ChunkLen + sample] = tensor[0, stem, 1, sample];
            }
        }

        return stemData;
    }

    private static float[] ExtractMaskSpec(Tensor<float> tensor)
    {
        var result = new float[StemCount * 2 * HostSpectro.FKeep * HostSpectro.Le * 2];
        var index = 0;
        for (var stem = 0; stem < StemCount; stem++)
        {
            for (var channel = 0; channel < 2; channel++)
            {
                for (var freq = 0; freq < HostSpectro.FKeep; freq++)
                {
                    for (var time = 0; time < HostSpectro.Le; time++)
                    {
                        result[index++] = tensor[0, stem, channel, freq, time, 0];
                        result[index++] = tensor[0, stem, channel, freq, time, 1];
                    }
                }
            }
        }

        return result;
    }

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
