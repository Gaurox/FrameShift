using System;
using System.Collections.Generic;
using System.Globalization;

namespace FrameShift.Core.Actions;

internal static class InterpolateVideoPlanner
{
    public static InterpolateVideoPlan BuildPlan(string extension, bool nvencAvailable)
    {
        var normalizedExtension = NormalizeExtension(extension);

        return normalizedExtension switch
        {
            ".avi" => new InterpolateVideoPlan("CPU", "mpeg4", ["-q:v", "2"]),
            ".webm" => new InterpolateVideoPlan("CPU", "libvpx-vp9", ["-crf", "31", "-b:v", "0", "-deadline", "good", "-cpu-used", "2", "-row-mt", "1"]),
            _ when nvencAvailable => new InterpolateVideoPlan("GPU", "h264_nvenc", ["-preset", "p5", "-cq", "21", "-pix_fmt", "yuv420p"]),
            _ => new InterpolateVideoPlan("CPU", "libx264", ["-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p"])
        };
    }

    public static string BuildPipelineDescription(InterpolateVideoPlan plan, double targetFps)
    {
        var fpsText = targetFps.ToString("0.###", CultureInfo.InvariantCulture);
        return $"cpu minterpolate ({fpsText} fps) -> {plan.VideoCodec}";
    }

    public static IReadOnlyList<string> BuildArguments(
        string inputPath,
        string outputPath,
        InterpolateVideoSettings settings,
        InterpolateVideoPlan plan)
    {
        var fpsCulture = settings.TargetFps.ToString("0.###", CultureInfo.InvariantCulture);

        return
        [
            "-hide_banner",
            "-loglevel", "error",
            "-stats_period", "0.25",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-i", inputPath,
            "-vf", $"minterpolate=fps={fpsCulture}",
            "-c:v", plan.VideoCodec,
            .. plan.VideoArgs,
            "-c:a", "copy",
            outputPath
        ];
    }

    private static string NormalizeExtension(string extension)
    {
        return string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.Trim().ToLowerInvariant();
    }
}
