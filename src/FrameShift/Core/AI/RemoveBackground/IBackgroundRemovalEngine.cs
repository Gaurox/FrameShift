using System;
using System.Threading;
using System.Threading.Tasks;

namespace FrameShift.Core.AI.RemoveBackground;

public readonly record struct InferenceProgress(int Percent, string Status);

public interface IBackgroundRemovalEngine
{
    Task<string> RemoveBackgroundAsync(
        string inputPath,
        IProgress<InferenceProgress> progress,
        CancellationToken cancellationToken);
}
