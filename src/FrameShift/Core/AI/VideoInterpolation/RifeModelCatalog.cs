using System;
using System.Collections.Generic;
using System.Linq;

namespace FrameShift.Core.AI.VideoInterpolation;

internal static class RifeModelCatalog
{
    private static readonly RifeModelDefinition[] s_models =
    [
        new(
            "rife-v425-lite",
            "RIFE v4.25 Lite",
            "rife_v425_lite.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/rife/rife_v425_lite.onnx",
            "FD9AD9E7AD52790E526423BD6BE0D93F6D7929441869BC6B448E828A2D820BB8",
            22_643_457L,
            "MIT License — Free for commercial and non-commercial use",
            [2, 4]),
        new(
            "rife-v426-x2",
            "RIFE v4.26 x2",
            "rife_v426_x2.onnx",
            "https://huggingface.co/Gaurox/frameshift-models/resolve/main/rife/rife_v426_x2.onnx",
            "51DDE376C8BD71BAAB7FFAB7E436188B5493F8E5E0308B6B05B0F3C700AE2737",
            22_808_987L,
            "MIT License — Free for commercial and non-commercial use",
            [2, 4])
    ];

    public static IReadOnlyList<RifeModelDefinition> GetAll() => s_models;

    public static RifeModelDefinition GetDefault() => s_models[0];

    public static RifeModelDefinition? GetById(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        return s_models.FirstOrDefault(model =>
            string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }
}
