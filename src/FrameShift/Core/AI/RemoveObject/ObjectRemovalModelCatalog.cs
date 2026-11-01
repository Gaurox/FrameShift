using System;
using System.Collections.Generic;
using System.Linq;

namespace FrameShift.Core.AI.RemoveObject;

internal static class ObjectRemovalModelCatalog
{
    private static readonly ObjectRemovalModelDefinition[] s_models =
    [
        new(
            "lama",
            L.Get("ObjectRemovalModelCatalog_LaMa_FP32_Quality_4fe013eb"),
            "lama-onnx",
            "lama_fp32.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/lama-onnx/lama_fp32.onnx",
            "1FAEF5301D78DB7DDA502FE59966957EC4B79DD64E16F03ED96913C7A4EB68D6",
            208_044_816L,
            L.Get("ObjectRemovalModelCatalog_Apache_2_0_code_weights_trained_on_P_14b31c28")),

        new(
            "lama-fast",
            L.Get("ObjectRemovalModelCatalog_LaMa_2025_Fast_707b9ad0"),
            "lama-opencv-onnx",
            "inpainting_lama_2025jan.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/lama-opencv-onnx/inpainting_lama_2025jan.onnx",
            "7DF918AC3921D3DAF0AAE1D219776CF0DC4E4935F035AF81841B40ADCF74FDF2",
            92_591_623L,
            "Apache-2.0 — opencv/inpainting_lama Jan 2025"),
    ];

    public static IReadOnlyList<ObjectRemovalModelDefinition> GetAll() => s_models;

    public static ObjectRemovalModelDefinition GetDefault() =>
        GetById("lama") ?? s_models[0];

    public static ObjectRemovalModelDefinition? GetById(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return null;
        return s_models.FirstOrDefault(m =>
            string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }
}
