using System.Collections.Generic;
using FrameShift.Core.Logging;
using FrameShift.Core.Progress;

namespace FrameShift.Core.Actions;

public sealed record ActionRequest(
    string InputPath,
    AppLogger Logger,
    IProgressReporter? ProgressReporter,
    IReadOnlyDictionary<string, string>? Options = null);
