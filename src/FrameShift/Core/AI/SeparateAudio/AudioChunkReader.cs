using System;
using System.IO;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FrameShift.Core.AI.SeparateAudio;

// Reads normalized audio sequentially for the monotonic HTDemucs chunk schedule.
// A single LEN-sized window is reused: only the 25 % overlap is retained between chunks.
internal sealed class AudioChunkReader : IDisposable
{
    private const int TargetSampleRate = 44100;
    private const int TargetChannels = 2;

    private readonly AudioFileReader? _reader;
    private readonly ISampleProvider _provider;
    private readonly float[] _window = new float[OverlapAddRing.ChunkLen * TargetChannels];
    private readonly float[] _readBuffer = new float[65536];
    private readonly float[] _lookahead = new float[TargetChannels];

    private bool _hasReadFirstChunk;
    private bool _completed;
    private long _nextStartSamplePerChannel;

    public long TotalSamplesPerChannel { get; private set; } = -1;
    public long EstimatedTotalSamplesPerChannel { get; }
    public int SampleRate => TargetSampleRate;
    public int Channels => TargetChannels;
    public bool IsLastChunk { get; private set; }

    public int EstimatedChunkCount => OverlapAddRing.ComputeChunkCount(EstimatedTotalSamplesPerChannel);

    public AudioChunkReader(string inputPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Audio input not found.", inputPath);

        _reader = new AudioFileReader(inputPath);
        _provider = CreateNormalizedProvider(_reader);
        EstimatedTotalSamplesPerChannel = EstimateTotalSamples(_reader);
    }

    // Test seam for cancellation and chunk-window behavior without depending on a media decoder.
    internal AudioChunkReader(ISampleProvider provider)
    {
        if (provider.WaveFormat.SampleRate != TargetSampleRate || provider.WaveFormat.Channels != TargetChannels)
            throw new ArgumentException("The test provider must already be stereo 44.1 kHz.", nameof(provider));

        _provider = provider;
        EstimatedTotalSamplesPerChannel = 0;
    }

    // Reads the next HTDemucs window in order. The returned LEN-sized buffer is reused by the
    // next call, so callers must finish consuming it before requesting another chunk.
    public float[] ReadChunk(
        long startSamplePerChannel,
        int lengthSamplesPerChannel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (lengthSamplesPerChannel != OverlapAddRing.ChunkLen)
            throw new ArgumentOutOfRangeException(
                nameof(lengthSamplesPerChannel),
                $"Separate Audio requires {OverlapAddRing.ChunkLen} samples per channel per chunk.");

        if (_completed)
            throw new InvalidOperationException("The audio stream has already reached its final chunk.");

        if (startSamplePerChannel != _nextStartSamplePerChannel)
            throw new InvalidOperationException("Audio chunks must be read sequentially using the Demucs hop size.");

        var framesInWindow = 0;
        if (_hasReadFirstChunk)
        {
            framesInWindow = MoveWindowForward();
        }
        else
        {
            _hasReadFirstChunk = true;
        }

        framesInWindow = FillWindow(framesInWindow, cancellationToken);
        IsLastChunk = framesInWindow < OverlapAddRing.ChunkLen || !TryReadLookahead(cancellationToken);

        if (IsLastChunk)
        {
            if (framesInWindow < OverlapAddRing.ChunkLen)
            {
                Array.Clear(
                    _window,
                    framesInWindow * TargetChannels,
                    (OverlapAddRing.ChunkLen - framesInWindow) * TargetChannels);
            }

            TotalSamplesPerChannel = startSamplePerChannel + framesInWindow;
            _completed = true;
        }

        _nextStartSamplePerChannel += OverlapAddRing.ChunkHop;
        return _window;
    }

    private int MoveWindowForward()
    {
        var overlapFrames = OverlapAddRing.ChunkLen - OverlapAddRing.ChunkHop;
        var overlapSamples = overlapFrames * TargetChannels;
        var hopSamples = OverlapAddRing.ChunkHop * TargetChannels;

        Array.Copy(_window, hopSamples, _window, 0, overlapSamples);
        Array.Copy(_lookahead, 0, _window, overlapSamples, TargetChannels);
        return overlapFrames + 1;
    }

    private int FillWindow(int framesInWindow, CancellationToken cancellationToken)
    {
        while (framesInWindow < OverlapAddRing.ChunkLen)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var samplesNeeded = (OverlapAddRing.ChunkLen - framesInWindow) * TargetChannels;
            var read = _provider.Read(_readBuffer, 0, Math.Min(_readBuffer.Length, samplesNeeded));

            cancellationToken.ThrowIfCancellationRequested();
            if (read <= 0)
                break;

            if (read % TargetChannels != 0)
                throw new InvalidDataException("The normalized audio provider returned an incomplete stereo frame.");

            Array.Copy(_readBuffer, 0, _window, framesInWindow * TargetChannels, read);
            framesInWindow += read / TargetChannels;
        }

        return framesInWindow;
    }

    private bool TryReadLookahead(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = _provider.Read(_lookahead, 0, TargetChannels);
        cancellationToken.ThrowIfCancellationRequested();

        if (read == 0)
            return false;

        if (read != TargetChannels)
            throw new InvalidDataException("The normalized audio provider returned an incomplete stereo frame.");

        return true;
    }

    private static ISampleProvider CreateNormalizedProvider(AudioFileReader reader)
    {
        ISampleProvider provider = reader;

        if (reader.WaveFormat.Channels == 1)
            provider = new MonoToStereoSampleProvider(provider);

        if (reader.WaveFormat.SampleRate != TargetSampleRate)
            provider = new WdlResamplingSampleProvider(provider, TargetSampleRate);

        return provider;
    }

    private static long EstimateTotalSamples(AudioFileReader reader)
    {
        var seconds = reader.TotalTime.TotalSeconds;
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
            return 0;

        return (long)Math.Ceiling(seconds * TargetSampleRate);
    }

    public void Dispose()
    {
        _reader?.Dispose();
        GC.SuppressFinalize(this);
    }
}
