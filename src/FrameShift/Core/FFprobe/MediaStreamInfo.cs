namespace FrameShift.Core.FFprobe;

public sealed record MediaStreamInfo(
    int Index,
    string CodecType,
    string? CodecName);
