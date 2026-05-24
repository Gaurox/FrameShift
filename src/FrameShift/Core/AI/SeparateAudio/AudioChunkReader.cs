using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FrameShift.Core.AI.SeparateAudio;

// Reads any audio file, resamples to 44100 Hz stereo float32, and provides chunk access.
// The entire file is loaded into memory to allow random-access chunking for OLA.
internal sealed class AudioChunkReader : IDisposable
{
    private const int TargetSampleRate = 44100;
    private const int TargetChannels = 2;

    // Interleaved stereo float32: [L0, R0, L1, R1, ...]
    private readonly float[] _samples;

    public long TotalSamplesPerChannel { get; }
    public int SampleRate => TargetSampleRate;
    public int Channels => TargetChannels;

    public AudioChunkReader(string inputPath)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Audio input not found.", inputPath);

        _samples = LoadAndNormalize(inputPath);
        TotalSamplesPerChannel = _samples.Length / TargetChannels;
    }

    // Reads exactly lengthSamplesPerChannel frames starting at startSamplePerChannel.
    // Returns float[2 * lengthSamplesPerChannel] interleaved (L, R, L, R, ...).
    // Zero-pads if reading past the end of the file.
    public float[] ReadChunk(long startSamplePerChannel, int lengthSamplesPerChannel)
    {
        var output = new float[lengthSamplesPerChannel * TargetChannels];
        var srcStart = startSamplePerChannel * TargetChannels;
        var copyFrames = Math.Min(
            lengthSamplesPerChannel,
            Math.Max(0L, TotalSamplesPerChannel - startSamplePerChannel));
        var copyCount = (int)copyFrames * TargetChannels;

        if (copyCount > 0)
            Array.Copy(_samples, srcStart, output, 0, copyCount);

        return output;
    }

    private static float[] LoadAndNormalize(string inputPath)
    {
        using var reader = new AudioFileReader(inputPath);
        ISampleProvider provider = reader;

        // Force stereo
        if (reader.WaveFormat.Channels == 1)
            provider = new MonoToStereoSampleProvider(provider);

        // Resample to 44100 Hz if needed
        if (reader.WaveFormat.SampleRate != TargetSampleRate)
            provider = new WdlResamplingSampleProvider(provider, TargetSampleRate);

        return ReadAllSamples(provider);
    }

    private static float[] ReadAllSamples(ISampleProvider provider)
    {
        var buffer = new float[65536];
        var result = new List<float>(capacity: 4096 * 1024);
        int read;

        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
                result.Add(buffer[i]);
        }

        return result.ToArray();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
