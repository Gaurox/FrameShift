using System;
using System.Collections.Generic;
using System.Globalization;
using FrameShift.Core.AI.Upscale;

namespace FrameShift.Core.Actions;

public sealed record UpscaleVideoSettings(
    string ModelId,
    int ScaleFactor = 4,
    int? TargetWidth = null,
    int? TargetHeight = null)
{
    internal UpscaleRequest Request => TargetWidth is int width && TargetHeight is int height
        ? new UpscaleRequest(TargetWidth: width, TargetHeight: height)
        : new UpscaleRequest(Factor: ScaleFactor);

    public string OutputSuffix => TargetWidth is int width && TargetHeight is int height
        ? $"_upscaled_{width}x{height}"
        : $"_upscaled_{ScaleFactor}x";

    public IReadOnlyDictionary<string, string> ToOptions()
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActionOptionKeys.UpscaleModel] = ModelId
        };

        if (TargetWidth is int width && TargetHeight is int height)
        {
            options[ActionOptionKeys.UpscaleTargetWidth] = width.ToString(CultureInfo.InvariantCulture);
            options[ActionOptionKeys.UpscaleTargetHeight] = height.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            options[ActionOptionKeys.UpscaleScale] = ScaleFactor.ToString(CultureInfo.InvariantCulture);
        }

        return options;
    }

    public static bool ContainsAnyOption(IReadOnlyDictionary<string, string>? options) =>
        options is not null &&
        (options.ContainsKey(ActionOptionKeys.UpscaleModel) ||
         options.ContainsKey(ActionOptionKeys.UpscaleScale) ||
         options.ContainsKey(ActionOptionKeys.UpscaleTargetWidth) ||
         options.ContainsKey(ActionOptionKeys.UpscaleTargetHeight));

    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string>? options,
        out UpscaleVideoSettings? settings,
        out string? errorMessage)
    {
        settings = null;
        errorMessage = null;
        string modelId = UpscaleModelCatalog.GetDefaultVideo().Id;
        if (options is not null &&
            options.TryGetValue(ActionOptionKeys.UpscaleModel, out var modelText) &&
            !string.IsNullOrWhiteSpace(modelText))
        {
            modelId = modelText.Trim();
        }

        string? widthText = null;
        string? heightText = null;
        bool hasWidth = options?.TryGetValue(ActionOptionKeys.UpscaleTargetWidth, out widthText) == true;
        bool hasHeight = options?.TryGetValue(ActionOptionKeys.UpscaleTargetHeight, out heightText) == true;
        if (hasWidth != hasHeight)
        {
            errorMessage = L.Get("UpscaleVideoSettings_Both_upscale_target_width_and_height_5b27a411");
            return false;
        }

        if (hasWidth && hasHeight)
        {
            if (!int.TryParse(widthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) || width <= 0 ||
                !int.TryParse(heightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) || height <= 0)
            {
                errorMessage = L.Get("UpscaleVideoSettings_Upscale_target_dimensions_must_be_po_85032621");
                return false;
            }

            settings = new UpscaleVideoSettings(modelId, TargetWidth: width, TargetHeight: height);
            return true;
        }

        int scale = 4;
        if (options is not null && options.TryGetValue(ActionOptionKeys.UpscaleScale, out var scaleText) &&
            (!int.TryParse(scaleText, NumberStyles.Integer, CultureInfo.InvariantCulture, out scale) ||
             scale is not 2 and not 3 and not 4))
        {
            errorMessage = L.Get("UpscaleVideoSettings_Upscale_scale_must_be_x2_x3_or_x4_afe75b2a");
            return false;
        }

        settings = new UpscaleVideoSettings(modelId, scale);
        return true;
    }
}
