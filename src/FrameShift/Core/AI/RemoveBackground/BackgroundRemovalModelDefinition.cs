namespace FrameShift.Core.AI.RemoveBackground;

public sealed record BackgroundRemovalModelDefinition(
    string Id,
    string DisplayName,
    string Folder,
    string FileName,
    string DownloadUrl,
    string ExpectedSha256,
    long ExpectedSizeBytes,
    string License,
    int InputSize,
    bool ForceCpu = false,
    // ManualOnly models are user-supplied: FrameShift never downloads, mirrors or
    // redistributes them. InfoPageUrl is the official page the user must visit to
    // obtain the file manually. ModeLabel overrides the progress/UI label.
    bool ManualOnly = false,
    string? InfoPageUrl = null,
    string? ModeLabel = null,
    // When true, the model output is already a [0,1] alpha matte (sigmoid baked into the
    // graph, e.g. the BRIA RMBG-2.0 transformers.js export whose output is named "alphas")
    // and must NOT be passed through sigmoid again. When false (BiRefNet upstream weights),
    // the output is raw logits and the engine applies sigmoid.
    bool OutputIsAlpha = false);
