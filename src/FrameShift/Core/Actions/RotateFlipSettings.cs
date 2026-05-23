using System;
using System.Collections.Generic;

namespace FrameShift.Core.Actions;

public enum RotateAngle
{
    None,
    Cw90,
    Cw180,
    Cw270
}

public sealed class RotateFlipSettings
{
    public RotateFlipSettings(RotateAngle angle, bool flipHorizontal, bool flipVertical)
    {
        Angle = angle;
        FlipHorizontal = flipHorizontal;
        FlipVertical = flipVertical;
    }

    public RotateAngle Angle { get; }
    public bool FlipHorizontal { get; }
    public bool FlipVertical { get; }

    public bool IsIdentity => Angle == RotateAngle.None && !FlipHorizontal && !FlipVertical;

    public string? BuildFilterString()
    {
        var filters = new List<string>();

        switch (Angle)
        {
            case RotateAngle.Cw90:
                filters.Add("transpose=1");
                break;
            case RotateAngle.Cw180:
                filters.Add("hflip");
                filters.Add("vflip");
                break;
            case RotateAngle.Cw270:
                filters.Add("transpose=2");
                break;
        }

        if (FlipHorizontal)
        {
            filters.Add("hflip");
        }

        if (FlipVertical)
        {
            filters.Add("vflip");
        }

        return filters.Count == 0 ? null : string.Join(",", filters);
    }

    public string BuildTransformSummary()
    {
        if (IsIdentity)
        {
            return "No transforms applied";
        }

        var parts = new List<string>();

        if (Angle != RotateAngle.None)
        {
            parts.Add(Angle switch
            {
                RotateAngle.Cw90 => "Rotation: 90° CW",
                RotateAngle.Cw180 => "Rotation: 180°",
                RotateAngle.Cw270 => "Rotation: 270° CW",
                _ => string.Empty
            });
        }

        if (FlipHorizontal)
        {
            parts.Add("Flip H");
        }

        if (FlipVertical)
        {
            parts.Add("Flip V");
        }

        return string.Join("  —  ", parts);
    }

    public IReadOnlyDictionary<string, string> ToOptions()
    {
        return new Dictionary<string, string>
        {
            [ActionOptionKeys.RotateAngle] = Angle switch
            {
                RotateAngle.Cw90 => "cw90",
                RotateAngle.Cw180 => "cw180",
                RotateAngle.Cw270 => "cw270",
                _ => "none"
            },
            [ActionOptionKeys.FlipHorizontal] = FlipHorizontal ? "true" : "false",
            [ActionOptionKeys.FlipVertical] = FlipVertical ? "true" : "false"
        };
    }

    public static bool ContainsAnyOption(IReadOnlyDictionary<string, string>? options)
    {
        if (options is null)
        {
            return false;
        }

        return options.ContainsKey(ActionOptionKeys.RotateAngle) ||
               options.ContainsKey(ActionOptionKeys.FlipHorizontal) ||
               options.ContainsKey(ActionOptionKeys.FlipVertical);
    }

    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string>? options,
        out RotateFlipSettings settings,
        out string? error)
    {
        settings = new RotateFlipSettings(RotateAngle.None, false, false);
        error = null;

        if (options is null || options.Count == 0)
        {
            error = MediaActionMessages.RotateFlipSettingsMissing();
            return false;
        }

        var angle = RotateAngle.None;
        if (options.TryGetValue(ActionOptionKeys.RotateAngle, out var angleValue))
        {
            angle = angleValue.ToLowerInvariant() switch
            {
                "cw90" => RotateAngle.Cw90,
                "cw180" => RotateAngle.Cw180,
                "cw270" => RotateAngle.Cw270,
                _ => RotateAngle.None
            };
        }

        var flipH = false;
        if (options.TryGetValue(ActionOptionKeys.FlipHorizontal, out var flipHValue))
        {
            flipH = string.Equals(flipHValue, "true", StringComparison.OrdinalIgnoreCase);
        }

        var flipV = false;
        if (options.TryGetValue(ActionOptionKeys.FlipVertical, out var flipVValue))
        {
            flipV = string.Equals(flipVValue, "true", StringComparison.OrdinalIgnoreCase);
        }

        var result = new RotateFlipSettings(angle, flipH, flipV);
        if (result.IsIdentity)
        {
            error = MediaActionMessages.RotateFlipNoTransformSelected();
            return false;
        }

        settings = result;
        return true;
    }
}
