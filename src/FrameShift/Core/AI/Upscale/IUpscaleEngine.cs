using System;
using System.Threading;
using System.Threading.Tasks;

namespace FrameShift.Core.AI.Upscale;

internal sealed record UpscaleProgress(int Percent, string Status);

/// <summary>
/// How much to enlarge. The model always runs at its native scale (x4); the engine then resamples
/// down to the requested result. When TargetWidth/TargetHeight are set they win over Factor.
/// Defaults to the native x4 (no resample).
/// </summary>
internal sealed record UpscaleRequest(
    double Factor = 4,
    int? TargetWidth = null,
    int? TargetHeight = null);

internal interface IUpscaleEngine : IDisposable
{
    string Provider { get; }

    Task<string> UpscaleAsync(
        string inputPath,
        UpscaleRequest request,
        IProgress<UpscaleProgress> progress,
        CancellationToken cancellationToken);
}
