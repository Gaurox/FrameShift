using System;
using System.Collections.Generic;
using System.Linq;

namespace FrameShift.Core.AI.Upscale;

internal static class UpscaleModelCatalog
{
    // Any missing, malformed, or non-hex digest is treated as unfinished. Download code checks this
    // before opening a network request, so future catalog entries cannot ship with placeholder text.
    public static bool IsSha256Placeholder(string? sha256) =>
        string.IsNullOrWhiteSpace(sha256) ||
        sha256.Length != 64 ||
        sha256.Any(character => !Uri.IsHexDigit(character));

    private static readonly UpscaleModelDefinition[] s_models =
    [
        new(
            "realesr-general-x4v3",
            "Real-ESRGAN General v3",
            "upscale-video-onnx",
            "realesr_general_x4v3.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/upscale-video-onnx/realesr_general_x4v3.onnx",
            "DBB0561758E0727C76B7BF6B539A988D3B3050D51F01DCF60C842DE6E6ADD349",
            4_867_430L,
            "BSD-3-Clause (Real-ESRGAN, © Xintao Wang)",
            ScaleFactor: 4,
            ForceCpu: false,
            Summary: "General video and photos. Fast, light, recommended for video.",
            RecommendedForImage: false,
            RecommendedForVideo: true,
            LegacyFolder: "upscale-onnx"),

        new(
            "realesr-animevideov3",
            "Real-ESRGAN AnimeVideo v3",
            "upscale-video-onnx",
            "realesr_animevideov3.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/upscale-video-onnx/realesr_animevideov3.onnx",
            "6CB9454787F6B0948CB1C25BE0C7DA797AD83EC2696FC005CA4C967217B9CD77",
            2_493_430L,
            "BSD-3-Clause (Real-ESRGAN, © Xintao Wang)",
            ScaleFactor: 4,
            ForceCpu: false,
            Summary: "Anime and animation video. Very fast with clean lines.",
            RecommendedForImage: false,
            RecommendedForVideo: true,
            LegacyFolder: "upscale-onnx"),

        new(
            "realesrgan-x4plus",
            "Real-ESRGAN x4plus",
            "upscale-image-onnx",
            "realesrgan_x4plus_fp16.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/upscale-image-onnx/realesrgan_x4plus_fp16.onnx",
            // SHA256 of the file hosted on Gaurox/frameshift-models (uploaded 2026-06-03).
            "37651C96722D0156AAEE27404F31FBB62E93B4AB4AB9E9DB07DA2200500232AC",
            // FP16 weights, float32 I/O. ~33.7 MB.
            33_748_852L,
            "BSD-3-Clause (Real-ESRGAN, © Xintao Wang)",
            ScaleFactor: 4,
            ForceCpu: false,
            Summary: "General photos, screenshots, AI images. Fast, balanced default.",
            LegacyFolder: "upscale-onnx"),

        new(
            "realesrgan-x4plus-video",
            "Real-ESRGAN x4plus (Quality)",
            "upscale-video-onnx",
            "realesrgan_x4plus_fp16.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/upscale-video-onnx/realesrgan_x4plus_fp16.onnx",
            "37651C96722D0156AAEE27404F31FBB62E93B4AB4AB9E9DB07DA2200500232AC",
            33_748_852L,
            "BSD-3-Clause (Real-ESRGAN, © Xintao Wang)",
            ScaleFactor: 4,
            ForceCpu: false,
            Summary: "General quality model. More detailed and slower than General v3.",
            RecommendedForImage: false,
            RecommendedForVideo: true,
            LegacyFolder: "upscale-onnx"),

        new(
            "realesrgan-anime",
            "Real-ESRGAN Anime 6B",
            "upscale-image-onnx",
            "realesrgan_x4plus_anime_6b.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/upscale-image-onnx/realesrgan_x4plus_anime_6b.onnx",
            "1BCCABC8E813AE287057C6DD342458C355E4505ABE36B5E0B2B94807D39D40DF",
            // FP32, exported from the official RealESRGAN_x4plus_anime_6B.pth. ~17.9 MB.
            17_939_940L,
            "BSD-3-Clause (Real-ESRGAN, © Xintao Wang)",
            ScaleFactor: 4,
            ForceCpu: false,
            Summary: "Anime, illustrations, line art. Cleaner edges on drawn content.",
            LegacyFolder: "upscale-onnx"),

        new(
            "swin2sr-quality",
            "Swin2SR (Quality)",
            "upscale-image-onnx",
            "swin2sr_realworld_x4.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/upscale-image-onnx/swin2sr_realworld_x4.onnx",
            "987D88B356554161CBB8F67B7A8F4162CAD6DC147839C344E3D5142140F25D6F",
            // FP32 transformer. ~53.8 MB. Slower; best fidelity / restoration.
            53_827_735L,
            "Apache-2.0 (Swin2SR, mv-lab)",
            ScaleFactor: 4,
            ForceCpu: false,
            InputTensorName: "pixel_values",
            OutputTensorName: "reconstruction",
            WindowMultiple: 8,
            Summary: "Restoration / anti-JPEG. Highest fidelity, noticeably slower.",
            LegacyFolder: "upscale-onnx"),
    ];

    public static IReadOnlyList<UpscaleModelDefinition> GetAll() => s_models;

    public static IReadOnlyList<UpscaleModelDefinition> GetImageModels() =>
        s_models.Where(model => model.RecommendedForImage).ToArray();

    public static IReadOnlyList<UpscaleModelDefinition> GetVideoModels() =>
        s_models.Where(model => model.RecommendedForVideo).ToArray();

    public static UpscaleModelDefinition GetDefault() =>
        GetById("realesrgan-x4plus") ?? s_models[0];

    public static UpscaleModelDefinition GetDefaultVideo() =>
        GetById("realesr-general-x4v3") ?? GetDefault();

    public static UpscaleModelDefinition? GetById(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return null;
        return s_models.FirstOrDefault(m =>
            string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }
}
