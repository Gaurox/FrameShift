using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.FFmpeg;

namespace FrameShift.Windows.Helpers;

public static class PreviewFrameHelper
{
    public static async Task<Bitmap> CaptureFrameAsync(
        string ffmpegPath,
        FfmpegRunner ffmpegRunner,
        string inputPath,
        double seconds,
        string currentAction,
        CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"frameshift_preview_{Guid.NewGuid():N}.png");

        try
        {
            var arguments = new[]
            {
                "-hide_banner",
                "-loglevel", "error",
                "-y",
                "-ss", Math.Max(0.0, seconds).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                "-i", inputPath,
                "-frames:v", "1",
                tempPath
            };

            var result = await ffmpegRunner.RunAsync(
                ffmpegPath,
                arguments,
                null,
                null,
                null,
                inputPath,
                currentAction,
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
}
