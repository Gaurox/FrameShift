using System.Buffers.Binary;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using Xunit;

namespace FrameShift.Tests;

public sealed class BundledFfmpegSecurityTests
{
    [Fact]
    public async Task RealFfmpegReportsThePinnedSourceAndRequiredLibraries()
    {
        var runner = new FfmpegRunner(new AppLogger());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var version = await runner.RunCaptureAsync(new ToolLocator().ResolveFfmpegPath(), ["-version"], timeout.Token);
        Assert.True(version.Success, version.StandardError);
        Assert.Contains("31ba826726", version.StandardOutput, StringComparison.OrdinalIgnoreCase);
        foreach (var option in new[] { "--enable-gpl", "--enable-version3", "--enable-libx264", "--enable-libx265",
                     "--enable-libvpx", "--enable-libopus", "--enable-libmp3lame", "--enable-libwebp", "--enable-libass" })
            Assert.Contains(option, version.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealFfprobeHandlesDeepMp4EncryptionMetadataWithoutStackExhaustion()
    {
        var root = Path.Combine(Path.GetTempPath(), "FrameShift MP4 sécurité 中文 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var logger = new AppLogger();
            var tools = new ToolLocator();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var original = Path.Combine(root, "original.mp4");
            var created = await new FfmpegRunner(logger).RunCaptureAsync(tools.ResolveFfmpegPath(),
                ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=blue:s=16x16:r=1:d=1",
                 "-frames:v", "1", "-c:v", "libx264", original], timeout.Token);
            Assert.True(created.Success, created.StandardError);

            // The recursive free fixed by CVE-2026-107678 consumes one stack
            // frame per PSSH entry. Keep the file small and the process bounded.
            const int entryCount = 20_000;
            const int entrySize = 33;
            var input = Path.Combine(root, "été metadata русский.mp4");
            var bytes = File.ReadAllBytes(original);
            var modified = false;
            using (var output = new FileStream(input, FileMode.CreateNew, FileAccess.Write))
            {
                for (var offset = 0; offset < bytes.Length;)
                {
                    var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
                    Assert.InRange(size, 8, bytes.Length - offset);
                    if (bytes.AsSpan(offset + 4, 4).SequenceEqual("moov"u8))
                    {
                        var sizeBytes = new byte[4];
                        BinaryPrimitives.WriteUInt32BigEndian(sizeBytes, checked((uint)(size + entryCount * entrySize)));
                        output.Write(sizeBytes);
                        output.Write(bytes, offset + 4, size - 4);
                        var entry = new byte[entrySize];
                        BinaryPrimitives.WriteUInt32BigEndian(entry, entrySize);
                        "pssh"u8.CopyTo(entry.AsSpan(4, 4));
                        BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(28, 4), 1);
                        for (var index = 0; index < entryCount; index++)
                        {
                            BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(12, 4), (uint)index);
                            output.Write(entry);
                        }
                        modified = true;
                    }
                    else output.Write(bytes, offset, size);
                    offset += size;
                }
            }
            Assert.True(modified);
            var attempt = await new FfprobeRunner(logger, TimeSpan.FromSeconds(10))
                .TryProbeMediaAsync(tools.ResolveFfprobePath(), input, timeout.Token);
            Assert.True(attempt.Success, attempt.ErrorMessage);
            Assert.True(attempt.Probe!.HasVideo);
            Assert.Equal(16, attempt.Probe.VideoWidth);
            Assert.True(File.Exists(original));
        }
        finally { Directory.Delete(root, true); }
    }
}
