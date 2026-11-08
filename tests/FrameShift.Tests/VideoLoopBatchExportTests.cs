using System.Collections.Concurrent;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using FrameShift.Core.Progress;
using Xunit;
using static FrameShift.Core.Actions.VideoLoopFrameReader;

namespace FrameShift.Tests;

public sealed class VideoLoopBatchExportTests
{
    private static async Task<VideoLoopExportFixture> Fixture(bool hdr = false)
    {
        var fixture = new VideoLoopExportFixture();
        try
        {
            await fixture.GenerateAsync("nullsrc=s=160x96:r=10,geq=lum='20+5*N':cb=128:cr=128", 4, audio: true,
                videoFilter: hdr ? "setparams=color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc" : null);
            return fixture;
        }
        catch { fixture.Dispose(); throw; }
    }

    private static IReadOnlyList<ActionRequest> Requests(VideoLoopExportFixture f, BatchReporter reporter,
        bool invalidMiddle = false)
    {
        var stamp = VideoLoopSourceStamp.Read(f.Source);
        var selections = new[]
        {
            VideoLoopExportSelection.FromCandidate(new(0, 0.8, 99), new(VideoLoopMode.Natural, KeepAudio: true)) with { SourceStamp = stamp },
            VideoLoopExportSelection.FromCandidate(new(1.2, invalidMiddle ? 10 : 2.4, 98), new(VideoLoopMode.Natural, KeepAudio: false)) with { SourceStamp = stamp },
            VideoLoopExportSelection.FromCandidate(new(2.5, 3.2, 97), new(VideoLoopMode.Natural, Repetitions: 2, KeepAudio: true)) with { SourceStamp = stamp }
        };
        return Program.CreateLoopBatchRequests(f.Source, new(1, 40, 10), selections, new Dictionary<string, string>(), new AppLogger())
            .Select(r => r with { ProgressReporter = reporter }).ToArray();
    }

