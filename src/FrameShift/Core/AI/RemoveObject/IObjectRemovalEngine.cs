using System;
using System.Threading;
using System.Threading.Tasks;

namespace FrameShift.Core.AI.RemoveObject;

internal sealed record InpaintProgress(int Percent, string Status);

internal interface IObjectRemovalEngine : IDisposable
{
    string Provider { get; }

    Task<string> InpaintAsync(
        string inputPath,
        bool[,] mask,
        IProgress<InpaintProgress> progress,
        CancellationToken cancellationToken);
}
