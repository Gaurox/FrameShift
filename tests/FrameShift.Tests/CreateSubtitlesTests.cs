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
using FrameShift.Windows.AI;
using Xunit;

namespace FrameShift.Tests;

public sealed class CreateSubtitlesTests
{
    [Fact]
    public void Segmenter_BreaksOnPunctuationAndSilence()
    {
        var words = new[]
        {
            new SubtitleWord("Hello", "hello", 0.00d, 0.00d, false),
            new SubtitleWord("world.", "world.", 0.42d, 0.42d, false),
            new SubtitleWord("This", "this", 1.95d, 1.95d, false),
            new SubtitleWord("is", "is", 2.20d, 2.20d, false),
            new SubtitleWord("FrameShift!", "frameshift!", 2.48d, 2.48d, false)
        };

        var segments = CreateSubtitlesSegmenter.BuildSegments(words, TimeSpan.FromSeconds(4));

        Assert.Equal(2, segments.Count);
        Assert.Equal("Hello world.", segments[0].Text);
        Assert.Equal("This is FrameShift!", segments[1].Text);
        Assert.True(segments[0].End < segments[1].Start);
        Assert.True(segments[0].HasReliableWordAlignment);
        Assert.All(segments[0].Words, word => Assert.True(word.IsTimingReliable));
    }

    [Fact]
    public void ProjectBuilder_Preserves_Word_Timestamps_Inside_Segments()
    {
        var workerWords = new[]
        {
            new CreateSubtitlesWorkerWord { Text = " Hello", StartSeconds = 0.10d },
            new CreateSubtitlesWorkerWord { Text = " world.", StartSeconds = 0.42d },
            new CreateSubtitlesWorkerWord { Text = " This", StartSeconds = 1.95d },
            new CreateSubtitlesWorkerWord { Text = " is", StartSeconds = 2.20d },
            new CreateSubtitlesWorkerWord { Text = " FrameShift!", StartSeconds = 2.48d }
        };

        var project = CreateSubtitlesProjectBuilder.Build(workerWords, TimeSpan.FromSeconds(4));

        Assert.Equal(2, project.Segments.Count);
        Assert.True(project.Segments[0].HasReliableWordAlignment);
        Assert.Collection(
            project.Segments[0].Words,
            word =>
            {
                Assert.Equal("Hello", word.Text);
                Assert.Equal(0.10d, word.StartSeconds, 3);
                Assert.Equal(0.42d, word.EndSeconds, 3);
                Assert.True(word.IsTimingReliable);
            },
            word =>
            {
                Assert.Equal("world.", word.Text);
                Assert.Equal(0.42d, word.StartSeconds, 3);
                Assert.True(word.EndSeconds > word.StartSeconds);
                Assert.True(word.IsTimingReliable);
            });
        Assert.Collection(
            project.Segments[1].Words,
            word => Assert.Equal(1.95d, word.StartSeconds, 3),
            word => Assert.Equal(2.20d, word.StartSeconds, 3),
            word => Assert.Equal(2.48d, word.StartSeconds, 3));
    }

    [Fact]
    public void ProjectBuilder_Falls_Back_When_Word_Timings_Are_Not_Reliable()
    {
        var workerWords = new[]
        {
            new CreateSubtitlesWorkerWord { Text = " Bonjour", StartSeconds = 0.00d },
            new CreateSubtitlesWorkerWord { Text = " encore", StartSeconds = 0.00d },
            new CreateSubtitlesWorkerWord { Text = " maintenant.", StartSeconds = 0.00d }
        };

        var project = CreateSubtitlesProjectBuilder.Build(workerWords, TimeSpan.FromSeconds(2));
        var segment = Assert.Single(project.Segments);

        Assert.False(segment.HasReliableWordAlignment);
        Assert.Equal(3, segment.Words.Count);
        Assert.All(segment.Words, word => Assert.False(word.IsTimingReliable));
        Assert.True(segment.Words[0].StartSeconds >= segment.Start.TotalSeconds);
        Assert.True(segment.Words[0].EndSeconds <= segment.Words[1].StartSeconds);
        Assert.True(segment.Words[1].EndSeconds <= segment.Words[2].StartSeconds);
        Assert.True(segment.Words[2].EndSeconds <= segment.End.TotalSeconds + 0.001d);
    }

