using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using Xunit;

namespace FrameShift.Tests;

public sealed class VideoLoopColorMediaTests
{
    [Theory]
    [InlineData("yuv444p10le")]
    [InlineData("yuv444p12le")]
    public async Task ExplicitSdrHighBitDepth_ExportsAsH264WithoutTheOldHdrRejection(string pixelFormat)
    {
        using var corpus = new VideoLoopTestCorpus();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await corpus.GenerateAsync("periodic", 2, timeout.Token, pixelFormat: pixelFormat);
        Assert.True(corpus.Probe.IsHdrLikely);
        var color = VideoLoopColorPolicy.Evaluate(corpus.Probe);
        Assert.True(color.Kind == VideoLoopColorKind.Sdr, System.Text.Json.JsonSerializer.Serialize(corpus.Probe));
        var result = await new VideoLoopExporter(corpus.Runner, corpus.Prober).ExportAsync(
            corpus.Tools.ResolveFfmpegPath(), corpus.Tools.ResolveFfprobePath(),
            new(corpus.Source, new AppLogger(), null), corpus.Probe, new(1, 15, 15),
            new(VideoLoopMode.Natural, KeepAudio: false), new(0, 1, 100), timeout.Token);
        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(result.OutputPath));
        Assert.Equal(corpus.Root, Path.GetDirectoryName(result.OutputPath));
        var rendered = (await corpus.Prober.TryProbeMediaAsync(corpus.Tools.ResolveFfprobePath(), result.OutputPath!, timeout.Token)).Probe;
        Assert.NotNull(rendered);
        Assert.Equal("h264", rendered.VideoCodec);
        Assert.Equal("yuv420p", rendered.VideoPixelFormat);
        Assert.InRange(rendered.Duration!.Value.TotalSeconds, 0.99, 1.01);
        Assert.Empty(Directory.GetDirectories(corpus.Root, ".frameshift-*"));
    }

    [Fact]
    public async Task UnsupportedColor_IsRejectedBeforeOpeningSourcesOrCreatingAnOutputWorkspace()
    {
        using var corpus = new VideoLoopTestCorpus();
        Directory.CreateDirectory(corpus.Root);
        var destination = Path.Combine(corpus.Root, "must not exist.mp4");
        var exporter = new VideoLoopExporter(corpus.Runner, corpus.Prober);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.RenderAsync(
            "absent.exe", "absent.exe", corpus.Source, destination,
            VideoLoopColorPolicyTests.Probe("smpte2084", "bt2020", "yuv420p10le", 10),
            new(1, 15, 15), new(VideoLoopMode.Natural, KeepAudio: false), new(0, 1, 99), null, CancellationToken.None));
        Assert.Contains("HDR-to-SDR", exception.Message);
        Assert.Empty(Directory.GetFileSystemEntries(corpus.Root));
    }
}
