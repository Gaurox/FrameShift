using System;
using System.Collections.Generic;
using System.Linq;

namespace FrameShift.Core.AI.RemoveBackground;

internal static class BackgroundRemovalModelCatalog
{
    private static readonly BackgroundRemovalModelDefinition[] s_models =
    [
        new(
            "fast",
            "BiRefNet Lite FP16 (Fast)",
            "birefnet_lite-onnx",
            "model_fp16.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/birefnet_lite-onnx/model_fp16.onnx",
            "D39B897CEB16AE654C1731F3DBA0CF9B368D9CAE74B5A57459B455CC8BFEC402",
            114_538_221L,
            "MIT License — Free for commercial and non-commercial use",
            1024),
        new(
            "high-resolution",
            "BiRefNet HR Matting (High Resolution Matting)",
            "birefnet_hr-matting-onnx",
            "BiRefNet_HR-matting-epoch_135.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/birefnet_hr-matting-onnx/BiRefNet_HR-matting-epoch_135.onnx",
            "45B7D92CE0E2E75E1C8A564A9E84C87AC9DA7F4315D2DEEEF3674864E809AEF3",
            1_098_928_867L,
            "MIT License — Official BiRefNet upstream weights and ONNX mirror",
            2048,
            ForceCpu: true),
        new(
            "high-resolution-general",
            "BiRefNet HR General (High Resolution General)",
            "birefnet_hr-general-onnx",
            "BiRefNet_HR-general-epoch_130.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/birefnet_hr-general-onnx/BiRefNet_HR-general-epoch_130.onnx",
            "DB0217E99B25E0C4F6F4DCA2892FF1F7EA7ABA38FB6AD84F93122A4024BE536A",
            1_098_928_953L,
            "MIT License — Official BiRefNet upstream weights and ONNX mirror",
            2048,
            ForceCpu: true),
        // BRIA RMBG-2.0 — user-supplied, NOT distributed by FrameShift.
        // The model is gated and licensed CC BY-NC 4.0 (non-commercial). The user must
        // obtain the ONNX file manually from the official BRIA page (see InfoPageUrl).
        // DownloadUrl is intentionally empty: FrameShift never downloads or mirrors it.
        // SHA256 values are the content hashes of the official onnx/model_fp16.onnx and
        // onnx/model.onnx files (briaai/RMBG-2.0), each cross-checked against the exact
        // official byte size (513,576,499 / 1,024,331,469). Verified 2026-06-03. Note: the
        // HF tree API lfs.oid is NOT the content SHA256 for these Xet-backed files.
        new(
            "bria-balanced",
            "Remove Background (BRIA Balanced)",
            @"RemoveBackground\BriaBalanced",
            "model_fp16.onnx",
            "",
            "9DC47DB40D113090BA5D7A13D8FCFD9EE4EDA510CE92613219B2FE19DA4746F6",
            513_576_499L,
            "BRIA RMBG-2.0 — CC BY-NC 4.0 (non-commercial). User-supplied; not distributed by FrameShift.",
            1024,
            ManualOnly: true,
            InfoPageUrl: "https://huggingface.co/briaai/RMBG-2.0/tree/main",
            ModeLabel: "BRIA Balanced",
            OutputIsAlpha: true),
        new(
            "bria-high-quality",
            "Remove Background (BRIA High Quality)",
            @"RemoveBackground\BriaHighQuality",
            "model.onnx",
            "",
            "5B486F08200F513F460DA46DD701DB5FBB47D79B4BE4B708A19444BCD4E79958",
            1_024_331_469L,
            "BRIA RMBG-2.0 — CC BY-NC 4.0 (non-commercial). User-supplied; not distributed by FrameShift.",
            1024,
            ManualOnly: true,
            InfoPageUrl: "https://huggingface.co/briaai/RMBG-2.0/tree/main",
            ModeLabel: "BRIA High Quality",
            OutputIsAlpha: true)
    ];

    public static IReadOnlyList<BackgroundRemovalModelDefinition> GetAll() => s_models;

    public static BackgroundRemovalModelDefinition GetDefault() =>
        GetById("fast") ?? s_models[0];

    public static BackgroundRemovalModelDefinition? GetById(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        return s_models.FirstOrDefault(model =>
            string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }
}
