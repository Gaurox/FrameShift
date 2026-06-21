using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SharpImage = SixLabors.ImageSharp.Image;

namespace FrameShift.Core.AI.Upscale;

internal sealed class UpscaleEngine : IUpscaleEngine
{
    private readonly UpscaleModelDefinition _definition;
    private readonly UpscaleFrameProcessor _frameProcessor;

    public UpscaleEngine(UpscaleModelDefinition definition)
    {
        _definition = definition;
        _frameProcessor = new UpscaleFrameProcessor(definition);
    }

    public string Provider => _frameProcessor.Provider;

    public async Task<string> UpscaleAsync(
        string inputPath,
        UpscaleRequest request,
        IProgress<UpscaleProgress> progress,
        CancellationToken cancellationToken)
    {
        return await Task.Run(
            () => ProcessImageCore(inputPath, request, progress, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private string ProcessImageCore(
        string inputPath,
        UpscaleRequest request,
        IProgress<UpscaleProgress> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Input image not found.", inputPath);

        progress.Report(new UpscaleProgress(5, "Loading image..."));
        using var source = SharpImage.Load<Rgba32>(inputPath);
        UpscaleFrameProcessor.ValidateSourceSize(source.Width, source.Height);
        AppLogger.LogStatic(
            $"UpscaleEngine: image loaded. size={source.Width}x{source.Height}, model={_definition.Id}, scale={_definition.ScaleFactor}");

        using var output = _frameProcessor.Upscale(source, request, progress, cancellationToken);
        var target = UpscaleFrameProcessor.ResolveFinalSize(
            source.Width,
            source.Height,
            request,
            _definition.ScaleFactor);
        return SaveOutput(inputPath, output, target.Suffix, progress);
    }

    private static string SaveOutput(
        string inputPath,
        Image<Rgba32> image,
        string suffix,
        IProgress<UpscaleProgress> progress)
    {
        progress.Report(new UpscaleProgress(95, "Saving PNG..."));
        var outputPath = OutputPathHelper.CreateUniqueOutputPath(inputPath, suffix, ".png");
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

    private static void DeletePartialOutput(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath)) return;
        try { File.Delete(outputPath); }
        catch (Exception ex) { AppLogger.LogStatic($"UpscaleEngine: failed to delete partial output. {ex.Message}"); }
    }

    public void Dispose() => _frameProcessor.Dispose();
}
