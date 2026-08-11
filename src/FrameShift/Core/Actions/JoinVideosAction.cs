using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class JoinVideosAction : IFrameShiftAction
{
    private static readonly string[] SupportedExtensions = [".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v"];
    private const string SupportedExtensionsText = ".mp4, .mkv, .avi, .mov, .webm, .m4v";

    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public JoinVideosAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "join-videos",
        "Join Videos",
        "Joins video clips in the selected order without transitions or editing tools.");

    public async Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        if (!JoinVideosSettings.TryFromOptions(request.Options, out var settings, out var settingsError) || settings is null)
        {
            return new ActionExecutionResult(false, settingsError ?? MediaActionMessages.JoinVideosSettingsInvalid());
        }

        foreach (var inputPath in settings.InputPaths)
        {
            if (!File.Exists(inputPath))
            {
                return new ActionExecutionResult(false, MediaActionMessages.InputFileNotFound(inputPath));
            }

            var extension = Path.GetExtension(inputPath).ToLowerInvariant();
            if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(extension, SupportedExtensionsText));
            }
        }

        var anchorPath = settings.InputPaths[0];
        string? outputPath = null;
        string? temporaryDirectory = null;

        try
        {
            request.Logger.Log($"Running '{Descriptor.Id}' on {settings.InputPaths.Count} clips, anchored at '{Path.GetFileName(anchorPath)}'.");
            request.ProgressReporter?.ReportState("preparing", "Inspecting clips...");

            var ffprobePath = _toolLocator.ResolveFfprobePath();
            var probes = new List<JoinVideoProbeResult>(settings.InputPaths.Count);
            foreach (var inputPath in settings.InputPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var probeAttempt = await _ffprobeRunner
                    .TryProbeJoinVideoAsync(ffprobePath, inputPath, cancellationToken)
                    .ConfigureAwait(false);
                if (probeAttempt.Probe is null)
                {
                    var message = probeAttempt.Error ?? MediaActionMessages.ProbeFailed();
                    request.ProgressReporter?.ReportState("failed", message);
                    return new ActionExecutionResult(false, message);
                }

                probes.Add(probeAttempt.Probe);
            }

            var plan = JoinVideosPlanner.BuildPlan(probes, settings.Mode);
            request.Logger.Log($"Join Videos plan: {plan.Pipeline}. {plan.DecisionReason}");
            if (!plan.IsSupported)
            {
                request.ProgressReporter?.ReportState("failed", plan.DecisionReason);
                return new ActionExecutionResult(false, plan.DecisionReason);
            }

            temporaryDirectory = CreateTemporaryDirectory();
            var ffmpegPath = _toolLocator.ResolveFfmpegPath();
            var totalDuration = TimeSpan.FromSeconds(JoinVideosPlanner.GetTotalDurationSeconds(probes));
            var estimatedFrames = JoinVideosPlanner.GetEstimatedTotalFrames(probes);

            if (plan.Pipeline == JoinVideosPipeline.DirectCopy)
            {
                outputPath = CreateOutputPath(anchorPath, Path.GetExtension(anchorPath));
                var directResult = await RunDirectCopyAsync(
                    ffmpegPath,
                    settings.InputPaths,
                    outputPath,
                    temporaryDirectory,
                    totalDuration,
                    estimatedFrames,
                    request,
                    cancellationToken).ConfigureAwait(false);

                if (directResult.Canceled)
                {
                    return Canceled(outputPath, request, directResult.CancellationScope);
                }

                if (directResult.ExitCode == 0 && File.Exists(outputPath))
                {
                    return Completed(outputPath, request);
                }

                ConversionActionHelper.DeleteIfExists(outputPath);
                if (settings.Mode == JoinVideosMode.Copy)
                {
                    return Failed(directResult.StandardError, request);
                }

                if (probes.Any(probe => probe.IsHdrLikely))
                {
                    request.ProgressReporter?.ReportState("failed", MediaActionMessages.JoinVideosHdrNormalizationNotSupported());
                    return new ActionExecutionResult(false, MediaActionMessages.JoinVideosHdrNormalizationNotSupported());
                }

                request.Logger.Log("Join Videos: direct concat failed; falling back to SDR normalization.");
                request.ProgressReporter?.ReportState("processing", "Direct join was not accepted; normalizing clips...");

                outputPath = CreateOutputPath(anchorPath, ".mp4");
                var normalizeFallbackResult = await RunNormalizationWithFallbackAsync(
                    ffmpegPath,
                    settings.InputPaths,
                    probes,
                    plan with { Pipeline = JoinVideosPipeline.Normalize, IncludeAudio = probes.Any(probe => probe.HasAudio) },
                    outputPath,
                    totalDuration,
                    estimatedFrames,
                    request,
                    cancellationToken).ConfigureAwait(false);

                if (normalizeFallbackResult.Canceled)
                {
                    return Canceled(outputPath, request, normalizeFallbackResult.CancellationScope);
                }

                return normalizeFallbackResult.ExitCode == 0 && File.Exists(outputPath)
                    ? Completed(outputPath, request)
                    : FailedWithCleanup(outputPath, normalizeFallbackResult.StandardError, request);
            }

            outputPath = CreateOutputPath(anchorPath, ".mp4");
            var normalizeResult = await RunNormalizationWithFallbackAsync(
                ffmpegPath,
                settings.InputPaths,
                probes,
                plan,
                outputPath,
                totalDuration,
                estimatedFrames,
                request,
                cancellationToken).ConfigureAwait(false);

            if (normalizeResult.Canceled)
            {
                return Canceled(outputPath, request, normalizeResult.CancellationScope);
            }

            return normalizeResult.ExitCode == 0 && File.Exists(outputPath)
                ? Completed(outputPath, request)
                : FailedWithCleanup(outputPath, normalizeResult.StandardError, request);
        }
        catch (OperationCanceledException)
        {
            return Canceled(outputPath, request, CancellationScope.All);
        }
        catch (Exception ex)
        {
            request.Logger.Log(ex.ToString());
            ConversionActionHelper.DeleteIfExists(outputPath ?? string.Empty);
            var message = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.JoinVideosFailed());
            request.ProgressReporter?.ReportState("failed", message);
            return new ActionExecutionResult(false, message);
        }
        finally
        {
            TryDeleteDirectory(temporaryDirectory);
        }
    }

    internal static string BuildConcatManifest(IReadOnlyList<string> inputPaths)
    {
        var builder = new StringBuilder("ffconcat version 1.0\n");
        foreach (var inputPath in inputPaths)
        {
            builder.Append("file '")
                .Append(EscapeConcatPath(inputPath))
                .Append("'\n");
        }

        return builder.ToString();
    }

    internal static IReadOnlyList<string> BuildDirectCopyArguments(string manifestPath, string outputPath)
    {
        return
        [
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-f", "concat",
            "-safe", "0",
            "-i", manifestPath,
            "-map", "0:v:0",
            "-map", "0:a:0?",
            "-map_metadata", "-1",
            "-map_chapters", "-1",
            "-sn",
            "-dn",
            "-c", "copy",
            outputPath
        ];
    }

    internal static IReadOnlyList<string> BuildNormalizationArguments(
        IReadOnlyList<string> inputPaths,
        IReadOnlyList<JoinVideoProbeResult> probes,
        JoinVideosPlan plan,
        string filterComplex,
        string outputPath,
        bool useNvenc)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y"
        };

        foreach (var inputPath in inputPaths)
        {
            arguments.AddRange(["-i", inputPath]);
        }

        if (plan.IncludeAudio)
        {
            foreach (var probe in probes)
            {
                if (!probe.HasAudio)
                {
                    arguments.AddRange([
                        "-f", "lavfi",
                        "-t", probe.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                        "-i", "anullsrc=channel_layout=stereo:sample_rate=48000"
                    ]);
                }
            }
        }

        // The bundled FFmpeg 9 essentials build does not implement -filter_complex_script;
        // the graph is passed inline instead.
        arguments.AddRange([
            "-filter_complex", filterComplex,
            "-map", "[vout]"
        ]);

        if (plan.IncludeAudio)
        {
            arguments.AddRange(["-map", "[aout]"]);
        }

        arguments.AddRange(["-map_metadata", "-1", "-map_chapters", "-1", "-sn", "-dn"]);
        if (useNvenc)
        {
            arguments.AddRange(["-c:v", "h264_nvenc", "-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p"]);
        }
        else
        {
            arguments.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p"]);
        }

        if (plan.IncludeAudio)
        {
            arguments.AddRange(["-c:a", "aac", "-b:a", "192k"]);
        }
        else
        {
            arguments.Add("-an");
        }

        arguments.AddRange(["-movflags", "+faststart", outputPath]);
        return arguments;
    }

    internal static string BuildNormalizationFilterScript(
        IReadOnlyList<JoinVideoProbeResult> probes,
        JoinVideosPlan plan)
    {
        var builder = new StringBuilder();
        var silenceInputIndex = probes.Count;
        var audioInputIndexes = new Dictionary<int, int>();
        if (plan.IncludeAudio)
        {
            for (var index = 0; index < probes.Count; index++)
            {
                if (!probes[index].HasAudio)
                {
                    audioInputIndexes[index] = silenceInputIndex++;
                }
            }
        }

        for (var index = 0; index < probes.Count; index++)
        {
            var duration = probes[index].DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            builder.Append('[').Append(index).Append(":v:0]")
                .Append("settb=AVTB,setpts=PTS-STARTPTS,fps=").Append(plan.TargetFrameRate)
                .Append(",scale=").Append(plan.TargetWidth).Append(':').Append(plan.TargetHeight)
                .Append(":force_original_aspect_ratio=decrease,pad=").Append(plan.TargetWidth).Append(':').Append(plan.TargetHeight)
                .Append(":(ow-iw)/2:(oh-ih)/2:color=black,setsar=1,format=yuv420p[v")
                .Append(index).Append("];\n");

            if (!plan.IncludeAudio)
            {
                continue;
            }

            if (probes[index].HasAudio)
            {
                builder.Append('[').Append(index).Append(":a:0]")
                    .Append("aresample=48000:async=1:first_pts=0,aformat=sample_rates=48000:sample_fmts=fltp:channel_layouts=stereo,apad,atrim=duration=")
                    .Append(duration).Append(",asetpts=PTS-STARTPTS[a").Append(index).Append("];\n");
            }
            else
            {
                builder.Append('[').Append(audioInputIndexes[index]).Append(":a:0]")
                    .Append("atrim=duration=").Append(duration).Append(",asetpts=PTS-STARTPTS[a").Append(index).Append("];\n");
            }
        }

        for (var index = 0; index < probes.Count; index++)
        {
            builder.Append("[v").Append(index).Append(']');
            if (plan.IncludeAudio)
            {
                builder.Append("[a").Append(index).Append(']');
            }
        }

        builder.Append("concat=n=").Append(probes.Count)
            .Append(":v=1:a=").Append(plan.IncludeAudio ? '1' : '0')
            .Append(plan.IncludeAudio ? "[vout][aout]\n" : "[vout]\n");
        return builder.ToString();
    }

    private async Task<FfmpegRunResult> RunDirectCopyAsync(
        string ffmpegPath,
        IReadOnlyList<string> inputPaths,
        string outputPath,
        string temporaryDirectory,
        TimeSpan totalDuration,
        long? estimatedFrames,
        ActionRequest request,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(temporaryDirectory, "join.ffconcat");
        File.WriteAllText(manifestPath, BuildConcatManifest(inputPaths), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        request.ProgressReporter?.ReportState("processing", "Joining compatible clips without re-encoding...");

        return await _ffmpegRunner.RunAsync(
            ffmpegPath,
            BuildDirectCopyArguments(manifestPath, outputPath),
            totalDuration,
            estimatedFrames,
            request.ProgressReporter,
            inputPaths[0],
            Descriptor.DisplayName,
            "Copy",
            "Concat stream copy",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<FfmpegRunResult> RunNormalizationWithFallbackAsync(
        string ffmpegPath,
        IReadOnlyList<string> inputPaths,
        IReadOnlyList<JoinVideoProbeResult> probes,
        JoinVideosPlan plan,
        string outputPath,
        TimeSpan totalDuration,
        long? estimatedFrames,
        ActionRequest request,
        CancellationToken cancellationToken)
    {
        var filterComplex = BuildNormalizationFilterScript(probes, plan);
        var canUseNvenc = await _ffmpegRunner.IsEncoderAvailableAsync(ffmpegPath, "h264_nvenc", cancellationToken).ConfigureAwait(false);

        request.ProgressReporter?.ReportState("processing", canUseNvenc
            ? "Normalizing clips to SDR H.264/AAC MP4..."
            : "Normalizing clips to SDR H.264/AAC MP4 on CPU...");
        var result = await RunNormalizationAsync(canUseNvenc).ConfigureAwait(false);
        if (result.Canceled || result.ExitCode == 0 || !canUseNvenc)
        {
            return result;
        }

        ConversionActionHelper.DeleteIfExists(outputPath);
        request.Logger.Log("Join Videos: NVENC normalization failed; retrying with libx264.");
        request.ProgressReporter?.ReportState("processing", "GPU encoding was unavailable; retrying on CPU...");
        return await RunNormalizationAsync(useNvenc: false).ConfigureAwait(false);

        Task<FfmpegRunResult> RunNormalizationAsync(bool useNvenc)
            => _ffmpegRunner.RunAsync(
                ffmpegPath,
                BuildNormalizationArguments(inputPaths, probes, plan, filterComplex, outputPath, useNvenc),
                totalDuration,
                estimatedFrames,
                request.ProgressReporter,
                inputPaths[0],
                Descriptor.DisplayName,
                useNvenc ? "GPU" : "CPU",
                "SDR H.264/AAC normalization",
                cancellationToken);
    }

    private static string CreateOutputPath(string anchorPath, string extension)
    {
        var normalizedExtension = string.IsNullOrWhiteSpace(extension) ? ".mp4" : extension;
        return OutputPathHelper.CreateUniqueOutputPath(anchorPath, "_joined", normalizedExtension);
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FrameShift", "JoinVideos", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static ActionExecutionResult Completed(string outputPath, ActionRequest request)
    {
        request.ProgressReporter?.ReportProgress(1000, request.InputPath, "Join Videos", "Completed.");
        request.ProgressReporter?.ReportState("done", "Completed.");
        return new ActionExecutionResult(true, MediaActionMessages.Completed("Join Videos"), outputPath);
    }

    private static ActionExecutionResult Canceled(string? outputPath, ActionRequest request, CancellationScope scope)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            ConversionActionHelper.DeleteIfExists(outputPath);
        }

        request.ProgressReporter?.ReportState("canceled", "Canceled.");
        return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled("Join Videos"), null, true, scope);
    }

    private static ActionExecutionResult Failed(string standardError, ActionRequest request)
    {
        var message = ConversionActionHelper.GetFriendlyFfmpegError(standardError, MediaActionMessages.JoinVideosFailed());
        request.ProgressReporter?.ReportState("failed", message);
        return new ActionExecutionResult(false, message);
    }

    private static ActionExecutionResult FailedWithCleanup(string outputPath, string standardError, ActionRequest request)
    {
        ConversionActionHelper.DeleteIfExists(outputPath);
        return Failed(standardError, request);
    }

    private static string EscapeConcatPath(string path)
    {
        return Path.GetFullPath(path)
            .Replace('\\', '/')
            .Replace("'", "'\\''", StringComparison.Ordinal);
    }

    private static void TryDeleteDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // A locked temp file is harmless and remains under FrameShift's temp root.
        }
    }
}
