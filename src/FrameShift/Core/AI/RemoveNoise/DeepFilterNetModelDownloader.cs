using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.AI;

namespace FrameShift.Core.AI.RemoveNoise;

internal static class DeepFilterNetModelDownloader
{
    public const string ModelDisplayName = "DeepFilterNet3 (enc + erb_dec + df_dec + config)";
    public static string ModelLicense => L.Get("DeepFilterNetModelDownloader_MIT_or_Apache_2_0_Upstream_dual_lice_199c07be");
    public const long   TotalSizeBytes   = 9_738_000L;

    private const string BaseUrl =
        "https://huggingface.co/Gaurox/frameshift-models/resolve/main/deepfilternet3_onnx/";

    private readonly record struct ModelFile(string Name, string Sha256);

    private static readonly ModelFile[] Files =
    [
        new("config.ini",   "415EB925D44990D938FB739F514AA3662C1EC0EA836CFF044FA1291B82CB4290"),
        new("enc.onnx",     "7C5399D3DA8A50EBEF1C1A0AE421B33376AA5E45D0E92DF16DA7E83C9C131916"),
        new("erb_dec.onnx", "AB669A1D10AFE20911728B33053A452071042317A90581092B325DA7B2F9D895"),
        new("df_dec.onnx",  "23114CE3B0F6464B763EE62F7BB8AAB6B2A129A21EABD5BCFE59413DB05F278A"),
    ];

    public static async Task DownloadAllAsync(
        string modelsDirectory,
        IProgress<AiModelDownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < Files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = Files[i];
            var fileIndex = i;
            var fileProgress = new ForwardProgress<AiModelDownloadProgress>(p => progress.Report(new(
                Math.Clamp(fileIndex * 25 + p.Percent * 25 / 100, 0, 99),
                $"[{fileIndex + 1}/{Files.Length}] {file.Name} — {p.Status}")));
            // A small config and three model files; a fixed ceiling remains effective even
            // when the server omits Content-Length. SHA256 still identifies each artifact.
            var maximumBytes = file.Name == "config.ini" ? 64L * 1024 : 16L * 1024 * 1024;
            await AiModelFileDownloader.DownloadAsync(BaseUrl + file.Name,
                Path.Combine(modelsDirectory, file.Name), file.Sha256, maximumBytes,
                fileProgress, cancellationToken, "DeepFilterNetModelDownloader").ConfigureAwait(false);
        }
        progress.Report(new(100, L.Get("DeepFilterNetModelDownloader_All_files_downloaded_982b1ffb")));
    }
}
