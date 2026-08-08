using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.AI.Upscale;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.PixelFormats;
using SharpImage = SixLabors.ImageSharp.Image;

namespace FrameShift.Core.AI.VideoUpscale;

internal sealed record VideoUpscaleResult(int ProcessedFrameCount, string Provider);

internal sealed class VideoUpscaleEngine : IDisposable
{
    private readonly UpscaleFrameProcessor _processor;

    public VideoUpscaleEngine(UpscaleModelDefinition model)
    {
        _processor = new UpscaleFrameProcessor(model);
    }

    public async Task<VideoUpscaleResult> ProcessAsync(
        string inputFramesDirectory,
        string outputFramesDirectory,
        UpscaleRequest request,
        IProgress<(int Processed, int Total)>? progress,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var frames = Directory.GetFiles(inputFramesDirectory, "*.bmp", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (frames.Length == 0)
                throw new InvalidOperationException("No extracted video frames were found to upscale.");

            Directory.CreateDirectory(outputFramesDirectory);
            int processed = 0;
            foreach (var inputFramePath in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outputFramePath = Path.Combine(outputFramesDirectory, Path.GetFileName(inputFramePath));
                using (var source = SharpImage.Load<Rgba32>(inputFramePath))
                using (var output = _processor.Upscale(source, request, progress: null, cancellationToken))
                {
                    output.SaveAsBmp(outputFramePath, new BmpEncoder { BitsPerPixel = BmpBitsPerPixel.Pixel32 });
                }

                // The output has been safely persisted; source BMPs are never read again.
                DeleteProcessedInputFrame(inputFramePath, cancellationToken);
                processed++;
                progress?.Report((processed, frames.Length));
            }

            return new VideoUpscaleResult(processed, _processor.Provider);
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static void DeleteProcessedInputFrame(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The action-level finally cleans the isolated temporary root.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose() => _processor.Dispose();
}
