using FrameShift.Core.Actions;
using FrameShift.Core.AI.SeparateAudio;
using FrameShift.Core.AI.CreateSubtitles;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Core.Progress;
using NAudio.Wave;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace FrameShift.Tests;

public sealed class SecurityOutputIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"frameshift sécurité {Guid.NewGuid():N}");
    public SecurityOutputIntegrationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealImageConversionPreservesCollisionAndSurvivesCompletionNotificationFailure(bool directory)
    {
        var input = Path.Combine(_root, "été.png");
        using (var image = new Image<Rgba32>(8, 8, new Rgba32(80, 120, 160))) image.SaveAsPng(input);
        var desired = Path.Combine(_root, "été_convert_png.png");
        var progress = new CollisionReporter(() =>
        {
            Assert.Single(Directory.GetDirectories(_root, ".frameshift-*"));
            if (directory) Directory.CreateDirectory(desired);
            else File.WriteAllText(desired, "sentinel");
        });
        var logger = new AppLogger();
        var action = new ConvertImageAction(new FfmpegRunner(logger), new ToolLocator());
        var result = await action.ExecuteAsync(new ActionRequest(input, logger, progress), CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Equal(Path.Combine(_root, "été_convert_png_001.png"), result.OutputPath);
        using var saved = SafeImageReader.Load<Rgba32>(result.OutputPath!);
        Assert.Equal(8, saved.Width);
        if (directory) Assert.True(Directory.Exists(desired));
        else Assert.Equal("sentinel", File.ReadAllText(desired));
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
    }

    [Fact]
    public async Task RealFrameExtractionPreservesForeignDirectoryAndPublishesWholePayload()
    {
        var logger = new AppLogger();
        var tools = new ToolLocator();
        var runner = new FfmpegRunner(logger);
        var input = Path.Combine(_root, "été.mp4");
        var created = await runner.RunCaptureAsync(tools.ResolveFfmpegPath(),
            ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=blue:s=32x32:r=2:d=1", "-c:v", "libx264", input], CancellationToken.None);
        Assert.True(created.Success, created.StandardError);
        var desired = Path.Combine(_root, "été_frames");
        var progress = new CollisionReporter(() =>
        {
            Directory.CreateDirectory(desired);
            File.WriteAllText(Path.Combine(desired, "sentinel"), "foreign");
        });
        var action = new ExtractFramesAction(runner, new FfprobeRunner(logger), tools);
        var result = await action.ExecuteAsync(new ActionRequest(input, logger, progress), CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Equal(desired + "_001", result.OutputPath);
        Assert.Equal("foreign", File.ReadAllText(Path.Combine(desired, "sentinel")));
        Assert.NotEmpty(Directory.GetFiles(result.OutputPath!, "*.png"));
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
    }

    [Fact]
    public async Task ConcurrentPdfExportsKeepBothCompletedDocuments()
    {
        var input = Path.Combine(_root, "été.webp");
        using (var image = new Image<Rgba32>(8, 8, new Rgba32(80, 120, 160))) image.SaveAsWebp(input);
        var settings = new ImageToPdfSettings
        {
            Items = [new ImageToPdfItemSettings { SourcePath = input, Width = 50, Height = 50 }]
        };
        var options = new Dictionary<string, string> { [ActionOptionKeys.ImageToPdfSettings] = settings.ToOptionPayload() };
        using var barrier = new Barrier(2);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            var action = new ImageToPdfAction(new ImageToPdfPdfExporter());
            return await action.ExecuteAsync(new ActionRequest(input, new AppLogger(), null, options), CancellationToken.None);
        })));
        Assert.All(results, result => Assert.True(result.Success, result.Message));
        Assert.NotEqual(results[0].OutputPath, results[1].OutputPath);
        foreach (var result in results) Assert.StartsWith("%PDF", File.ReadAllText(result.OutputPath!));
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
    }

    [Fact]
    public async Task DiagnosticCollisionKeepsForeignReportAndTmpFile()
    {
        var output = Path.Combine(_root, "été.ass");
        File.WriteAllText(output, "valid subtitles");
        File.WriteAllText(output + ".diagnostic.json", "foreign report");
        File.WriteAllText(output + ".tmp", "foreign tmp");
        var report = await CreateSubtitlesAssDiagnosticWriter.WriteReportIfNeededAsync(
            CreateSubtitlesOutputFormat.AdvancedAss, "input.wav", output,
            new SubtitleProject(TimeSpan.Zero, []), new CreateSubtitlesDisplayTimingAnalysis(),
            CreateSubtitlesAssPreset.Classic, new AppLogger(), CancellationToken.None);
        Assert.Equal(output + ".diagnostic_001.json", report);
        Assert.Equal("foreign report", File.ReadAllText(output + ".diagnostic.json"));
        Assert.Equal("foreign tmp", File.ReadAllText(output + ".tmp"));
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(report!));
        Assert.Equal(output, json.RootElement.GetProperty("OutputAssPath").GetString());
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
    }

    [Fact]
    public async Task CanceledConversionPreservesForeignOutputAndCleansWorkspace()
    {
        var input = Path.Combine(_root, "été.png");
        using (var image = new Image<Rgba32>(8, 8)) image.SaveAsPng(input);
        using var cancellation = new CancellationTokenSource();
        var desired = Path.Combine(_root, "été_convert_png.png");
        var progress = new CollisionReporter(() =>
        {
            File.WriteAllText(desired, "foreign");
            cancellation.Cancel();
        });
        var logger = new AppLogger();
        var action = new ConvertImageAction(new FfmpegRunner(logger), new ToolLocator());
        var result = await action.ExecuteAsync(new ActionRequest(input, logger, progress), cancellation.Token);
        Assert.True(result.Canceled, result.Message);
        Assert.False(result.Success);
        Assert.Equal("foreign", File.ReadAllText(desired));
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
    }

    [Fact]
    public void MultiStemFailureKeepsEarlierCommitAndReportsItsPath()
    {
        var input = Path.Combine(_root, "été.wav");
        var desired = Path.Combine(_root, "été_instrumental.wav");
        for (var index = 0; index < 1000; index++)
            File.WriteAllText(index == 0 ? desired : Path.Combine(_root, $"été_instrumental_{index:000}.wav"), "sentinel");
        using (var writers = AudioSeparationEngine.OutputWriters.Create(input, new StemSelection { Vocals = true, Instrumental = true }))
        {
            writers.Vocals!.WriteSamples([0.2f, -0.2f], 2);
            writers.Instrumental!.WriteSamples([0.3f, -0.3f], 2);
            var failure = Assert.Throws<IOException>(() => writers.MarkCompleted(CancellationToken.None));
            Assert.Contains(Path.Combine(_root, "été_vocals.wav"), failure.Message);
            writers.DeletePartialOutputs();
        }
        using var reader = new WaveFileReader(Path.Combine(_root, "été_vocals.wav"));
        Assert.Equal(4, reader.Length);
        Assert.Equal("sentinel", File.ReadAllText(desired));
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
    }

    private sealed class CollisionReporter(Action createCollision) : IProgressReporter
    {
        private bool _created;
        public bool IsCancellationRequested => false;
        public bool IsQueueItemRemovalRequested(string inputPath) => false;
        public bool IsQueueItemCancellationRequested(string inputPath) => false;
        public void ReportQueue(IReadOnlyList<string> items) { }
        public void ReportQueueItem(string currentFile, string state, string? message = null) { }
        public void ReportState(string state, string? message = null)
        {
            if (state == "processing" && !_created) { _created = true; createCollision(); }
        }
        public void ReportProgress(int value, string? file, string? action, string? message, string? etaText = null)
        {
            if (value == 1000 && message == "Completed.") throw new InvalidOperationException("Simulated UI notification failure.");
        }
    }
}
