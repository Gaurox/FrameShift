using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using FrameShift.Core.Progress;
using NAudio.Wave;
using Xunit;

namespace FrameShift.Tests;

public sealed class BalanceVolumeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"FrameShift équilibrage été {Guid.NewGuid():N}");
    private readonly AppLogger _logger = new();
    private readonly ToolLocator _tools = new();
    public BalanceVolumeTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private FfmpegRunner Runner => new(_logger);
    private BalanceVolumeAction Action => new(Runner, new FfprobeRunner(_logger), _tools);

    [Fact]
    public void DefaultsAndCli_ValidateIntensity()
    {
        Assert.Equal("moderate", new BalanceVolumeSettings().Intensity);
        Assert.True(ActionRegistry.CreateDefault().TryGet("balance-volume", out _));
        foreach (var intensity in new[] { "light", "moderate", "strong" })
        {
            Assert.True(Program.TryParseArguments(["--action", "balance-volume", "--volume-intensity", intensity, "film.mkv"],
                out _, out _, out var options, out _));
            Assert.Equal(intensity, options[BalanceVolumeSettings.OptionKey]);
        }
        Assert.False(Program.TryParseArguments(["--action", "balance-volume", "--volume-intensity", "invalid", "film.mkv"],
            out _, out _, out _, out _));
        Assert.False(Program.TryParseArguments(["--action", "balance-volume", "--volume-intensity"],
            out _, out _, out _, out _));
    }

    [Fact]
    public async Task RealAudio_AllIntensitiesReduceDynamicRange_WithoutClipping()
    {
        var input = CreateWave(36);
        var originalGap = Gap(input);
        var gaps = new List<double>();
        foreach (var intensity in new[] { "light", "moderate", "strong" })
        {
            var result = await Action.ExecuteAsync(Request(input, intensity), CancellationToken.None);
            Assert.True(result.Success, result.Message);
            gaps.Add(Gap(result.OutputPath!));
            using var reader = new WaveFileReader(result.OutputPath!);
            Assert.InRange(reader.TotalTime.TotalSeconds, 35.98, 36.02);
            var samples = reader.ToSampleProvider();
            var buffer = new float[48000];
            int count;
            while ((count = samples.Read(buffer, 0, buffer.Length)) > 0)
                Assert.All(buffer.Take(count), sample => Assert.InRange(Math.Abs(sample), 0, 0.951f));
        }
        Assert.True(gaps[0] < originalGap - 3, $"Original={originalGap}, light={gaps[0]}");
        Assert.True(gaps[1] < gaps[0] - 1, $"Light={gaps[0]}, moderate={gaps[1]}");
        Assert.True(gaps[2] < gaps[1] - 1, $"Moderate={gaps[1]}, strong={gaps[2]}");
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
    }

    [Fact]
    public async Task Video_IsBitIdentical_AndKeepsAudioTracksSubtitlesChaptersAndMetadata()
    {
        var input = Path.Combine(_root, "film été.mkv");
        var subtitle = Path.Combine(_root, "sous titres.srt");
        File.WriteAllText(subtitle, "1\n00:00:00,000 --> 00:00:01,000\nBonjour\n");
        var metadata = Path.Combine(_root, "chapitres.txt");
        File.WriteAllText(metadata, ";FFMETADATA1\ntitle=Film été\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=1000\ntitle=Début\n");
        var created = await Runner.RunCaptureAsync(_tools.ResolveFfmpegPath(),
            ["-v", "error", "-f", "lavfi", "-i", "color=s=32x32:r=10:d=2", "-i", CreateWave(2),
             "-i", subtitle, "-i", metadata, "-map", "0:v", "-map", "1:a", "-map", "1:a", "-map", "2:s",
             "-map_metadata", "3", "-map_chapters", "3", "-metadata:s:a:0", "language=fra", "-metadata:s:a:1", "language=eng",
             "-c:v", "libx264", "-c:a", "aac", "-c:s", "srt", input], CancellationToken.None);
        Assert.True(created.Success, created.StandardError);
        var result = await Action.ExecuteAsync(Request(input), CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Equal(await VideoHash(input), await VideoHash(result.OutputPath!));
        var probe = await new FfprobeRunner(_logger).TryProbeMediaAsync(_tools.ResolveFfprobePath(), result.OutputPath!, CancellationToken.None);
        Assert.Equal(2, probe.Probe!.AudioCodecs.Count);
        Assert.Single(probe.Probe.SubtitleCodecs);
        var metadataOutput = Path.Combine(_root, "metadata sortie.txt");
        var copiedMetadata = await Runner.RunCaptureAsync(_tools.ResolveFfmpegPath(),
            ["-v", "error", "-i", result.OutputPath!, "-f", "ffmetadata", metadataOutput], CancellationToken.None);
        Assert.True(copiedMetadata.Success, copiedMetadata.StandardError);
        var metadataText = File.ReadAllText(metadataOutput);
        Assert.Contains("title=Film été", metadataText);
        Assert.Contains("title=Début", metadataText);
        var tracks = await Runner.RunCaptureAsync(_tools.ResolveFfmpegPath(),
            ["-hide_banner", "-i", result.OutputPath!, "-map", "0:a", "-c", "copy", "-f", "null", "-"], CancellationToken.None);
        Assert.Contains("(fra)", tracks.StandardError);
        Assert.Contains("(eng)", tracks.StandardError);
    }

    [Theory]
    [InlineData("mp4", "libx264")]
    [InlineData("webm", "libvpx-vp9")]
    [InlineData("avi", "mpeg4")]
    public async Task VideoContainers_KeepVideoWithoutReencoding(string extension, string videoCodec)
    {
        var input = Path.Combine(_root, "film." + extension);
        var created = await Runner.RunCaptureAsync(_tools.ResolveFfmpegPath(),
            ["-v", "error", "-f", "lavfi", "-i", "color=s=32x32:r=10:d=2", "-i", CreateWave(2),
             "-c:v", videoCodec, "-c:a", extension == "webm" ? "libopus" : "libmp3lame", input], CancellationToken.None);
        Assert.True(created.Success, created.StandardError);
        var result = await Action.ExecuteAsync(Request(input), CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Equal(await VideoHash(input), await VideoHash(result.OutputPath!));
    }

    [Theory]
    [InlineData(2, "mkv")]
    [InlineData(6, "mkv")]
    [InlineData(6, "avi")]
    public async Task StereoAndSurround_PreserveChannelCount(int channels, string extension)
    {
        var input = Path.Combine(_root, "surround." + extension);
        var layout = channels == 6 ? "5.1" : "stereo";
        var created = await Runner.RunCaptureAsync(_tools.ResolveFfmpegPath(),
            ["-v", "error", "-f", "lavfi", "-i", "color=s=32x32:r=10:d=2",
             "-f", "lavfi", "-i", $"aevalsrc=0.1*sin(440*2*PI*t):c={layout}:d=2:s=48000",
             "-c:v", "mpeg4", "-c:a", "ac3", input], CancellationToken.None);
        Assert.True(created.Success, created.StandardError);
        var result = await Action.ExecuteAsync(Request(input), CancellationToken.None);
        Assert.True(result.Success, result.Message);
        var probe = await new FfprobeRunner(_logger).TryProbeMediaAsync(_tools.ResolveFfprobePath(), result.OutputPath!, CancellationToken.None);
        Assert.Equal(channels, probe.Probe!.PrimaryAudioChannels);
    }

    [Theory]
    [InlineData("mp3", "libmp3lame")]
    [InlineData("m4a", "aac")]
    [InlineData("ogg", "libvorbis")]
    [InlineData("flac", "flac")]
    [InlineData("aac", "aac")]
    [InlineData("wma", "wmav2")]
    public async Task AudioFormats_BalanceInTheirOriginalContainer(string extension, string codec)
    {
        var input = Path.Combine(_root, "audio été." + extension);
        var created = await Runner.RunCaptureAsync(_tools.ResolveFfmpegPath(),
            ["-v", "error", "-i", CreateWave(2), "-c:a", codec, input], CancellationToken.None);
        Assert.True(created.Success, created.StandardError);
        var result = await Action.ExecuteAsync(Request(input), CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Equal("." + extension, Path.GetExtension(result.OutputPath));
    }

    [Fact]
    public async Task CollisionAndFailures_PreserveExistingFilesAndCleanWorkspace()
    {
        var input = CreateWave(2);
        var collision = Path.Combine(_root, "dialogues été_balanced_moderate.wav");
        File.WriteAllText(collision, "sentinel");
        var result = await Action.ExecuteAsync(Request(input), CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.EndsWith("_001.wav", result.OutputPath);
        Assert.Equal("sentinel", File.ReadAllText(collision));
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));

        var corrupt = Path.Combine(_root, "invalide.mp4");
        File.WriteAllText(corrupt, "invalid");
        Assert.False((await Action.ExecuteAsync(Request(corrupt), CancellationToken.None)).Success);
        Assert.False((await Action.ExecuteAsync(Request(input, "bad"), CancellationToken.None)).Success);
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
    }

    [Fact]
    public async Task MissingAudioAndEncodingFailure_ReturnFailureWithoutPartialOutput()
    {
        var silentVideo = Path.Combine(_root, "muet.mp4");
        var created = await Runner.RunCaptureAsync(_tools.ResolveFfmpegPath(),
            ["-v", "error", "-f", "lavfi", "-i", "color=s=32x32:r=10:d=1", "-c:v", "libx264", silentVideo], CancellationToken.None);
        Assert.True(created.Success, created.StandardError);
        var missingAudio = await Action.ExecuteAsync(Request(silentVideo), CancellationToken.None);
        Assert.False(missingAudio.Success);
        Assert.Equal(MediaActionMessages.MissingAudioTrack(), missingAudio.Message);

        // A valid probe followed by an incompatible mux forces failure after the
        // output workspace has been created (not merely a validation failure).
        var incompatible = Path.Combine(_root, "conteneur incompatible.mp3");
        created = await Runner.RunCaptureAsync(_tools.ResolveFfmpegPath(),
            ["-v", "error", "-i", silentVideo, "-i", CreateWave(1), "-map", "0:v", "-map", "1:a",
             "-c:v", "copy", "-c:a", "aac", "-f", "matroska", incompatible], CancellationToken.None);
        Assert.True(created.Success, created.StandardError);
        var failed = await Action.ExecuteAsync(Request(incompatible), CancellationToken.None);
        Assert.False(failed.Success);
        Assert.False(failed.Canceled);
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
        Assert.Empty(Directory.GetFiles(_root, "*_balanced_*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringProcessing_CleansPartialOutputAndStopsProcess(bool currentItem)
    {
        var input = CreateWave(120);
        using var cancel = new CancellationTokenSource();
        var reporter = new CancelReporter(cancel, currentItem);
        var result = await Action.ExecuteAsync(Request(input, reporter: reporter), cancel.Token);
        Assert.True(result.Canceled, result.Message);
        Assert.Equal(currentItem ? CancellationScope.CurrentItem : CancellationScope.All, result.CancellationScope);
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
        Assert.Empty(Directory.GetFiles(_root, "*_balanced_*"));
        // Windows would reject exclusive access while a decoder still holds the input.
        using var exclusive = File.Open(input, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private ActionRequest Request(string input, string? intensity = null, IProgressReporter? reporter = null) =>
        new(input, _logger, reporter, intensity is null ? null : new Dictionary<string, string> { [BalanceVolumeSettings.OptionKey] = intensity });

    private string CreateWave(int seconds)
    {
        var input = Path.Combine(_root, "dialogues été.wav");
        using var writer = new WaveFileWriter(input, new WaveFormat(48000, 16, 1));
        for (var i = 0; i < 48000 * seconds; i++)
        {
            var amplitude = i < 48000 * seconds / 2 ? 0.02 : 0.8;
            writer.WriteSample((float)(amplitude * Math.Sin(2 * Math.PI * 440 * i / 48000)));
        }
        return input;
    }

    private static double Gap(string path)
    {
        using var reader = new WaveFileReader(path);
        var samples = reader.ToSampleProvider();
        var buffer = new float[48000];
        double quiet = 0, loud = 0;
        for (var second = 0; second < 36; second++)
        {
            var count = samples.Read(buffer, 0, buffer.Length);
            var sum = buffer.Take(count).Sum(sample => (double)sample * sample);
            if (second is >= 8 and < 12) quiet += sum;
            if (second is >= 26 and < 30) loud += sum;
        }
        return 10 * Math.Log10(loud / quiet);
    }

    private async Task<string> VideoHash(string input)
    {
        var result = await Runner.RunCaptureAsync(_tools.ResolveFfmpegPath(),
            ["-v", "error", "-i", input, "-map", "0:v:0", "-c", "copy", "-f", "streamhash", "-hash", "sha256", "-"], CancellationToken.None);
        Assert.True(result.Success, result.StandardError);
        return result.StandardOutput.Trim();
    }

    private sealed class CancelReporter(CancellationTokenSource cancel, bool currentItem) : IProgressReporter
    {
        private bool _processing;
        public bool IsCancellationRequested => false;
        public bool IsQueueItemRemovalRequested(string path) => false;
        public bool IsQueueItemCancellationRequested(string path) => currentItem && _processing;
        public void ReportQueue(IReadOnlyList<string> items) { }
        public void ReportQueueItem(string file, string state, string? message = null) { }
        public void ReportState(string state, string? message = null) { }
        public void ReportProgress(int value, string? file, string? action, string? message, string? etaText = null)
        {
            _processing = true;
            if (!currentItem) cancel.Cancel();
        }
    }
}
