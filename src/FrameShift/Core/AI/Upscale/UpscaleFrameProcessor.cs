using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FrameShift.Core.AI.Upscale;

/// <summary>
/// Reusable per-frame upscale processor. The ONNX session is kept for the lifetime of the processor,
/// allowing video processing to reuse one DirectML/CPU session across all frames.
/// </summary>
internal sealed class UpscaleFrameProcessor : IDisposable
{
    private const int DefaultTileSize = 512;
    private const int TileMargin = 16;
    private static readonly int[] TileSizeLadder = [DefaultTileSize, 256, 128];

    public const long MaxInputPixels = 16_000_000L;

    private readonly UpscaleModelDefinition _definition;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private InferenceSession? _session;
    private DenseTensor<float>? _inputTensor;
    private IReadOnlyList<NamedOnnxValue>? _reusableInputs;
    private string _providerName = "None";
    private bool _sessionReady;
    private bool _sessionForcedCpu;
    private bool _runtimeForceCpu;
    private int _bufferedInputWidth;
    private int _bufferedInputHeight;

    public UpscaleFrameProcessor(UpscaleModelDefinition definition)
    {
        _definition = definition;
        _runtimeForceCpu = definition.ForceCpu;
    }

    public string Provider => _providerName;

    public Image<Rgba32> Upscale(
        Image<Rgba32> source,
        UpscaleRequest request,
        IProgress<UpscaleProgress>? progress,
        CancellationToken cancellationToken) =>
        UpscaleCore(
            source.Width,
            source.Height,
            request,
            progress,
            cancellationToken,
            (tile, input, padWidth, padHeight) => FillInputFromRgba32(source, tile, input, padWidth, padHeight));

    public Image<Rgba32> Upscale(
        Image<Rgb24> source,
        UpscaleRequest request,
        IProgress<UpscaleProgress>? progress,
        CancellationToken cancellationToken) =>
        UpscaleCore(
            source.Width,
            source.Height,
            request,
            progress,
            cancellationToken,
            (tile, input, padWidth, padHeight) => FillInputFromRgb24(source, tile, input, padWidth, padHeight));

    private Image<Rgba32> UpscaleCore(
        int sourceWidth,
        int sourceHeight,
        UpscaleRequest request,
        IProgress<UpscaleProgress>? progress,
        CancellationToken cancellationToken,
        FillTileDelegate fillTile)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSourceSize(sourceWidth, sourceHeight);

