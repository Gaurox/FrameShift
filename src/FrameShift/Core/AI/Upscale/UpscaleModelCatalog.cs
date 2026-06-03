using System;
using System.Collections.Generic;
using System.Linq;

namespace FrameShift.Core.AI.Upscale;

internal static class UpscaleModelCatalog
{
    // ---------------------------------------------------------------------------------------------
    // RELEASE BLOCKER — DO NOT SHIP UNTIL RESOLVED
    //
    // The Real-ESRGAN x4plus FP16 model is NOT yet hosted on Gaurox/frameshift-models.
    // Until it is uploaded and its SHA256 is confirmed:
    //   1. The DownloadUrl below points to the EXPECTED final Gaurox location (not yet live).
    //   2. ExpectedSha256 is a PLACEHOLDER (see PlaceholderSha256). While it is the placeholder,
    //      integrity verification is intentionally bypassed (see ModelDownloader.IsModelFileValid)
    //      so a manually-placed model can be tested locally, but auto-download MUST NOT be trusted.
    //
    // BEFORE RELEASE:
    //   - upload realesrgan_x4plus_fp16.onnx to the Gaurox repo under "upscale-onnx/"
    //   - compute the final SHA256 of the uploaded file
    //   - replace PlaceholderSha256 usage below with the real uppercase hex hash
    //   - never hardcode a FuryTMP URL here
    // ---------------------------------------------------------------------------------------------
    public const string PlaceholderSha256 = "PLACEHOLDER_SHA256_REPLACE_AFTER_GAUROX_UPLOAD";

    public static bool IsSha256Placeholder(string sha256) =>
        string.Equals(sha256, PlaceholderSha256, StringComparison.OrdinalIgnoreCase);

    private static readonly UpscaleModelDefinition[] s_models =
    [
        new(
            "realesrgan-x4plus",
            "Real-ESRGAN x4plus",
            "upscale-onnx",
            "realesrgan_x4plus_fp16.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/upscale-onnx/realesrgan_x4plus_fp16.onnx",
            // SHA256 of the file hosted on Gaurox/frameshift-models (uploaded 2026-06-03).
            "37651C96722D0156AAEE27404F31FBB62E93B4AB4AB9E9DB07DA2200500232AC",
            // FP16 weights, float32 I/O. ~33.7 MB.
            33_748_852L,
            "BSD-3-Clause (Real-ESRGAN, © Xintao Wang)",
            ScaleFactor: 4,
            ForceCpu: false,
            Summary: "General photos, screenshots, AI images. Fast, balanced default."),

        new(
            "realesrgan-anime",
            "Real-ESRGAN Anime 6B",
            "upscale-onnx",
            "realesrgan_x4plus_anime_6b.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/upscale-onnx/realesrgan_x4plus_anime_6b.onnx",
            "1BCCABC8E813AE287057C6DD342458C355E4505ABE36B5E0B2B94807D39D40DF",
            // FP32, exported from the official RealESRGAN_x4plus_anime_6B.pth. ~17.9 MB.
            17_939_940L,
            "BSD-3-Clause (Real-ESRGAN, © Xintao Wang)",
            ScaleFactor: 4,
            ForceCpu: false,
            Summary: "Anime, illustrations, line art. Cleaner edges on drawn content."),

        new(
            "swin2sr-quality",
            "Swin2SR (Quality)",
            "upscale-onnx",
            "swin2sr_realworld_x4.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/upscale-onnx/swin2sr_realworld_x4.onnx",
            "987D88B356554161CBB8F67B7A8F4162CAD6DC147839C344E3D5142140F25D6F",
            // FP32 transformer. ~53.8 MB. Slower; best fidelity / restoration.
            53_827_735L,
            "Apache-2.0 (Swin2SR, mv-lab)",
            ScaleFactor: 4,
            ForceCpu: false,
            InputTensorName: "pixel_values",
            OutputTensorName: "reconstruction",
            WindowMultiple: 8,
            Summary: "Restoration / anti-JPEG. Highest fidelity, noticeably slower."),
    ];

    public static IReadOnlyList<UpscaleModelDefinition> GetAll() => s_models;

    public static UpscaleModelDefinition GetDefault() =>
        GetById("realesrgan-x4plus") ?? s_models[0];

    public static UpscaleModelDefinition? GetById(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return null;
        return s_models.FirstOrDefault(m =>
            string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }
}
