using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class BalanceVolumeAction : IFrameShiftAction
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public BalanceVolumeAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new("balance-volume",
        L.Get("BalanceVolume_Name"), L.Get("BalanceVolume_Description"));

    public async Task<ActionExecutionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath))
            return new(false, MediaActionMessages.InputPathRequired());
        if (!File.Exists(request.InputPath))
            return new(false, MediaActionMessages.InputFileNotFound(request.InputPath));

        var intensity = ConversionActionHelper.GetOption(request, BalanceVolumeSettings.OptionKey, "moderate");
        if (!BalanceVolumeSettings.TryParse(intensity, out var settings))
            return new(false, L.Get("BalanceVolume_InvalidIntensity"));

        OutputOperation? output = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(request.InputPath).ToLowerInvariant();
            var codec = GetAudioCodec(extension);
            if (codec is null)
                return new(false, L.Get("BalanceVolume_UnsupportedFormat"));

            var ffmpegPath = _toolLocator.ResolveFfmpegPath();
            var attempt = await _ffprobeRunner.TryProbeMediaAsync(
                _toolLocator.ResolveFfprobePath(), request.InputPath, cancellationToken).ConfigureAwait(false);
            if (attempt.Probe is null)
                return new(false, attempt.ErrorMessage ?? MediaActionMessages.ProbeFailed());
            var probe = attempt.Probe;
            if (!probe.HasAudio)
                return new(false, MediaActionMessages.MissingAudioTrack());

            output = OutputOperation.ForFile(OutputPathHelper.GetOutputPath(request.InputPath,
                $"_balanced_{settings.Intensity}", extension == ".wave" ? ".wav" : extension));
            request.Logger.Log($"Running 'balance-volume': intensity={settings.Intensity}, codec={codec}, single pass, video stream copy.");
            request.ProgressReporter?.ReportState("processing", L.Get("BalanceVolume_Processing"));
            var result = await _ffmpegRunner.RunAsync(ffmpegPath,
                BuildArguments(request.InputPath, output.WorkingPath, settings, codec, probe.PrimaryAudioChannels),
                probe.Duration, request.ProgressReporter, request.InputPath, Descriptor.DisplayName, "CPU",
                cancellationToken).ConfigureAwait(false);

            if (!result.ProcessTerminationConfirmed)
                output.RetainForUnconfirmedProcess();
            if (result.Canceled)
            {
                request.ProgressReporter?.ReportState("canceled", MediaActionMessages.OperationCanceled(Descriptor.DisplayName));
                return new(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, result.CancellationScope);
            }
            if (result.ExitCode != 0)
            {
                var error = ConversionActionHelper.GetFriendlyFfmpegError(result.StandardError,
                    MediaActionMessages.Failed(Descriptor.DisplayName));
                request.Logger.Log($"Balance volume failed: {error}");
                request.ProgressReporter?.ReportState("failed", error);
                return new(false, error);
            }

            return ConversionActionHelper.CompleteOutput(output, request, Descriptor.DisplayName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            request.ProgressReporter?.ReportState("canceled", MediaActionMessages.OperationCanceled(Descriptor.DisplayName));
            return new(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, CancellationScope.All);
        }
        catch (Exception ex)
        {
            request.Logger.Log($"Balance volume failed: {ex}");
            var error = ConversionActionHelper.GetFriendlyExceptionMessage(ex, MediaActionMessages.Failed(Descriptor.DisplayName));
            request.ProgressReporter?.ReportState("failed", error);
            return new(false, error);
        }
        finally
        {
            output?.Dispose();
        }
    }

    private static string? GetAudioCodec(string extension) => extension switch
    {
        ".mp4" or ".mkv" or ".mov" or ".m4v" or ".m4a" or ".aac" => "aac",
        ".webm" => "libopus",
        ".avi" => "ac3",
        ".mp3" => "libmp3lame",
        ".wav" or ".wave" => "pcm_s16le",
        ".flac" => "flac",
        ".ogg" => "libvorbis",
        ".wma" => "wmav2",
        _ => null
    };

    internal static IReadOnlyList<string> BuildArguments(string input, string output,
        BalanceVolumeSettings settings, string codec, int channels)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-stats_period", "0.25",
            "-progress", "pipe:1", "-nostats", "-n", "-i", input,
            "-map", "0", "-map_metadata", "0", "-map_chapters", "0",
            "-c", "copy", "-c:a", codec, "-filter:a", settings.AudioFilter
        };
        if (codec is "aac" or "libopus" or "libmp3lame" or "wmav2" or "ac3")
            args.AddRange(["-b:a", channels > 2 ? "384k" : "192k"]);
        if (codec == "libvorbis") args.AddRange(["-q:a", "5"]);
        args.Add(output);
        return args;
    }
}
