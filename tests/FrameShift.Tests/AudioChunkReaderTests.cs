using System;
using System.IO;
using System.Threading;
using FrameShift.Core.AI.SeparateAudio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Xunit;

namespace FrameShift.Tests;

public sealed class AudioChunkReaderTests
{
    [Fact]
    public void SequentialWindows_MatchThePreviousRandomAccessSemantics_ForLongAudio()
    {
        var frames = OverlapAddRing.ChunkLen + OverlapAddRing.ChunkHop + 17;
        var source = BuildStereoSamples(frames);
        using var file = TemporaryWaveFile.Create(source);
        using var reader = new AudioChunkReader(file.Path, CancellationToken.None);

        var expectedChunkCount = OverlapAddRing.ComputeChunkCount(frames);
        for (var chunkIndex = 0; chunkIndex < expectedChunkCount; chunkIndex++)
        {
            var start = (long)chunkIndex * OverlapAddRing.ChunkHop;
            var actual = reader.ReadChunk(start, OverlapAddRing.ChunkLen, CancellationToken.None);

            Assert.Equal(OverlapAddRing.ChunkLen * 2, actual.Length);
            Assert.Equal(BuildExpectedChunk(source, start), actual);
            Assert.Equal(chunkIndex == expectedChunkCount - 1, reader.IsLastChunk);
        }

        Assert.Equal(frames, reader.TotalSamplesPerChannel);
    }

    [Fact]
    public void ShortAudio_ProducesOnePaddedFirstAndLastWindow()
    {
        var source = BuildStereoSamples(17);
        using var file = TemporaryWaveFile.Create(source);
        using var reader = new AudioChunkReader(file.Path, CancellationToken.None);

        var actual = reader.ReadChunk(0, OverlapAddRing.ChunkLen, CancellationToken.None);

        Assert.True(reader.IsLastChunk);
        Assert.Equal(17, reader.TotalSamplesPerChannel);
        Assert.Equal(BuildExpectedChunk(source, 0), actual);
        Assert.All(actual.AsSpan(source.Length).ToArray(), sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void ConsecutiveWindows_KeepTheExistingTwentyFivePercentOverlap()
    {
        var frames = OverlapAddRing.ChunkLen + 1;
        var source = BuildStereoSamples(frames);
        using var file = TemporaryWaveFile.Create(source);
        using var reader = new AudioChunkReader(file.Path, CancellationToken.None);

        var first = reader.ReadChunk(0, OverlapAddRing.ChunkLen, CancellationToken.None);
        var second = reader.ReadChunk(OverlapAddRing.ChunkHop, OverlapAddRing.ChunkLen, CancellationToken.None);
        var overlapFrames = OverlapAddRing.ChunkLen - OverlapAddRing.ChunkHop;

        Assert.Same(first, second);
        for (var frame = 0; frame < overlapFrames; frame++)
        {
            var sourceIndex = (OverlapAddRing.ChunkHop + frame) * 2;
            var secondIndex = frame * 2;
            Assert.Equal(source[sourceIndex], second[secondIndex]);
            Assert.Equal(source[sourceIndex + 1], second[secondIndex + 1]);
        }
    }

    [Fact]
    public void Reading_CanBeCanceledAfterTheProviderHasStarted()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new CancelAfterFirstReadProvider(BuildStereoSamples(OverlapAddRing.ChunkLen), cancellation);
        using var reader = new AudioChunkReader(source);

        Assert.Throws<OperationCanceledException>(() =>
            reader.ReadChunk(0, OverlapAddRing.ChunkLen, cancellation.Token));
    }

    private static float[] BuildStereoSamples(int frames)
    {
        var samples = new float[frames * 2];
        for (var frame = 0; frame < frames; frame++)
        {
            samples[frame * 2] = (frame % 997 - 498) / 1000f;
            samples[frame * 2 + 1] = (frame % 541 - 270) / 1000f;
        }

        return samples;
    }

    private static float[] BuildExpectedChunk(float[] source, long startFrame)
    {
        var expected = new float[OverlapAddRing.ChunkLen * 2];
        var sourceOffset = checked((int)(startFrame * 2));
        var sampleCount = Math.Max(0, Math.Min(expected.Length, source.Length - sourceOffset));
        if (sampleCount > 0)
            Array.Copy(source, sourceOffset, expected, 0, sampleCount);

        return expected;
    }

    private sealed class TemporaryWaveFile : IDisposable
    {
        private readonly string _directory;

        private TemporaryWaveFile(string directory, string path)
        {
            _directory = directory;
            Path = path;
        }

        public string Path { get; }

        public static TemporaryWaveFile Create(float[] samples)
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FrameShift.Tests",
                "AudioChunkReader",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "input.wav");

            using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)))
                writer.WriteSamples(samples, 0, samples.Length);

            return new TemporaryWaveFile(directory, path);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class CancelAfterFirstReadProvider : ISampleProvider
    {
        private readonly float[] _samples;
        private readonly CancellationTokenSource _cancellation;
        private int _offset;

        public CancelAfterFirstReadProvider(float[] samples, CancellationTokenSource cancellation)
        {
            _samples = samples;
            _cancellation = cancellation;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = Math.Min(count, _samples.Length - _offset);
            if (read > 0)
            {
                Array.Copy(_samples, _offset, buffer, offset, read);
                _offset += read;
                _cancellation.Cancel();
            }

            return read;
        }
    }
}