    [Fact]
    public void SrtFormatter_Formats_Subtitle_Project_Without_Changing_Output()
    {
        var project = new SubtitleProject(
            TimeSpan.FromSeconds(4),
            new[]
            {
                new SubtitleSegment(
                    1,
                    "Hello world.",
                    TimeSpan.FromSeconds(0),
                    TimeSpan.FromMilliseconds(740),
                    true,
                    new[]
                    {
                        new SubtitleWord("Hello", "hello", 0.00d, 0.42d, true),
                        new SubtitleWord("world.", "world.", 0.42d, 0.74d, true)
                    }),
                new SubtitleSegment(
                    2,
                    "This is FrameShift!",
                    TimeSpan.FromMilliseconds(1950),
                    TimeSpan.FromMilliseconds(3860),
                    true,
                    new[]
                    {
                        new SubtitleWord("This", "this", 1.95d, 2.20d, true),
                        new SubtitleWord("is", "is", 2.20d, 2.48d, true),
                        new SubtitleWord("FrameShift!", "frameshift!", 2.48d, 3.86d, true)
                    })
            });

        var srt = CreateSubtitlesSrtFormatter.Format(project);

        Assert.Equal(
            "1\r\n00:00:00,000 --> 00:00:00,740\r\nHello world.\r\n\r\n2\r\n00:00:01,950 --> 00:00:03,860\r\nThis is FrameShift!\r\n\r\n",
            srt);
    }

    [Fact]
    public void ProjectSerializer_RoundTrips_Unicode_And_Timing_Metadata()
    {
        var project = new SubtitleProject(
            TimeSpan.FromSeconds(5.2d),
            new[]
            {
                new SubtitleSegment(
                    1,
                    "Bonjour 世界 !",
                    TimeSpan.FromSeconds(0.1d),
                    TimeSpan.FromSeconds(2.0d),
                    true,
                    new[]
                    {
                        new SubtitleWord("Bonjour", "bonjour", 0.10d, 0.85d, true),
                        new SubtitleWord("世界", "世界", 0.85d, 1.45d, true),
                        new SubtitleWord("!", "!", 1.45d, 2.00d, true)
                    }),
                new SubtitleSegment(
                    2,
                    "Ligne sans alignement fiable.",
                    TimeSpan.FromSeconds(2.2d),
                    TimeSpan.FromSeconds(4.0d),
                    false,
                    new[]
                    {
                        new SubtitleWord("Ligne", "ligne", 2.20d, 2.80d, false),
                        new SubtitleWord("sans", "sans", 2.80d, 3.30d, false),
                        new SubtitleWord("alignement", "alignement", 3.30d, 3.65d, false),
                        new SubtitleWord("fiable.", "fiable.", 3.65d, 4.00d, false)
                    })
            });

        var json = CreateSubtitlesProjectSerializer.Serialize(project);
        var roundTrip = CreateSubtitlesProjectSerializer.Deserialize(json);

        Assert.Contains("\"format\": \"frameshift-subtitle-project\"", json, StringComparison.Ordinal);
        Assert.Contains("\"version\": 1", json, StringComparison.Ordinal);
        Assert.Equal(project.TotalDuration, roundTrip.TotalDuration);
        Assert.Equal(2, roundTrip.Segments.Count);
        Assert.Equal("Bonjour 世界 !", roundTrip.Segments[0].Text);
        Assert.True(roundTrip.Segments[0].HasReliableWordAlignment);
        Assert.False(roundTrip.Segments[1].HasReliableWordAlignment);
        Assert.Equal("世界", roundTrip.Segments[0].Words[1].Text);
        Assert.Equal(0.85d, roundTrip.Segments[0].Words[1].StartSeconds, 3);
        Assert.False(roundTrip.Segments[1].Words[0].IsTimingReliable);
    }

