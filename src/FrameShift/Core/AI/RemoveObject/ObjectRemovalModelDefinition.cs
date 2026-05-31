namespace FrameShift.Core.AI.RemoveObject;

internal sealed record ObjectRemovalModelDefinition(
    string Id,
    string DisplayName,
    string Folder,
    string FileName,
    string DownloadUrl,
    string ExpectedSha256,
    long ExpectedSizeBytes,
    string License,
    int InputSize = 512);
