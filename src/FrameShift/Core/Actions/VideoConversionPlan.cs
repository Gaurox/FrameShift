using System.Collections.Generic;

namespace FrameShift.Core.Actions;

public sealed record VideoConversionPlan(
    string TargetId,
    string ProfileId,
    string ModeLabel,
    string VideoCodec,
    IReadOnlyList<string> VideoArgs,
    string? AudioCodec,
    IReadOnlyList<string> AudioArgs,
    IReadOnlyList<string> GlobalArgs,
    IReadOnlyList<string> MapArgs,
    IReadOnlyList<string> SubtitleCodecArgs,
    IReadOnlyList<string> Warnings,
    bool IsRemux);
