using System.Text.Json;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using Xunit;

namespace FrameShift.Tests;

public sealed class MediaTimelineRegressionTests
{
    [Fact]
    public async Task SmallVideoTransforms_CompleteAtDimensionsRejectedByNvenc()
    {
        using var fixture = new VideoLoopExportFixture();
        var source = Path.Combine(fixture.Root, "petite vidéo #.mp4");
        await fixture.RunAsync(["-f", "lavfi", "-i", "testsrc2=s=96x64:r=25:d=1", "-c:v", "libx264", source]);
        var transforms = new (IFrameShiftAction Action, IReadOnlyDictionary<string, string> Options, int Width, int Height)[]
        {
            (new ResizeVideoAction(fixture.Runner, fixture.Prober, fixture.Tools), new ResizeSettings(64, 48).ToOptions(), 64, 48),
            (new CropVideoAction(fixture.Runner, fixture.Prober, fixture.Tools), new VideoCropSettings(8, 8, 64, 48).ToOptions(), 64, 48),
            (new RotateFlipVideoAction(fixture.Runner, fixture.Prober, fixture.Tools),
                new RotateFlipSettings(RotateAngle.Cw90, false, false).ToOptions(), 64, 96)
        };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        foreach (var (action, options, width, height) in transforms)
        {
            var output = await Execute(action, source, options, deadline.Token);
            var probe = await fixture.ReadProbeAsync(output);
            Assert.Equal((width, height), (probe.VideoWidth, probe.VideoHeight));
            Assert.Equal(25, probe.EstimatedVideoFrameCount);
        }
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".frameshift-*"));
    }

    [Theory]
    [InlineData("cut")]
    [InlineData("speed-keep-pitch")]
    [InlineData("speed-change-pitch")]
    public async Task AudioTracks_AreKeptInOrderWithIndependentTiming(string operation)
    {
        using var fixture = new VideoLoopExportFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var source = Path.Combine(fixture.Root, "jeu et micro.mp4");
        await fixture.RunAsync([
            "-f", "lavfi", "-i", "color=s=64x64:r=25:d=4",
            "-f", "lavfi", "-i", @"aevalsrc=0.5*sin(2*PI*440*t)*between(t\,0.6\,0.8):s=48000:d=4",
            "-itsoffset", "0.7", "-f", "lavfi", "-i",
            @"aevalsrc=0.5*sin(2*PI*880*t)*between(t\,0.6\,0.8):s=44100:d=3.3",
            "-map", "0:v:0", "-map", "1:a:0", "-map", "2:a:0", "-c:v", "libx264", "-c:a", "aac", source
        ]);
        var isCut = operation == "cut";
        var action = isCut ? (IFrameShiftAction)new CutVideoAction(fixture.Runner, fixture.Prober, fixture.Tools)
            : new ChangeVideoSpeedAction(fixture.Runner, fixture.Prober, fixture.Tools);
        var options = isCut ? new CutVideoSettings(11, 45, 25).ToOptions()
            : new ChangeSpeedSettings(2, operation == "speed-keep-pitch").ToOptions();
        var output = await Execute(action, source, options, deadline.Token);
        var probe = await fixture.ReadProbeAsync(output);
        Assert.Equal(new[] { 48000, 44100 }, probe.Streams.Where(stream => stream.CodecType == "audio").Select(stream => stream.SampleRate));
        var first = await CutVideoActionTimingTests.ReadToneOnsetAsync(fixture, output, deadline.Token);
        var second = await CutVideoActionTimingTests.ReadToneOnsetAsync(fixture, output, deadline.Token, audioStream: 1);
        Assert.InRange(first - (isCut ? 0.2 : 0.3), -0.045, 0.045);
        Assert.InRange(second - (isCut ? 0.9 : 0.65), -0.045, 0.045);
        if (isCut)
        {
            var beforeMicro = await Cut(fixture, source, new CutVideoSettings(1, 10, 25).ToOptions());
            Assert.Equal(2, (await fixture.ReadProbeAsync(beforeMicro)).AudioCodecs.Count);
        }
    }

    [Theory]
    [InlineData(2.0, 1.0, 1.75)]
    [InlineData(0.5, 4.0, 7.0)]
    public async Task Speed_UpdatesChapterBoundaries(double factor, double secondStart, double secondEnd)
    {
        using var fixture = new VideoLoopExportFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var metadata = Path.Combine(fixture.Root, "chapitres.ffmetadata");
        await File.WriteAllTextAsync(metadata, ";FFMETADATA1\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=2000\ntitle=Intro\n" +
            "[CHAPTER]\nTIMEBASE=1/1000\nSTART=2000\nEND=3500\ntitle=Fin # été\n".Replace("#", "\\#"), deadline.Token);
        await fixture.RunAsync(["-f", "lavfi", "-i", "color=s=64x64:r=25:d=4",
            "-f", "ffmetadata", "-i", metadata, "-map", "0:v:0", "-map_chapters", "1", "-c:v", "libx264", fixture.Source]);
        var action = new ChangeVideoSpeedAction(fixture.Runner, fixture.Prober, fixture.Tools);
        var output = await Execute(action, fixture.Source, new ChangeSpeedSettings(factor, true).ToOptions(), deadline.Token);
        var probe = (await fixture.Prober.TryProbeMediaAsync(fixture.Tools.ResolveFfprobePath(), output,
            deadline.Token, includeChapters: true)).Probe!;
        Assert.Equal(2, probe.Chapters.Count);
        Assert.Equal(secondStart, probe.Chapters[0].End, 3);
        Assert.Equal(secondStart, probe.Chapters[1].Start, 3);
        Assert.Equal(secondEnd, probe.Chapters[1].End, 3);
        Assert.Contains("Fin # été", probe.Chapters[1].Tags.Values);
    }

    [Fact]
    public async Task WebmCut_LeavesOnlySubtitlesInTheSelection()
    {
        using var fixture = new VideoLoopExportFixture();
        var subtitles = Path.Combine(fixture.Root, "dialogues # été.srt");
        await File.WriteAllTextAsync(subtitles,
            "1\n00:00:00,200 --> 00:00:00,800\nEarly\n\n2\n00:00:08,000 --> 00:00:09,000\nLate\n");
        var source = Path.Combine(fixture.Root, "source.webm");
        await fixture.RunAsync(["-f", "lavfi", "-i", "color=s=64x64:r=25:d=10", "-i", subtitles,
            "-c:v", "libvpx-vp9", "-c:s", "webvtt", source]);

        var output = await Cut(fixture, source, new CutVideoSettings(1, 25, 25).ToOptions());
        var probe = await fixture.ReadProbeAsync(output);
        Assert.True(probe.HasSubtitles);
        Assert.Equal(1.0, probe.Duration!.Value.TotalSeconds, 3);
        var packets = await fixture.Prober.RunProbeAsync(fixture.Tools.ResolveFfprobePath(),
            ["-v", "error", "-select_streams", "s:0", "-show_packets",
                "-show_entries", "packet=pts_time,duration_time", "-of", "csv=p=0", output], CancellationToken.None);
        Assert.True(packets.Success, packets.StandardError);
        Assert.Equal("0.200000,0.600000", packets.Output.Trim());
    }

    [Fact]
    public async Task TimedCut_ExportsTheSameVideoStreamUsedForFrameNumbers()
    {
        using var fixture = new VideoLoopExportFixture();
        var source = Path.Combine(fixture.Root, "deux pistes.mp4");
        await fixture.RunAsync(["-f", "lavfi", "-i", "color=c=white:s=64x64:r=10:d=4",
            "-f", "lavfi", "-i", "color=c=black:s=128x128:r=25:d=4",
            "-map", "0:v:0", "-map", "1:v:0", "-disposition:v:0", "0", "-disposition:v:1", "default",
            "-c:v", "libx264", source]);

        var output = await Cut(fixture, source, new Dictionary<string, string>
        {
            [ActionOptionKeys.StartTime] = "2", [ActionOptionKeys.EndTime] = "2.5"
        });
        var probe = await fixture.ReadProbeAsync(output);
        Assert.Equal(64, probe.VideoWidth);
        Assert.Equal(5, probe.EstimatedVideoFrameCount);
        Assert.Equal(0.5, probe.Duration!.Value.TotalSeconds, 3);
        using var preview = await FrameShift.Windows.Helpers.PreviewFrameHelper.CaptureFrameAsync(
            fixture.Tools.ResolveFfmpegPath(), fixture.Runner, source, 2, "Cut preview", CancellationToken.None,
            useFirstVideoStream: true);
        Assert.Equal(64, preview.Width);
    }

    [Theory]
    [InlineData(".mkv", 0.5)]
    [InlineData(".mp4", 1.0)]
    public async Task Cut_ClipsAndRebasesChapters(string extension, double expectedChapterEnd)
    {
        using var fixture = new VideoLoopExportFixture();
        var metadata = Path.Combine(fixture.Root, "chapitres.ffmetadata");
        await File.WriteAllTextAsync(metadata, """
            ;FFMETADATA1
            [CHAPTER]
            TIMEBASE=1/1000
            START=0
            END=1500
            title=Introduction \# été
            [CHAPTER]
            TIMEBASE=1/1000
            START=8000
            END=9000
            title=Late
            """ + "\n");
        var source = Path.Combine(fixture.Root, "chapitres" + extension);
        await fixture.RunAsync(["-f", "lavfi", "-i", "color=s=64x64:r=25:d=10",
            "-f", "ffmetadata", "-i", metadata, "-map", "0:v:0", "-map_chapters", "1", "-c:v", "libx264", source]);

        var output = await Cut(fixture, source, new CutVideoSettings(26, 50, 25).ToOptions());
        var probe = (await fixture.Prober.TryProbeMediaAsync(fixture.Tools.ResolveFfprobePath(),
            output, CancellationToken.None, includeChapters: true)).Probe!;
        var chapter = Assert.Single(probe.Chapters);
        Assert.Equal(0, chapter.Start, 3);
        Assert.Equal(expectedChapterEnd, chapter.End, 3);
        Assert.Equal("Introduction # été", chapter.Tags.Single(tag =>
            tag.Key.Equals("title", StringComparison.OrdinalIgnoreCase)).Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FasterVideo_ScalesTheInitialAudioDelay(bool keepPitch)
    {
        using var fixture = new VideoLoopExportFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var source = Path.Combine(fixture.Root, "piste retardée.mp4");
        await fixture.RunAsync(["-f", "lavfi", "-i", "color=c=black:s=64x64:r=25:d=6",
            "-itsoffset", "1.5", "-f", "lavfi", "-i", @"aevalsrc=0.5*sin(2*PI*1000*t)*between(t\,1\,1.1):s=48000:d=4.5",
            "-vf", "drawbox=c=white:t=fill:enable='between(n,62,65)'", "-c:v", "libx264", "-c:a", "aac", source]);
        var action = new ChangeVideoSpeedAction(fixture.Runner, fixture.Prober, fixture.Tools);
        var result = await action.ExecuteAsync(new(source, new AppLogger(), null,
            new ChangeSpeedSettings(2, keepPitch).ToOptions()), deadline.Token);
        Assert.True(result.Success, result.Message);
        var output = Assert.IsType<string>(result.OutputPath);
        var frames = await new VideoLoopFrameReader(fixture.Runner, fixture.Tools.ResolveFfmpegPath(), output)
            .ReadAsync(0, 3.2, null, 200, deadline.Token);
        var flash = frames.First(frame => frame.Pixels[0] > 210).Time;
        var onset = await CutVideoActionTimingTests.ReadToneOnsetAsync(fixture, output, deadline.Token);
        Assert.InRange(flash, 1.22, 1.26);
        Assert.InRange(onset - flash, -0.04, 0.04);
    }

    [Fact]
    public async Task Cut_CompletesWithTheAacTrackThatStalled()
    {
        using var fixture = new VideoLoopExportFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await fixture.RunAsync(["-f", "lavfi", "-i", "color=c=white:s=160x120:r=25:d=3",
            "-f", "lavfi", "-i", @"aevalsrc=0.5*sin(2*PI*440*t)*between(t\,0.6\,0.8):s=48000:d=3",
            "-c:v", "libx264", "-c:a", "aac", fixture.Source]);

        var action = new CutVideoAction(fixture.Runner, fixture.Prober, fixture.Tools);
        var output = await Execute(action, fixture.Source, new CutVideoSettings(1, 25, 25).ToOptions(), deadline.Token);
        var probe = await fixture.ReadProbeAsync(output);
        Assert.Equal("aac", Assert.Single(probe.AudioCodecs));
        Assert.InRange(probe.Duration!.Value.TotalSeconds, 0.98, 1.05);
        Assert.InRange(await CutVideoActionTimingTests.ReadToneOnsetAsync(fixture, output, deadline.Token), 0.58, 0.65);
    }

    [Theory]
    [InlineData(true, 2.0)]
    [InlineData(false, 2.0)]
    [InlineData(true, 0.5)]
    [InlineData(false, 0.5)]
    public async Task Speed_ScalesAnInternalTimestampGap(bool keepPitch, double factor)
    {
        using var fixture = new VideoLoopExportFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await fixture.RunAsync(["-f", "lavfi", "-i", "color=c=black:s=96x64:r=25:d=4",
            "-f", "lavfi", "-i", @"aevalsrc=0.5*sin(2*PI*1000*t)*between(t\,2.5\,2.7):s=48000:d=4",
            "-vf", "drawbox=c=white:t=fill:enable='between(n,62,65)'", "-af", "aselect='not(between(t,1,2))'",
            "-c:v", "libx264", "-c:a", "aac", fixture.Source]);
        var action = new ChangeVideoSpeedAction(fixture.Runner, fixture.Prober, fixture.Tools);
        var output = await Execute(action, fixture.Source, new ChangeSpeedSettings(factor, keepPitch).ToOptions(), deadline.Token);
        var frames = await new VideoLoopFrameReader(fixture.Runner, fixture.Tools.ResolveFfmpegPath(), output)
            .ReadAsync(0, 4.1 / factor, null, 110, deadline.Token);
        var flash = frames.First(frame => frame.Pixels[0] > 210).Time;
        var onset = await CutVideoActionTimingTests.ReadToneOnsetAsync(fixture, output, deadline.Token);
        Assert.InRange(flash * factor, 2.475, 2.485);
        Assert.InRange(onset * factor, 2.455, 2.545);
    }

    [Theory]
    [InlineData(".mkv", "libx264", "srt")]
    [InlineData(".webm", "libvpx-vp9", "webvtt")]
    [InlineData(".mp4", "libx264", "mov_text")]
    [InlineData(".mov", "libx264", "mov_text")]
    [InlineData(".m4v", "libx264", "mov_text")]
    public async Task CutAndSpeed_KeepBothSubtitleTracksAndUpdateCueTiming(string extension, string videoCodec, string subtitleCodec)
    {
        using var fixture = new VideoLoopExportFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var source = Path.Combine(fixture.Root, "dialogues # été" + extension);
        var english = Path.Combine(fixture.Root, "anglais.srt");
        var french = Path.Combine(fixture.Root, "français.srt");
        await File.WriteAllTextAsync(english, "1\n00:00:01,200 --> 00:00:01,600\nEnglish\n", deadline.Token);
        await File.WriteAllTextAsync(french, "1\n00:00:01,600 --> 00:00:02,400\nFrançais\n", deadline.Token);
        var mux = extension == ".m4v" ? new[] { "-f", "mp4" } : Array.Empty<string>();
        await fixture.RunAsync(["-f", "lavfi", "-i", "color=s=160x120:r=25:d=4", "-i", english, "-i", french,
            "-map", "0:v", "-map", "1:s", "-map", "2:s", "-c:v", videoCodec, "-c:s", subtitleCodec,
            "-metadata:s:s:0", "language=eng", "-metadata:s:s:1", "language=fra", "-disposition:s:1", "forced", ..mux, source]);

        var cut = await Cut(fixture, source, new CutVideoSettings(26, 50, 25).ToOptions());
        var speedAction = new ChangeVideoSpeedAction(fixture.Runner, fixture.Prober, fixture.Tools);
        var speed = await Execute(speedAction, source, new ChangeSpeedSettings(2, true).ToOptions(), deadline.Token);
        foreach (var (path, expected) in new[]
        {
            (cut, new[] { (0.2, 0.4), (0.6, 0.4) }),
            (speed, new[] { (0.6, 0.2), (0.8, 0.4) })
        })
        {
            Assert.Equal(2, (await fixture.ReadProbeAsync(path)).SubtitleCodecs.Count);
            for (var i = 0; i < expected.Length; i++)
            {
                var packets = await fixture.Prober.RunProbeAsync(fixture.Tools.ResolveFfprobePath(),
                    ["-v", "error", "-select_streams", $"s:{i}", "-show_packets",
                        "-show_entries", "packet=pts_time,duration_time,size", "-of", "json", path], deadline.Token);
                Assert.True(packets.Success, packets.StandardError);
                using var json = JsonDocument.Parse(packets.Output);
                // MOV-family muxers also write two-byte mov_text packets to clear displayed text.
                var cue = Assert.Single(json.RootElement.GetProperty("packets").EnumerateArray(), packet =>
                    subtitleCodec != "mov_text" || int.Parse(packet.GetProperty("size").ToString()) > 2);
                Assert.Equal(expected[i].Item1, ReadPacketTime(cue, "pts_time"), 3);
                Assert.Equal(expected[i].Item2, ReadPacketTime(cue, "duration_time"), 3);
            }
        }
    }

    [Fact]
    public async Task RemoveAudio_CopiesAllOtherTracksAndSpeedKeepsBothVideos()
    {
        using var fixture = new VideoLoopExportFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var subtitles = Path.Combine(fixture.Root, "dialogue.srt");
        var attachment = Path.Combine(fixture.Root, "notice.txt");
        await File.WriteAllTextAsync(subtitles, "1\n00:00:00,200 --> 00:00:00,800\nBonjour\n", deadline.Token);
        await File.WriteAllTextAsync(attachment, "notice", deadline.Token);
        await fixture.RunAsync(["-f", "lavfi", "-i", "color=s=160x120:r=25:d=3",
            "-f", "lavfi", "-i", "testsrc2=s=320x240:r=25:d=3", "-f", "lavfi", "-i", "sine=d=3", "-i", subtitles,
            "-map", "0:v", "-map", "1:v", "-map", "2:a", "-map", "3:s", "-map", "3:s", "-c:v", "libx264", "-c:a", "aac",
            "-disposition:s:1", "forced", "-metadata:s:s:1", "language=fra", "-c:s", "srt",
            "-attach", attachment, "-metadata:s:t:0", "mimetype=text/plain", fixture.Source]);

        var remove = new RemoveAudioAction(fixture.Runner, fixture.Prober, fixture.Tools);
        var silent = await Execute(remove, fixture.Source, new Dictionary<string, string>(), deadline.Token);
        var speedAction = new ChangeVideoSpeedAction(fixture.Runner, fixture.Prober, fixture.Tools);
        var faster = await Execute(speedAction, fixture.Source, new ChangeSpeedSettings(2, true).ToOptions(), deadline.Token);
        foreach (var path in new[] { silent, faster })
        {
            var probe = await fixture.ReadProbeAsync(path);
            Assert.Equal(path == faster, probe.HasAudio);
            Assert.InRange(probe.Duration!.Value.TotalSeconds, path == faster ? 1.45 : 2.95, path == faster ? 1.55 : 3.05);
            Assert.Equal(2, probe.Streams.Count(stream => stream.CodecType == "video"));
            Assert.Equal(new[] { "subrip", "subrip" }, probe.SubtitleCodecs);
            Assert.Single(probe.Streams, stream => stream.CodecType == "attachment");
            var detail = await fixture.Prober.RunProbeAsync(fixture.Tools.ResolveFfprobePath(),
                ["-v", "error", "-select_streams", "s:1", "-show_entries", "stream_disposition=forced:stream_tags=language",
                    "-of", "default=nw=1", path], deadline.Token);
            Assert.True(detail.Success, detail.StandardError);
            Assert.Contains("DISPOSITION:forced=1", detail.Output);
            Assert.Contains("TAG:language=fra", detail.Output);
        }
    }

    [Fact]
    public async Task AttachedCover_IsNotATimedVideoTrack()
    {
        using var fixture = new VideoLoopExportFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cover = Path.Combine(fixture.Root, "pochette.jpg");
        var audioOnly = Path.Combine(fixture.Root, "audio et pochette.mp4");
        var withVideo = Path.Combine(fixture.Root, "film et pochette.mp4");
        await fixture.RunAsync(["-f", "lavfi", "-i", "color=c=red:s=64x64", "-frames:v", "1", "-update", "1", cover]);
        await fixture.RunAsync(["-i", cover, "-f", "lavfi", "-i", "sine=d=2", "-map", "0:v", "-map", "1:a",
            "-c:v", "copy", "-disposition:v:0", "attached_pic", "-c:a", "aac", audioOnly]);
        var probe = await fixture.ReadProbeAsync(audioOnly);
        Assert.True(probe.HasAudio);
        Assert.False(probe.HasVideo);
        Assert.Contains(probe.Streams, stream => stream.CodecName == "mjpeg");
        var (joinProbe, _) = await fixture.Prober.TryProbeJoinVideoAsync(fixture.Tools.ResolveFfprobePath(), audioOnly, deadline.Token);
        Assert.False(joinProbe!.HasVideo);

        var cut = new CutVideoAction(fixture.Runner, fixture.Prober, fixture.Tools);
        var speed = new ChangeVideoSpeedAction(fixture.Runner, fixture.Prober, fixture.Tools);
        var remove = new RemoveAudioAction(fixture.Runner, fixture.Prober, fixture.Tools);
        var options = new CutVideoSettings(1, 25, 25).ToOptions();
        foreach (var (action, settings) in new (IFrameShiftAction, IReadOnlyDictionary<string, string>)[]
        {
            (cut, options), (speed, new ChangeSpeedSettings(2, true).ToOptions()), (remove, new Dictionary<string, string>())
        })
        {
            var result = await action.ExecuteAsync(new(audioOnly, new AppLogger(), null, settings), deadline.Token);
            Assert.False(result.Success);
            Assert.Equal(MediaActionMessages.MissingVideoTrack(), result.Message);
            Assert.Null(result.OutputPath);
        }

        await fixture.RunAsync(["-i", audioOnly, "-f", "lavfi", "-i", "color=s=160x120:r=25:d=2",
            "-map", "0:v", "-map", "1:v", "-map", "0:a", "-c", "copy", "-c:v:1", "libx264", withVideo]);
        Assert.Equal(160, (await fixture.ReadProbeAsync(withVideo)).VideoWidth);
        var output = await Execute(cut, withVideo, options, deadline.Token);
        Assert.Equal(25, (await fixture.ReadProbeAsync(output)).EstimatedVideoFrameCount);
        var silent = await Execute(remove, withVideo, new Dictionary<string, string>(), deadline.Token);
        Assert.Contains((await fixture.ReadProbeAsync(silent)).Streams, stream => stream.CodecName == "mjpeg");
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".frameshift-*"));
    }

    private static double ReadPacketTime(JsonElement packet, string property) =>
        double.Parse(packet.GetProperty(property).GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<string> Cut(VideoLoopExportFixture fixture, string source,
        IReadOnlyDictionary<string, string> options)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var action = new CutVideoAction(fixture.Runner, fixture.Prober, fixture.Tools);
        return await Execute(action, source, options, deadline.Token);
    }

    private static async Task<string> Execute(IFrameShiftAction action, string source,
        IReadOnlyDictionary<string, string> options, CancellationToken token)
    {
        var result = await action.ExecuteAsync(new(source, new AppLogger(), null, options), token);
        Assert.True(result.Success, result.Message);
        return Assert.IsType<string>(result.OutputPath);
    }
}
