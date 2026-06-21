using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.AI;
using FrameShift.Core.AI.CreateSubtitles;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using Xunit;

namespace FrameShift.Tests;

public sealed class CreateSubtitlesTests
{
    [Fact]
    public void Segmenter_BreaksOnPunctuationAndSilence()
    {
        var words = new[]
        {
            new CreateSubtitlesWordTiming("Hello", "hello", 0.00d),
            new CreateSubtitlesWordTiming("world.", "world.", 0.42d),
            new CreateSubtitlesWordTiming("This", "this", 1.95d),
            new CreateSubtitlesWordTiming("is", "is", 2.20d),
            new CreateSubtitlesWordTiming("FrameShift!", "frameshift!", 2.48d)
        };

        var cues = CreateSubtitlesSegmenter.BuildCues(words, TimeSpan.FromSeconds(4));

        Assert.Equal(2, cues.Count);
        Assert.Equal("Hello world.", cues[0].Text);
        Assert.Equal("This is FrameShift!", cues[1].Text);
        Assert.True(cues[0].End < cues[1].Start);
    }

    [Fact]
    public async Task Audio_And_Video_Actions_Produce_Equivalent_Subtitle_Text()
    {
        var repoRoot = GetRepositoryRoot();
        var sampleAudio = Path.Combine(repoRoot, "scratch", "WhisperBaseOnnxSpike", "samples", "mixed_fr_en_16k.wav");
        var modelSourceDir = Path.Combine(repoRoot, "scratch", "WhisperBaseOnnxSpike", "export-control");

        if (!File.Exists(sampleAudio) || !File.Exists(Path.Combine(modelSourceDir, "base-encoder.onnx")))
            return; // integration test — requires local dev assets in scratch/

        var tempRoot = Path.Combine(Path.GetTempPath(), $"frameshift_subtitles_tests_{Guid.NewGuid():N}_é_日本語");
        Directory.CreateDirectory(tempRoot);

        var modelsRoot = Path.Combine(tempRoot, "models space");
        Directory.CreateDirectory(modelsRoot);
        var stagedModelDir = Path.Combine(modelsRoot, "whisper-base-onnx");
        Directory.CreateDirectory(stagedModelDir);
        foreach (var fileName in new[] { "base-encoder.onnx", "base-decoder.onnx", "base-tokens.txt" })
        {
            File.Copy(Path.Combine(modelSourceDir, fileName), Path.Combine(stagedModelDir, fileName), overwrite: true);
        }

        var previousSettings = File.Exists(AiModelSettings.ConfigFilePath)
            ? File.ReadAllText(AiModelSettings.ConfigFilePath)
            : null;

        try
        {
            var settings = new AiModelSettings { ModelsDirectory = modelsRoot };
            settings.Save();
            AiModelStorage.InvalidateCache();

            var audioCopy = Path.Combine(tempRoot, "bonjour à tous.wav");
            File.Copy(sampleAudio, audioCopy, overwrite: true);

            var logger = new AppLogger();
            var toolLocator = new ToolLocator();
            var ffmpegRunner = new FfmpegRunner(logger);
            var ffprobeRunner = new FfprobeRunner(logger);
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            var videoCopy = Path.Combine(tempRoot, "bonjour à tous vidéo.mkv");

            var videoArgs = new[]
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-f", "lavfi", "-i", "color=c=black:s=640x360:r=25:d=20",
                "-i", audioCopy,
                "-shortest",
                "-c:v", "libx264",
                "-pix_fmt", "yuv420p",
                "-c:a", "pcm_s16le",
                videoCopy
            };

            var createVideoResult = await ffmpegRunner.RunAsync(
                ffmpegPath,
                videoArgs,
                TimeSpan.FromSeconds(20),
                progressReporter: null,
                currentFile: videoCopy,
                currentAction: "Create test video",
                executionMode: "CPU",
                cancellationToken: CancellationToken.None);

            Assert.Equal(0, createVideoResult.ExitCode);
            Assert.True(File.Exists(videoCopy), "The test video was not created.");

            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ActionOptionKeys.SubtitlesModel] = "whisper-base"
            };

            var audioAction = new CreateSubtitlesAction(CreateSubtitlesSourceKind.Audio, ffmpegRunner, ffprobeRunner, toolLocator);
            var videoAction = new CreateSubtitlesAction(CreateSubtitlesSourceKind.Video, ffmpegRunner, ffprobeRunner, toolLocator);

            var audioResult = await audioAction.ExecuteAsync(
                new ActionRequest(audioCopy, logger, null, options),
                CancellationToken.None);
            var videoResult = await videoAction.ExecuteAsync(
                new ActionRequest(videoCopy, logger, null, options),
                CancellationToken.None);

            Assert.True(audioResult.Success, audioResult.Message);
            Assert.True(videoResult.Success, videoResult.Message);
            Assert.NotNull(audioResult.OutputPath);
            Assert.NotNull(videoResult.OutputPath);
            Assert.True(File.Exists(audioResult.OutputPath!));
            Assert.True(File.Exists(videoResult.OutputPath!));

            var audioSrt = await File.ReadAllTextAsync(audioResult.OutputPath!);
            var videoSrt = await File.ReadAllTextAsync(videoResult.OutputPath!);

            var normalizedAudio = NormalizeSrtText(audioSrt);
            var normalizedVideo = NormalizeSrtText(videoSrt);

            Assert.Equal(normalizedAudio, normalizedVideo);
            Assert.Contains("Bonjour à tous", normalizedAudio, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (previousSettings is null)
            {
                if (File.Exists(AiModelSettings.ConfigFilePath))
                {
                    File.Delete(AiModelSettings.ConfigFilePath);
                }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AiModelSettings.ConfigFilePath)!);
                File.WriteAllText(AiModelSettings.ConfigFilePath, previousSettings);
            }

            AiModelStorage.InvalidateCache();

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Long_Audio_Over_Thirty_Seconds_Produces_Subtitles()
    {
        var repoRoot = GetRepositoryRoot();
        var sampleAudio = Path.Combine(repoRoot, "scratch", "WhisperBaseOnnxSpike", "samples", "mixed_fr_en_long.wav");
        var modelSourceDir = Path.Combine(repoRoot, "scratch", "WhisperBaseOnnxSpike", "export-control");
        if (!File.Exists(sampleAudio))
            return; // integration test — requires local dev assets in scratch/

        var tempRoot = Path.Combine(Path.GetTempPath(), $"frameshift_subtitles_long_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var modelsRoot = Path.Combine(tempRoot, "models");
        Directory.CreateDirectory(Path.Combine(modelsRoot, "whisper-base-onnx"));
        foreach (var fileName in new[] { "base-encoder.onnx", "base-decoder.onnx", "base-tokens.txt" })
        {
            File.Copy(Path.Combine(modelSourceDir, fileName), Path.Combine(modelsRoot, "whisper-base-onnx", fileName), overwrite: true);
        }

        var previousSettings = File.Exists(AiModelSettings.ConfigFilePath)
            ? File.ReadAllText(AiModelSettings.ConfigFilePath)
            : null;

        try
        {
            new AiModelSettings { ModelsDirectory = modelsRoot }.Save();
            AiModelStorage.InvalidateCache();

            var audioCopy = Path.Combine(tempRoot, "long sample.wav");
            File.Copy(sampleAudio, audioCopy, overwrite: true);

            var logger = new AppLogger();
            var action = new CreateSubtitlesAction(
                CreateSubtitlesSourceKind.Audio,
                new FfmpegRunner(logger),
                new FfprobeRunner(logger),
                new ToolLocator());

            var result = await action.ExecuteAsync(
                new ActionRequest(
                    audioCopy,
                    logger,
                    null,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [ActionOptionKeys.SubtitlesModel] = "whisper-base"
                    }),
                CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath!));
            var srt = await File.ReadAllTextAsync(result.OutputPath!);
            Assert.Contains("-->", srt, StringComparison.Ordinal);
        }
        finally
        {
            RestoreSettings(previousSettings);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Long_Audio_Can_Be_Canceled_Between_Windows()
    {
        var repoRoot = GetRepositoryRoot();
        var sampleAudio = Path.Combine(repoRoot, "scratch", "WhisperBaseOnnxSpike", "samples", "mixed_fr_en_long.wav");
        var modelSourceDir = Path.Combine(repoRoot, "scratch", "WhisperBaseOnnxSpike", "export-control");
        if (!File.Exists(sampleAudio))
            return; // integration test — requires local dev assets in scratch/

        var tempRoot = Path.Combine(Path.GetTempPath(), $"frameshift_subtitles_cancel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var modelsRoot = Path.Combine(tempRoot, "models");
        Directory.CreateDirectory(Path.Combine(modelsRoot, "whisper-base-onnx"));
        foreach (var fileName in new[] { "base-encoder.onnx", "base-decoder.onnx", "base-tokens.txt" })
        {
            File.Copy(Path.Combine(modelSourceDir, fileName), Path.Combine(modelsRoot, "whisper-base-onnx", fileName), overwrite: true);
        }

        var previousSettings = File.Exists(AiModelSettings.ConfigFilePath)
            ? File.ReadAllText(AiModelSettings.ConfigFilePath)
            : null;

        try
        {
            new AiModelSettings { ModelsDirectory = modelsRoot }.Save();
            AiModelStorage.InvalidateCache();

            var logger = new AppLogger();
            var toolLocator = new ToolLocator();
            var ffmpegRunner = new FfmpegRunner(logger);
            var ffprobeRunner = new FfprobeRunner(logger);
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            var concatenatedAudio = Path.Combine(tempRoot, "very long sample.wav");

            var concatResult = await ffmpegRunner.RunAsync(
                ffmpegPath,
                new[]
                {
                    "-hide_banner", "-loglevel", "error", "-y",
                    "-stream_loop", "1", "-i", sampleAudio,
                    "-c:a", "pcm_s16le",
                    "-t", "70",
                    concatenatedAudio
                },
                TimeSpan.FromSeconds(70),
                progressReporter: null,
                currentFile: concatenatedAudio,
                currentAction: "Create long cancel sample",
                executionMode: "CPU",
                cancellationToken: CancellationToken.None);

            Assert.Equal(0, concatResult.ExitCode);
            Assert.True(File.Exists(concatenatedAudio));

            var action = new CreateSubtitlesAction(
                CreateSubtitlesSourceKind.Audio,
                ffmpegRunner,
                ffprobeRunner,
                toolLocator);

            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.CancelAfter(TimeSpan.FromMilliseconds(1200));

            var result = await action.ExecuteAsync(
                new ActionRequest(
                    concatenatedAudio,
                    logger,
                    null,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [ActionOptionKeys.SubtitlesModel] = "whisper-base"
                    }),
                cancellationSource.Token);

            Assert.True(result.Canceled, result.Message);
            Assert.False(result.Success);
            Assert.True(string.IsNullOrWhiteSpace(result.OutputPath) || !File.Exists(result.OutputPath));
        }
        finally
        {
            RestoreSettings(previousSettings);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Video_Without_Audio_Track_Fails_Cleanly()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"frameshift_subtitles_noaudio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var logger = new AppLogger();
            var ffmpegRunner = new FfmpegRunner(logger);
            var ffprobeRunner = new FfprobeRunner(logger);
            var toolLocator = new ToolLocator();
            var ffmpegPath = toolLocator.ResolveFfmpegPath();
            var videoPath = Path.Combine(tempRoot, "silent video.mp4");

            var createVideoResult = await ffmpegRunner.RunAsync(
                ffmpegPath,
                new[]
                {
                    "-hide_banner", "-loglevel", "error", "-y",
                    "-f", "lavfi", "-i", "color=c=black:s=320x180:r=25:d=4",
                    "-c:v", "libx264",
                    "-pix_fmt", "yuv420p",
                    videoPath
                },
                TimeSpan.FromSeconds(4),
                progressReporter: null,
                currentFile: videoPath,
                currentAction: "Create silent video",
                executionMode: "CPU",
                cancellationToken: CancellationToken.None);

            Assert.Equal(0, createVideoResult.ExitCode);

            var action = new CreateSubtitlesAction(CreateSubtitlesSourceKind.Video, ffmpegRunner, ffprobeRunner, toolLocator);
            var result = await action.ExecuteAsync(
                new ActionRequest(
                    videoPath,
                    logger,
                    null,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [ActionOptionKeys.SubtitlesModel] = "whisper-base"
                    }),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(MediaActionMessages.MissingAudioTrack(), result.Message);
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
    public async Task Corrupted_Audio_File_Fails_Cleanly()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"frameshift_subtitles_corrupt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var logger = new AppLogger();
            var corruptPath = Path.Combine(tempRoot, "broken.wav");
            await File.WriteAllBytesAsync(corruptPath, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 });

            var action = new CreateSubtitlesAction(
                CreateSubtitlesSourceKind.Audio,
                new FfmpegRunner(logger),
                new FfprobeRunner(logger),
                new ToolLocator());

            var result = await action.ExecuteAsync(
                new ActionRequest(
                    corruptPath,
                    logger,
                    null,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [ActionOptionKeys.SubtitlesModel] = "whisper-base"
                    }),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.NotNull(result.Message);
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
    public async Task Small_Model_Produces_Subtitles_With_FR_EN_Audio()
    {
        var repoRoot = GetRepositoryRoot();
        var sampleAudio = Path.Combine(repoRoot, "scratch", "WhisperBaseOnnxSpike", "samples", "mixed_fr_en_16k.wav");
        var smallExportDir = Path.Combine(repoRoot, "scratch", "WhisperBaseOnnxSpike", "export-control", "small-export");

        if (!File.Exists(Path.Combine(smallExportDir, "small-encoder.onnx")))
        {
            // Small model not exported locally — skip (CI will skip, dev can run after export)
            return;
        }

        Assert.True(File.Exists(sampleAudio), $"Missing sample audio: {sampleAudio}");

        var tempRoot = Path.Combine(Path.GetTempPath(), $"frameshift_subtitles_small_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var modelsRoot = Path.Combine(tempRoot, "models");
        Directory.CreateDirectory(Path.Combine(modelsRoot, "whisper-small-onnx"));
        foreach (var fileName in new[] { "small-encoder.onnx", "small-decoder.onnx", "small-tokens.txt" })
        {
            File.Copy(Path.Combine(smallExportDir, fileName), Path.Combine(modelsRoot, "whisper-small-onnx", fileName), overwrite: true);
        }

        var previousSettings = File.Exists(AiModelSettings.ConfigFilePath)
            ? File.ReadAllText(AiModelSettings.ConfigFilePath)
            : null;

        try
        {
            new AiModelSettings { ModelsDirectory = modelsRoot }.Save();
            AiModelStorage.InvalidateCache();

            var logger = new AppLogger();
            var action = new CreateSubtitlesAction(
                CreateSubtitlesSourceKind.Audio,
                new FfmpegRunner(logger),
                new FfprobeRunner(logger),
                new ToolLocator());

            var result = await action.ExecuteAsync(
                new ActionRequest(
                    sampleAudio,
                    logger,
                    null,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [ActionOptionKeys.SubtitlesModel] = "whisper-small"
                    }),
                CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath!));
            var srt = await File.ReadAllTextAsync(result.OutputPath!);
            Assert.Contains("-->", srt, StringComparison.Ordinal);
            Assert.Contains("Bonjour", srt, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            RestoreSettings(previousSettings);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Default_Model_Is_Whisper_Small()
    {
        var defaultModel = CreateSubtitlesModelCatalog.GetDefault();
        Assert.Equal("whisper-small", defaultModel.Id);
        Assert.Equal(3, defaultModel.Artifacts.Count);
    }

    [Fact]
    public void Turbo_Model_Has_Four_Artifacts_Including_Weights_File()
    {
        var turboModel = CreateSubtitlesModelCatalog.GetById("whisper-turbo");
        Assert.NotNull(turboModel);
        Assert.Equal(4, turboModel!.Artifacts.Count);
        Assert.Contains(turboModel.Artifacts, a => a.FileName == "turbo-encoder.weights");
        Assert.Equal("turbo-encoder.onnx", turboModel.Artifacts[0].FileName);
        Assert.Equal("turbo-decoder.onnx", turboModel.Artifacts[1].FileName);
        Assert.Equal("turbo-tokens.txt", turboModel.Artifacts[2].FileName);
        Assert.Equal("turbo-encoder.weights", turboModel.Artifacts[3].FileName);
    }

    private static string NormalizeSrtText(string srt)
    {
        var lines = srt
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line =>
                !Regex.IsMatch(line, @"^\d+$", RegexOptions.CultureInvariant) &&
                !line.Contains("-->", StringComparison.Ordinal))
            .Select(line => line.Trim());

        return string.Join(" ", lines)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "FrameShift", "FrameShift.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the FrameShift repository root from the current test output.");
    }

    private static void RestoreSettings(string? previousSettings)
    {
        if (previousSettings is null)
        {
            if (File.Exists(AiModelSettings.ConfigFilePath))
            {
                File.Delete(AiModelSettings.ConfigFilePath);
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AiModelSettings.ConfigFilePath)!);
            File.WriteAllText(AiModelSettings.ConfigFilePath, previousSettings);
        }

        AiModelStorage.InvalidateCache();
    }
}
