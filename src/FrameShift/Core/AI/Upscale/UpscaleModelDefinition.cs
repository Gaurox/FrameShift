namespace FrameShift.Core.AI.Upscale;

internal sealed record UpscaleModelDefinition(
    string Id,
    string DisplayName,
    string Folder,
    string FileName,
    string DownloadUrl,
    string ExpectedSha256,
    long ExpectedSizeBytes,
    string License,
    int ScaleFactor = 4,
    bool ForceCpu = false,
    // ONNX tensor names. Real-ESRGAN uses "input"/"output"; Swin2SR uses "pixel_values"/"reconstruction".
    string InputTensorName = "input",
    string OutputTensorName = "output",
    // When > 0, the model requires input height/width to be a multiple of this value (Swin2SR = 8).
    // The engine pads each tile to that multiple (edge-replicated) and crops the result back.
    int WindowMultiple = 0,
    // Short one-line description shown in the model picker.
    string Summary = "");
