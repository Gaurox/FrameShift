using System;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;

namespace FrameShift.Windows.Helpers;

public static class PreviewFrameHelper
{
    private const int AnimatedPreviewFps = 12;
    private const int AnimatedPreviewMaxDimension = 960;

    public static async Task<Bitmap> CaptureFrameAsync(
        string ffmpegPath,
        FfmpegRunner ffmpegRunner,
        string inputPath,
        double seconds,
        string currentAction,
        CancellationToken cancellationToken)
    {
        return await CaptureFrameAsync(
            ffmpegPath,
            ffmpegRunner,
            inputPath,
            seconds,
            currentAction,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Bitmap> CaptureFrameAsync(
        string ffmpegPath,
        FfmpegRunner ffmpegRunner,
        string inputPath,
        double seconds,
        string currentAction,
        string? videoFilter,
        CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"frameshift_preview_{Guid.NewGuid():N}.png");

        try
        {
            var arguments = new System.Collections.Generic.List<string>
            {
                "-hide_banner",
                "-loglevel", "error",
                "-y",
                "-ss", Math.Max(0.0, seconds).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                "-i", inputPath
            };

            if (!string.IsNullOrWhiteSpace(videoFilter))
            {
                arguments.AddRange(["-vf", videoFilter]);
            }

            arguments.AddRange(["-frames:v", "1", tempPath]);

            var result = await ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                null,
                null,
                inputPath,
                currentAction,
                "CPU",
                cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested || result.Canceled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (result.ExitCode != 0 || !File.Exists(tempPath))
            {
                throw new InvalidOperationException(FrameShift.Core.Actions.MediaActionMessages.PreviewGenerationFailed());
            }

            using var stream = File.OpenRead(tempPath);
            using var image = Image.FromStream(stream, false, false);
            return new Bitmap(image);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }
    }

    public static async Task<string> CreateAnimatedGifAsync(
        string ffmpegPath,
        FfmpegRunner ffmpegRunner,
        string inputPath,
        string currentAction,
        CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"frameshift_preview_{Guid.NewGuid():N}.gif");

        try
        {
            var arguments = new List<string>
            {
                "-hide_banner",
                "-loglevel", "error",
                "-y",
                "-i", inputPath,
                "-vf", BuildAnimatedPreviewGifFilter(),
                "-loop", "0",
                tempPath
            };

            var result = await ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                null,
                null,
                inputPath,
                currentAction,
                "CPU",
                cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested || result.Canceled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (result.ExitCode != 0 || !File.Exists(tempPath))
            {
                throw new InvalidOperationException(FrameShift.Core.Actions.MediaActionMessages.PreviewGenerationFailed());
            }

            return tempPath;
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }

            throw;
        }
    }

    private static string BuildAnimatedPreviewGifFilter()
    {
        return $"fps={AnimatedPreviewFps},scale='if(gte(iw,ih),min(iw,{AnimatedPreviewMaxDimension}),-2)':'if(gte(iw,ih),-2,min(ih,{AnimatedPreviewMaxDimension}))':flags=lanczos";
    }
}
