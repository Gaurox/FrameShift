using System;

namespace FrameShift.Core.FFprobe;

/// <summary>
/// Deliberately narrow FFprobe result used only by Join Videos.  The generic
/// media probe stays small because concat-copy needs a stricter stream
/// signature than most actions.
/// </summary>
public sealed record JoinVideoProbeResult(
    TimeSpan? Duration,
    string? FormatName,
    int VideoStreamCount,
    int AudioStreamCount,
    int OtherStreamCount,
    JoinVideoStreamInfo? Video,
    JoinAudioStreamInfo? Audio)
{
    public bool HasVideo => Video is not null;

    public bool HasAudio => Audio is not null;

    public int DisplayVideoWidth => Video is null || !RequiresDisplayDimensionSwap
        ? Video?.Width ?? 0
        : Video.Height;

    public int DisplayVideoHeight => Video is null || !RequiresDisplayDimensionSwap
        ? Video?.Height ?? 0
        : Video.Width;

    public bool RequiresDisplayDimensionSwap => Video is not null &&
        Math.Abs(NormalizeRotation(Video.RotationDegrees)) is 90 or 270;

    public bool IsHdrLikely => Video?.IsHdrLikely ?? false;

    public double DurationSeconds => Duration?.TotalSeconds ?? 0d;

    private static int NormalizeRotation(int rotation)
    {
        var normalized = rotation % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}

public sealed record JoinVideoStreamInfo(
    string? CodecName,
    string? CodecTag,
    string? Profile,
    string? Level,
    int Width,
    int Height,
    string? PixelFormat,
    string? ColorSpace,
    string? ColorTransfer,
    string? ColorPrimaries,
    string? ColorRange,
    string? FieldOrder,
    string? TimeBase,
    string? FrameRate,
    string? SampleAspectRatio,
    string? StartTime,
    string? ExtraDataHash,
    int RotationDegrees)
{
    public bool IsHdrLikely
    {
        get
        {
            var transfer = Normalize(ColorTransfer);
            var primaries = Normalize(ColorPrimaries);
            var space = Normalize(ColorSpace);
            var pixelFormat = Normalize(PixelFormat);
            var profile = Normalize(Profile);

            return transfer is "smpte2084" or "arib-std-b67" ||
                   primaries == "bt2020" ||
                   space is "bt2020nc" or "bt2020c" ||
                   profile.Contains("main 10", StringComparison.Ordinal) ||
                   pixelFormat.Contains("10", StringComparison.Ordinal) ||
                   pixelFormat.Contains("12", StringComparison.Ordinal);
        }
    }

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
}

public sealed record JoinAudioStreamInfo(
    string? CodecName,
    string? CodecTag,
    string? Profile,
    string? SampleFormat,
    string? SampleRate,
    int Channels,
    string? ChannelLayout,
    string? TimeBase,
    string? StartTime,
    string? ExtraDataHash);
