using System.Buffers.Binary;
using System.Text;
using FrameShift.Core.Actions;
using FrameShift.SubtitlesWorker;
using Xunit;

namespace FrameShift.Tests;

public sealed class AudioMemoryTests
{
    [Theory]
    [InlineData(1)] [InlineData(64)] [InlineData(8193)] [InlineData(20001)]
    public void WaveformMatchesBucketPeaksWithFixedReads(int count)
    {
        using var stream = new GeneratedPcmStream(count);
        var actual = CutAudioEditingService.ReadWaveform(stream, 32, CancellationToken.None);
        var expected = new double[32];
        var bucketSize = (count + 31) / 32;
        for (var i = 0; i < count; i++)
            expected[i / bucketSize] = Math.Max(expected[i / bucketSize], Math.Abs(GeneratedPcmStream.Sample(i) / 32768d));
        Assert.Equal(expected, actual);
        Assert.InRange(stream.MaximumRead, 1, 16 * 1024);
    }

    [Fact]
    public void WaveformAllocationsDoNotGrowWithRecordingDuration()
    {
        using (var warmup = new GeneratedPcmStream(16000))
            CutAudioEditingService.ReadWaveform(warmup, 860, CancellationToken.None);
        var shortBytes = MeasureWaveformAllocations(16000);
        var hourBytes = MeasureWaveformAllocations(16000 * 3600);
        Assert.InRange(hourBytes, 0, 200_000);
        Assert.InRange(hourBytes - shortBytes, -20_000, 20_000);
    }

    private static long MeasureWaveformAllocations(int count)
    {
        using var stream = new GeneratedPcmStream(count);
        var before = GC.GetAllocatedBytesForCurrentThread();
        CutAudioEditingService.ReadWaveform(stream, 860, CancellationToken.None);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void WaveformHonorsChunkLengthsAndOddPadding()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
        {
            writer.Write("RIFF"u8); writer.Write(0); writer.Write("WAVEfmt "u8); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write("JUNK"u8); writer.Write(3); writer.Write(new byte[] { 1, 2, 3, 0 });
            writer.Write("data"u8); writer.Write(128); writer.Write(new byte[128]);
            writer.Write("JUNK"u8); writer.Write(8); writer.Write(long.MaxValue);
            stream.Position = 4; writer.Write((int)stream.Length - 8);
        }
        stream.Position = 0;
        Assert.All(CutAudioEditingService.ReadWaveform(stream, 32, CancellationToken.None), peak => Assert.Equal(0, peak));
    }

