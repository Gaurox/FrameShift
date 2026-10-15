namespace FrameShift.Core.AI;

public readonly record struct AiModelDownloadProgress(int Percent, string Status)
{
    public long BytesReceived { get; init; }
    public long? TotalBytes { get; init; }
}

internal sealed class ForwardProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
