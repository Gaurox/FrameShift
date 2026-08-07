using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;

namespace FrameShift.Core.FFprobe;

public sealed class FfprobeRunner
{
    private readonly AppLogger _logger;

    public FfprobeRunner(AppLogger logger)
    {
        _logger = logger;
    }

    public ProcessStartInfo CreateStartInfo(string executablePath, IReadOnlyCollection<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public async Task<MediaProbeAttemptResult> TryProbeMediaAsync(
        string executablePath,
        string inputPath,
        CancellationToken cancellationToken)
    {
        var arguments = new[]
        {
            "-v", "error",
            "-show_entries", "format=duration:stream=index,codec_type,codec_name,width,height,avg_frame_rate,r_frame_rate,nb_frames,duration,sample_rate,channels,pix_fmt,color_space,color_transfer,color_primaries,profile,bits_per_raw_sample:stream_tags=rotate:stream_side_data=rotation",
            "-of", "json",
            inputPath
        };

        var result = await RunProbeAsync(executablePath, arguments, cancellationToken).ConfigureAwait(false);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            return new MediaProbeAttemptResult(null, ConversionActionHelper.GetFriendlyProbeError(result.StandardError));
        }

        try
        {
            var document = JsonDocument.Parse(result.Output);
            if (!document.RootElement.TryGetProperty("streams", out var streamsElement))
            {
                return new MediaProbeAttemptResult(null, ConversionActionHelper.GetFriendlyProbeError(result.StandardError));
            }

            var streams = new List<MediaStreamInfo>();
            var audioCodecs = new List<string>();
            var subtitleCodecs = new List<string>();
            var videoCodec = default(string);
            var videoWidth = 0;
            var videoHeight = 0;
            double? videoFrameRate = null;
            long? videoFrameCount = null;
            double? videoStreamDurationSeconds = null;
            var primaryAudioSampleRate = 0;
            var primaryAudioChannels = 0;
            var rotationDegrees = 0;
            var videoPixelFormat = default(string);
            var videoColorSpace = default(string);
            var videoColorTransfer = default(string);
            var videoColorPrimaries = default(string);
            var videoProfile = default(string);
            int? videoBitDepth = null;

            foreach (var streamElement in streamsElement.EnumerateArray())
            {
                var index = streamElement.TryGetProperty("index", out var indexElement)
                    ? indexElement.GetInt32()
                    : -1;
                var codecType = streamElement.TryGetProperty("codec_type", out var typeElement)
                    ? typeElement.GetString() ?? string.Empty
                    : string.Empty;
                var codecName = streamElement.TryGetProperty("codec_name", out var codecElement)
                    ? codecElement.GetString()
                    : null;

                streams.Add(new MediaStreamInfo(index, codecType, codecName));

                if (string.Equals(codecType, "audio", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(codecName))
                {
                    audioCodecs.Add(codecName.ToLowerInvariant());

                    if (primaryAudioSampleRate == 0 &&
                        streamElement.TryGetProperty("sample_rate", out var srElement) &&
                        int.TryParse(srElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSampleRate) &&
                        parsedSampleRate > 0)
                    {
                        primaryAudioSampleRate = parsedSampleRate;
                    }

                    if (primaryAudioChannels == 0 &&
                        streamElement.TryGetProperty("channels", out var chElement) &&
                        chElement.TryGetInt32(out var parsedChannels) &&
                        parsedChannels > 0)
                    {
                        primaryAudioChannels = parsedChannels;
                    }
                }
                else if (string.Equals(codecType, "subtitle", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(codecName))
                {
                    subtitleCodecs.Add(codecName.ToLowerInvariant());
                }
                else if (string.Equals(codecType, "video", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(videoCodec))
                {
                    videoCodec = codecName?.ToLowerInvariant();
                    videoPixelFormat = GetStringValue(streamElement, "pix_fmt");
                    videoColorSpace = GetStringValue(streamElement, "color_space");
                    videoColorTransfer = GetStringValue(streamElement, "color_transfer");
                    videoColorPrimaries = GetStringValue(streamElement, "color_primaries");
                    videoProfile = GetStringValue(streamElement, "profile");
                    videoBitDepth = TryParseBitDepth(GetStringValue(streamElement, "bits_per_raw_sample"));
                    rotationDegrees = TryGetRotationDegrees(streamElement);

                    if (streamElement.TryGetProperty("width", out var widthElement))
                    {
                        videoWidth = widthElement.GetInt32();
                    }

                    if (streamElement.TryGetProperty("height", out var heightElement))
                    {
                        videoHeight = heightElement.GetInt32();
                    }

                    if (streamElement.TryGetProperty("avg_frame_rate", out var avgFrameRateElement))
                    {
                        videoFrameRate = ParseFrameRate(avgFrameRateElement.GetString());
                    }

                    if (videoFrameRate is null &&
                        streamElement.TryGetProperty("r_frame_rate", out var frameRateElement))
                    {
                        videoFrameRate = ParseFrameRate(frameRateElement.GetString());
                    }

                    if (streamElement.TryGetProperty("nb_frames", out var frameCountElement) &&
                        long.TryParse(frameCountElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFrameCount) &&
                        parsedFrameCount > 0)
                    {
                        videoFrameCount = parsedFrameCount;
                    }

                    if (streamElement.TryGetProperty("duration", out var streamDurationElement) &&
                        double.TryParse(streamDurationElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedStreamDuration) &&
                        parsedStreamDuration > 0)
                    {
                        videoStreamDurationSeconds = parsedStreamDuration;
                    }
                }
            }

            TimeSpan? duration = null;
            if (document.RootElement.TryGetProperty("format", out var formatElement) &&
                formatElement.TryGetProperty("duration", out var durationElement))
            {
                var durationText = durationElement.GetString();
                if (!string.IsNullOrWhiteSpace(durationText) &&
                    double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                    seconds > 0)
                {
                    duration = TimeSpan.FromSeconds(seconds);
                }
            }

            var durationSeconds = duration?.TotalSeconds ?? videoStreamDurationSeconds;
            if (videoFrameCount is null &&
                videoFrameRate is not null &&
                durationSeconds is not null &&
                durationSeconds.Value > 0)
            {
                var estimatedFrameCount = (long)Math.Round(videoFrameRate.Value * durationSeconds.Value);
                if (estimatedFrameCount > 0)
                {
                    videoFrameCount = estimatedFrameCount;
                }
            }

            return new MediaProbeAttemptResult(new MediaProbeResult(
                duration,
                audioCodecs.Count > 0,
                streams.Any(stream => string.Equals(stream.CodecType, "video", StringComparison.OrdinalIgnoreCase)),
                videoWidth,
                videoHeight,
                videoCodec,
                videoFrameRate,
                videoFrameCount,
                audioCodecs,
                subtitleCodecs,
                streams)
            {
                PrimaryAudioSampleRate = primaryAudioSampleRate,
                PrimaryAudioChannels = primaryAudioChannels,
                RotationDegrees = rotationDegrees,
                VideoPixelFormat = videoPixelFormat,
                VideoColorSpace = videoColorSpace,
                VideoColorTransfer = videoColorTransfer,
                VideoColorPrimaries = videoColorPrimaries,
                VideoProfile = videoProfile,
                VideoBitDepth = videoBitDepth
            }, null);
        }
        catch (Exception ex)
        {
            _logger.Log($"ffprobe parse failed: {ex.Message}");
            return new MediaProbeAttemptResult(null, ConversionActionHelper.GetFriendlyProbeError(result.StandardError));
        }
    }

    public async Task<(MediaInfoData? Data, string? Error)> TryProbeMediaInfoAsync(
        string executablePath,
        string inputPath,
        long fileSize,
        CancellationToken cancellationToken)
    {
        var arguments = new[]
        {
            "-v",
            "quiet",
            "-print_format",
            "json",
            "-show_streams",
            "-show_format",
            inputPath
        };

        var result = await RunProbeAsync(executablePath, arguments, cancellationToken).ConfigureAwait(false);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            _logger.Log($"FfprobeRunner: media info ffprobe failed. {result.StandardError}");
            return (null, "Impossible de lire les informations du fichier.");
        }

        try
        {
            return (ParseMediaInfoData(result.Output, fileSize), null);
        }
        catch (Exception ex)
        {
            _logger.Log($"FfprobeRunner: media info parse error: {ex.Message}");
            return (null, "Impossible d'analyser les informations du fichier.");
        }
    }

    private static MediaInfoData ParseMediaInfoData(string json, long fileSize)
    {
        var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        string? formatName = null;
        string? formatLongName = null;
        string? bitRate = null;
        string? duration = null;
        string? tagEncoder = null;
        string? tagCreationTime = null;
        var videoStreams = new List<MediaInfoVideoStream>();
        var audioStreams = new List<MediaInfoAudioStream>();

        if (root.TryGetProperty("format", out var fmt))
        {
            formatName = GetMediaInfoString(fmt, "format_name");
            formatLongName = GetMediaInfoString(fmt, "format_long_name");
            bitRate = GetMediaInfoString(fmt, "bit_rate");
            duration = GetMediaInfoString(fmt, "duration");

            if (fmt.TryGetProperty("tags", out var tags))
            {
                tagEncoder = GetMediaInfoString(tags, "encoder");
                tagCreationTime = GetMediaInfoString(tags, "creation_time");
            }
        }

        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var codecType = GetMediaInfoString(stream, "codec_type");
                var index = stream.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0;

                if (string.Equals(codecType, "video", StringComparison.OrdinalIgnoreCase))
                {
                    int? width = null;
                    int? height = null;

                    if (stream.TryGetProperty("width", out var w) &&
                        w.ValueKind == JsonValueKind.Number &&
                        w.GetInt32() > 0)
                    {
                        width = w.GetInt32();
                    }

                    if (stream.TryGetProperty("height", out var h) &&
                        h.ValueKind == JsonValueKind.Number &&
                        h.GetInt32() > 0)
                    {
                        height = h.GetInt32();
                    }

                    string? tagLanguage = null;
                    string? tagStreamEncoder = null;
                    string? tagStreamCreationTime = null;

                    if (stream.TryGetProperty("tags", out var vtags))
                    {
                        tagLanguage = GetMediaInfoString(vtags, "language");
                        tagStreamEncoder = GetMediaInfoString(vtags, "encoder");
                        tagStreamCreationTime = GetMediaInfoString(vtags, "creation_time");
                    }

                    videoStreams.Add(new MediaInfoVideoStream
                    {
                        Index = index,
                        CodecName = GetMediaInfoString(stream, "codec_name"),
                        CodecTagString = GetMediaInfoString(stream, "codec_tag_string"),
                        Profile = GetMediaInfoString(stream, "profile"),
                        Width = width,
                        Height = height,
                        DisplayAspectRatio = GetMediaInfoString(stream, "display_aspect_ratio"),
                        PixFmt = GetMediaInfoString(stream, "pix_fmt"),
                        BitsPerRawSample = GetMediaInfoString(stream, "bits_per_raw_sample"),
                        ColorRange = GetMediaInfoString(stream, "color_range"),
                        ColorSpace = GetMediaInfoString(stream, "color_space"),
                        AvgFrameRate = GetMediaInfoString(stream, "avg_frame_rate"),
                        BitRate = GetMediaInfoString(stream, "bit_rate"),
                        TagLanguage = tagLanguage,
                        TagEncoder = tagStreamEncoder,
                        TagCreationTime = tagStreamCreationTime
                    });
                }
                else if (string.Equals(codecType, "audio", StringComparison.OrdinalIgnoreCase))
                {
                    string? tagLanguage = null;
                    string? tagStreamCreationTime = null;

                    if (stream.TryGetProperty("tags", out var atags))
                    {
                        tagLanguage = GetMediaInfoString(atags, "language");
                        tagStreamCreationTime = GetMediaInfoString(atags, "creation_time");
                    }

                    audioStreams.Add(new MediaInfoAudioStream
                    {
                        Index = index,
                        CodecName = GetMediaInfoString(stream, "codec_name"),
                        CodecTagString = GetMediaInfoString(stream, "codec_tag_string"),
                        BitRate = GetMediaInfoString(stream, "bit_rate"),
                        Channels = GetMediaInfoString(stream, "channels"),
                        ChannelLayout = GetMediaInfoString(stream, "channel_layout"),
                        SampleRate = GetMediaInfoString(stream, "sample_rate"),
                        TagLanguage = tagLanguage,
                        TagCreationTime = tagStreamCreationTime
                    });
                }
            }
        }

        return new MediaInfoData
        {
            FormatName = formatName,
            FormatLongName = formatLongName,
            BitRate = bitRate,
            Duration = duration,
            TagEncoder = tagEncoder,
            TagCreationTime = tagCreationTime,
            FileSize = fileSize,
            VideoStreams = videoStreams,
            AudioStreams = audioStreams
        };
    }

