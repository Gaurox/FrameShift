using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using Xunit;
using Xunit.Abstractions;

namespace FrameShift.Tests;

// Run without overlapping WinForms/media tests so host-memory measurements are interpretable.
[Collection(WinFormsTestCollection.Name)]
public sealed class VideoLoopPerformanceTests(ITestOutputHelper output)
{
    private static readonly string RunId = $"{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";

    [VideoLoopPerformanceTheory]
    [InlineData("periodic", 5)]
    [InlineData("periodic", 30)]
    [InlineData("periodic", 50)]
    [InlineData("periodic", 60)]
    [InlineData("periodic", 300)]
    [InlineData("periodic", 3600)]
    [InlineData("whole-source", 5)]
    [InlineData("four-regions", 12)]
    [InlineData("longer-better", 12)]
    public async Task RecordSearchReference(string scenario, int duration)
    {
        using var corpus = new VideoLoopTestCorpus();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await corpus.GenerateAsync(scenario, duration, timeout.Token, lowResolution: true);
        using var stopMonitor = new CancellationTokenSource();
        long peakHostBytes = 0;
        var monitoring = MonitorHostAsync();
        var allocationStart = GC.GetTotalAllocatedBytes(precise: false);
        var elapsed = Stopwatch.StartNew();
        VideoLoopAnalysis analysis;
        try
        {
            analysis = await new VideoLoopDetector(corpus.Runner).AnalyzeAsync(corpus.Tools.ResolveFfmpegPath(),
                corpus.Source, corpus.Probe, 1, LoopSensitivity.Normal, null, timeout.Token);
        }
        finally
        {
            elapsed.Stop();
            stopMonitor.Cancel();
            await monitoring;
        }

        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - allocationStart;
        Assert.All(analysis.Candidates, candidate =>
        {
            Assert.True(double.IsFinite(candidate.StartSeconds) && double.IsFinite(candidate.EndSeconds));
            Assert.InRange(candidate.StartSeconds, 0, duration);
            Assert.InRange(candidate.EndSeconds, candidate.StartSeconds + 1 - 1e-7, duration + 1e-7);
        });
        Assert.NotEmpty(analysis.Candidates);
        var selected = analysis.Candidates[0];
        var cut = new CutVideoSettings(1, duration * VideoLoopTestCorpus.Fps, VideoLoopTestCorpus.Fps);
        var settings = new VideoLoopSettings(VideoLoopMode.Natural, Repetitions: 3, KeepAudio: false);
        var previewWatch = Stopwatch.StartNew();
        long previewBytes;
        string previewDirectory;
        using (var preview = await new VideoLoopPreviewBuilder(corpus.Runner, corpus.Prober).BuildAsync(
            corpus.Tools.ResolveFfmpegPath(), corpus.Tools.ResolveFfprobePath(), corpus.Source, corpus.Probe,
            cut, settings, selected, VideoLoopPreviewKind.Junction, null, timeout.Token))
        {
            previewWatch.Stop();
            previewDirectory = preview.Directory;
            previewBytes = Directory.GetFiles(preview.Directory).Sum(file => new FileInfo(file).Length);
        }
        Assert.False(Directory.Exists(previewDirectory));
        var exportWatch = Stopwatch.StartNew();
        var export = await new VideoLoopExporter(corpus.Runner, corpus.Prober).ExportAsync(
            corpus.Tools.ResolveFfmpegPath(), corpus.Tools.ResolveFfprobePath(),
            new(corpus.Source, new AppLogger(), null), corpus.Probe, cut, settings, selected, timeout.Token);
        exportWatch.Stop();
        Assert.True(export.Success, export.Message);
        var exported = (await corpus.Prober.TryProbeMediaAsync(corpus.Tools.ResolveFfprobePath(), export.OutputPath!, timeout.Token)).Probe!;
        Assert.InRange(exported.Duration!.Value.TotalSeconds, selected.DurationSeconds * 3 - 0.02, selected.DurationSeconds * 3 + 0.1);
        Assert.Empty(Directory.GetDirectories(corpus.Root, ".frameshift-*"));
        using (File.Open(corpus.Source, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        var assembly = typeof(VideoLoopDetector).Assembly;
        var report = new
        {
            Scenario = scenario, SourceDurationSeconds = duration,
            ExpectedPeriodSeconds = scenario == "whole-source" ? duration : scenario == "longer-better" ? 4 : 1,
            ExpectedDistinctRegions = scenario == "four-regions" ? 4 : 1,
            SourceWidth = corpus.Probe.VideoWidth, SourceHeight = corpus.Probe.VideoHeight,
            SourceFps = corpus.Probe.VideoFrameRate,
            SourceSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(corpus.Source, timeout.Token))),
            AssemblyVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            AssemblySha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(assembly.Location, timeout.Token))),
            Os = RuntimeInformation.OSDescription, Runtime = RuntimeInformation.FrameworkDescription,
            LogicalProcessors = Environment.ProcessorCount,
            SearchWallMilliseconds = elapsed.Elapsed.TotalMilliseconds,
            SelectedDurationSeconds = selected.DurationSeconds,
            JunctionPreviewWallMilliseconds = previewWatch.Elapsed.TotalMilliseconds,
            JunctionProxyBytes = previewBytes,
            ThreeCycleExportWallMilliseconds = exportWatch.Elapsed.TotalMilliseconds,
            ExportBytes = new FileInfo(export.OutputPath!).Length,
            PeakTestHostWorkingSetBytes = peakHostBytes,
            TestHostAllocatedBytesDuringSearch = allocatedBytes,
            MemoryScope = "Test host only, excluding FFmpeg children; generation precedes measurement.",
            TemporaryDiskScope = "Largest coarse/refinement RGB decode file, excluding source and export intermediates.",
            QualityScope = "Known synthetic cycles; scores, assessments and candidate counts do not certify perceptual quality.",
            Analysis = analysis
        };
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, "scratch", "loop-validation", "improvements", "baseline", RunId);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"{scenario}-{duration}s.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), timeout.Token);
        output.WriteLine(file);
        output.WriteLine($"{scenario}/{duration}s: {elapsed.Elapsed.TotalSeconds:0.###}s; {analysis.Candidates.Count} candidates; {analysis.Limits}.");

        async Task MonitorHostAsync()
        {
            using var process = Process.GetCurrentProcess();
            try
            {
                while (true)
                {
                    process.Refresh();
                    peakHostBytes = Math.Max(peakHostBytes, process.WorkingSet64);
                    await Task.Delay(20, stopMonitor.Token);
                }
            }
            catch (OperationCanceledException) when (stopMonitor.IsCancellationRequested) { }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "FrameShift", "FrameShift.csproj"))) return directory.FullName;
        throw new DirectoryNotFoundException("Find Loop benchmarks require the FrameShift source checkout.");
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class VideoLoopPerformanceTheoryAttribute : TheoryAttribute
{
    public VideoLoopPerformanceTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("FRAMESHIFT_LOOP_BENCHMARK") != "1")
            Skip = "Set FRAMESHIFT_LOOP_BENCHMARK=1 to generate the long-video search reference corpus.";
    }
}
