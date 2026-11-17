using FrameShift.Core.FFmpeg;
using Xunit;

namespace FrameShift.Tests;

public sealed class FfmpegRunnerTests
{
    [Fact]
    public async Task Concat_ResolvesLocalClipsInWorkingDirectoryWithHash()
    {
        using var fixture = new VideoLoopExportFixture();
        await fixture.GenerateAsync(seconds: 1);
        var directory = Path.Combine(fixture.Root, "assemblage # été");
        Directory.CreateDirectory(directory);
        File.Copy(fixture.Source, Path.Combine(directory, "forward_0.mkv"));
        await File.WriteAllTextAsync(Path.Combine(directory, "repeats.txt"),
            "file 'forward_0.mkv'\nfile 'forward_0.mkv'\n");
        var output = Path.Combine(fixture.Root, "répétitions.mkv");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var result = await fixture.Runner.RunAsync(fixture.Tools.ResolveFfmpegPath(),
            ["-hide_banner", "-loglevel", "error", "-progress", "pipe:1", "-nostats",
                "-f", "concat", "-safe", "1", "-i", "repeats.txt", "-c", "copy", output],
            null, null, fixture.Source, "Concat fixture", "CPU", deadline.Token, workingDirectory: directory);

        Assert.True(result.ExitCode == 0, result.StandardError);
        var probe = await fixture.ReadProbeAsync(output);
        Assert.InRange(probe.Duration!.Value.TotalSeconds, 1.99, 2.01);
    }

    // TryParseFfmpegTime — valid inputs

    [Theory]
    [InlineData("00:00:00.000", 0.0)]
    [InlineData("00:00:01.000", 1.0)]
    [InlineData("00:01:30.500", 90.5)]
    [InlineData("01:02:03.250", 3723.25)]
    [InlineData("00:59:59.999", 3599.999)]
    public void TryParseFfmpegTime_ValidHhMmSs_ReturnsTrueAndCorrectSeconds(string input, double expected)
    {
        var result = FfmpegRunner.TryParseFfmpegTime(input, out var seconds);

        Assert.True(result);
        Assert.Equal(expected, seconds, precision: 2);
    }

    [Theory]
    [InlineData("01:30.500", 90.5)]
    [InlineData("00:00.000", 0.0)]
    [InlineData("59:59.999", 3599.999)]
    public void TryParseFfmpegTime_ValidMmSs_ReturnsTrueAndCorrectSeconds(string input, double expected)
    {
        var result = FfmpegRunner.TryParseFfmpegTime(input, out var seconds);

        Assert.True(result);
        Assert.Equal(expected, seconds, precision: 2);
    }

    // TryParseFfmpegTime — invalid inputs

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("1:2:3:4")]
    [InlineData("abc:de.fg")]
    [InlineData("30")]
    public void TryParseFfmpegTime_InvalidInput_ReturnsFalse(string input)
    {
        var result = FfmpegRunner.TryParseFfmpegTime(input, out _);

        Assert.False(result);
    }
}
