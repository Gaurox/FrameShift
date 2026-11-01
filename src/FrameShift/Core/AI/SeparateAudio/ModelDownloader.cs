using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FrameShift.Core.AI.SeparateAudio;

public readonly record struct ModelDownloadProgress(
    long BytesReceived,
    long? TotalBytes,
    int Percent,
    string Status);

internal static class ModelDownloader
{
    public const string CpuModelDisplayName = "HTDemucs V1 CPU";
    public const string GpuModelDisplayName = "HTDemucs V2 GPU (split)";
    public static string ModelLicense => L.Get("ModelDownloader_MIT_License_Free_for_commercial_and__594b40f6");

    public const long CpuExpectedSizeBytes = 289L * 1024L * 1024L;
    public const long GpuExpectedSizeBytes = 161L * 1024L * 1024L;

    private const string CpuModelUrl =
        "https://huggingface.co/Gaurox/frameshift-models/resolve/main/htdemucs/htdemucs.onnx";

    private const string GpuModelUrl =
        "https://huggingface.co/Gaurox/frameshift-models/resolve/main/htdemucs-split/htdemucs_split.onnx";

    private const string CpuExpectedSha256 =
        "A56BEF35B4C1B776502A53A36D4E1CAC1CB903BD9AE225939668E310B6DB1D44";

    private const string GpuExpectedSha256 =
        "7776FA928E69720966694CDF622A7F887AF543EF67E23A31979AAB81F0C97206";

    public static Task DownloadAsync(
        bool forGpu,
        string destinationPath,
        IProgress<ModelDownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        var expectedSize = forGpu ? GpuExpectedSizeBytes : CpuExpectedSizeBytes;
        // The historical UI sizes are rounded; permit 10% headroom, still bounded by the
        // pinned digest. Exact catalogue byte counts use their exact size elsewhere.
        var maximumBytes = expectedSize + expectedSize / 10;
        var adapter = new ForwardProgress<AiModelDownloadProgress>(p => progress.Report(
            new ModelDownloadProgress(p.BytesReceived, p.TotalBytes, p.Percent, p.Status)));
        return AiModelFileDownloader.DownloadAsync(
            forGpu ? GpuModelUrl : CpuModelUrl, destinationPath,
            forGpu ? GpuExpectedSha256 : CpuExpectedSha256, maximumBytes,
            adapter, cancellationToken, "ModelDownloader (audio)");
    }
}
