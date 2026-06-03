using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using FrameShift.Core.Actions;

namespace FrameShift;

internal static partial class Program
{
    internal static bool TryParseArguments(
        string[] args,
        out string actionId,
        out List<string> inputPaths,
        out Dictionary<string, string> options,
        out string? error)
    {
        actionId = string.Empty;
        inputPaths = new List<string>();
        options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error = null;

        var actionIndex = Array.IndexOf(args, "--action");
        if (actionIndex < 0 || actionIndex + 1 >= args.Length)
        {
            error = MediaActionMessages.UnsupportedCommandLine("FrameShift.exe --action <id> [--target <format>] [--profile <name>] <input-paths...>");
            return false;
        }

        actionId = args[actionIndex + 1];

        for (var index = actionIndex + 2; index < args.Length; index++)
        {
            var token = args[index];
            if (string.Equals(token, "--target", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.Target] = args[++index];
                continue;
            }

            if (string.Equals(token, "--profile", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.Profile] = args[++index];
                continue;
            }

            if (string.Equals(token, "--stems", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.Stems] = args[++index];
                continue;
            }

            if ((string.Equals(token, "--separate-engine", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token, "--engine", StringComparison.OrdinalIgnoreCase)) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.SeparateEngine] = args[++index];
                continue;
            }

            if (string.Equals(token, "--noise-strength", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.NoiseStrength] = args[++index];
                continue;
            }

            if ((string.Equals(token, "--rmbg-model", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token, "--remove-background-model", StringComparison.OrdinalIgnoreCase)) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.BackgroundRemovalModel] = args[++index];
                continue;
            }

            if (string.Equals(token, "--upscale-model", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.UpscaleModel] = args[++index];
                continue;
            }

            if (string.Equals(token, "--stereo", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.ProcessStereoAudio] = args[++index];
                continue;
            }

            if ((string.Equals(token, "--target-size-bytes", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token, "--size-bytes", StringComparison.OrdinalIgnoreCase)) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.TargetSizeBytes] = args[++index];
                continue;
            }

            if (string.Equals(token, "--start", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.StartTime] = args[++index];
                continue;
            }

            if (string.Equals(token, "--end", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.EndTime] = args[++index];
                continue;
            }

            if (string.Equals(token, "--duration", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.Duration] = args[++index];
                continue;
            }

            if ((string.Equals(token, "--gif-resolution", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token, "--resolution", StringComparison.OrdinalIgnoreCase)) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.GifResolution] = args[++index];
                continue;
            }

            if ((string.Equals(token, "--gif-fps", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token, "--fps", StringComparison.OrdinalIgnoreCase)) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.GifFps] = args[++index];
                continue;
            }

            if ((string.Equals(token, "--interpolate-model-id", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token, "--model-id", StringComparison.OrdinalIgnoreCase)) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.InterpolateModelId] = args[++index];
                continue;
            }

            if ((string.Equals(token, "--interpolate-multiplier", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token, "--multiplier", StringComparison.OrdinalIgnoreCase)) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.InterpolateMultiplier] = args[++index];
                continue;
            }

            if ((string.Equals(token, "--interpolate-playback-divisor", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token, "--playback-divisor", StringComparison.OrdinalIgnoreCase)) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.InterpolatePlaybackDivisor] = args[++index];
                continue;
            }

            if (string.Equals(token, "--interpolate-slowmotion-keep-pitch", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.InterpolateSlowMotionKeepPitch] = args[++index];
                continue;
            }

            if (string.Equals(token, "--interpolate-slowmotion-remove-audio", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.InterpolateSlowMotionRemoveAudio] = args[++index];
                continue;
            }

            if ((string.Equals(token, "--interpolate-pipeline", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token, "--pipeline", StringComparison.OrdinalIgnoreCase)) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.InterpolatePipeline] = args[++index];
                continue;
            }

            if ((string.Equals(token, "--gif-quality", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token, "--quality", StringComparison.OrdinalIgnoreCase)) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.GifQuality] = args[++index];
                continue;
            }

            if (string.Equals(token, "--icon-sizes", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.IconSizes] = args[++index];
                continue;
            }

            if (string.Equals(token, "--icon-fit-mode", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.IconFitMode] = args[++index];
                continue;
            }

            if (string.Equals(token, "--icon-background", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.IconBackground] = args[++index];
                continue;
            }

            if (string.Equals(token, "--semitones", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.PitchSemitones] = args[++index];
                continue;
            }

            if (string.Equals(token, "--keep-duration", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.PitchKeepDuration] = args[++index];
                continue;
            }

            if (string.Equals(token, "--start-frame", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.StartFrame] = args[++index];
                continue;
            }

            if (string.Equals(token, "--end-frame", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.EndFrame] = args[++index];
                continue;
            }

            if (string.Equals(token, "--resize-width", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.ResizeWidth] = args[++index];
                continue;
            }

            if (string.Equals(token, "--resize-height", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options[ActionOptionKeys.ResizeHeight] = args[++index];
                continue;
            }

            if (token.StartsWith("--", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            inputPaths.Add(token);
        }

        NormalizeSplitInputPaths(inputPaths);
        return true;
    }

    private static void NormalizeSplitInputPaths(List<string> inputPaths)
    {
        if (inputPaths.Count <= 1)
        {
            return;
        }

        if (inputPaths.Any(File.Exists))
        {
            return;
        }

        var combinedPath = string.Join(" ", inputPaths);
        if (!File.Exists(combinedPath) && !Directory.Exists(combinedPath))
        {
            return;
        }

        inputPaths.Clear();
        inputPaths.Add(combinedPath);
    }

    private static void ShowCliError(string message)
    {
        MessageBox.Show(
            message,
            "FrameShift",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
