using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.AI;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SharpImage = SixLabors.ImageSharp.Image;

namespace FrameShift.Core.AI.RemoveObject;

internal sealed class ObjectRemovalEngine : IObjectRemovalEngine
{
    private const int ModelSize = 512;
    private const long MaxPixels = 80_000_000L;

    private readonly ObjectRemovalModelDefinition _def;
    private InferenceSession? _session;
    private string _providerName = "None";
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _sessionReady;
    private int _disposed;

    public string Provider => _providerName;

    public ObjectRemovalEngine(ObjectRemovalModelDefinition def)
    {
        _def = def;
    }

    public async Task<string> InpaintAsync(
        string inputPath,
        bool[,] mask,
        IProgress<InpaintProgress> progress,
        CancellationToken cancellationToken)
    {
        return await Task.Run(
            () => ProcessImageCore(inputPath, mask, progress, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private string ProcessImageCore(
        string inputPath,
        bool[,] mask,
        IProgress<InpaintProgress> progress,
        CancellationToken cancellationToken)
    {
        string? outputPath = null;
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Input image not found.", inputPath);

        EnsureSession(progress, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new InpaintProgress(10, "Loading image..."));

        using var original = SharpImage.Load<Rgba32>(inputPath);

        if ((long)original.Width * original.Height > MaxPixels)
            throw new InvalidOperationException(
                $"Image too large: {original.Width}x{original.Height}. Limit is {MaxPixels:N0} pixels.");

        AppLogger.LogStatic($"ObjectRemovalEngine: image loaded. size={original.Width}x{original.Height}, model={_def.Id}");

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new InpaintProgress(25, "Preparing inputs..."));

        using var resized = original.Clone(ctx => ctx.Resize(ModelSize, ModelSize));
        var mask512 = ScaleMask(mask, original.Width, original.Height, ModelSize, ModelSize);

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new InpaintProgress(45, "Running AI inference..."));

        var output = RunLama(resized, mask512);

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new InpaintProgress(78, "Recompositing image..."));

        using var outputAt512 = BuildOutputImage(output, ModelSize, ModelSize);
        outputAt512.Mutate(ctx => ctx.Resize(original.Width, original.Height));

        using var finalImage = ComposeResult(original, outputAt512, mask);

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new InpaintProgress(93, "Saving PNG..."));

        outputPath = OutputPathHelper.CreateUniqueOutputPath(inputPath, "_cleaned", ".png");
        try
        {
            finalImage.SaveAsPng(outputPath, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });
        }
        catch
        {
            DeletePartialOutput(outputPath);
            throw;
        }

        progress.Report(new InpaintProgress(100, "Done."));
        AppLogger.LogStatic($"ObjectRemovalEngine: complete. output={outputPath}");
        return outputPath;
    }

    private float[] RunLama(Image<Rgba32> img, float[,] mask512)
    {
        var imageTensor = new DenseTensor<float>([1, 3, ModelSize, ModelSize]);
        var maskTensor = new DenseTensor<float>([1, 1, ModelSize, ModelSize]);

        for (int y = 0; y < ModelSize; y++)
        {
            for (int x = 0; x < ModelSize; x++)
            {
                var p = img[x, y];
                imageTensor[0, 0, y, x] = p.R / 255f;
                imageTensor[0, 1, y, x] = p.G / 255f;
                imageTensor[0, 2, y, x] = p.B / 255f;
                maskTensor[0, 0, y, x] = mask512[x, y];
            }
        }

        var inputs = new[]
        {
            NamedOnnxValue.CreateFromTensor("image", imageTensor),
            NamedOnnxValue.CreateFromTensor("mask", maskTensor),
        };

        using var results = _session!.Run(inputs);
        var outputTensor = results.First().AsTensor<float>();

        var flat = new float[3 * ModelSize * ModelSize];
        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < ModelSize; y++)
            {
                for (int x = 0; x < ModelSize; x++)
                {
                    // LaMa outputs in [0, 255] — normalize to [0, 1]
                    flat[c * ModelSize * ModelSize + y * ModelSize + x] =
                        Math.Clamp(outputTensor[0, c, y, x] / 255f, 0f, 1f);
                }
            }
        }

        return flat;
    }

private static float[,] ScaleMask(bool[,] mask, int srcW, int srcH, int dstW, int dstH)
    {
        var scaled = new float[dstW, dstH];
        for (int dy = 0; dy < dstH; dy++)
        {
            int sy = Math.Min((int)((long)dy * srcH / dstH), srcH - 1);
            for (int dx = 0; dx < dstW; dx++)
            {
                int sx = Math.Min((int)((long)dx * srcW / dstW), srcW - 1);
                scaled[dx, dy] = mask[sx, sy] ? 1f : 0f;
            }
        }
        return scaled;
    }

    private static Image<Rgba32> BuildOutputImage(float[] output, int width, int height)
    {
        var img = new Image<Rgba32>(width, height);
        int plane = width * height;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                byte r = (byte)(output[i] * 255f);
                byte g = (byte)(output[plane + i] * 255f);
                byte b = (byte)(output[2 * plane + i] * 255f);
                img[x, y] = new Rgba32(r, g, b, 255);
            }
        }
        return img;
    }

    private static Image<Rgba32> ComposeResult(Image<Rgba32> original, Image<Rgba32> inpainted, bool[,] mask)
    {
        var result = original.Clone();
        for (int y = 0; y < original.Height; y++)
        {
            for (int x = 0; x < original.Width; x++)
            {
                if (mask[x, y])
                    result[x, y] = new Rgba32(inpainted[x, y].R, inpainted[x, y].G, inpainted[x, y].B, original[x, y].A);
            }
        }
        return result;
    }

    private void EnsureSession(IProgress<InpaintProgress> progress, CancellationToken cancellationToken)
    {
        if (_sessionReady) return;

        _initGate.Wait(cancellationToken);
        try
        {
            if (_sessionReady) return;
            progress.Report(new InpaintProgress(3, "Loading ONNX model..."));
            var modelPath = ModelLocator.GetModelPath(_def);
            AppLogger.LogStatic($"ObjectRemovalEngine: loading session. model={modelPath}");
            // ForceCpu=true: LaMa-based models use FFC/FFT operators unsupported by DirectML at runtime
            (_session, _providerName) = CreateSession(modelPath, forceCpu: _def.ForceCpu);
            AppLogger.LogStatic($"ObjectRemovalEngine: session ready. provider={_providerName}");
            _sessionReady = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private static (InferenceSession session, string provider) CreateSession(string modelPath, bool forceCpu) =>
        OnnxProviderHelper.CreateSessionPreferred(modelPath, "ObjectRemovalEngine", forceCpu);

    private static void DeletePartialOutput(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath)) return;
        try { File.Delete(outputPath); }
        catch (Exception ex) { AppLogger.LogStatic($"ObjectRemovalEngine: failed to delete partial output. {ex.Message}"); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _session?.Dispose();
        _session = null;
        _initGate.Dispose();
    }
}
