using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class ChangeVideoSpeedAction : IFrameShiftAction
{
    private const int FallbackSampleRate = 44100;

    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public ChangeVideoSpeedAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "change-video-speed",
        L.Get("ChangeVideoSpeedAction_Change_Video_Speed_2f7bf506"),
        L.Get("ChangeVideoSpeedAction_Changes_the_playback_speed_of_a_vide_73b9bfe8"));

    public async Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath))
        {
            return new ActionExecutionResult(false, MediaActionMessages.InputPathRequired());
        }

        if (!File.Exists(request.InputPath))
        {
            return new ActionExecutionResult(false, MediaActionMessages.InputFileNotFound(request.InputPath));
        }

        var sourceExtension = NormalizeExtension(Path.GetExtension(request.InputPath));
        if (!IsSupportedExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(
                sourceExtension, GetSupportedFormatsText()));
        }

        if (!ChangeSpeedSettings.TryFromOptions(request.Options, out var settings, out var settingsError) || settings is null)
        {
            return new ActionExecutionResult(false, settingsError ?? MediaActionMessages.ChangeSpeedSettingsMissing());
        }

        var ffmpegPath = _toolLocator.ResolveFfmpegPath();
        var ffprobePath = _toolLocator.ResolveFfprobePath();

        var probeAttempt = await _ffprobeRunner.TryProbeMediaAsync(ffprobePath, request.InputPath, cancellationToken, includeChapters: true).ConfigureAwait(false);
        if (probeAttempt.Probe is null)
        {
            var probeError = probeAttempt.ErrorMessage ?? MediaActionMessages.ProbeFailed();
            request.ProgressReporter?.ReportState("failed", probeError);
            return new ActionExecutionResult(false, probeError);
        }

        var probe = probeAttempt.Probe;
        if (!probe.HasVideo)
        {
            request.ProgressReporter?.ReportState("failed", MediaActionMessages.MissingVideoTrack());
            return new ActionExecutionResult(false, MediaActionMessages.MissingVideoTrack());
        }

        if (probe.Duration is null || probe.Duration.Value.TotalSeconds <= 0)
        {
            request.ProgressReporter?.ReportState("failed", MediaActionMessages.DurationUnavailable());
            return new ActionExecutionResult(false, MediaActionMessages.DurationUnavailable());
        }

        var sourceDurationSeconds = probe.Duration.Value.TotalSeconds;
        var targetDurationSeconds = settings.EstimatedOutputDuration(sourceDurationSeconds);
        using var output = OutputOperation.ForFile(OutputPathHelper.GetOutputPath(request.InputPath, settings.OutputSuffix, sourceExtension));
        var outputPath = output.WorkingPath;

        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.Logger.Log($"SpeedFactor: {settings.SpeedFactor:0.######}, keepPitch={settings.KeepPitch}");
        request.ProgressReporter?.ReportState("processing", L.Get("ChangeVideoSpeedAction_Changing_video_speed_e2917417"));

        try
        {
            var chaptersPath = await VideoChapterMetadata.WriteAsync(output.WorkspacePath, probe.Chapters,
                probe.FormatStartSeconds, probe.FormatStartSeconds + sourceDurationSeconds, settings.SpeedFactor,
                cancellationToken).ConfigureAwait(false);
            var audioStreams = probe.Streams.Where(stream => stream.CodecType == "audio" && stream.Index >= 0).ToArray();
            var result = await _ffmpegRunner.RunAsync(ffmpegPath, BuildArguments(request.InputPath, outputPath,
                sourceExtension, settings, probe.FormatStartSeconds, audioStreams, probe.HasSubtitles, chaptersPath),
                TimeSpan.FromSeconds(targetDurationSeconds), request.ProgressReporter, request.InputPath,
                Descriptor.DisplayName, "Video", cancellationToken).ConfigureAwait(false);
            if (!result.ProcessTerminationConfirmed)
                output.RetainForUnconfirmedProcess();

            if (result.Canceled)
            {
                request.ProgressReporter?.ReportState("canceled", L.Get("ChangeVideoSpeedAction_Canceled_437433cc"));
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, result.CancellationScope);
            }

            if (!result.ProcessTerminationConfirmed || result.ExitCode != 0 || !File.Exists(outputPath))
            {
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError, L.Get("ChangeVideoSpeedAction_FFmpeg_failed_during_video_speed_cha_c82d4c7d"));
                request.ProgressReporter?.ReportState("failed", failureMessage);
                return new ActionExecutionResult(false, failureMessage, null, false);
            }

            return ConversionActionHelper.CompleteOutput(output, request, Descriptor.DisplayName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ConversionActionHelper.DeleteIfExists(outputPath);
            request.ProgressReporter?.ReportState("canceled", L.Get("ChangeVideoSpeedAction_Canceled_437433cc"));
            return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, CancellationScope.All);
        }
        catch (Exception ex)
        {
            ConversionActionHelper.DeleteIfExists(outputPath);
            var failureMessage = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed(Descriptor.DisplayName));
            request.ProgressReporter?.ReportState("failed", failureMessage);
            return new ActionExecutionResult(false, failureMessage);
        }
    }

    private static IReadOnlyList<string> BuildArguments(
        string inputPath,
        string outputPath,
        string extension,
        ChangeSpeedSettings settings,
        double formatStartSeconds,
        IReadOnlyList<MediaStreamInfo> audioStreams,
        bool hasSubtitles,
        string? chaptersPath)
    {
        List<string> args = [.. Prefix(), "-i", inputPath];
        if (chaptersPath is not null)
            args.AddRange(["-f", "ffmetadata", "-i", chaptersPath]);
        args.AddRange(["-map_chapters", chaptersPath is null ? "-1" : "1"]);

        var setPtsFactor = (1.0 / settings.SpeedFactor).ToString("0.#########", CultureInfo.InvariantCulture);
        var videoFilter = $"settb=AVTB,setpts={setPtsFactor}*PTS";
        args.AddRange(["-map", "0:V", "-vf", videoFilter]);

        for (var index = 0; index < audioStreams.Count; index++)
        {
            var stream = audioStreams[index];
            args.AddRange(["-map", FormattableString.Invariant($"0:{stream.Index}"),
                FormattableString.Invariant($"-filter:a:{index}"),
                BuildAudioFilter(stream.SampleRate, settings, AudioDelay(stream, formatStartSeconds))]);
        }

        if (hasSubtitles && extension != ".avi")
        {
            var subtitleFilter = $"setts=pts='round(PTS*{setPtsFactor})':dts='round(DTS*{setPtsFactor})':" +
                $"duration='if(gt(DURATION,0),max(1,round(DURATION*{setPtsFactor})),DURATION)'";
            args.AddRange(["-map", "0:s?", "-c:s", "copy", "-bsf:s", subtitleFilter]);
        }
        if (extension == ".mkv")
            args.AddRange(["-map", "0:t?", "-c:t", "copy"]);

        var codecArgs = GetVideoCodecArgs(extension, audioStreams.Count != 0);
        args.AddRange(codecArgs);
        if (extension != ".avi")
            args.AddRange(["-fps_mode", "passthrough", "-enc_time_base:v", "filter"]);

        args.Add(outputPath);
        return args;
    }

    private static string[] Prefix() => ["-hide_banner", "-loglevel", "error", "-stats_period", "0.25",
        "-progress", "pipe:1", "-nostats", "-y"];

    internal static double AudioDelay(MediaStreamInfo? stream, double formatStartSeconds) =>
        Math.Max(0, (stream?.StartSeconds ?? formatStartSeconds) - formatStartSeconds);

    internal static string BuildAudioFilter(int sampleRate, ChangeSpeedSettings settings, double sourceDelay)
    {
        // Fill timestamp gaps before changing the sample stream so those gaps scale with the video.
        const string fillGaps = "asetpts=PTS-STARTPTS,aresample=async=1:min_comp=0.002:min_hard_comp=0.002:first_pts=0";
        var scaledDelay = sourceDelay / settings.SpeedFactor;
        if (settings.KeepPitch)
            return fillGaps + "," + ChangeAudioSpeedAction.BuildAtempoChain(settings.SpeedFactor) +
                FormattableString.Invariant($",asetpts=PTS-STARTPTS+{scaledDelay:0.#########}/TB");

        if (sampleRate <= 0) sampleRate = FallbackSampleRate;
        var targetRate = Math.Max(1000, (int)Math.Round(sampleRate * settings.SpeedFactor));
        var delaySamples = (long)Math.Round(scaledDelay * sampleRate);
        var delayFilter = delaySamples > 0 ? FormattableString.Invariant($",adelay={delaySamples}S:all=1") : string.Empty;
        return FormattableString.Invariant($"{fillGaps},asetrate={targetRate},aresample={sampleRate}{delayFilter},asetpts=N/SR/TB");
    }

    private static IReadOnlyList<string> GetVideoCodecArgs(string extension, bool hasAudio)
    {
        var args = new List<string>();
        switch (extension)
        {
            case ".webm":
                args.AddRange(["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0"]);
                break;
            case ".avi":
                args.AddRange(["-c:v", "mpeg4", "-q:v", "3"]);
                break;
            default:
                args.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p"]);
                break;
        }
        if (hasAudio) args.AddRange(GetAudioCodecArgs(extension));
        return args;
    }

    private static string[] GetAudioCodecArgs(string extension) => extension switch
    {
        ".webm" => ["-c:a", "libopus", "-b:a", "160k"],
        ".avi" => ["-c:a", "libmp3lame", "-b:a", "192k"],
        // The bundled AAC encoder's default coder can hang on some retimed 44.1 kHz tracks.
        _ => ["-c:a", "aac", "-aac_coder", "fast", "-b:a", "192k"]
    };

    private static string NormalizeExtension(string extension)
    {
        return string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.Trim().ToLowerInvariant();
    }

    private static bool IsSupportedExtension(string extension)
    {
        return extension is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".m4v";
    }

    private static string GetSupportedFormatsText()
    {
        return ".mp4, .mkv, .avi, .mov, .webm, .m4v";
    }
}