    [Fact]
    public void ProjectSerializer_Rejects_Unsupported_Version()
    {
        var project = BuildSimpleSubtitleProject();
        var json = CreateSubtitlesProjectSerializer.Serialize(project)
            .Replace("\"version\": 1", "\"version\": 99", StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidOperationException>(() => CreateSubtitlesProjectSerializer.Deserialize(json));

        Assert.Contains("Unsupported subtitle project version", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssFormatter_Escapes_Unicode_Braces_Backslashes_And_NewLines()
    {
        var project = new SubtitleProject(
            TimeSpan.FromSeconds(2),
            new[]
            {
                new SubtitleSegment(
                    1,
                    "Bonjour {世界}\r\nC:\\clips\\test!",
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1.23d),
                    true,
                    new[]
                    {
                        new SubtitleWord("Bonjour", "bonjour", 0.00d, 0.60d, true),
                        new SubtitleWord("{世界}", "世界", 0.60d, 1.23d, true)
                    })
            });

        var ass = CreateSubtitlesAssFormatter.Format(project);

        Assert.Contains("[Script Info]", ass, StringComparison.Ordinal);
        Assert.Contains("[V4+ Styles]", ass, StringComparison.Ordinal);
        Assert.Contains("[Events]", ass, StringComparison.Ordinal);
        Assert.Contains("Dialogue: 0,0:00:00.00,0:00:01.23,Default,,0,0,0,,Bonjour \\{世界\\}\\NC:\\\\clips\\\\test!", ass, StringComparison.Ordinal);
    }

    [Fact]
    public void AssFormatter_Exports_Plain_Text_For_Segments_Without_Reliable_Alignment()
    {
        var project = new SubtitleProject(
            TimeSpan.FromSeconds(2),
            new[]
            {
                new SubtitleSegment(
                    1,
                    "Texte simple, sans effet dynamique.",
                    TimeSpan.FromSeconds(0.5d),
                    TimeSpan.FromSeconds(1.5d),
                    false,
                    new[]
                    {
                        new SubtitleWord("Texte", "texte", 0.50d, 0.80d, false),
                        new SubtitleWord("simple,", "simple,", 0.80d, 1.10d, false),
                        new SubtitleWord("sans", "sans", 1.10d, 1.30d, false),
                        new SubtitleWord("effet", "effet", 1.30d, 1.40d, false),
                        new SubtitleWord("dynamique.", "dynamique.", 1.40d, 1.50d, false)
                    })
            });

        var ass = CreateSubtitlesAssFormatter.Format(project);

        Assert.Contains("Texte simple, sans effet dynamique.", ass, StringComparison.Ordinal);
        Assert.DoesNotContain("\\k", ass, StringComparison.Ordinal);
        Assert.DoesNotContain("\\kf", ass, StringComparison.Ordinal);
        Assert.DoesNotContain("\\ko", ass, StringComparison.Ordinal);
    }

    [Fact]
    public void AssFormatter_WordHighlight_Uses_Word_Timings_When_Reliable()
    {
        var project = new SubtitleProject(
            TimeSpan.FromSeconds(1),
            new[]
            {
                new SubtitleSegment(
                    1,
                    "Hello world.",
                    TimeSpan.FromSeconds(0),
                    TimeSpan.FromMilliseconds(740),
                    true,
                    new[]
                    {
                        new SubtitleWord("Hello", "hello", 0.00d, 0.42d, true),
                        new SubtitleWord("world.", "world.", 0.42d, 0.74d, true)
                    })
            });

        var ass = CreateSubtitlesAssFormatter.Format(project, CreateSubtitlesAssPreset.WordHighlight);

        Assert.Contains("Dialogue: 0,0:00:00.00,0:00:00.42,Default,,0,0,0,,{\\c&H0000FFFF&\\b1}Hello{\\r} world.", ass, StringComparison.Ordinal);
        Assert.Contains("Dialogue: 0,0:00:00.42,0:00:00.74,Default,,0,0,0,,Hello {\\c&H0000FFFF&\\b1}world.{\\r}", ass, StringComparison.Ordinal);
    }

    [Fact]
    public void AssFormatter_ProgressiveReveal_Reveals_Words_Progressively_When_Reliable()
    {
        var project = new SubtitleProject(
            TimeSpan.FromSeconds(1),
            new[]
            {
                new SubtitleSegment(
                    1,
                    "Hello world.",
                    TimeSpan.FromSeconds(0),
                    TimeSpan.FromMilliseconds(740),
                    true,
                    new[]
                    {
                        new SubtitleWord("Hello", "hello", 0.00d, 0.42d, true),
                        new SubtitleWord("world.", "world.", 0.42d, 0.74d, true)
                    })
            });

        var ass = CreateSubtitlesAssFormatter.Format(project, CreateSubtitlesAssPreset.ProgressiveReveal);

        Assert.Contains("Dialogue: 0,0:00:00.00,0:00:00.42,Default,,0,0,0,,Hello", ass, StringComparison.Ordinal);
        Assert.Contains("Dialogue: 0,0:00:00.42,0:00:00.74,Default,,0,0,0,,Hello world.", ass, StringComparison.Ordinal);
    }

    [Fact]
    public void AssFormatter_Dynamic_Presets_Fall_Back_To_Classic_When_Segment_Is_Not_Reliable()
    {
        var project = new SubtitleProject(
            TimeSpan.FromSeconds(2),
            new[]
            {
                new SubtitleSegment(
                    1,
                    "Texte simple, sans effet dynamique.",
                    TimeSpan.FromSeconds(0.5d),
                    TimeSpan.FromSeconds(1.5d),
                    false,
                    new[]
                    {
                        new SubtitleWord("Texte", "texte", 0.50d, 0.80d, false),
                        new SubtitleWord("simple,", "simple,", 0.80d, 1.10d, false),
                        new SubtitleWord("sans", "sans", 1.10d, 1.30d, false),
                        new SubtitleWord("effet", "effet", 1.30d, 1.40d, false),
                        new SubtitleWord("dynamique.", "dynamique.", 1.40d, 1.50d, false)
                    })
            });

        var ass = CreateSubtitlesAssFormatter.Format(project, CreateSubtitlesAssPreset.WordHighlight);

        Assert.Contains("Dialogue: 0,0:00:00.50,0:00:01.50,Default,,0,0,0,,Texte simple, sans effet dynamique.", ass, StringComparison.Ordinal);
        Assert.DoesNotContain("{\\c&H0000FFFF&\\b1}", ass, StringComparison.Ordinal);
    }

    [Fact]
    public void OutputFormats_Default_To_StandardSrt_And_Expose_Expected_Extensions()
    {
        Assert.Equal(CreateSubtitlesOutputFormat.StandardSrt, CreateSubtitlesOutputFormats.Default);
        Assert.Equal(".srt", CreateSubtitlesOutputFormats.Default.GetOutputExtension());
        Assert.Equal(".ass", CreateSubtitlesOutputFormat.AdvancedAss.GetOutputExtension());
        Assert.Equal(".frameshift-subtitles.json", CreateSubtitlesOutputFormat.FrameShiftSubtitleProject.GetOutputExtension());
    }

    [Fact]
    public void Picker_Defaults_To_StandardSrt_Output()
    {
        using var form = new CreateSubtitlesPickerForm("Create Subtitle File", "sample.wav");

        Assert.Equal(CreateSubtitlesOutputFormat.StandardSrt, form.SelectedOutputFormat);
        Assert.Equal(CreateSubtitlesAssPreset.Classic, form.SelectedAssPreset);
        Assert.False(form.IsAssPresetSectionVisible);
    }

    [Fact]
    public void Picker_Shows_Ass_Preset_Only_When_AdvancedAss_Is_Selected()
    {
        using var form = new CreateSubtitlesPickerForm(
            "Create Subtitle File",
            "sample.wav",
            CreateSubtitlesOutputFormat.AdvancedAss,
            CreateSubtitlesAssPreset.WordHighlight);

        Assert.Equal(CreateSubtitlesOutputFormat.AdvancedAss, form.SelectedOutputFormat);
        Assert.Equal(CreateSubtitlesAssPreset.WordHighlight, form.SelectedAssPreset);
        Assert.True(form.IsAssPresetSectionVisible);
    }

    [Fact]
    public async Task Alternative_Output_Formats_Are_Written_With_Expected_Extensions()
    {
        var repoRoot = GetRepositoryRoot();
        var sampleAudio = Path.Combine(repoRoot, "scratch", "WhisperBaseOnnxSpike", "samples", "mixed_fr_en_16k.wav");
        var modelSourceDir = Path.Combine(repoRoot, "scratch", "WhisperBaseOnnxSpike", "export-control");

        if (!File.Exists(sampleAudio) || !File.Exists(Path.Combine(modelSourceDir, "base-encoder.onnx")))
            return;

        var tempRoot = Path.Combine(Path.GetTempPath(), $"frameshift_subtitles_alt_formats_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var modelsRoot = Path.Combine(tempRoot, "models");
        var modelTargetDir = Path.Combine(modelsRoot, "whisper-base-onnx");
        Directory.CreateDirectory(modelTargetDir);
        foreach (var fileName in new[] { "base-encoder.onnx", "base-decoder.onnx", "base-tokens.txt" })
        {
            File.Copy(Path.Combine(modelSourceDir, fileName), Path.Combine(modelTargetDir, fileName), overwrite: true);
        }

        var previousSettings = File.Exists(AiModelSettings.ConfigFilePath)
            ? File.ReadAllText(AiModelSettings.ConfigFilePath)
            : null;

        try
        {
            new AiModelSettings { ModelsDirectory = modelsRoot }.Save();
            AiModelStorage.InvalidateCache();

            var audioCopy = Path.Combine(tempRoot, "alt formats.wav");
            File.Copy(sampleAudio, audioCopy, overwrite: true);

            var logger = new AppLogger();
            var action = new CreateSubtitlesAction(
                CreateSubtitlesSourceKind.Audio,
                new FfmpegRunner(logger),
                new FfprobeRunner(logger),
                new ToolLocator());

            var assResult = await action.ExecuteAsync(
                new ActionRequest(
                    audioCopy,
                    logger,
                    null,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [ActionOptionKeys.SubtitlesModel] = "whisper-base",
                        [ActionOptionKeys.SubtitlesOutputFormat] = "ass",
                        [ActionOptionKeys.SubtitlesAssPreset] = "word-highlight"
                    }),
                CancellationToken.None);

            var projectResult = await action.ExecuteAsync(
                new ActionRequest(
                    audioCopy,
                    logger,
                    null,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [ActionOptionKeys.SubtitlesModel] = "whisper-base",
                        [ActionOptionKeys.SubtitlesOutputFormat] = "project"
                    }),
                CancellationToken.None);

            Assert.True(assResult.Success, assResult.Message);
            Assert.True(projectResult.Success, projectResult.Message);
            Assert.EndsWith(".ass", assResult.OutputPath, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".frameshift-subtitles.json", projectResult.OutputPath, StringComparison.OrdinalIgnoreCase);
            var assText = await File.ReadAllTextAsync(assResult.OutputPath!);
            Assert.Contains("[Script Info]", assText, StringComparison.Ordinal);
            Assert.Contains("{\\c&H0000FFFF&\\b1}", assText, StringComparison.Ordinal);
            Assert.Contains("\"format\": \"frameshift-subtitle-project\"", await File.ReadAllTextAsync(projectResult.OutputPath!), StringComparison.Ordinal);
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

    private static SubtitleProject BuildSimpleSubtitleProject() =>
        new(
            TimeSpan.FromSeconds(4),
            new[]
            {
                new SubtitleSegment(
                    1,
                    "Hello world.",
                    TimeSpan.FromSeconds(0),
                    TimeSpan.FromMilliseconds(740),
                    true,
                    new[]
                    {
                        new SubtitleWord("Hello", "hello", 0.00d, 0.42d, true),
                        new SubtitleWord("world.", "world.", 0.42d, 0.74d, true)
                    }),
                new SubtitleSegment(
                    2,
                    "This is FrameShift!",
                    TimeSpan.FromMilliseconds(1950),
                    TimeSpan.FromMilliseconds(3860),
                    true,
                    new[]
                    {
                        new SubtitleWord("This", "this", 1.95d, 2.20d, true),
                        new SubtitleWord("is", "is", 2.20d, 2.48d, true),
                        new SubtitleWord("FrameShift!", "frameshift!", 2.48d, 3.86d, true)
                    })
            });

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
