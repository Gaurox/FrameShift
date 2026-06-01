using System;
using System.Collections.Generic;
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

namespace FrameShift.Core.AI.RemoveBackground;

internal sealed class BackgroundRemovalEngine : IBackgroundRemovalEngine, IDisposable
{
    private const int ModelSize = 1024;
    private const long MaxPixels = 80_000_000L;

    public string Provider { get; private set; } = "—";

    private InferenceSession? _session;
    private string _providerName = "None";
    private Type _inputElementType = typeof(float);
    private Type _outputElementType = typeof(float);
    private string _inputName = "input";

    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _sessionReady;

    public async Task<string> RemoveBackgroundAsync(
        string inputPath,
        IProgress<InferenceProgress> progress,
        CancellationToken cancellationToken)
    {
        return await Task.Run(
            () => ProcessImageCore(inputPath, progress, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private string ProcessImageCore(
        string inputPath,
        IProgress<InferenceProgress> progress,
        CancellationToken cancellationToken)
    {
        string? outputPath = null;

        cancellationToken.ThrowIfCancellationRequested();

        ValidateInput(inputPath);

        EnsureSession(progress, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new InferenceProgress(5, "Loading image..."));

        using var original = SharpImage.Load<Rgba32>(inputPath);

        if ((long)original.Width * original.Height > MaxPixels)
            throw new InvalidOperationException(
                $"Image too large: {original.Width}x{original.Height}. " +
                $"Limit is {MaxPixels:N0} pixels.");

        AppLogger.LogStatic(
            $"BackgroundRemovalEngine: image loaded. size={original.Width}x{original.Height}");

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new InferenceProgress(15, "Resizing for inference..."));

        using var resized = original.Clone(ctx =>
            ctx.Resize(ModelSize, ModelSize));

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new InferenceProgress(30, "Building input tensor..."));

        var inputs = BuildInputs(resized);

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new InferenceProgress(50, "Running AI inference..."));

        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results;
        try
        {
            results = _session!.Run(inputs);
        }
        catch (Exception ex) when (_providerName == "DirectML" && OnnxProviderHelper.IsLikelyDmlRuntimeFailure(ex))
        {
            AppLogger.LogStatic($"BackgroundRemovalEngine: DirectML inference failed, retrying on CPU. {ex}");
            progress.Report(new InferenceProgress(50, OnnxProviderHelper.GetDmlFallbackUserMessage()));
            _session?.Dispose();
            _session = null;
            _sessionReady = false;
            var modelPath = ModelLocator.ModelPath;
            var cpuOpts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            _session = new InferenceSession(modelPath, cpuOpts);
            _providerName = "CPU";
            Provider = "CPU";
            _sessionReady = true;
            results = _session.Run(inputs);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new InferenceProgress(70, "Building alpha mask..."));

        SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.L8> mask;
        using (results)
        {
            mask = BuildMask(results);
        }

        using (mask)
        {
            mask.Mutate(ctx => ctx.Resize(original.Width, original.Height));

            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(new InferenceProgress(85, "Compositing final PNG..."));

            using var finalImage = CompositeFinalImage(original, mask);

            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(new InferenceProgress(95, "Saving PNG..."));

            outputPath = OutputPathHelper.CreateUniqueOutputPath(inputPath, "_nobg", ".png");

            try
            {
                finalImage.SaveAsPng(outputPath, new PngEncoder
                {
                    CompressionLevel = PngCompressionLevel.BestSpeed
                });
            }
            catch
            {
                DeletePartialOutput(outputPath);
                throw;
            }
        }

        progress.Report(new InferenceProgress(100, "Done."));

        AppLogger.LogStatic($"BackgroundRemovalEngine: complete. output={outputPath}");

        return outputPath;
    }

    private void EnsureSession(IProgress<InferenceProgress> progress, CancellationToken cancellationToken)
    {
        if (_sessionReady) return;

        _initGate.Wait(cancellationToken);
        try
        {
            if (_sessionReady) return;

            progress.Report(new InferenceProgress(2, "Loading ONNX model..."));

            var modelPath = ModelLocator.ModelPath;
            AppLogger.LogStatic($"BackgroundRemovalEngine: loading session. model={modelPath}");

            (_session, _providerName) = CreateSession(modelPath);
            Provider = _providerName;

            var firstInput = _session.InputMetadata.First();
            _inputName = firstInput.Key;
            _inputElementType = firstInput.Value.ElementType;
            _outputElementType = _session.OutputMetadata.First().Value.ElementType;

            AppLogger.LogStatic(
                $"BackgroundRemovalEngine: session ready. provider={_providerName}");
            AppLogger.LogStatic(
                "ONNX inputs: " + string.Join(", ",
                    _session.InputMetadata.Select(x => $"{x.Key}:{x.Value.ElementType.Name}")));
            AppLogger.LogStatic(
                "ONNX outputs: " + string.Join(", ",
                    _session.OutputMetadata.Select(x => $"{x.Key}:{x.Value.ElementType.Name}")));

            _sessionReady = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private List<NamedOnnxValue> BuildInputs(SixLabors.ImageSharp.Image<Rgba32> resized)
    {
        if (_inputElementType == typeof(Float16))
            return [BuildInputF16(resized, _inputName)];

        return [BuildInputF32(resized, _inputName)];
    }

    private static NamedOnnxValue BuildInputF32(SixLabors.ImageSharp.Image<Rgba32> resized, string inputName)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 3, ModelSize, ModelSize });

