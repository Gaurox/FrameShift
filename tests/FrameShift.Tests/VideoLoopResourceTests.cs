using System.Collections.Concurrent;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using Xunit;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class VideoLoopResourceTests
{
    [Fact]
    public async Task CurrentItemCancellation_DuringReverse_KeepsTheOriginalQueueIdentityAndScope()
    {
        using var fixture = new VideoLoopExportFixture(); await fixture.GenerateAsync();
        var reversing = false;
        var queried = new ConcurrentBag<string>();
        var progress = new LoopExportTestProgress(state: text => { if (text?.StartsWith("Preparing backward") == true) reversing = true; })
        { QueueCancel = path => { queried.Add(path); return reversing && path == fixture.Source; } };
        var options = new Dictionary<string, string>(new CutVideoSettings(1, 20, 10).ToOptions());
        foreach (var pair in new VideoLoopSettings(VideoLoopMode.PingPong, KeepAudio: false).ToOptions()) options[pair.Key] = pair.Value;
        var action = new CutVideoAction(fixture.Runner, fixture.Prober, fixture.Tools);
        var result = await action.ExecuteAsync(new(fixture.Source, new AppLogger(), progress, options), CancellationToken.None);
        Assert.True(result.Canceled); Assert.Equal(CancellationScope.CurrentItem, result.CancellationScope);
        Assert.NotEmpty(queried); Assert.All(queried, path => Assert.Equal(fixture.Source, path));
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".frameshift-*"));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*_pingpong*.mp4"));
    }

    [Fact]
    public void DiskSpaceQuery_SupportsExtendedLocalPaths()
    {
        using var fixture = new VideoLoopExportFixture();
        var folder = fixture.Root;
        while (folder.Length < 280) folder = Path.Combine(folder, "dossier d'été avec espaces");
        Directory.CreateDirectory(folder);
        Assert.True(VideoLoopResourceBudget.GetAvailableSpace(folder) > 0);
        Assert.True(VideoLoopResourceBudget.GetAvailableSpace(Path.Combine(folder, "future.mp4")) > 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void NaturalHdPlan_StoresOneCompactCycle_IndependentOfRepeatCount(int repeats)
    {
        var settings = new VideoLoopSettings(VideoLoopMode.Natural, Repetitions: repeats);
        var plan = VideoLoopResourcePlan.Create(1920, 1080, 36, 30, settings, false, true, 48000, 2);
        Assert.True(36d * 30 * 1920 * 1080 * 6 > VideoLoopResourceBudget.MaximumTemporaryBytes);
        Assert.True(plan.TemporaryBytes < VideoLoopResourceBudget.MaximumTemporaryBytes);
        Assert.Equal(36 * repeats, plan.OutputSeconds);
        Assert.Equal(VideoLoopResourcePlan.Create(1920, 1080, 36, 30, settings with { Repetitions = 1 }, false, true, 48000, 2).TemporaryBytes, plan.TemporaryBytes);
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(0.75d)]
    [InlineData(0.5d)]
    public void ReversePlan_CountsBothDirectionsAndLosslessArtifacts(double speed)
    {
        var plan = VideoLoopResourcePlan.Create(1920, 1080, 2, 30, new(VideoLoopMode.PingPong, speed, 3, false), false, false, 0, 0);
        Assert.Equal(2d * 30 * 1920 * 1080 * 6 * 4, plan.TemporaryBytes);
        Assert.Equal(12 / speed, plan.OutputSeconds); // Preflight estimate; final progress uses the actual N+(N-2) frame count.
        var preview = VideoLoopResourcePlan.Create(960, 540, 3, 30, new(VideoLoopMode.Natural), true, false, 0, 0);
        Assert.True(preview.TemporaryBytes < plan.TemporaryBytes);
    }

    [Fact]
    public async Task NoTemporarySpace_FailsBeforeEncoding_AndCleansOwnedFiles()
    {
        using var fixture = new VideoLoopExportFixture(); await fixture.GenerateAsync();
        var owned = new ConcurrentBag<string>();
        var exporter = new VideoLoopExporter(fixture.Runner, fixture.Prober) { AvailableSpace = path => { if (Directory.Exists(path)) owned.Add(path); return 0; } };
        var output = Path.Combine(fixture.Root, "no space.mp4");
        var error = await Assert.ThrowsAsync<IOException>(() => fixture.RenderAsync(output, new(VideoLoopMode.Natural), new(0, 2, 0), exporter: exporter));
        Assert.Contains("natural cycle", error.Message); Assert.Contains("estimated", error.Message);
        Assert.False(File.Exists(output)); Assert.NotEmpty(owned);
        Assert.All(owned, path => Assert.False(Directory.Exists(path)));
    }

    [Fact]
    public async Task OutputSpace_IsRecheckedAfterTheCycle_WithoutPublishingAPartialFile()
    {
        using var fixture = new VideoLoopExportFixture(); await fixture.GenerateAsync();
        var assembling = false;
        var exporter = new VideoLoopExporter(fixture.Runner, fixture.Prober) { AvailableSpace = _ => assembling ? 0 : long.MaxValue };
        var progress = new LoopExportTestProgress(state: text => { if (text?.StartsWith("Assembling") == true) assembling = true; });
        var output = Path.Combine(fixture.Root, "output full.mp4");
        var error = await Assert.ThrowsAsync<IOException>(() => fixture.RenderAsync(output, new(VideoLoopMode.Natural), new(0, 2, 0), progress, exporter: exporter));
        Assert.Contains("Assembling", error.Message); Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData("boundary")]
    [InlineData("encode")]
    [InlineData("assembly")]
    [InlineData("reverse")]
    public async Task CancellationInEachPhase_CleansBothWorkspacesAndKeepsTheSource(string phase)
    {
        using var fixture = new VideoLoopExportFixture(); await fixture.GenerateAsync();
        var original = await File.ReadAllBytesAsync(fixture.Source);
        using var cancel = new CancellationTokenSource();
        var assembling = false;
        var reversing = false;
        var encoding = false;
        var owned = new ConcurrentBag<string>();
        var exporter = new VideoLoopExporter(fixture.Runner, fixture.Prober) { AvailableSpace = path => { if (Directory.Exists(path)) owned.Add(path); return long.MaxValue; } };
        var progress = new LoopExportTestProgress((_, file) => {
            if (phase == "boundary" || phase == "encode" && encoding || phase == "assembly" && assembling || phase == "reverse" && reversing) cancel.Cancel();
        }, text => {
            if (text?.StartsWith("Assembling") == true) assembling = true;
            if (text?.StartsWith("Preparing backward") == true) reversing = true;
            if (text?.StartsWith("Encoding") == true) encoding = true;
        });
        var output = Path.Combine(fixture.Root, "canceled.mp4");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.RenderAsync(output,
            new(phase == "reverse" ? VideoLoopMode.PingPong : VideoLoopMode.Natural, Repetitions: 3, KeepAudio: false), new(0, 2, 0), progress, cancel.Token, exporter));
        Assert.True(cancel.IsCancellationRequested); Assert.False(File.Exists(output));
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Source));
        Assert.All(owned, path => Assert.False(Directory.Exists(path)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingAndLateDestinationCollisions_NeverOverwriteOrDeleteForeignFiles(bool late)
    {
        using var fixture = new VideoLoopExportFixture(); await fixture.GenerateAsync();
        var output = Path.Combine(fixture.Root, "foreign.mp4");
        if (!late) await File.WriteAllTextAsync(output, "foreign sentinel");
        var progress = new LoopExportTestProgress(state: text => { if (late && text?.StartsWith("Assembling") == true) File.WriteAllText(output, "foreign sentinel"); });
        await Assert.ThrowsAsync<IOException>(() => fixture.RenderAsync(output, new(VideoLoopMode.Natural), new(0, 2, 0), progress));
        Assert.Equal("foreign sentinel", await File.ReadAllTextAsync(output));
    }

    [Fact]
    public async Task SourceReplacedAtAssembly_DoesNotLeaveAnOutput()
    {
        using var fixture = new VideoLoopExportFixture(); await fixture.GenerateAsync();
        var output = Path.Combine(fixture.Root, "stale.mp4");
        var progress = new LoopExportTestProgress(state: text => {
            if (text?.StartsWith("Assembling") == true) File.SetLastWriteTimeUtc(fixture.Source, File.GetLastWriteTimeUtc(fixture.Source).AddSeconds(2));
        });
        await Assert.ThrowsAsync<VideoLoopSourceChangedException>(() => fixture.RenderAsync(output, new(VideoLoopMode.Natural), new(0, 2, 0), progress));
        Assert.False(File.Exists(output));
    }
}
