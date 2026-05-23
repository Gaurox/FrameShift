namespace FrameShift.Core.Actions;

public sealed record VideoCompressionProfile(
    string Id,
    string DisplayName,
    string Description,
    string FFmpegPreset,
    int VideoCrf,
    int AudioBitrateKbps) : IConversionChoice;
