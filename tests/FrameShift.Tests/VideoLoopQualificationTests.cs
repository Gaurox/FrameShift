using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using Xunit;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class VideoLoopQualificationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(150)]
    [InlineData(230)]
    public async Task UnicodeApostrophesAndLongPaths_WorkThroughPreviewAndPublishedExport(int padding)
    {
        using var f = new VideoLoopExportFixture();
        await f.GenerateAsync(audio: true);
        var directory = Path.Combine(f.Root, "Dossier d'été avec espaces", new string('x', Math.Max(1, padding)));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "vidéo d'été source.mkv");
        File.Copy(f.Source, source);
        var probe = await f.ReadProbeAsync(source);
        var cut = new CutVideoSettings(1, 10, 10);
        var settings = new VideoLoopSettings(VideoLoopMode.Natural, Repetitions: 2, KeepAudio: true);
        var bounds = new VideoLoopCandidate(0, 1, 0);
        string previewDirectory;
        using (var preview = await new VideoLoopPreviewBuilder(f.Runner, f.Prober).BuildAsync(
            f.Tools.ResolveFfmpegPath(), f.Tools.ResolveFfprobePath(), source, probe, cut, settings,
            bounds, VideoLoopPreviewKind.Junction, null, CancellationToken.None))
        {
            previewDirectory = preview.Directory;
            var previewProbe = await f.ReadProbeAsync(Assert.Single(preview.Clips).Path);
            Assert.True(previewProbe.HasVideo);
            Assert.False(previewProbe.HasAudio);
        }
        Assert.False(Directory.Exists(previewDirectory));
        var options = new Dictionary<string, string>(cut.ToOptions());
        foreach (var pair in VideoLoopExportSelection.FromCandidate(bounds, settings).ToOptions()) options[pair.Key] = pair.Value;
        var action = new CutVideoAction(f.Runner, f.Prober, f.Tools);
        var result = await action.ExecuteAsync(new(source, new AppLogger(), null, options), CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Equal(directory, Path.GetDirectoryName(result.OutputPath));
        var exported = await f.ReadProbeAsync(result.OutputPath!);
        Assert.True(exported.HasAudio);
        Assert.InRange(exported.Duration!.Value.TotalSeconds, 1.99, 2.06);
        Assert.Empty(Directory.GetDirectories(directory, ".frameshift-*"));
        using var exclusive = File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrExclusivelyLockedSource_FailsWithoutPublishing(bool locked)
    {
        using var f = new VideoLoopExportFixture();
        await f.GenerateAsync();
        var options = new Dictionary<string, string>(new CutVideoSettings(1, 10, 10).ToOptions());
        foreach (var pair in new VideoLoopSettings(VideoLoopMode.Natural).ToOptions()) options[pair.Key] = pair.Value;
        using var held = locked ? File.Open(f.Source, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
        if (!locked) File.Delete(f.Source);
        var result = await new CutVideoAction(f.Runner, f.Prober, f.Tools)
            .ExecuteAsync(new(f.Source, new AppLogger(), null, options), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(35));
        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Empty(Directory.GetFiles(f.Root, "*.mp4"));
        Assert.Empty(Directory.GetDirectories(f.Root, ".frameshift-*"));
    }

    [Fact]
    public async Task DestinationParentBecameAFile_FailsAndPreservesForeignContent()
    {
        using var f = new VideoLoopExportFixture();
        await f.GenerateAsync();
        var destination = Path.Combine(f.Root, "destination d'été");
        await File.WriteAllTextAsync(destination, "Keep this unrelated file.");
        await Assert.ThrowsAnyAsync<IOException>(() => f.RenderAsync(Path.Combine(destination, "loop.mp4"),
            new(VideoLoopMode.Natural), new(0, 1, 0)));
        Assert.Equal("Keep this unrelated file.", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetDirectories(f.Root, ".frameshift-*"));
        using var exclusive = File.Open(f.Source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
}
