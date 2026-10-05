using System.Security.Cryptography;
using System.Text.Json;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI.Ocr;

internal sealed record OcrModelFile(string Path, long Bytes, string Sha256);
internal sealed record OcrModelDefinition(string Id, string Folder, int Classes, double BoxThreshold, IReadOnlyList<OcrModelFile> Files)
{
    public string DisplayName => $"PP-OCRv6 {(Id == "small" ? "Small" : "Tiny")}";
    public long SizeBytes => Files.Sum(file => file.Bytes);
    public string DirectoryPath => Path.Combine(AiModelStorage.RootDirectory, Folder);
}

internal static class OcrModelCatalog
{
    public const string Revision = "b4f8fa610edff25e059959fb478bd3879e26f7a7";
    private static readonly IReadOnlyDictionary<string, OcrModelDefinition> Models = Load();
    public static OcrModelDefinition Get(string id) => Models.TryGetValue(id, out var model) ? model : throw new ArgumentException("Unknown OCR model. Select small or tiny.");
    private static IReadOnlyDictionary<string, OcrModelDefinition> Load()
    {
        using var stream = typeof(OcrModelCatalog).Assembly.GetManifestResourceStream("FrameShift.Core.AI.Ocr.model-files.json")!;
        var files = JsonSerializer.Deserialize<Dictionary<string, OcrModelFile[]>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return files.ToDictionary(pair => pair.Key, pair => new OcrModelDefinition(pair.Key, $"pp-ocrv6-{pair.Key}-onnx", pair.Key == "tiny" ? 6906 : 18710, pair.Key == "tiny" ? .4 : .45, pair.Value));
    }

    public static bool IsPresent(OcrModelDefinition model) => model.Files.All(file => File.Exists(Path.Combine(model.DirectoryPath, file.Path)));
    public static bool IsReady(OcrModelDefinition model, string? directory = null, CancellationToken token = default)
    {
        directory ??= model.DirectoryPath;
        foreach (var file in model.Files)
        {
            token.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, file.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Bytes)
                return false;
            using var input = File.OpenRead(path);
            if (!Convert.ToHexString(SHA256.HashData(input)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}

internal static class OcrModelDownloader
{
    public static async Task DownloadAsync(OcrModelDefinition model, IProgress<AiModelDownloadProgress> progress, CancellationToken token)
    {
        // File sharing makes the lock cross-process; unlike a Mutex, it can be held across awaits.
        OcrModelDirectorySafety.ValidateDownloadPath(AiModelStorage.RootDirectory, model.DirectoryPath);
        AiModelStorage.EnsureDirectory(model.DirectoryPath);
        FileStream? lease = null;
        var lockPath = Path.Combine(model.DirectoryPath, ".download-lock");
        OcrModelDirectorySafety.ValidateDownloadPath(AiModelStorage.RootDirectory, lockPath);
        try
        {
            while (lease is null)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
                {
                    progress.Report(new(0, "Waiting for another OCR download..."));
                    await Task.Delay(250, token).ConfigureAwait(false);
                }
            }

            Directory.CreateDirectory(Path.Combine(model.DirectoryPath, "det"));
            Directory.CreateDirectory(Path.Combine(model.DirectoryPath, "rec"));
            long complete = 0;
            foreach (var file in model.Files)
            {
                token.ThrowIfCancellationRequested();
                var destination = Path.Combine(model.DirectoryPath, file.Path);
                OcrModelDirectorySafety.ValidateDownloadPath(AiModelStorage.RootDirectory, destination);
                OcrModelDirectorySafety.ValidateDownloadPath(AiModelStorage.RootDirectory, destination + ".tmp");
                var valid = File.Exists(destination) && new FileInfo(destination).Length == file.Bytes && AiModelFileDownloader.VerifySha256File(destination, file.Sha256, null, "OCR");
                if (!valid)
                {
                    var completedBytes = complete;
                    var fileProgress = new ForwardProgress<AiModelDownloadProgress>(p => progress.Report(new((int)((completedBytes + file.Bytes * p.Percent / 100) * 100 / model.SizeBytes), $"{file.Path}: {p.Status}")));
                    await AiModelFileDownloader.DownloadAsync($"https://huggingface.co/Gaurox/frameshift-models/resolve/{OcrModelCatalog.Revision}/{model.Folder}/{file.Path}", destination, file.Sha256, fileProgress, token, "OCR").ConfigureAwait(false);
                }

                complete += file.Bytes;
                progress.Report(new((int)(complete * 100 / model.SizeBytes), $"Verified {file.Path}"));
            }
        }
        finally
        {
            lease?.Dispose();
        }
        // Keep the empty lock file: removing it after releasing the lease can race another downloader.
    }
}

internal sealed class ForwardProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
