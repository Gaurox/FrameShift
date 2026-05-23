namespace FrameShift.Core.Actions;

public sealed record ActionExecutionResult(
    bool Success,
    string Message,
    string? OutputPath = null,
    bool Canceled = false,
    CancellationScope CancellationScope = CancellationScope.None);
