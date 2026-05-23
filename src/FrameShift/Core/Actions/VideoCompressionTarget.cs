namespace FrameShift.Core.Actions;

public sealed record VideoCompressionTarget(
    long TargetBytes,
    int VideoBitrateKbps,
    int AudioBitrateKbps,
    int BufferSizeKbps);
