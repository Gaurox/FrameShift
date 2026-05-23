using System.Collections.Generic;

namespace FrameShift.Core.Actions;

internal sealed record InterpolateVideoPlan(
    string ModeLabel,
    string VideoCodec,
    IReadOnlyList<string> VideoArgs);
