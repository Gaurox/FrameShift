using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SharpImage = SixLabors.ImageSharp.Image;

namespace FrameShift.Core.AI.Upscale;

internal sealed class UpscaleEngine : IUpscaleEngine
{
    private const int DefaultTileSize = 512;
    private const int TileMargin = 16;

    // Adaptive tile-size ladder used when a tile fails (typically GPU out-of-memory).
    private static readonly int[] TileSizeLadder = [DefaultTileSize, 256, 128];

    // Guard against pathological inputs. At x4 the output canvas is 16x the pixel count, held in
    // memory as Rgba32 (4 bytes/px). 16 MP in -> 256 MP out -> ~1 GB output buffer.
    private const long MaxInputPixels = 16_000_000L;

    private readonly UpscaleModelDefinition _def;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private InferenceSession? _session;
    private string _providerName = "None";
    private bool _sessionReady;
    private bool _sessionForcedCpu;

    public string Provider => _providerName;

    public UpscaleEngine(UpscaleModelDefinition def)
    {
        _def = def;
    }

    public async Task<string> UpscaleAsync(
        string inputPath,
        IProgress<UpscaleProgress> progress,
        CancellationToken cancellationToken)
    {
        return await Task.Run(
            () => ProcessImageCore(inputPath, progress, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private string ProcessImageCore(
        string inputPath,
        IProgress<UpscaleProgress> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Input image not found.", inputPath);

        progress.Report(new UpscaleProgress(5, "Loading image..."));
        using var original = SharpImage.Load<Rgba32>(inputPath);

        if ((long)original.Width * original.Height > MaxInputPixels)
            throw new InvalidOperationException(
                $"Image too large to upscale: {original.Width}x{original.Height}. " +
                $"Limit is {MaxInputPixels:N0} source pixels.");

        AppLogger.LogStatic(
            $"UpscaleEngine: image loaded. size={original.Width}x{original.Height}, model={_def.Id}, scale={_def.ScaleFactor}");

        bool forceCpu = _def.ForceCpu;
        bool alreadyFellBackToCpu = false;
        int tileIndex = 0;

        while (true)
        {
            int tileSize = TileSizeLadder[tileIndex];
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                EnsureSession(forceCpu, progress, cancellationToken);
                using var upscaled = RunTiled(original, tileSize, progress, cancellationToken);
                return SaveOutput(inputPath, upscaled, progress);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // DirectML runtime failure -> recreate the session on CPU and retry the same tile size.
                bool dmlFailure = !forceCpu &&
                    string.Equals(_providerName, "DirectML", StringComparison.OrdinalIgnoreCase) &&
                    OnnxProviderHelper.IsLikelyDmlRuntimeFailure(ex);
                if (dmlFailure && !alreadyFellBackToCpu)
                {
                    AppLogger.LogStatic($"UpscaleEngine: DirectML runtime failure, falling back to CPU. {ex.Message}");
                    progress.Report(new UpscaleProgress(5, OnnxProviderHelper.GetDmlFallbackUserMessage()));
                    forceCpu = true;
                    alreadyFellBackToCpu = true;
                    ResetSession();
                    continue;
                }

                // Otherwise assume resource pressure (e.g. out-of-memory) and try a smaller tile.
                if (tileIndex < TileSizeLadder.Length - 1)
                {
                    tileIndex++;
                    AppLogger.LogStatic(
                        $"UpscaleEngine: tile run failed at tileSize={tileSize}, retrying at {TileSizeLadder[tileIndex]}. {ex.Message}");
                    ResetSession();
                    continue;
                }

                AppLogger.LogStatic($"UpscaleEngine: upscale failed after all fallbacks. {ex}");
                throw;
            }
        }
    }

    private Image<Rgba32> RunTiled(
        Image<Rgba32> original,
        int tileSize,
        IProgress<UpscaleProgress> progress,
        CancellationToken cancellationToken)
    {
        int scale = _def.ScaleFactor;
        int outWidth = original.Width * scale;
        int outHeight = original.Height * scale;

        var tiles = UpscaleTiler.Plan(original.Width, original.Height, tileSize, TileMargin);
        AppLogger.LogStatic(
            $"UpscaleEngine: tiling. tileSize={tileSize}, margin={TileMargin}, tiles={tiles.Count}, provider={_providerName}");

        var output = new Image<Rgba32>(outWidth, outHeight);
        try
        {
            int done = 0;
            foreach (var tile in tiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RunTileToOutput(original, tile, scale, output);

                done++;
                int percent = 10 + (int)(80L * done / Math.Max(1, tiles.Count));
                progress.Report(new UpscaleProgress(
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
        Image<Rgba32> original,
        UpscaleTiler.TilePlan tile,
        int scale,
        Image<Rgba32> output)
    {
        // Some models (Swin2SR) require the input to be a multiple of a window size. Pad the read region
        // to that multiple by replicating the right/bottom edges, so the top-left readW x readH region
        // still maps 1:1 to its upscaled counterpart. WindowMultiple == 0 means no padding.
        int padW = RoundUpToMultiple(tile.ReadW, _def.WindowMultiple);
        int padH = RoundUpToMultiple(tile.ReadH, _def.WindowMultiple);

        var input = new DenseTensor<float>([1, 3, padH, padW]);
        for (int y = 0; y < padH; y++)
        {
            int sy = tile.ReadY + Math.Min(y, tile.ReadH - 1);
            for (int x = 0; x < padW; x++)
            {
                var p = original[tile.ReadX + Math.Min(x, tile.ReadW - 1), sy];
                input[0, 0, y, x] = p.R / 255f;
                input[0, 1, y, x] = p.G / 255f;
                input[0, 2, y, x] = p.B / 255f;
            }
        }

        var inputs = new[] { NamedOnnxValue.CreateFromTensor(_def.InputTensorName, input) };
        using var results = _session!.Run(inputs);
        // Both supported models have a single output; reading the first is robust to its name.
        var outTensor = results.First().AsTensor<float>();

        // The core sub-rectangle inside the (upscaled) tile, in output pixels.
        int srcOffsetX = (tile.CoreX - tile.ReadX) * scale;
        int srcOffsetY = (tile.CoreY - tile.ReadY) * scale;
        int coreW = tile.CoreW * scale;
        int coreH = tile.CoreH * scale;
        int destX = tile.CoreX * scale;
        int destY = tile.CoreY * scale;

        for (int y = 0; y < coreH; y++)
        {
            int sy = srcOffsetY + y;
            int oy = destY + y;
            for (int x = 0; x < coreW; x++)
            {
                int sx = srcOffsetX + x;
                byte r = ToByte(outTensor[0, 0, sy, sx]);
                byte g = ToByte(outTensor[0, 1, sy, sx]);
                byte b = ToByte(outTensor[0, 2, sy, sx]);
                output[destX + x, oy] = new Rgba32(r, g, b, 255);
            }
        }
    }

    private static int RoundUpToMultiple(int value, int multiple)
    {
        if (multiple <= 1) return value;
        int remainder = value % multiple;
        return remainder == 0 ? value : value + (multiple - remainder);
    }

    private static byte ToByte(float value) =>
        (byte)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);

    private static string SaveOutput(string inputPath, Image<Rgba32> image, IProgress<UpscaleProgress> progress)
    {
        progress.Report(new UpscaleProgress(95, "Saving PNG..."));
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(inputPath, "_upscaled_4x", ".png");
        try
        {
            image.SaveAsPng(outputPath, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });
        }
        catch
        {
            DeletePartialOutput(outputPath);
            throw;
        }

        progress.Report(new UpscaleProgress(100, "Done."));
        AppLogger.LogStatic($"UpscaleEngine: complete. output={outputPath}");
        return outputPath;
    }

    private void EnsureSession(bool forceCpu, IProgress<UpscaleProgress> progress, CancellationToken cancellationToken)
    {
        if (_sessionReady && _sessionForcedCpu == forceCpu) return;

        _initGate.Wait(cancellationToken);
        try
        {
            if (_sessionReady && _sessionForcedCpu == forceCpu) return;

            DisposeSession();
            progress.Report(new UpscaleProgress(3, "Loading ONNX model..."));
            var modelPath = ModelLocator.GetModelPath(_def);
            AppLogger.LogStatic($"UpscaleEngine: loading session. model={modelPath}, forceCpu={forceCpu}");
            (_session, _providerName) =
                OnnxProviderHelper.CreateSessionPreferred(modelPath, "UpscaleEngine", forceCpu);
            _sessionForcedCpu = forceCpu;
            _sessionReady = true;
            AppLogger.LogStatic($"UpscaleEngine: session ready. provider={_providerName}");
        }
        finally
        {
            _initGate.Release();
        }
    }

    private void ResetSession()
    {
        _initGate.Wait();
        try
        {
            DisposeSession();
        }
        finally
        {
            _initGate.Release();
        }
    }

    private void DisposeSession()
    {
        _session?.Dispose();
        _session = null;
        _sessionReady = false;
        _providerName = "None";
    }

    private static void DeletePartialOutput(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath)) return;
        try { File.Delete(outputPath); }
        catch (Exception ex) { AppLogger.LogStatic($"UpscaleEngine: failed to delete partial output. {ex.Message}"); }
    }

    public void Dispose()
    {
        DisposeSession();
        _initGate.Dispose();
    }
}