    [Fact]
    public void WaveformCanBeCanceledDuringReading()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new GeneratedPcmStream(16000 * 60, () => cancellation.Cancel());
        Assert.ThrowsAny<OperationCanceledException>(() => CutAudioEditingService.ReadWaveform(stream, 32, cancellation.Token));
        Assert.InRange(stream.DataBytesRead, 1, 16 * 1024);
    }

    [Fact]
    public void PreCanceledWaveformDoesNotOpenTheFile()
    {
        Assert.ThrowsAny<OperationCanceledException>(() => CutAudioEditingService.GetWaveformPointsFromWav(
            "absent.wav", cancellationToken: new CancellationToken(true)));
    }

    [Theory]
    [InlineData(0)] [InlineData(16000 * 23)] [InlineData(16000 * 58)]
    public void SubtitlesWindowsMatchWholeAudioSamplesIncludingOverlapAndRetry(int start)
    {
        using var stream = new GeneratedPcmStream(16000 * 60);
        using var reader = new WaveWindowReader(stream);
        Assert.Equal(60, reader.DurationSeconds);
        var windows = reader.Plan(29000, 6000);
        Assert.Equal(new long[] { 0, 16000 * 23, 16000 * 46 }, windows.Select(w => w.StartSample));
        var length = Math.Min(16000 * 2, (int)(reader.SampleCount - start));
        var actual = reader.Read(start, length, () => false);
        for (var i = 0; i < actual.Length; i++) Assert.Equal(GeneratedPcmStream.Sample(start + i) / 32768f, actual[i]);
        Assert.Equal(actual, reader.Read(start, length, () => false));
        Assert.Equal(GeneratedPcmStream.Sample(0) / 32768f, reader.Read(0, 1, () => false)[0]);
        Assert.InRange(stream.MaximumRead, 1, 8192);
    }

    [Theory]
    [InlineData(1)] [InlineData(3600)]
    public void SubtitlesReadAllocatesOnlyOneWindowAndDoesNotReadTheWholeRecording(int seconds)
    {
        using var stream = new GeneratedPcmStream(16000 * seconds);
        using var reader = new WaveWindowReader(stream);
        Assert.Equal(0, stream.DataBytesRead);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var data = reader.Read(reader.SampleCount - 16000, 16000, () => false);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(16000, data.Length);
        Assert.Equal(32000, stream.DataBytesRead);
        Assert.InRange(bytes, 64000, 150000);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SubtitlesCancelBeforeOrDuringReadAndReaderRemainsReusable(bool during)
    {
        var canceled = !during;
        using var stream = new GeneratedPcmStream(16000 * 60, () => canceled = true);
        using var reader = new WaveWindowReader(stream);
        Assert.ThrowsAny<OperationCanceledException>(() => reader.Read(0, 16000 * 29, () => canceled));
        Assert.InRange(stream.DataBytesRead, 0, during ? 8192 : 0);
        Assert.Single(reader.Read(0, 1, () => false));
    }

    [Theory]
    [InlineData(2, 16000, 1)] [InlineData(1, 8000, 1)] [InlineData(1, 16000, 3)]
    public void SubtitlesRejectsUnexpectedFormatsAndReleasesStream(short channels, int rate, short encoding)
    {
        var stream = new GeneratedPcmStream(16000, channels: channels, rate: rate, encoding: encoding);
        Assert.ThrowsAny<Exception>(() => new WaveWindowReader(stream));
        Assert.True(stream.Disposed);
    }

    // A seekable, virtual PCM recording: even an hour consumes only a 44-byte header.
    private sealed class GeneratedPcmStream : Stream
    {
        private readonly byte[] _header = new byte[44];
        private readonly Action? _onData;
        public int MaximumRead { get; private set; }
        public long DataBytesRead { get; private set; }
        public bool Disposed { get; private set; }
        public GeneratedPcmStream(int sampleCount, Action? onData = null, short channels = 1, int rate = 16000, short encoding = 1)
        {
            _onData = onData;
            Length = 44L + sampleCount * 2L;
            "RIFF"u8.CopyTo(_header); "WAVEfmt "u8.CopyTo(_header.AsSpan(8)); "data"u8.CopyTo(_header.AsSpan(36));
            BinaryPrimitives.WriteUInt32LittleEndian(_header.AsSpan(4), (uint)Length - 8);
            BinaryPrimitives.WriteInt32LittleEndian(_header.AsSpan(16), 16);
            BinaryPrimitives.WriteInt16LittleEndian(_header.AsSpan(20), encoding);
            BinaryPrimitives.WriteInt16LittleEndian(_header.AsSpan(22), channels);
            BinaryPrimitives.WriteInt32LittleEndian(_header.AsSpan(24), rate);
            BinaryPrimitives.WriteInt32LittleEndian(_header.AsSpan(28), rate * 2 * channels);
            BinaryPrimitives.WriteInt16LittleEndian(_header.AsSpan(32), (short)(2 * channels));
            BinaryPrimitives.WriteInt16LittleEndian(_header.AsSpan(34), 16);
            BinaryPrimitives.WriteUInt32LittleEndian(_header.AsSpan(40), (uint)Length - 44);
        }
        public static short Sample(long index) => (short)((index * 7919 % 65536) - 32768);
        public override int Read(byte[] buffer, int offset, int count)
        {
            MaximumRead = Math.Max(MaximumRead, count);
            var read = (int)Math.Min(count, Length - Position);
            var dataRead = 0;
            for (var i = 0; i < read; i++, Position++)
            {
                if (Position < 44) buffer[offset + i] = _header[Position];
                else
                {
                    var sample = (ushort)Sample((Position - 44) / 2);
                    buffer[offset + i] = (byte)((Position - 44) % 2 == 0 ? sample & 255 : sample >> 8);
                    dataRead++;
                }
            }
            DataBytesRead += dataRead;
            if (dataRead > 0) _onData?.Invoke();
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        { SeekOrigin.Begin => offset, SeekOrigin.Current => Position + offset, SeekOrigin.End => Length + offset, _ => throw new ArgumentOutOfRangeException() };
        public override long Length { get; }
        public override long Position { get; set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => !Disposed;
        public override bool CanWrite => false;
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
