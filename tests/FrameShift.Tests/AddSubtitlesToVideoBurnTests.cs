using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.AI.CreateSubtitles;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using Xunit;

namespace FrameShift.Tests;

public sealed class AddSubtitlesToVideoBurnTests
{
    [Theory]
    [InlineData("srt")]
    [InlineData("ass")]
    [InlineData("project")]
    public async Task BurnIntoVideo_WithSupportedSubtitleInputs_CreatesOutput(string inputKind)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"frameshift_add_subtitles_burn_{inputKind}_{Guid.NewGuid():N}", "vidéo été [test], spécial");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var logger = new AppLogger();
            var toolLocator = new ToolLocator();
            var ffmpegRunner = new FfmpegRunner(logger);
            var ffprobeRunner = new FfprobeRunner(logger);
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            var videoPath = Path.Combine(tempRoot, "source vidéo clip.mp4");

            var createVideoResult = await ffmpegRunner.RunAsync(
                ffmpegPath,
                new[]
                {
                    "-hide_banner", "-loglevel", "error", "-y",
                    "-f", "lavfi", "-i", "color=c=black:s=640x360:r=25:d=2",
                    "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:d=2",
                    "-shortest",
                    "-c:v", "libx264",
                    "-pix_fmt", "yuv420p",
                    "-c:a", "aac",
                    "-b:a", "160k",
                    videoPath
                },
                TimeSpan.FromSeconds(2),
                progressReporter: null,
                currentFile: videoPath,
                currentAction: "Create burn sample",
                executionMode: "CPU",
                cancellationToken: CancellationToken.None);

            Assert.Equal(0, createVideoResult.ExitCode);
            Assert.True(File.Exists(videoPath));

            var subtitlePath = await CreateSubtitleInputAsync(tempRoot, inputKind);
            var action = new AddSubtitlesToVideoAction(ffmpegRunner, ffprobeRunner, toolLocator);
            var result = await action.ExecuteAsync(
                new ActionRequest(
                    videoPath,
                    logger,
                    null,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [ActionOptionKeys.SubtitleMode] = "burn",
                        [ActionOptionKeys.SubtitleFilePath] = subtitlePath
                    }),
                CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath!));

            var probeAttempt = await ffprobeRunner.TryProbeMediaAsync(toolLocator.ResolveFfprobePath(), result.OutputPath!, CancellationToken.None);
            Assert.NotNull(probeAttempt.Probe);
            Assert.True(probeAttempt.Probe!.HasVideo);
            Assert.True(probeAttempt.Probe.HasAudio);
            Assert.EndsWith(".mp4", result.OutputPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SubtitleSourceLoader_FromSrt_GeneratesVideoSizedAss()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"frameshift_subtitle_loader_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var subtitlePath = Path.Combine(tempRoot, "track.srt");
            await File.WriteAllTextAsync(
                subtitlePath,
                "1\r\n00:00:00,000 --> 00:00:01,200\r\nBonjour monde\r\n\r\n",
                CancellationToken.None);

            var probe = new MediaProbeResult(
                TimeSpan.FromSeconds(2),
                HasAudio: true,
                HasVideo: true,
                VideoWidth: 720,
                VideoHeight: 1280,
                VideoCodec: "h264",
                VideoFrameRate: 30d,
                EstimatedVideoFrameCount: 60,
                AudioCodecs: ["aac"],
                SubtitleCodecs: Array.Empty<string>(),
                Streams: Array.Empty<MediaStreamInfo>())
            {
                RotationDegrees = 90
            };

            var burnSettings = new AddSubtitlesToVideoBurnSettings(
                CreateSubtitlesAssPreset.WordHighlight,
                "Tahoma",
                44,
                "#FFEECC",
                "#00FF00",
                "#112233",
                "#334455",
                3.5d,
                1.2d,
                AddSubtitlesToVideoVerticalAlignment.Top,
                96);
            var prepared = await AddSubtitlesToVideoSubtitleSourceLoader.PrepareAssInputAsync(subtitlePath, probe, burnSettings, CancellationToken.None);

            Assert.True(prepared.DeleteAfterUse);
            var ass = await File.ReadAllTextAsync(prepared.AssFilePath, CancellationToken.None);
            Assert.Contains("PlayResX: 1280", ass, StringComparison.Ordinal);
            Assert.Contains("PlayResY: 720", ass, StringComparison.Ordinal);
            Assert.Contains("Style: Default,Tahoma,44,&H00CCEEFF,&H0000FF00,&H00332211,&H64554433,0,0,0,0,100,100,0,0,1,3.5,1.2,8,90,90,96,1", ass, StringComparison.Ordinal);
            Assert.DoesNotContain("{\\c&H0000FF00&\\b1}", ass, StringComparison.Ordinal);

            ConversionActionHelper.DeleteIfExists(prepared.AssFilePath);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SubtitleSourceLoader_FromAss_CopiesToTemporaryWorkingPath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"frameshift_subtitle_loader_ass_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var subtitlePath = Path.Combine(tempRoot, "piste été [final].ass");
            var assText = """
[Script Info]
Title: Unicode Test
ScriptType: v4.00+

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Arial,28,&H00FFFFFF,&H000000FF,&H00000000,&H64000000,0,0,0,0,100,100,0,0,1,2,1,2,40,40,30,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:00.00,0:00:01.20,Default,,0,0,0,,مرحبا FrameShift
""";
            await File.WriteAllTextAsync(subtitlePath, assText, CancellationToken.None);

            var prepared = await AddSubtitlesToVideoSubtitleSourceLoader.PrepareAssInputAsync(
                subtitlePath,
                CreateProbe(),
                AddSubtitlesToVideoBurnSettings.Default,
                CancellationToken.None);

            Assert.Equal(AddSubtitlesToVideoSubtitleSourceKind.Ass, prepared.SourceKind);
            Assert.True(prepared.DeleteAfterUse);
            Assert.NotEqual(subtitlePath, prepared.AssFilePath);
            Assert.EndsWith(".ass", prepared.AssFilePath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(assText, await File.ReadAllTextAsync(prepared.AssFilePath, CancellationToken.None));

            ConversionActionHelper.DeleteIfExists(prepared.AssFilePath);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SubtitleSourceLoader_ReadsWindowsDefaultEncodedSrt()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"frameshift_subtitle_loader_ansi_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var subtitlePath = Path.Combine(tempRoot, "track.srt");
            var srtText = "1\r\n00:00:00,000 --> 00:00:01,000\r\nété déjà\r\n\r\n";
            await File.WriteAllBytesAsync(subtitlePath, Encoding.Default.GetBytes(srtText), CancellationToken.None);

            var prepared = await AddSubtitlesToVideoSubtitleSourceLoader.PrepareAssInputAsync(
                subtitlePath,
                CreateProbe(),
                AddSubtitlesToVideoBurnSettings.Default,
                CancellationToken.None);

            var ass = await File.ReadAllTextAsync(prepared.AssFilePath, CancellationToken.None);
            Assert.Contains("été déjà", ass, StringComparison.Ordinal);

            ConversionActionHelper.DeleteIfExists(prepared.AssFilePath);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SubtitleSourceLoader_PreservesUtf8RtlText()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"frameshift_subtitle_loader_rtl_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var subtitlePath = Path.Combine(tempRoot, "track.srt");
            var srtText = "1\r\n00:00:00,000 --> 00:00:01,000\r\nمرحبا بالعالم\r\n\r\n";
            await File.WriteAllTextAsync(subtitlePath, srtText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), CancellationToken.None);

            var prepared = await AddSubtitlesToVideoSubtitleSourceLoader.PrepareAssInputAsync(
                subtitlePath,
                CreateProbe(),
                AddSubtitlesToVideoBurnSettings.Default,
                CancellationToken.None);

            var ass = await File.ReadAllTextAsync(prepared.AssFilePath, CancellationToken.None);
            Assert.Contains("مرحبا بالعالم", ass, StringComparison.Ordinal);

            ConversionActionHelper.DeleteIfExists(prepared.AssFilePath);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static async Task<string> CreateSubtitleInputAsync(string root, string inputKind)
    {
        return inputKind switch
        {
            "ass" => await CreateAssInputAsync(root),
            "project" => await CreateProjectInputAsync(root),
            _ => await CreateSrtInputAsync(root)
        };
    }

    private static async Task<string> CreateSrtInputAsync(string root)
    {
        var path = Path.Combine(root, "track.srt");
        await File.WriteAllTextAsync(
            path,
            "1\r\n00:00:00,000 --> 00:00:01,200\r\nBonjour monde\r\n\r\n2\r\n00:00:01,200 --> 00:00:01,900\r\nFrameShift\r\n\r\n",
            CancellationToken.None);
        return path;
    }

    private static async Task<string> CreateAssInputAsync(string root)
    {
        var path = Path.Combine(root, "track.ass");
        var content = """
[Script Info]
Title: Test
ScriptType: v4.00+
WrapStyle: 0
ScaledBorderAndShadow: yes
PlayResX: 640
PlayResY: 360

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Arial,32,&H00FFFFFF,&H000000FF,&H00000000,&H64000000,0,0,0,0,100,100,0,0,1,2,1,2,40,40,30,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:00.00,0:00:01.20,Default,,0,0,0,,Bonjour monde
Dialogue: 0,0:00:01.20,0:00:01.90,Default,,0,0,0,,FrameShift
""";
        await File.WriteAllTextAsync(path, content, CancellationToken.None);
        return path;
    }

    private static async Task<string> CreateProjectInputAsync(string root)
    {
        var path = Path.Combine(root, $"track{CreateSubtitlesProjectSerializer.FileExtension}");
        var project = new SubtitleProject(
            TimeSpan.FromSeconds(2),
            new[]
            {
                new SubtitleSegment(
                    1,
                    "Bonjour monde",
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1.2d),
                    false,
                    new[]
                    {
                        new SubtitleWord("Bonjour", "bonjour", 0.00d, 0.60d, false),
                        new SubtitleWord("monde", "monde", 0.60d, 1.20d, false)
                    }),
                new SubtitleSegment(
                    2,
                    "FrameShift",
                    TimeSpan.FromSeconds(1.2d),
                    TimeSpan.FromSeconds(1.9d),
                    false,
                    new[]
                    {
                        new SubtitleWord("FrameShift", "frameshift", 1.20d, 1.90d, false)
                    })
            });

        await File.WriteAllTextAsync(path, CreateSubtitlesProjectSerializer.Serialize(project), CancellationToken.None);
        return path;
    }

    private static MediaProbeResult CreateProbe()
    {
        return new MediaProbeResult(
            TimeSpan.FromSeconds(2),
            HasAudio: true,
            HasVideo: true,
            VideoWidth: 1280,
            VideoHeight: 720,
            VideoCodec: "h264",
            VideoFrameRate: 30d,
            EstimatedVideoFrameCount: 60,
            AudioCodecs: ["aac"],
            SubtitleCodecs: Array.Empty<string>(),
            Streams: Array.Empty<MediaStreamInfo>());
    }
}
