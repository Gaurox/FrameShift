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
            "LaMa FP32",
            "lama-onnx",
            "lama_fp32.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/lama-onnx/lama_fp32.onnx",
            "1FAEF5301D78DB7DDA502FE59966957EC4B79DD64E16F03ED96913C7A4EB68D6",
            208_044_816L,
            "Apache-2.0 (code) — weights trained on Places2; commercial use not guaranteed"),
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
