using System.Collections.Generic;

namespace FrameShift.Core.FFprobe;

public sealed class MediaInfoData
{
    public string? FormatName { get; init; }
    public string? FormatLongName { get; init; }
    public string? BitRate { get; init; }
    public string? Duration { get; init; }
    public string? TagEncoder { get; init; }
    public string? TagCreationTime { get; init; }
    public long FileSize { get; init; }
    public IReadOnlyList<MediaInfoVideoStream> VideoStreams { get; init; } = [];
    public IReadOnlyList<MediaInfoAudioStream> AudioStreams { get; init; } = [];
}

public sealed class MediaInfoVideoStream
{
    public int Index { get; init; }
    public string? CodecName { get; init; }
    public string? CodecTagString { get; init; }
    public string? Profile { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? DisplayAspectRatio { get; init; }
    public string? PixFmt { get; init; }
    public string? BitsPerRawSample { get; init; }
    public string? ColorRange { get; init; }
    public string? ColorSpace { get; init; }
    public string? AvgFrameRate { get; init; }
    public string? BitRate { get; init; }
    public string? TagLanguage { get; init; }
    public string? TagEncoder { get; init; }
    public string? TagCreationTime { get; init; }
}

public sealed class MediaInfoAudioStream
{
    public int Index { get; init; }
    public string? CodecName { get; init; }
    public string? CodecTagString { get; init; }
    public string? BitRate { get; init; }
    public string? Channels { get; init; }
    public string? ChannelLayout { get; init; }
    public string? SampleRate { get; init; }
    public string? TagLanguage { get; init; }
    public string? TagCreationTime { get; init; }
}
