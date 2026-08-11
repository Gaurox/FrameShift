using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using Xunit;

namespace FrameShift.Tests;

public sealed class JoinVideosActionIntegrationTests
{
    [Fact]
    public async Task ExecuteAsync_ClipsWithDifferentResolutionAndAudio_NormalizesAndJoins()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"frameshift_join_videos_normalize_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var logger = new AppLogger();
            var toolLocator = new ToolLocator();
            var ffmpegRunner = new FfmpegRunner(logger);
            var ffprobeRunner = new FfprobeRunner(logger);
            var ffmpegPath = toolLocator.ResolveFfmpegPath();

            var clipA = Path.Combine(tempRoot, "clip a.mp4");
            var clipB = Path.Combine(tempRoot, "clip b.mp4");

            // Different resolution defeats the direct-copy stream match and forces the
            // filter_complex normalization pipeline; one clip has no audio to exercise
            // the silence-synthesis branch too.
            await CreateSampleClipAsync(ffmpegRunner, ffmpegPath, clipA, "640x360", withAudio: true);
            await CreateSampleClipAsync(ffmpegRunner, ffmpegPath, clipB, "480x270", withAudio: false);

            var settings = new JoinVideosSettings { InputPaths = [clipA, clipB], Mode = JoinVideosMode.Auto };
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ActionOptionKeys.JoinVideosSettings] = settings.ToOptionPayload()
            };

            var action = new JoinVideosAction(ffmpegRunner, ffprobeRunner, toolLocator);
            var result = await action.ExecuteAsync(new ActionRequest(clipA, logger, null, options), CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));

            var probe = await ffprobeRunner.TryProbeJoinVideoAsync(toolLocator.ResolveFfprobePath(), result.OutputPath!, CancellationToken.None);
            Assert.NotNull(probe.Probe);
            Assert.True(probe.Probe!.HasVideo);
            Assert.True(probe.Probe.DurationSeconds > 1.5d);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static async Task CreateSampleClipAsync(FfmpegRunner ffmpegRunner, string ffmpegPath, string outputPath, string size, bool withAudio)
    {
        var arguments = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", $"color=c=blue:s={size}:r=25:d=1"
        };

        if (withAudio)
        {
            arguments.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:d=1", "-shortest"]);
        }

        arguments.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p"]);
        if (withAudio)
        {
            arguments.AddRange(["-c:a", "aac", "-b:a", "128k"]);
        }

        arguments.Add(outputPath);

        var result = await ffmpegRunner.RunAsync(
            ffmpegPath,
            arguments,
            TimeSpan.FromSeconds(1),
            progressReporter: null,
            currentFile: outputPath,
            currentAction: "Create join-videos sample",
            executionMode: "CPU",
            cancellationToken: CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath));
    }
}
