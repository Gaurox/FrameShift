using NAudio.Wave;

namespace FrameShift.SubtitlesWorker;

// Only the requested window is decoded. Seeking also permits a full CPU retry
// without retaining the recording, and overlap samples are read identically.
internal sealed class WaveWindowReader : IDisposable
{
    private readonly WaveFileReader _reader;
    public const int SampleRate = 16000;
    public long SampleCount => _reader.Length / _reader.WaveFormat.BlockAlign;
    public double DurationSeconds => SampleCount / (double)SampleRate;

    public WaveWindowReader(string path) : this(File.OpenRead(path)) { }

    internal WaveWindowReader(Stream stream)
    {
        try
        {
            _reader = new WaveFileReader(stream);
            if (_reader.WaveFormat.Channels != 1 || _reader.WaveFormat.SampleRate != SampleRate ||
                _reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm || _reader.WaveFormat.BitsPerSample != 16 ||
                _reader.Length % _reader.WaveFormat.BlockAlign != 0)
                throw new InvalidDataException("Expected a mono PCM 16-bit, 16 kHz WAV.");
            if (SampleCount == 0) throw new InvalidDataException("No audio samples found.");
        }
        catch
        {
            _reader?.Dispose();
            stream.Dispose();
            throw;
        }
    }

    public float[] Read(long startSample, int length, Func<bool> isCanceled)
    {
        ThrowIfCanceled(isCanceled);
        if (startSample < 0 || length < 0 || length > SampleRate * 29 || startSample > SampleCount - length)
            throw new ArgumentOutOfRangeException(nameof(length));
        _reader.Position = checked(startSample * _reader.WaveFormat.BlockAlign);
        var provider = _reader.ToSampleProvider();
        var samples = new float[length];
        var offset = 0;
        while (offset < length)
        {
            ThrowIfCanceled(isCanceled);
            var count = provider.Read(samples, offset, Math.Min(4096, length - offset));
            if (count == 0) throw new InvalidDataException("Truncated WAV samples.");
            offset += count;
        }
        ThrowIfCanceled(isCanceled);
        return samples;
    }

    public IReadOnlyList<WaveWindow> Plan(int windowMilliseconds, int overlapMilliseconds)
    {
        var cappedWindow = Math.Clamp(windowMilliseconds, 10_000, 29_000);
        var cappedOverlap = Math.Clamp(overlapMilliseconds, 0, cappedWindow / 3);
        var windowSamples = SampleRate * cappedWindow / 1000;
        var stepSamples = windowSamples - SampleRate * cappedOverlap / 1000;
        var windows = new List<WaveWindow>();
        for (long start = 0; start < SampleCount; start += stepSamples)
        {
            var length = (int)Math.Min(windowSamples, SampleCount - start);
            windows.Add(new WaveWindow(start, length, start / (double)SampleRate));
            if (start + length == SampleCount) break;
        }
        return windows;
    }

    private static void ThrowIfCanceled(Func<bool> isCanceled)
    {
        if (isCanceled()) throw new OperationCanceledException("Cancellation signal detected while reading audio.");
    }

    public void Dispose() => _reader.Dispose();
}

internal sealed record WaveWindow(long StartSample, int LengthSamples, double StartSeconds);
