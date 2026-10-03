using System;
using System.IO;
using NAudio.Wave;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.AI.SeparateAudio;

// Writes a single audio stem as a 44100 Hz stereo PCM_16 WAV file.
// Call WriteSamples() repeatedly as chunks are flushed from the OLA ring buffer,
// then Dispose() to finalize the WAV header.
internal sealed class AudioStemWriter : IDisposable
{
    private static readonly WaveFormat OutputFormat = new(44100, 16, 2);

    private readonly WaveFileWriter _writer;
    private readonly OutputOperation _publication;
    private bool _disposed;

    public string OutputPath => _publication.PublishedPath ?? _publication.WorkingPath;

    public AudioStemWriter(string outputPath)
    {
        _publication = OutputOperation.ForFile(outputPath);
        try { _writer = new WaveFileWriter(_publication.WorkingPath, OutputFormat); }
        catch { _publication.Dispose(); throw; }
    }

    public string Publish(CancellationToken cancellationToken)
    {
        Dispose(); // Finalize WAV headers before moving any stem.
        return _publication.Publish(cancellationToken);
    }

    public void CleanupWorkspace() => _publication.Dispose();

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
        try { Dispose(); }
        catch { /* Continue cleaning the other stems if WAV finalization failed. */ }
        finally { _publication.Dispose(); }
    }
}
