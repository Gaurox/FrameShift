using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class CreateGifAction : IFrameShiftAction
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly ToolLocator _toolLocator;

    public CreateGifAction(FfmpegRunner ffmpegRunner, FfprobeRunner ffprobeRunner, ToolLocator toolLocator)
    {
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _toolLocator = toolLocator;
    }

    public ActionDescriptor Descriptor { get; } = new(
        "create-gif",
        "Create GIF",
        "Creates a GIF from a selected video range.");

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

        var sourceExtension = Path.GetExtension(request.InputPath).ToLowerInvariant();
        if (!VideoConversionCatalog.IsSupportedSourceExtension(sourceExtension))
        {
            return new ActionExecutionResult(false, MediaActionMessages.UnsupportedSourceFormat(sourceExtension, VideoConversionCatalog.GetSupportedSourceFormatsText()));
        }

        var ffmpegPath = _toolLocator.ResolveFfmpegPath();
        var ffprobePath = _toolLocator.ResolveFfprobePath();
        var probeAttempt = await _ffprobeRunner.TryProbeMediaAsync(ffprobePath, request.InputPath, cancellationToken).ConfigureAwait(false);
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

        if (!CreateGifSettings.TryFromOptions(request.Options, probe.Duration.Value.TotalSeconds, out var settings, out var settingsError) ||
            settings is null)
        {
            return new ActionExecutionResult(false, settingsError ?? MediaActionMessages.CreateGifSettingsInvalid());
        }

        var outputPath = BuildOutputPath(request.InputPath, settings, probe.Duration.Value.TotalSeconds);
        request.Logger.Log($"Running '{Descriptor.Id}' on '{Path.GetFileName(request.InputPath)}'...");
        request.Logger.Log($"Create GIF output path: {outputPath}");
        request.ProgressReporter?.ReportState("processing", "Preparing GIF creation on CPU...");

        try
        {
            var arguments = BuildArguments(request.InputPath, outputPath, settings);
            var runResult = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                TimeSpan.FromSeconds(settings.DurationSeconds),
                EstimateFrameCount(settings),
                request.ProgressReporter,
                request.InputPath,
                Descriptor.DisplayName,
                "CPU",
                cancellationToken).ConfigureAwait(false);

            if (runResult.Canceled)
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                request.ProgressReporter?.ReportState("canceled", "Canceled.");
                return new ActionExecutionResult(false, MediaActionMessages.OperationCanceled(Descriptor.DisplayName), null, true, runResult.CancellationScope);
            }

            if (runResult.ExitCode != 0 || !File.Exists(outputPath))
            {
                ConversionActionHelper.DeleteIfExists(outputPath);
                var failureMessage = ConversionActionHelper.GetFriendlyFfmpegError(
                    runResult.StandardError,
                    MediaActionMessages.CreateGifFailed());
                request.ProgressReporter?.ReportState("failed", failureMessage);
                return new ActionExecutionResult(false, failureMessage);
            }

            request.ProgressReporter?.ReportProgress(1000, request.InputPath, Descriptor.DisplayName, "Completed.");
            request.ProgressReporter?.ReportState("done", "Completed.");
            return new ActionExecutionResult(true, MediaActionMessages.Completed(Descriptor.DisplayName), outputPath);
        }
        catch (OperationCanceledException)
        {
            ConversionActionHelper.DeleteIfExists(outputPath);
            request.ProgressReporter?.ReportState("canceled", "Canceled.");
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

    public static IReadOnlyList<string> BuildArguments(string inputPath, string outputPath, CreateGifSettings settings)
    {
        var qualityProfile = GetQualityProfile(settings.QualityKey);
        var scaleFilter = GetScaleFilter(settings.ResolutionKey, qualityProfile.ScaleFlags);
        var filterComplex = $"[0:v]fps={settings.Fps},{scaleFilter},split[s0][s1];[s0]palettegen={qualityProfile.PaletteGen}[p];[s1][p]paletteuse={qualityProfile.PaletteUse}[gif]";

        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y"
        };

        if (settings.StartSeconds > 0)
        {
            args.Add("-ss");
            args.Add(settings.StartSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }

        args.Add("-t");
        args.Add(settings.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        args.Add("-i");
        args.Add(inputPath);
        args.Add("-an");
        args.Add("-sn");
        args.Add("-dn");
        args.Add("-filter_complex");
        args.Add(filterComplex);
        args.Add("-map");
        args.Add("[gif]");
        args.Add("-loop");
        args.Add("0");
        args.Add(outputPath);

        return args;
    }

    public static string BuildOutputPath(string inputPath, CreateGifSettings settings, double sourceDurationSeconds)
    {
        var suffix = settings.IsCustomRange(sourceDurationSeconds)
            ? $"_gif_{FormatTimeForFilename(settings.StartSeconds)}_for_{FormatTimeForFilename(settings.DurationSeconds)}"
            : "_gif";

        return OutputPathHelper.CreateUniqueOutputPath(inputPath, suffix, ".gif");
    }

    public static GifQualityProfile GetQualityProfile(string qualityKey)
    {
        return qualityKey.ToLowerInvariant() switch
        {
            "high" => new GifQualityProfile("GIF High", "lanczos", "max_colors=256:stats_mode=full", "dither=sierra2_4a"),
            "balanced" => new GifQualityProfile("GIF Balanced", "bicubic", "max_colors=192:stats_mode=diff", "dither=sierra2_4a"),
            "small" => new GifQualityProfile("GIF Small", "bicubic", "max_colors=128:stats_mode=diff", "dither=bayer:bayer_scale=3"),
            _ => throw new InvalidOperationException(MediaActionMessages.CreateGifQualityInvalid())
        };
    }

    private static string GetScaleFilter(string resolutionKey, string scaleFlags)
    {
        return resolutionKey switch
        {
            "original" => $"scale=iw:-1:flags={scaleFlags}",
            "1280" => $"scale='if(gt(iw\\,1280)\\,1280\\,iw)':-1:flags={scaleFlags}",
            "960" => $"scale='if(gt(iw\\,960)\\,960\\,iw)':-1:flags={scaleFlags}",
            "720" => $"scale='if(gt(iw\\,720)\\,720\\,iw)':-1:flags={scaleFlags}",
            "540" => $"scale='if(gt(iw\\,540)\\,540\\,iw)':-1:flags={scaleFlags}",
            "360" => $"scale='if(gt(iw\\,360)\\,360\\,iw)':-1:flags={scaleFlags}",
            _ => throw new InvalidOperationException(MediaActionMessages.CreateGifResolutionInvalid())
        };
    }

    private static long EstimateFrameCount(CreateGifSettings settings)
    {
        return Math.Max(1L, (long)Math.Ceiling(settings.DurationSeconds * settings.Fps));
    }

    private static string FormatTimeForFilename(double seconds)
    {
        var safeSeconds = Math.Max(0d, seconds);
        var hours = (int)Math.Floor(safeSeconds / 3600d);
        var remaining = safeSeconds - (hours * 3600d);
        var minutes = (int)Math.Floor(remaining / 60d);
        var secs = (int)Math.Floor(remaining - (minutes * 60d));
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}-{1:D2}-{2:D2}", hours, minutes, secs);
    }
}

public sealed record GifQualityProfile(
    string ModeLabel,
    string ScaleFlags,
    string PaletteGen,
    string PaletteUse);
