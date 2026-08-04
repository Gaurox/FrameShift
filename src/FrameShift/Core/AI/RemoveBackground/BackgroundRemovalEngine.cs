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
    private const long MaxPixels = 80_000_000L;
    private readonly BackgroundRemovalModelDefinition _model;

    public string Provider { get; private set; } = "—";

    private InferenceSession? _session;
    private string _providerName = "None";
    private Type _inputElementType = typeof(float);
    private Type _outputElementType = typeof(float);
    private string _inputName = "input";

    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _sessionReady;

    public BackgroundRemovalEngine(BackgroundRemovalModelDefinition model)
    {
        _model = model;
    }

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
            ctx.Resize(_model.InputSize, _model.InputSize));

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new InferenceProgress(30, "Building input tensor..."));

        var inputs = BuildInputs(resized);

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new InferenceProgress(50, GetInferenceStatus()));

        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results;
        try
        {
            results = RunInferenceWithProgress(inputs, progress, cancellationToken);
        }
        catch (Exception ex) when (_providerName == "DirectML" && OnnxProviderHelper.IsLikelyDmlRuntimeFailure(ex))
        {
            AppLogger.LogStatic($"BackgroundRemovalEngine: DirectML inference failed, retrying on CPU. {ex}");
            progress.Report(new InferenceProgress(50, OnnxProviderHelper.GetDmlFallbackUserMessage()));
            _session?.Dispose();
            _session = null;
            _sessionReady = false;
            var modelPath = ModelLocator.GetModelPath(_model);
            var cpuOpts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            _session = new InferenceSession(modelPath, cpuOpts);
            _providerName = "CPU";
            Provider = "CPU";
            _sessionReady = true;
            results = RunInferenceWithProgress(inputs, progress, cancellationToken);
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

            var modelPath = ModelLocator.GetModelPath(_model);
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
        var size = resized.Width;
        var tensor = new DenseTensor<float>(new[] { 1, 3, size, size });
        Memory<float> buffer = tensor.Buffer;
        int plane = size * size;
        resized.ProcessPixelRows(acc =>
        {
            Span<float> buf = buffer.Span;
            for (int y = 0; y < size; y++)
            {
                Span<Rgba32> row = acc.GetRowSpan(y);
                int rowOff = y * size;
                for (int x = 0; x < size; x++)
                {
                    var (r, g, b) = GetNormalizedPixel(row[x]);
                    buf[rowOff + x] = r;
                    buf[plane + rowOff + x] = g;
                    buf[2 * plane + rowOff + x] = b;
                }
            }
        });
        return NamedOnnxValue.CreateFromTensor(inputName, tensor);
    }

    private static NamedOnnxValue BuildInputF16(SixLabors.ImageSharp.Image<Rgba32> resized, string inputName)
    {
        var size = resized.Width;
        var tensor = new DenseTensor<Float16>(new[] { 1, 3, size, size });
        Memory<Float16> buffer = tensor.Buffer;
        int plane = size * size;
        resized.ProcessPixelRows(acc =>
        {
            Span<Float16> buf = buffer.Span;
            for (int y = 0; y < size; y++)
            {
                Span<Rgba32> row = acc.GetRowSpan(y);
                int rowOff = y * size;
                for (int x = 0; x < size; x++)
                {
                    var (r, g, b) = GetNormalizedPixel(row[x]);
                    buf[rowOff + x] = (Float16)r;
                    buf[plane + rowOff + x] = (Float16)g;
                    buf[2 * plane + rowOff + x] = (Float16)b;
                }
            }
        });
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

    // BiRefNet upstream weights output raw logits, so sigmoid is applied here (per
    // preprocessor_config.json / HF inference example). The BRIA RMBG-2.0 transformers.js
    // export already outputs a [0,1] alpha matte ("alphas"), so for those models
    // (_model.OutputIsAlpha) the value is used directly without a second sigmoid.
    private SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.L8> BuildMask(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        if (_outputElementType == typeof(Float16))
        {
            var output = results.First().AsTensor<Float16>();
            var sizeY = output.Dimensions[^2];
            var sizeX = output.Dimensions[^1];
            var mask = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.L8>(sizeX, sizeY);
            mask.ProcessPixelRows(acc =>
            {
                for (int y = 0; y < sizeY; y++)
                {
                    Span<L8> row = acc.GetRowSpan(y);
                    for (int x = 0; x < sizeX; x++)
                        row[x] = new L8(MaskByte((float)output[0, 0, y, x]));
                }
            });
            return mask;
        }

        var outputF32 = results.First().AsTensor<float>();
        var outputSizeY = outputF32.Dimensions[^2];
        var outputSizeX = outputF32.Dimensions[^1];
        var outputMask = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.L8>(outputSizeX, outputSizeY);
        outputMask.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < outputSizeY; y++)
            {
                Span<L8> row = acc.GetRowSpan(y);
                for (int x = 0; x < outputSizeX; x++)
                    row[x] = new L8(MaskByte(outputF32[0, 0, y, x]));
            }
        });
        return outputMask;
    }

    private byte MaskByte(float value)
    {
        var alpha = _model.OutputIsAlpha
            ? Math.Clamp(value, 0f, 1f)
            : 1f / (1f + MathF.Exp(-value));
        return (byte)(alpha * 255f);
    }

    private (InferenceSession session, string provider) CreateSession(string modelPath) =>
        OnnxProviderHelper.CreateSessionPreferred(modelPath, "BackgroundRemovalEngine", _model.ForceCpu);

    private IDisposableReadOnlyCollection<DisposableNamedOnnxValue> RunInferenceWithProgress(
        IReadOnlyCollection<NamedOnnxValue> inputs,
        IProgress<InferenceProgress> progress,
        CancellationToken cancellationToken)
    {
        const int startPercent = 50;
        const int endPercent = 82;

        var status = GetInferenceStatus();
        using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progressTask = Task.Run(async () =>
        {
            var currentPercent = startPercent;
            progress.Report(new InferenceProgress(currentPercent, status));

            while (!progressCts.IsCancellationRequested && currentPercent < endPercent)
            {
                try
                {
                    await Task.Delay(1000, progressCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (progressCts.IsCancellationRequested)
                {
                    break;
                }

                currentPercent++;
                progress.Report(new InferenceProgress(currentPercent, status));
            }
        }, CancellationToken.None);

        try
        {
            return _session!.Run(inputs);
        }
        finally
        {
            progressCts.Cancel();
            try
            {
                progressTask.Wait(1000);
            }
            catch
            {
            }

            progress.Report(new InferenceProgress(endPercent, status));
        }
    }

    private string GetInferenceStatus()
    {
        if (_providerName == "CPU")
        {
            return "Running AI inference on CPU...";
        }

        if (_providerName == "DirectML")
        {
            return "Running AI inference on GPU...";
        }

        return "Running AI inference...";
    }

    private static SixLabors.ImageSharp.Image<Rgba32> CompositeFinalImage(
        SixLabors.ImageSharp.Image<Rgba32> original,
        SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.L8> mask)
    {
        int w = original.Width;
        int h = original.Height;
        var result = new SixLabors.ImageSharp.Image<Rgba32>(w, h);
        original.ProcessPixelRows(result, mask, (srcAcc, dstAcc, maskAcc) =>
        {
            for (int y = 0; y < h; y++)
            {
                Span<Rgba32> srcRow = srcAcc.GetRowSpan(y);
                Span<Rgba32> dstRow = dstAcc.GetRowSpan(y);
                Span<L8> mskRow = maskAcc.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    var s = srcRow[x];
                    dstRow[x] = new Rgba32(s.R, s.G, s.B, mskRow[x].PackedValue);
                }
            }
        });
        return result;
    }

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