    private static CutVideoAction Action(VideoLoopExportFixture f) => new(f.Runner, f.Prober, f.Tools);
    private static Task<IReadOnlyList<ActionExecutionResult>> Run(VideoLoopExportFixture f, IReadOnlyList<ActionRequest> requests, CancellationToken token = default)
        => new ActionQueueRunner().RunAsync(Action(f), requests, token).WaitAsync(TimeSpan.FromMinutes(2));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameSource_HasIndependentBoundsAudioAndRepetitions_AndPreservesCollisions(bool hdr)
    {
        using var f = await Fixture(hdr);
        var reporter = new BatchReporter();
        var requests = Requests(f, reporter);
        var foreign = Path.Combine(f.Root, "source vidéo_loop.mp4");
        await File.WriteAllTextAsync(foreign, "foreign collision");
        var results = await Run(f, requests);
        Assert.All(results, result => Assert.True(result.Success, result.Message));
        Assert.Equal(3, results.Select(r => r.OutputPath).Distinct().Count());
        Assert.Equal("foreign collision", await File.ReadAllTextAsync(foreign));
        var source = await new VideoLoopFrameReader(f.Runner, f.Tools.ResolveFfmpegPath(), f.Source,
            colorPolicy: VideoLoopColorPolicy.Evaluate(f.Probe)).ReadAsync(0, 4, null, 50, CancellationToken.None);
        var counts = new[] { 8, 12, 14 }; var starts = new[] { 0, 12, 25 }; var seconds = new[] { 0.8, 1.2, 1.4 };
        for (var i = 0; i < results.Count; i++)
        {
            Assert.Equal(f.Root, Path.GetDirectoryName(results[i].OutputPath));
            var probe = await f.ReadProbeAsync(results[i].OutputPath!);
            Assert.Equal(i != 1, probe.HasAudio);
            Assert.InRange(probe.Duration!.Value.TotalSeconds, seconds[i] - 0.03, seconds[i] + 0.03);
            if (hdr) { Assert.Equal("bt709", probe.VideoColorTransfer); Assert.False(probe.IsHdrLikely); }
            var actual = await new VideoLoopFrameReader(f.Runner, f.Tools.ResolveFfmpegPath(), results[i].OutputPath!)
                .ReadAsync(0, 5, null, 50, CancellationToken.None);
            Assert.Equal(counts[i], actual.Count);
            for (var j = 0; j < actual.Count; j++)
            {
                var expected = source[starts[i] + j % (i == 2 ? 7 : counts[i])];
                Assert.InRange(actual[j].Pixels.Zip(expected.Pixels, (x, y) => (double)Math.Abs(x - y)).Average(), 0, 4);
            }
            Assert.Equal("done", reporter.States[$"generic:{i}"]);
            Assert.Contains($"Candidate {i + 1}/3", reporter.Messages[$"generic:{i}"]);
        }
        Assert.Empty(Directory.GetDirectories(f.Root, ".frameshift-*"));
        Assert.NotSame(requests[0].Options, requests[1].Options);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)requests[0].Options!)["loop-end"] = "3");
    }

    [Fact]
    public async Task CurrentCancellation_DoesNotCancelOtherCandidatesWithTheSameSource()
    {
        using var f = await Fixture();
        var reporter = new BatchReporter();
        var encoding = false;
        reporter.OnState = message => { if (message?.Contains("Encoding the natural") == true) encoding = true; };
        reporter.OnProgress = id => { if (id == "generic:0" && encoding) reporter.CancelItems[id] = true; };
        var results = await Run(f, Requests(f, reporter));
        Assert.True(encoding); Assert.True(results[0].Canceled); Assert.Equal(CancellationScope.CurrentItem, results[0].CancellationScope);
        Assert.All(results.Skip(1), r => Assert.True(r.Success, r.Message));
        Assert.Equal("canceled", reporter.States["generic:0"]);
        Assert.Equal("done", reporter.States["generic:1"]); Assert.Equal("done", reporter.States["generic:2"]);
        Assert.Equal(2, Directory.GetFiles(f.Root, "*_loop*.mp4").Length);
        Assert.Empty(Directory.GetDirectories(f.Root, ".frameshift-*"));
        using var exclusive = File.Open(f.Source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task GlobalCancellation_StopsPendingCandidates_AndKeepsTheCompletedOutput()
    {
        using var f = await Fixture(); var reporter = new BatchReporter();
        reporter.OnItem = (id, state) => { if (id == "generic:0" && state == "done") reporter.CancelAll = true; };
        var results = await Run(f, Requests(f, reporter));
        Assert.True(results[0].Success);
        Assert.All(results.Skip(1), r => { Assert.True(r.Canceled); Assert.Equal(CancellationScope.All, r.CancellationScope); });
        Assert.Single(Directory.GetFiles(f.Root, "*_loop*.mp4")); Assert.True(File.Exists(results[0].OutputPath));
        Assert.Equal("done", reporter.States["generic:0"]); Assert.Equal("canceled", reporter.States["generic:2"]);
    }

    [Fact]
    public async Task GlobalCancellationDuringEncoding_CleansPartialOutput_AndStopsTheRest()
    {
        using var f = await Fixture(); var reporter = new BatchReporter(); var encoding = false;
        using var cancel = new CancellationTokenSource();
        reporter.OnState = message => { if (message?.Contains("Encoding the natural") == true) encoding = true; };
        reporter.OnProgress = _ => { if (encoding) { reporter.CancelAll = true; cancel.Cancel(); } };
        var results = await Run(f, Requests(f, reporter), cancel.Token);
        Assert.True(encoding);
        Assert.All(results, r => { Assert.True(r.Canceled); Assert.Equal(CancellationScope.All, r.CancellationScope); });
        Assert.Empty(Directory.GetFiles(f.Root, "*_loop*.mp4")); Assert.Empty(Directory.GetDirectories(f.Root, ".frameshift-*"));
        using var exclusive = File.Open(f.Source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task RemovingSecondOccurrence_LeavesFirstAndThirdIndependent()
    {
        using var f = await Fixture(); var reporter = new BatchReporter();
        reporter.OnItem = (id, state) => { if (id == "generic:0" && state == "processing") reporter.Removed["generic:1"] = true; };
        var results = await Run(f, Requests(f, reporter));
        Assert.True(results[0].Success); Assert.True(results[1].Canceled); Assert.True(results[2].Success);
        Assert.Equal(2, Directory.GetFiles(f.Root, "*_loop*.mp4").Length);
    }

    [Fact]
    public async Task IntermediateFailure_PreservesEarlierOutput_AndContinuesWithLaterCandidate()
    {
        using var f = await Fixture(); var reporter = new BatchReporter();
        var results = await Run(f, Requests(f, reporter, invalidMiddle: true));
        Assert.True(results[0].Success); Assert.False(results[1].Success); Assert.False(results[1].Canceled); Assert.True(results[2].Success);
        Assert.Equal("failed", reporter.States["generic:1"]); Assert.Contains("boundaries", reporter.Messages["generic:1"]);
        Assert.True(File.Exists(results[0].OutputPath)); Assert.True(File.Exists(results[2].OutputPath));
        Assert.Equal(2, Directory.GetFiles(f.Root, "*_loop*.mp4").Length); Assert.Empty(Directory.GetDirectories(f.Root, ".frameshift-*"));
    }

    [Fact]
    public async Task ReplacedSource_BlocksRemainingSnapshots_AndKeepsCompletedOutput()
    {
        using var f = await Fixture(); var reporter = new BatchReporter();
        reporter.OnItem = (id, state) => { if (id == "generic:0" && state == "done") File.SetLastWriteTimeUtc(f.Source, DateTime.UtcNow.AddMinutes(1)); };
        var results = await Run(f, Requests(f, reporter));
        Assert.True(results[0].Success);
        Assert.All(results.Skip(1), r => { Assert.False(r.Success); Assert.Contains("source changed", r.Message, StringComparison.OrdinalIgnoreCase); });
        Assert.Single(Directory.GetFiles(f.Root, "*_loop*.mp4")); Assert.True(File.Exists(results[0].OutputPath));
    }

    private sealed class BatchReporter : IProgressReporter
    {
        public bool CancelAll;
        public ConcurrentDictionary<string, bool> CancelItems { get; } = new();
        public ConcurrentDictionary<string, bool> Removed { get; } = new();
        public ConcurrentDictionary<string, string> States { get; } = new();
        public ConcurrentDictionary<string, string> Messages { get; } = new();
        public Action<string, string>? OnItem;
        public Action<string>? OnProgress;
        public Action<string?>? OnState;
        public bool IsCancellationRequested => CancelAll;
        public bool IsQueueItemRemovalRequested(string path) => throw new InvalidOperationException("Occurrence ID required.");
        public bool IsQueueItemRemovalRequested(string path, string id) => Removed.TryRemove(id, out _);
        public bool IsQueueItemCancellationRequested(string path) => throw new InvalidOperationException("Occurrence ID required.");
        public bool IsQueueItemCancellationRequested(string path, string id) => CancelItems.ContainsKey(id);
        public void ReportQueue(IReadOnlyList<string> items) { Assert.Equal(3, items.Count); Assert.Single(items.Distinct()); }
        public void ReportProgress(int value, string? file, string? action, string? message, string? eta = null) => throw new InvalidOperationException("Occurrence ID required.");
        public void ReportProgress(int value, string? file, string? action, string? message, string? eta, string id) => OnProgress?.Invoke(id);
        public void ReportState(string state, string? message = null) => OnState?.Invoke(message);
        public void ReportQueueItem(string file, string state, string? message = null) => throw new InvalidOperationException("Occurrence ID required.");
        public void ReportQueueItem(string file, string id, string state, string? message)
        {
            States[id] = state; Messages[id] = message ?? ""; OnItem?.Invoke(id, state);
        }
    }
}
