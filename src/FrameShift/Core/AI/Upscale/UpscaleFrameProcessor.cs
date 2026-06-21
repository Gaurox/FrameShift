using System;
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
    private string _providerName = "None";
    private bool _sessionReady;
    private bool _sessionForcedCpu;
    private bool _runtimeForceCpu;

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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSourceSize(source.Width, source.Height);

        int tileIndex = 0;
        while (true)
        {
            int tileSize = TileSizeLadder[tileIndex];
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                EnsureSession(_runtimeForceCpu, progress, cancellationToken);
                using var nativeOutput = RunTiled(source, tileSize, progress, cancellationToken);
                var target = ResolveFinalSize(source.Width, source.Height, request, _definition.ScaleFactor);

                if (target.Width == nativeOutput.Width && target.Height == nativeOutput.Height)
                    return nativeOutput.Clone();

                progress?.Report(new UpscaleProgress(93, "Resizing to target..."));
                var result = nativeOutput.Clone();
                result.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size(target.Width, target.Height),
                    Sampler = KnownResamplers.Lanczos3,
                    Mode = ResizeMode.Stretch
                }));
                return result;
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
        Image<Rgba32> source,
        int tileSize,
        IProgress<UpscaleProgress>? progress,
        CancellationToken cancellationToken)
    {
        int scale = _definition.ScaleFactor;
        var tiles = UpscaleTiler.Plan(source.Width, source.Height, tileSize, TileMargin);
        AppLogger.LogStatic(
            $"UpscaleFrameProcessor: tiling. tileSize={tileSize}, margin={TileMargin}, tiles={tiles.Count}, provider={_providerName}");

        var output = new Image<Rgba32>(source.Width * scale, source.Height * scale);
        try
        {
            int done = 0;
            foreach (var tile in tiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RunTileToOutput(source, tile, scale, output);
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
        Image<Rgba32> source,
        UpscaleTiler.TilePlan tile,
        int scale,
        Image<Rgba32> output)
    {
        int padWidth = RoundUpToMultiple(tile.ReadW, _definition.WindowMultiple);
        int padHeight = RoundUpToMultiple(tile.ReadH, _definition.WindowMultiple);
        var input = new DenseTensor<float>([1, 3, padHeight, padWidth]);

        for (int y = 0; y < padHeight; y++)
        {
            int sourceY = tile.ReadY + Math.Min(y, tile.ReadH - 1);
            for (int x = 0; x < padWidth; x++)
            {
                var pixel = source[tile.ReadX + Math.Min(x, tile.ReadW - 1), sourceY];
                input[0, 0, y, x] = pixel.R / 255f;
                input[0, 1, y, x] = pixel.G / 255f;
                input[0, 2, y, x] = pixel.B / 255f;
            }
        }

        var inputs = new[] { NamedOnnxValue.CreateFromTensor(_definition.InputTensorName, input) };
        using var results = _session!.Run(inputs);
        var outputTensor = results.First().AsTensor<float>();
        int sourceOffsetX = (tile.CoreX - tile.ReadX) * scale;
        int sourceOffsetY = (tile.CoreY - tile.ReadY) * scale;
        int coreWidth = tile.CoreW * scale;
        int coreHeight = tile.CoreH * scale;
        int destinationX = tile.CoreX * scale;
        int destinationY = tile.CoreY * scale;

        for (int y = 0; y < coreHeight; y++)
        {
            int sourceTensorY = sourceOffsetY + y;
            int outputY = destinationY + y;
            for (int x = 0; x < coreWidth; x++)
            {
                int sourceTensorX = sourceOffsetX + x;
                output[destinationX + x, outputY] = new Rgba32(
                    ToByte(outputTensor[0, 0, sourceTensorY, sourceTensorX]),
                    ToByte(outputTensor[0, 1, sourceTensorY, sourceTensorX]),
                    ToByte(outputTensor[0, 2, sourceTensorY, sourceTensorX]),
                    255);
            }
        }
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
        _sessionReady = false;
        _providerName = "None";
    }

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