    private static string? GetMediaInfoString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static double? ParseFrameRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("0/0", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = value.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
            numerator > 0 &&
            denominator > 0)
        {
            return numerator / denominator;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var frameRate) &&
            frameRate > 0)
        {
            return frameRate;
        }

        return null;
    }

    private static string? GetStringValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ValueKind == JsonValueKind.Number
                ? value.GetRawText()
                : null;
    }

    private static int? TryParseBitDepth(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBitDepth) &&
            parsedBitDepth > 0)
        {
            return parsedBitDepth;
        }

        return null;
    }

    private static int TryGetRotationDegrees(JsonElement streamElement)
    {
        if (streamElement.TryGetProperty("side_data_list", out var sideDataList) &&
            sideDataList.ValueKind == JsonValueKind.Array)
        {
            foreach (var sideData in sideDataList.EnumerateArray())
            {
                if (sideData.TryGetProperty("rotation", out var rotationElement) &&
                    TryParseRotationDegrees(rotationElement, out var rotationFromSideData))
                {
                    return rotationFromSideData;
                }
            }
        }

        if (streamElement.TryGetProperty("tags", out var tagsElement) &&
            tagsElement.TryGetProperty("rotate", out var rotateElement) &&
            TryParseRotationDegrees(rotateElement, out var rotationFromTags))
        {
            return rotationFromTags;
        }

        return 0;
    }

    private static bool TryParseRotationDegrees(JsonElement rotationElement, out int rotationDegrees)
    {
        rotationDegrees = 0;
        return rotationElement.ValueKind switch
        {
            JsonValueKind.Number => rotationElement.TryGetInt32(out rotationDegrees),
            JsonValueKind.String => int.TryParse(rotationElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out rotationDegrees),
            _ => false
        };
    }

    public async Task<TimeSpan?> TryGetDurationAsync(
        string executablePath,
        string inputPath,
        CancellationToken cancellationToken)
    {
        var probeAttempt = await TryProbeMediaAsync(executablePath, inputPath, cancellationToken).ConfigureAwait(false);
        return probeAttempt.Probe?.Duration;
    }

    internal async Task<ProbeResult> RunProbeAsync(
        string executablePath,
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process { StartInfo = CreateStartInfo(executablePath, arguments) };

        process.Start();

        var cancellationSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            _logger.Log($"FfprobeRunner: cancellation requested. pid={TryGetProcessId(process)}.");
            TryKill(process, "cancellation");
            cancellationSignal.TrySetResult(true);
        });

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var waitForExitTask = process.WaitForExitAsync();

        if (await Task.WhenAny(waitForExitTask, cancellationSignal.Task).ConfigureAwait(false) != waitForExitTask)
        {
            var processExited = await WaitForExitAfterCancellationAsync(process, waitForExitTask).ConfigureAwait(false);
            if (!processExited)
            {
                throw new TimeoutException("FFprobe process did not exit after cancellation.");
            }
        }
        else
        {
            await waitForExitTask.ConfigureAwait(false);
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = (await errorTask.ConfigureAwait(false)).Trim();

        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(error))
        {
            _logger.Log(error);
        }

        return new ProbeResult(process.ExitCode == 0, output, error);
    }

    private void TryKill(Process process, string reason)
    {
        try
        {
            _logger.Log($"FfprobeRunner: kill process requested. pid={TryGetProcessId(process)}, reason={reason}, hasExited={process.HasExited}.");
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            _logger.Log($"FfprobeRunner: kill process returned. pid={TryGetProcessId(process)}, reason={reason}, hasExited={process.HasExited}.");
        }
        catch (Exception ex)
        {
            _logger.Log($"FfprobeRunner: kill process failed. pid={TryGetProcessId(process)}, reason={reason}, error={ex}.");
        }
    }

    private async Task<bool> WaitForExitAfterCancellationAsync(Process process, Task? pendingWaitForExitTask = null)
    {
        _logger.Log($"FfprobeRunner: WaitForExitAsync started after cancellation. pid={TryGetProcessId(process)}.");
        var waitForExitTask = pendingWaitForExitTask ?? process.WaitForExitAsync();
        var completedTask = await Task.WhenAny(waitForExitTask, Task.Delay(5000)).ConfigureAwait(false);
        if (completedTask == waitForExitTask)
        {
            await waitForExitTask.ConfigureAwait(false);
            _logger.Log($"FfprobeRunner: WaitForExitAsync finished after cancellation. pid={TryGetProcessId(process)}, exitCode={process.ExitCode}.");
            return true;
        }

        _logger.Log($"FfprobeRunner: WaitForExitAsync timeout after cancellation — forcing kill. pid={TryGetProcessId(process)}.");
        TryKill(process, "post-cancellation-timeout");

        _logger.Log($"FfprobeRunner: WaitForExitAsync started after second kill. pid={TryGetProcessId(process)}.");
        completedTask = await Task.WhenAny(waitForExitTask, Task.Delay(5000)).ConfigureAwait(false);
        if (completedTask == waitForExitTask)
        {
            await waitForExitTask.ConfigureAwait(false);
            _logger.Log($"FfprobeRunner: WaitForExitAsync finished after second kill. pid={TryGetProcessId(process)}, exitCode={process.ExitCode}.");
            return true;
        }

        _logger.Log($"FfprobeRunner: WaitForExitAsync did not finish after second kill. pid={TryGetProcessId(process)}.");
        return false;
    }

    private static int TryGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return -1;
        }
    }

    internal sealed record ProbeResult(bool Success, string Output, string StandardError);
}

public sealed record MediaProbeAttemptResult(MediaProbeResult? Probe, string? ErrorMessage)
{
    public bool Success => Probe is not null;
}
