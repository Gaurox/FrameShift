using System;
using System.Collections.Generic;

namespace FrameShift.Core.FFprobe;

public sealed record MediaProbeResult(
    TimeSpan? Duration,
    bool HasAudio,
    bool HasVideo,
    int VideoWidth,
    int VideoHeight,
    string? VideoCodec,
    double? VideoFrameRate,
    long? EstimatedVideoFrameCount,
    IReadOnlyList<string> AudioCodecs,
    IReadOnlyList<string> SubtitleCodecs,
    IReadOnlyList<MediaStreamInfo> Streams)
{
    public bool HasSubtitles => SubtitleCodecs.Count > 0;

    public int PrimaryAudioSampleRate { get; init; } = 0;

    public int PrimaryAudioChannels { get; init; } = 0;

    public int RotationDegrees { get; init; } = 0;

    public string? VideoPixelFormat { get; init; }

    public string? VideoColorSpace { get; init; }

    public string? VideoColorTransfer { get; init; }

    public string? VideoColorPrimaries { get; init; }

    public string? VideoProfile { get; init; }

    public int? VideoBitDepth { get; init; }

    public int DisplayVideoWidth => RequiresDisplayDimensionSwap ? VideoHeight : VideoWidth;

    public int DisplayVideoHeight => RequiresDisplayDimensionSwap ? VideoWidth : VideoHeight;

    public bool HasDisplayRotation => RotationDegrees != 0;

    public bool RequiresDisplayDimensionSwap => Math.Abs(NormalizeRotationDegrees(RotationDegrees)) is 90 or 270;

    public bool IsHdrLikely
    {
        get
        {
            var normalizedTransfer = NormalizeText(VideoColorTransfer);
            var normalizedPrimaries = NormalizeText(VideoColorPrimaries);
            var normalizedSpace = NormalizeText(VideoColorSpace);
            var normalizedPixelFormat = NormalizeText(VideoPixelFormat);
            var normalizedProfile = NormalizeText(VideoProfile);

            return normalizedTransfer is "smpte2084" or "arib-std-b67" ||
                   normalizedPrimaries == "bt2020" ||
                   normalizedSpace is "bt2020nc" or "bt2020c" ||
                   normalizedProfile.Contains("main 10", StringComparison.Ordinal) ||
                   (VideoBitDepth ?? 0) >= 10 ||
                   normalizedPixelFormat.Contains("10", StringComparison.Ordinal) ||
                   normalizedPixelFormat.Contains("12", StringComparison.Ordinal);
        }
    }

    public string GetDisplayGeometrySummary()
    {
        var width = DisplayVideoWidth > 0 ? DisplayVideoWidth : VideoWidth;
        var height = DisplayVideoHeight > 0 ? DisplayVideoHeight : VideoHeight;
        if (width <= 0 || height <= 0)
        {
            return "unknown";
        }

        return HasDisplayRotation
            ? $"{width} x {height} (rotation {RotationDegrees:+#;-#;0}°)"
            : $"{width} x {height}";
    }

    private static int NormalizeRotationDegrees(int value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static string NormalizeText(string? value)
    {
        return value?.Trim().ToLowerInvariant() ?? string.Empty;
    }
}
