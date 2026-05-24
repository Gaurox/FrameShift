using System;
using System.IO;
using NAudio.Wave;

namespace FrameShift.Core.AI.SeparateAudio;

// Writes a single audio stem as a 44100 Hz stereo PCM_16 WAV file.
// Call WriteSamples() repeatedly as chunks are flushed from the OLA ring buffer,
// then Dispose() to finalize the WAV header.
internal sealed class AudioStemWriter : IDisposable
{
    private static readonly WaveFormat OutputFormat = new(44100, 16, 2);

    private readonly WaveFileWriter _writer;
    private bool _disposed;

    public string OutputPath { get; }

    public AudioStemWriter(string outputPath)
    {
        OutputPath = outputPath;
        _writer = new WaveFileWriter(outputPath, OutputFormat);
    }

    // Writes count interleaved stereo float32 samples (count/2 frames).
    // Samples are clamped to [-1, 1] and converted to PCM_16.
    public void WriteSamples(float[] interleaved, int count)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioStemWriter));

        var byteBuffer = new byte[count * 2];
        for (var i = 0; i < count; i++)
        {
            var s = (short)(Math.Clamp(interleaved[i], -1f, 1f) * 32767f);
            byteBuffer[i * 2] = (byte)(s & 0xFF);
            byteBuffer[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }

        _writer.Write(byteBuffer, 0, byteBuffer.Length);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _writer.Dispose();
        GC.SuppressFinalize(this);
    }

    // Deletes the partial output file if called before normal completion (e.g. on cancellation).
    public void DeletePartialOutput()
    {
        Dispose();
        try
        {
            if (File.Exists(OutputPath))
                File.Delete(OutputPath);
        }
        catch
        {
            // Cleanup failure must never propagate.
        }
    }
}
