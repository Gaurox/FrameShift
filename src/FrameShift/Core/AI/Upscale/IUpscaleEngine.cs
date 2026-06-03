using System;
using System.Threading;
using System.Threading.Tasks;

namespace FrameShift.Core.AI.Upscale;

internal sealed record UpscaleProgress(int Percent, string Status);

internal interface IUpscaleEngine : IDisposable
{
    string Provider { get; }

    Task<string> UpscaleAsync(
        string inputPath,
        IProgress<UpscaleProgress> progress,
        CancellationToken cancellationToken);
}
