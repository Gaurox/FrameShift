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
}
