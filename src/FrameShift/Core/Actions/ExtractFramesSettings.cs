using System;
using System.Collections.Generic;

namespace FrameShift.Core.Actions;

internal enum ExtractFramesMode
{
    All,
    First,
    Last,
    Keyframes
}

internal sealed record ExtractFramesSettings(ExtractFramesMode Mode)
{
    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string>? options,
        out ExtractFramesSettings? settings,
        out string? error)
    {
        settings = null;
        error = null;

        if (options is null || !options.TryGetValue(ActionOptionKeys.FrameMode, out var configuredMode))
        {
            settings = new ExtractFramesSettings(ExtractFramesMode.All);
            return true;
        }

        switch (configuredMode?.Trim().ToLowerInvariant())
        {
            case "all":
                settings = new ExtractFramesSettings(ExtractFramesMode.All);
                return true;
            case "first":
                settings = new ExtractFramesSettings(ExtractFramesMode.First);
                return true;
            case "last":
                settings = new ExtractFramesSettings(ExtractFramesMode.Last);
                return true;
            case "keyframes":
                settings = new ExtractFramesSettings(ExtractFramesMode.Keyframes);
                return true;
            default:
                error = MediaActionMessages.FrameExtractionModeInvalid();
                return false;
        }
    }
}