        for (int y = 0; y < ModelSize; y++)
        {
            for (int x = 0; x < ModelSize; x++)
            {
                var (r, g, b) = GetNormalizedPixel(resized[x, y]);
                tensor[0, 0, y, x] = r;
                tensor[0, 1, y, x] = g;
                tensor[0, 2, y, x] = b;
            }
        }

        return NamedOnnxValue.CreateFromTensor(inputName, tensor);
    }

    private static NamedOnnxValue BuildInputF16(SixLabors.ImageSharp.Image<Rgba32> resized, string inputName)
    {
        var tensor = new DenseTensor<Float16>(new[] { 1, 3, ModelSize, ModelSize });

        for (int y = 0; y < ModelSize; y++)
        {
            for (int x = 0; x < ModelSize; x++)
            {
                var (r, g, b) = GetNormalizedPixel(resized[x, y]);
                tensor[0, 0, y, x] = (Float16)r;
                tensor[0, 1, y, x] = (Float16)g;
                tensor[0, 2, y, x] = (Float16)b;
            }
        }

        return NamedOnnxValue.CreateFromTensor(inputName, tensor);
    }

    // Alpha-composite over white, rescale to [0,1], then apply ImageNet normalization.
    // Source: preprocessor_config.json — do_normalize=true, mean=[0.485,0.456,0.406], std=[0.229,0.224,0.225]
    private static (float r, float g, float b) GetNormalizedPixel(Rgba32 pixel)
    {
        float alpha = pixel.A / 255f;
        float r = (((pixel.R * alpha) + (255f * (1f - alpha))) / 255f - 0.485f) / 0.229f;
        float g = (((pixel.G * alpha) + (255f * (1f - alpha))) / 255f - 0.456f) / 0.224f;
        float b = (((pixel.B * alpha) + (255f * (1f - alpha))) / 255f - 0.406f) / 0.225f;
        return (r, g, b);
    }

    // Model outputs raw logits — sigmoid applied here per preprocessor_config.json / HF inference example.
    private SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.L8> BuildMask(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        var mask = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.L8>(ModelSize, ModelSize);

        if (_outputElementType == typeof(Float16))
        {
            var output = results.First().AsTensor<Float16>();
            for (int y = 0; y < ModelSize; y++)
                for (int x = 0; x < ModelSize; x++)
                {
                    float sigmoid = 1f / (1f + MathF.Exp(-(float)output[0, 0, y, x]));
                    mask[x, y] = new L8((byte)(sigmoid * 255f));
                }
        }
        else
        {
            var output = results.First().AsTensor<float>();
            for (int y = 0; y < ModelSize; y++)
                for (int x = 0; x < ModelSize; x++)
                {
                    float sigmoid = 1f / (1f + MathF.Exp(-output[0, 0, y, x]));
                    mask[x, y] = new L8((byte)(sigmoid * 255f));
                }
        }

        return mask;
    }

    private static SixLabors.ImageSharp.Image<Rgba32> CompositeFinalImage(
        SixLabors.ImageSharp.Image<Rgba32> original,
        SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.L8> mask)
    {
        var result = new SixLabors.ImageSharp.Image<Rgba32>(original.Width, original.Height);
        for (int y = 0; y < original.Height; y++)
        {
            for (int x = 0; x < original.Width; x++)
            {
                var src = original[x, y];
                byte alpha = mask[x, y].PackedValue;
                result[x, y] = new Rgba32(src.R, src.G, src.B, alpha);
            }
        }
        return result;
    }

    private static (InferenceSession session, string provider) CreateSession(string modelPath) =>
        OnnxProviderHelper.CreateSessionPreferred(modelPath, "BackgroundRemovalEngine");

    private static void ValidateInput(string inputPath)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Input image not found.", inputPath);

        var ext = Path.GetExtension(inputPath).ToLowerInvariant();
        if (ext is not ".png" and not ".jpg" and not ".jpeg" and not ".webp" and not ".bmp")
            throw new NotSupportedException($"Unsupported image format: {ext}");
    }

    // Loads the ONNX session and logs all input/output metadata without running inference.
    internal async Task<string> DiagnoseSessionAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            EnsureSession(new Progress<InferenceProgress>(), cancellationToken);
            return
                $"provider={_providerName} " +
                $"input='{_inputName}'({_inputElementType.Name}) " +
                $"output({_outputElementType.Name})";
        }, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _session?.Dispose();
        _initGate.Dispose();
    }

    private static void DeletePartialOutput(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
        {
            return;
        }

        try
        {
            File.Delete(outputPath);
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"BackgroundRemovalEngine: failed to delete partial output '{outputPath}'. {ex}");
        }
    }
}
