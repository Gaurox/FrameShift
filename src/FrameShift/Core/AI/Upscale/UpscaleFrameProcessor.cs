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
    public const long MaxOutputPixels = 64_000_000L;

    private readonly UpscaleModelDefinition _definition;
    private readonly Action<int, string>? _beforeTileRun;
    private readonly Func<string, bool, (InferenceSession Session, string Provider)> _createSession;
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
    private (int Width, int Height, string Provider)? _successfulTileProfile;
    private int _successfulTileIndex;

    public UpscaleFrameProcessor(UpscaleModelDefinition definition)
        : this(definition, null, null)
    {
    }

    internal UpscaleFrameProcessor(UpscaleModelDefinition definition, Action<int, string>? beforeTileRun,
        Func<string, bool, (InferenceSession Session, string Provider)>? createSession = null)
    {
        _definition = definition;
        _beforeTileRun = beforeTileRun;
        _createSession = createSession ?? ((path, forceCpu) => OnnxProviderHelper.CreateSessionPreferred(path, "UpscaleFrameProcessor", forceCpu));
        _runtimeForceCpu = definition.ForceCpu;
    }

    public string Provider => _providerName;

    public Image<Rgba32> Upscale(
        Image<Rgba32> source,
        UpscaleRequest request,
        IProgress<UpscaleProgress>? progress,
        CancellationToken cancellationToken)
    {
        var output = UpscaleCore(
            source.Width,
            source.Height,
            request,
            progress,
            cancellationToken,
            (tile, input, padWidth, padHeight) => FillInputFromRgba32(source, tile, input, padWidth, padHeight));
        try
        {
            RestoreAlpha(source, output, cancellationToken);
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

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
        ValidateOutputSize(sourceWidth, sourceHeight, request, _definition.ScaleFactor);

        // Session/model errors are not tile-size failures. Initialize before the
        // retry loop and key the remembered size by actual provider and geometry.
        EnsureSession(_runtimeForceCpu, progress, cancellationToken);
        int tileIndex = PreferredTileIndex(sourceWidth, sourceHeight);
        Image<Rgba32> nativeOutput;
        while (true)
        {
            int tileSize = TileSizeLadder[tileIndex];
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _beforeTileRun?.Invoke(tileSize, _providerName);
                nativeOutput = RunTiled(sourceWidth, sourceHeight, tileSize, progress, cancellationToken, fillTile);
                break;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var resourceFailure = IsTileResourceFailure(ex);
                if (resourceFailure && tileIndex < TileSizeLadder.Length - 1)
                {
                    tileIndex++;
                    AppLogger.LogStatic($"UpscaleFrameProcessor: resource limit at tileSize={tileSize}, retrying at {TileSizeLadder[tileIndex]}. {ex.Message}");
                    var previousProvider = _providerName;
                    ResetSession();
                    EnsureSession(_runtimeForceCpu, progress, cancellationToken);
                    if (!string.Equals(previousProvider, _providerName, StringComparison.Ordinal))
                        tileIndex = PreferredTileIndex(sourceWidth, sourceHeight);
                    continue;
                }

                bool dmlFailure = !_runtimeForceCpu && !IsPermanentModelFailure(ex) &&
                    string.Equals(_providerName, "DirectML", StringComparison.OrdinalIgnoreCase) &&
                    OnnxProviderHelper.IsLikelyDmlRuntimeFailure(ex);
                if (dmlFailure)
                {
                    AppLogger.LogStatic($"UpscaleFrameProcessor: DirectML runtime failure, falling back to CPU. {ex.Message}");
                    progress?.Report(new UpscaleProgress(5, OnnxProviderHelper.GetDmlFallbackUserMessage()));
                    _runtimeForceCpu = true;
                    ResetSession();
                    EnsureSession(_runtimeForceCpu, progress, cancellationToken);
                    tileIndex = PreferredTileIndex(sourceWidth, sourceHeight);
                    continue;
                }
                AppLogger.LogStatic($"UpscaleFrameProcessor: upscale failed. {ex}");
                throw;
            }
        }

        try
        {
            var target = ResolveFinalSize(sourceWidth, sourceHeight, request, _definition.ScaleFactor);
            if (target.Width != nativeOutput.Width || target.Height != nativeOutput.Height)
            {
                progress?.Report(new UpscaleProgress(93, L.Get("UpscaleFrameProcessor_Resizing_to_target_19f4c134")));
                cancellationToken.ThrowIfCancellationRequested();
                nativeOutput.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size(target.Width, target.Height),
                    Sampler = KnownResamplers.Lanczos3,
                    Mode = ResizeMode.Stretch
                }));
            }
            cancellationToken.ThrowIfCancellationRequested();
            _successfulTileProfile = (sourceWidth, sourceHeight, _providerName);
            _successfulTileIndex = tileIndex;
            return nativeOutput;
        }
        catch { nativeOutput.Dispose(); throw; }
    }

    private int PreferredTileIndex(int width, int height)
        => _successfulTileProfile == (width, height, _providerName) ? _successfulTileIndex : 0;

    internal static bool IsTileResourceFailure(Exception ex)
        => !IsPermanentModelFailure(ex) && (ex is OutOfMemoryException || ex is OnnxRuntimeException &&
            new[] { "out of memory", "bad_alloc", "failed to allocate", "not enough memory", "memory allocation", "E_OUTOFMEMORY", "0x8007000e" }
                .Any(text => ex.Message.Contains(text, StringComparison.OrdinalIgnoreCase)));

    private static bool IsPermanentModelFailure(Exception ex)
        => ex is ArgumentException or InvalidDataException or FileNotFoundException ||
            ex is OnnxRuntimeException && new[] { "INVALID_ARGUMENT", "InvalidArgument", "INVALID_GRAPH", "InvalidGraph", "INVALID_PROTOBUF", "InvalidProtobuf", "NO_SUCHFILE", "NoSuchFile", "NO_MODEL", "NoModel", "invalid dimensions", "input name", "output name" }
                .Any(text => ex.Message.Contains(text, StringComparison.OrdinalIgnoreCase));

    public static void ValidateSourceSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > MaxInputPixels)
            throw new InvalidOperationException(
                L.Format("UpscaleFrameProcessor_Image_too_large_to_upscale_0_x_1_Lim_5ddf0969",width,height,MaxInputPixels));
    }

    internal static void ValidateOutputSize(int width, int height, UpscaleRequest request, int scale)
    {
        ValidateSourceSize(width, height);
        if (scale < 1 || !double.IsFinite(request.Factor))
            throw new InvalidOperationException(L.Get("UpscaleFrameProcessor_07bbb0bd38"));
        var nativeWidth = (long)width * scale;
        var nativeHeight = (long)height * scale;
        if (nativeWidth > int.MaxValue || nativeHeight > int.MaxValue ||
            nativeWidth > MaxOutputPixels / nativeHeight)
            throw new InvalidOperationException(
                $"Upscale output too large: native {nativeWidth}x{nativeHeight}. Limit is {MaxOutputPixels:N0} pixels. Use a smaller source.");
        var target = ResolveFinalSize(width, height, request, scale);
        if ((long)target.Width * target.Height > MaxOutputPixels)
            throw new InvalidOperationException(L.Get("UpscaleFrameProcessor_98ec4ea0f9"));
    }

    // ONNX models process RGB only. Resize coverage independently at the final output
    // size so both tensor-writing paths preserve transparency without changing RGB.
    private static void RestoreAlpha(Image<Rgba32> source, Image<Rgba32> output, CancellationToken token)
    {
        var transparent = false;
        source.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height && !transparent; y++)
            {
                token.ThrowIfCancellationRequested();
                foreach (var pixel in rows.GetRowSpan(y))
                {
                    if (pixel.A == 255) continue;
                    transparent = true;
                    break;
                }
            }
        });
        if (!transparent) return;

        using var alpha = new Image<L8>(source.Width, source.Height);
        source.ProcessPixelRows(alpha, (pixels, mask) =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var input = pixels.GetRowSpan(y);
                var row = mask.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++) row[x] = new L8(input[x].A);
            }
        });

        if (alpha.Width != output.Width || alpha.Height != output.Height)
        {
            alpha.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(output.Width, output.Height),
                Sampler = KnownResamplers.Lanczos3,
                Mode = ResizeMode.Stretch
            }));
        }

        output.ProcessPixelRows(alpha, (pixels, mask) =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var row = pixels.GetRowSpan(y);
                var coverage = mask.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++) row[x].A = coverage[x].PackedValue;
            }
        });
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
                    tiles.Count > 1 ? L.Format("UpscaleFrameProcessor_Upscaling_tile_0_1_4be54f77",done,tiles.Count) : L.Get("UpscaleFrameProcessor_Upscaling_85e80807")));
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
            progress?.Report(new UpscaleProgress(3, L.Get("UpscaleFrameProcessor_Loading_ONNX_model_559af544")));
            var modelPath = ModelLocator.GetModelPath(_definition);
            AppLogger.LogStatic($"UpscaleFrameProcessor: loading session. model={modelPath}, forceCpu={forceCpu}");
            (_session, _providerName) = _createSession(modelPath, forceCpu);
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