        int tileIndex = 0;
        while (true)
        {
            int tileSize = TileSizeLadder[tileIndex];
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                EnsureSession(_runtimeForceCpu, progress, cancellationToken);
                var nativeOutput = RunTiled(sourceWidth, sourceHeight, tileSize, progress, cancellationToken, fillTile);
                try
                {
                    var target = ResolveFinalSize(sourceWidth, sourceHeight, request, _definition.ScaleFactor);

                    // Hand the freshly-built image straight to the caller; no per-frame clone.
                    if (target.Width == nativeOutput.Width && target.Height == nativeOutput.Height)
                        return nativeOutput;

                    progress?.Report(new UpscaleProgress(93, "Resizing to target..."));
                    nativeOutput.Mutate(ctx => ctx.Resize(new ResizeOptions
                    {
                        Size = new SixLabors.ImageSharp.Size(target.Width, target.Height),
                        Sampler = KnownResamplers.Lanczos3,
                        Mode = ResizeMode.Stretch
                    }));
                    return nativeOutput;
                }
                catch
                {
                    nativeOutput.Dispose();
                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool dmlFailure = !_runtimeForceCpu &&
                    string.Equals(_providerName, "DirectML", StringComparison.OrdinalIgnoreCase) &&
                    OnnxProviderHelper.IsLikelyDmlRuntimeFailure(ex);
                if (dmlFailure)
                {
                    AppLogger.LogStatic($"UpscaleFrameProcessor: DirectML runtime failure, falling back to CPU. {ex.Message}");
                    progress?.Report(new UpscaleProgress(5, OnnxProviderHelper.GetDmlFallbackUserMessage()));
                    _runtimeForceCpu = true;
                    ResetSession();
                    continue;
                }

                if (tileIndex < TileSizeLadder.Length - 1)
                {
                    tileIndex++;
                    AppLogger.LogStatic(
                        $"UpscaleFrameProcessor: tile run failed at tileSize={tileSize}, retrying at {TileSizeLadder[tileIndex]}. {ex.Message}");
                    ResetSession();
                    continue;
                }

                AppLogger.LogStatic($"UpscaleFrameProcessor: upscale failed after all fallbacks. {ex}");
                throw;
            }
        }
    }

    public static void ValidateSourceSize(int width, int height)
    {
        if ((long)width * height > MaxInputPixels)
            throw new InvalidOperationException(
                $"Image too large to upscale: {width}x{height}. Limit is {MaxInputPixels:N0} source pixels.");
    }

    public static (int Width, int Height, string Suffix) ResolveFinalSize(
        int sourceWidth,
        int sourceHeight,
        UpscaleRequest request,
        int nativeScaleFactor = 4)
    {
        int nativeWidth = sourceWidth * nativeScaleFactor;
        int nativeHeight = sourceHeight * nativeScaleFactor;

        if (request.TargetWidth is int targetWidth && request.TargetHeight is int targetHeight)
        {
            int width = Math.Clamp(targetWidth, sourceWidth, nativeWidth);
            int height = Math.Clamp(targetHeight, sourceHeight, nativeHeight);
            return (width, height, $"_upscaled_{width}x{height}");
        }

        double factor = Math.Clamp(request.Factor, 1d, nativeScaleFactor);
        int roundedFactor = (int)Math.Round(factor);
        int resolvedWidth = Math.Clamp((int)Math.Round(sourceWidth * factor), sourceWidth, nativeWidth);
        int resolvedHeight = Math.Clamp((int)Math.Round(sourceHeight * factor), sourceHeight, nativeHeight);
        return (resolvedWidth, resolvedHeight, $"_upscaled_{roundedFactor}x");
    }

    private Image<Rgba32> RunTiled(
        int sourceWidth,
        int sourceHeight,
        int tileSize,
        IProgress<UpscaleProgress>? progress,
        CancellationToken cancellationToken,
        FillTileDelegate fillTile)
    {
        int scale = _definition.ScaleFactor;
        var tiles = UpscaleTiler.Plan(sourceWidth, sourceHeight, tileSize, TileMargin);
        AppLogger.LogStatic(
            $"UpscaleFrameProcessor: tiling. tileSize={tileSize}, margin={TileMargin}, tiles={tiles.Count}, provider={_providerName}");

        var output = new Image<Rgba32>(sourceWidth * scale, sourceHeight * scale);
        try
        {
            int done = 0;
            foreach (var tile in tiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RunTileToOutput(tile, scale, output, fillTile);
                done++;
                int percent = 10 + (int)(80L * done / Math.Max(1, tiles.Count));
                progress?.Report(new UpscaleProgress(
                    percent,
                    tiles.Count > 1 ? $"Upscaling tile {done}/{tiles.Count}..." : "Upscaling..."));
            }
        }
        catch
        {
            output.Dispose();
            throw;
        }

        return output;
    }

    private void RunTileToOutput(
        UpscaleTiler.TilePlan tile,
        int scale,
        Image<Rgba32> output,
        FillTileDelegate fillTile)
    {
        int padWidth = RoundUpToMultiple(tile.ReadW, _definition.WindowMultiple);
        int padHeight = RoundUpToMultiple(tile.ReadH, _definition.WindowMultiple);
        EnsureReusableInputBuffer(padWidth, padHeight);
        fillTile(tile, _inputTensor!, padWidth, padHeight);

        using var results = _session!.Run(_reusableInputs!);
        var outputTensor = results.First().AsTensor<float>();
        int sourceOffsetX = (tile.CoreX - tile.ReadX) * scale;
        int sourceOffsetY = (tile.CoreY - tile.ReadY) * scale;
        int coreWidth = tile.CoreW * scale;
        int coreHeight = tile.CoreH * scale;
        int destinationX = tile.CoreX * scale;
        int destinationY = tile.CoreY * scale;

        if (outputTensor is DenseTensor<float> denseOutputTensor)
        {
            WriteDenseTensorToImage(
                output,
                denseOutputTensor.Buffer,
                padWidth * scale,
                padHeight * scale,
                sourceOffsetX,
                sourceOffsetY,
                coreWidth,
                coreHeight,
                destinationX,
                destinationY);
            return;
        }

        WriteTensorToImage(
            output,
            outputTensor,
            sourceOffsetX,
            sourceOffsetY,
            coreWidth,
            coreHeight,
            destinationX,
            destinationY);
    }

    private void EnsureSession(
        bool forceCpu,
        IProgress<UpscaleProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_sessionReady && _sessionForcedCpu == forceCpu) return;

        _initGate.Wait(cancellationToken);
        try
        {
            if (_sessionReady && _sessionForcedCpu == forceCpu) return;
            DisposeSession();
            progress?.Report(new UpscaleProgress(3, "Loading ONNX model..."));
            var modelPath = ModelLocator.GetModelPath(_definition);
            AppLogger.LogStatic($"UpscaleFrameProcessor: loading session. model={modelPath}, forceCpu={forceCpu}");
            (_session, _providerName) = OnnxProviderHelper.CreateSessionPreferred(
                modelPath,
                "UpscaleFrameProcessor",
                forceCpu);
            _sessionForcedCpu = forceCpu;
            _sessionReady = true;
            AppLogger.LogStatic($"UpscaleFrameProcessor: session ready. provider={_providerName}");
        }
        finally
        {
            _initGate.Release();
        }
    }

    private void ResetSession()
    {
        _initGate.Wait();
        try { DisposeSession(); }
        finally { _initGate.Release(); }
    }

    private void DisposeSession()
    {
        _session?.Dispose();
        _session = null;
        DisposeReusableInputBuffer();
        _sessionReady = false;
        _providerName = "None";
    }

    private void EnsureReusableInputBuffer(int padWidth, int padHeight)
    {
        if (_inputTensor is not null &&
            _reusableInputs is not null &&
            _bufferedInputWidth == padWidth &&
            _bufferedInputHeight == padHeight)
        {
            return;
        }

        DisposeReusableInputBuffer();
        _inputTensor = new DenseTensor<float>([1, 3, padHeight, padWidth]);
        _reusableInputs =
        [
            NamedOnnxValue.CreateFromTensor(_definition.InputTensorName, _inputTensor)
        ];
        _bufferedInputWidth = padWidth;
        _bufferedInputHeight = padHeight;
    }

    private void DisposeReusableInputBuffer()
    {
        _reusableInputs = null;
        _inputTensor = null;
        _bufferedInputWidth = 0;
        _bufferedInputHeight = 0;
    }

    private static void FillInputFromRgba32(
        Image<Rgba32> source,
        UpscaleTiler.TilePlan tile,
        DenseTensor<float> input,
        int padWidth,
        int padHeight)
    {
        const float normalizationScale = 1f / 255f;
        source.ProcessPixelRows(accessor =>
        {
            var inputBuffer = input.Buffer.Span;
            int planeSize = padWidth * padHeight;
            for (int y = 0; y < padHeight; y++)
            {
                int sourceY = tile.ReadY + Math.Min(y, tile.ReadH - 1);
                var sourceRow = accessor.GetRowSpan(sourceY);
                int rowOffset = y * padWidth;
                int redOffset = rowOffset;
                int greenOffset = planeSize + rowOffset;
                int blueOffset = (planeSize * 2) + rowOffset;
                for (int x = 0; x < padWidth; x++)
                {
                    var pixel = sourceRow[tile.ReadX + Math.Min(x, tile.ReadW - 1)];
                    inputBuffer[redOffset + x] = pixel.R * normalizationScale;
                    inputBuffer[greenOffset + x] = pixel.G * normalizationScale;
                    inputBuffer[blueOffset + x] = pixel.B * normalizationScale;
                }
            }
        });
    }

    private static void FillInputFromRgb24(
        Image<Rgb24> source,
        UpscaleTiler.TilePlan tile,
        DenseTensor<float> input,
        int padWidth,
        int padHeight)
    {
        const float normalizationScale = 1f / 255f;
        source.ProcessPixelRows(accessor =>
        {
            var inputBuffer = input.Buffer.Span;
            int planeSize = padWidth * padHeight;
            for (int y = 0; y < padHeight; y++)
            {
                int sourceY = tile.ReadY + Math.Min(y, tile.ReadH - 1);
                var sourceRow = accessor.GetRowSpan(sourceY);
                int rowOffset = y * padWidth;
                int redOffset = rowOffset;
                int greenOffset = planeSize + rowOffset;
                int blueOffset = (planeSize * 2) + rowOffset;
                for (int x = 0; x < padWidth; x++)
                {
                    var pixel = sourceRow[tile.ReadX + Math.Min(x, tile.ReadW - 1)];
                    inputBuffer[redOffset + x] = pixel.R * normalizationScale;
                    inputBuffer[greenOffset + x] = pixel.G * normalizationScale;
                    inputBuffer[blueOffset + x] = pixel.B * normalizationScale;
                }
            }
        });
    }

    private static void WriteDenseTensorToImage(
        Image<Rgba32> output,
        Memory<float> outputBuffer,
        int tensorWidth,
        int tensorHeight,
        int sourceOffsetX,
        int sourceOffsetY,
        int coreWidth,
        int coreHeight,
        int destinationX,
        int destinationY)
    {
        int planeSize = tensorWidth * tensorHeight;
        output.ProcessPixelRows(accessor =>
        {
            var outputBufferSpan = outputBuffer.Span;
            for (int y = 0; y < coreHeight; y++)
            {
                int sourceTensorY = sourceOffsetY + y;
                int outputY = destinationY + y;
                var row = accessor.GetRowSpan(outputY).Slice(destinationX, coreWidth);
                int rowOffset = sourceTensorY * tensorWidth;
                int redOffset = rowOffset;
                int greenOffset = planeSize + rowOffset;
                int blueOffset = (planeSize * 2) + rowOffset;
                for (int x = 0; x < coreWidth; x++)
                {
                    int sourceTensorX = sourceOffsetX + x;
                    row[x] = new Rgba32(
                        ToByte(outputBufferSpan[redOffset + sourceTensorX]),
                        ToByte(outputBufferSpan[greenOffset + sourceTensorX]),
                        ToByte(outputBufferSpan[blueOffset + sourceTensorX]),
                        255);
                }
            }
        });
    }

    private static void WriteTensorToImage(
        Image<Rgba32> output,
        Tensor<float> outputTensor,
        int sourceOffsetX,
        int sourceOffsetY,
        int coreWidth,
        int coreHeight,
        int destinationX,
        int destinationY)
    {
        output.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < coreHeight; y++)
            {
                int sourceTensorY = sourceOffsetY + y;
                int outputY = destinationY + y;
                var row = accessor.GetRowSpan(outputY).Slice(destinationX, coreWidth);
                for (int x = 0; x < coreWidth; x++)
                {
                    int sourceTensorX = sourceOffsetX + x;
                    row[x] = new Rgba32(
                        ToByte(outputTensor[0, 0, sourceTensorY, sourceTensorX]),
                        ToByte(outputTensor[0, 1, sourceTensorY, sourceTensorX]),
                        ToByte(outputTensor[0, 2, sourceTensorY, sourceTensorX]),
                        255);
                }
            }
        });
    }

    private delegate void FillTileDelegate(
        UpscaleTiler.TilePlan tile,
        DenseTensor<float> input,
        int padWidth,
        int padHeight);

    private static int RoundUpToMultiple(int value, int multiple)
    {
        if (multiple <= 1) return value;
        int remainder = value % multiple;
        return remainder == 0 ? value : value + multiple - remainder;
    }

    private static byte ToByte(float value) =>
        (byte)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);

    public void Dispose()
    {
        DisposeSession();
        _initGate.Dispose();
    }
}
