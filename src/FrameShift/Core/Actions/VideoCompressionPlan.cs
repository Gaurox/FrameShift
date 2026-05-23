using System.Collections.Generic;

namespace FrameShift.Core.Actions;

public sealed record VideoCompressionPlan(
    string ProfileId,
    string ProfileName,
    string ModeLabel,
    string OutputExtension,
    bool UseTargetSize,
    long? TargetBytes,
    string VideoCodec,
    IReadOnlyList<string> VideoArgs,
    string? AudioCodec,
    IReadOnlyList<string> AudioArgs,
    IReadOnlyList<string> GlobalArgs,
    IReadOnlyList<string> MapArgs,
    IReadOnlyList<string> SubtitleCodecArgs,
    IReadOnlyList<string> Warnings);
