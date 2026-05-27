namespace FrameShift.Core.AI.VideoInterpolation;

internal sealed record RifeModelDefinition(
    string Id,
    string DisplayName,
    string FileName,
    string DownloadUrl,
    string ExpectedSha256,
    long ExpectedSizeBytes,
    string License,
    int[] SupportedMultipliers);
