using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FrameShift.Core.Actions;
using FrameShift.Core.AI.Upscale;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace FrameShift.Core.AI.VideoUpscale;

internal sealed record UpscaleRawVideoPipelineResult(
    string Provider,
    int SourceFrameCount,
    int OutputFrameCount);

internal sealed class UpscaleRawVideoPipeline
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly AppLogger _logger;

    public UpscaleRawVideoPipeline(FfmpegRunner ffmpegRunner, AppLogger logger)
    {
        _ffmpegRunner = ffmpegRunner;
        _logger = logger;
    }

    public async Task<UpscaleRawVideoPipelineResult> RunAsync(
        string ffmpegPath,
        string sourceVideoPath,
        string outputPath,
        UpscaleModelDefinition model,
        MediaProbeResult probe,
        UpscaleRequest request,
        string videoCodec,
        IReadOnlyList<string> videoArgs,
        bool transcodeAudio,
        IProgress<(int Processed, int Total)>? progress,
        CancellationToken cancellationToken)
    {
        if (probe.VideoFrameRate is null || probe.VideoFrameRate <= 0 || !probe.Duration.HasValue)
            throw new InvalidOperationException("Rawvideo upscale pipeline requires a probed source FPS and duration.");
        if (probe.DisplayVideoWidth <= 0 || probe.DisplayVideoHeight <= 0)
            throw new InvalidOperationException("Rawvideo upscale pipeline requires a valid source resolution.");

        int sourceWidth = probe.DisplayVideoWidth;
        int sourceHeight = probe.DisplayVideoHeight;
        var target = UpscaleFrameProcessor.ResolveFinalSize(sourceWidth, sourceHeight, request, model.ScaleFactor);
        int inputFrameBytes = checked(sourceWidth * sourceHeight * 3);
        int outputFrameBytes = checked(target.Width * target.Height * 3);
        int estimatedTotal = probe.EstimatedVideoFrameCount is long frameCount && frameCount > 0
            ? (int)Math.Min(int.MaxValue, frameCount)
            : Math.Max(1, (int)Math.Round(probe.Duration.Value.TotalSeconds * probe.VideoFrameRate.Value));

        var decodeArguments = BuildDecodeArguments(sourceVideoPath);
        var encodeArguments = BuildEncodeArguments(
            sourceVideoPath,
            outputPath,
            probe.VideoFrameRate.Value,
            target.Width,
            target.Height,
            videoCodec,
            videoArgs,
            probe.HasAudio,
            transcodeAudio);

        using var decoder = new Process { StartInfo = _ffmpegRunner.CreateStartInfo(ffmpegPath, decodeArguments) };
        using var encoder = new Process { StartInfo = _ffmpegRunner.CreateStartInfo(ffmpegPath, encodeArguments) };
        encoder.StartInfo.RedirectStandardInput = true;

        var decoderError = new StringBuilder();
        var encoderError = new StringBuilder();

        decoder.Start();
        encoder.Start();
        _logger.Log($"UpscaleRawVideoPipeline: decoder started pid={decoder.Id}");
        _logger.Log($"UpscaleRawVideoPipeline: encoder started pid={encoder.Id}");

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            TryKill(decoder, "upscale-rawvideo-cancel-decoder");
            TryKill(encoder, "upscale-rawvideo-cancel-encoder");
        });

        using var stderrCancellationSource = new CancellationTokenSource();
        var decoderErrorTask = ReadErrorAsync(decoder.StandardError, decoderError, stderrCancellationSource.Token);
        var encoderErrorTask = ReadErrorAsync(encoder.StandardError, encoderError, stderrCancellationSource.Token);

        var inputFrame = ArrayPool<byte>.Shared.Rent(inputFrameBytes);
        var outputFrame = ArrayPool<byte>.Shared.Rent(outputFrameBytes);

        try
        {
            int sourceFrameCount = 0;
            using var processor = new UpscaleFrameProcessor(model);

            while (await TryReadFrameAsync(
                decoder.StandardOutput.BaseStream,
                inputFrame.AsMemory(0, inputFrameBytes),
                cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var source = ImageSharpImage.LoadPixelData<Rgb24>(inputFrame.AsSpan(0, inputFrameBytes), sourceWidth, sourceHeight);
                using var output = processor.Upscale(source, request, progress: null, cancellationToken);
                WriteImageToRgb24Bytes(output, outputFrame, outputFrameBytes);
                await encoder.StandardInput.BaseStream.WriteAsync(
                    outputFrame.AsMemory(0, outputFrameBytes),
                    cancellationToken).ConfigureAwait(false);

                sourceFrameCount++;
                progress?.Report((sourceFrameCount, estimatedTotal));
            }

            await encoder.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            encoder.StandardInput.Close();

            await encoder.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await decoder.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            stderrCancellationSource.Cancel();
            await SuppressCancellationAsync(decoderErrorTask).ConfigureAwait(false);
            await SuppressCancellationAsync(encoderErrorTask).ConfigureAwait(false);

            if (decoder.ExitCode != 0)
                throw new InvalidOperationException($"Rawvideo decoder failed: {decoderError}");
            if (encoder.ExitCode != 0 || !File.Exists(outputPath))
                throw new InvalidOperationException($"Rawvideo encoder failed: {encoderError}");

            return new UpscaleRawVideoPipelineResult(processor.Provider, sourceFrameCount, sourceFrameCount);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(inputFrame);
            ArrayPool<byte>.Shared.Return(outputFrame);

            if (!decoder.HasExited)
                TryKill(decoder, "upscale-rawvideo-finally-decoder");
            if (!encoder.HasExited)
                TryKill(encoder, "upscale-rawvideo-finally-encoder");
        }
    }

    internal static IReadOnlyList<string> BuildDecodeArguments(string sourceVideoPath) =>
    [
        "-hide_banner",
        "-loglevel", "error",
        "-nostats",
        "-i", sourceVideoPath,
        "-map", "0:v:0",
        "-an",
        "-sn",
        "-dn",
        "-vsync", "0",
        "-pix_fmt", "rgb24",
        "-f", "rawvideo",
        "-"
    ];

    internal static IReadOnlyList<string> BuildEncodeArguments(
        string sourceVideoPath,
        string outputPath,
        double frameRate,
        int width,
        int height,
        string videoCodec,
        IReadOnlyList<string> videoArgs,
        bool hasAudio,
        bool transcodeAudio)
    {
        var sizeText = $"{width}x{height}";
        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-nostats",
            "-y",
            "-f", "rawvideo",
            "-pix_fmt", "rgb24",
            "-video_size", sizeText,
            "-framerate", frameRate.ToString("0.###", CultureInfo.InvariantCulture),
            "-i", "-",
            "-i", sourceVideoPath,
            "-map", "0:v:0"
        };

        if (hasAudio)
        {
            arguments.Add("-map");
            arguments.Add("1:a?");
        }

        arguments.Add("-c:v");
        arguments.Add(videoCodec);
        arguments.AddRange(videoArgs);

        if (hasAudio)
        {
            if (transcodeAudio)
            {
                arguments.AddRange(RifeInterpolateVideoAction.GetAudioCodecArgs(Path.GetExtension(outputPath)));
            }
            else
            {
                arguments.Add("-c:a");
                arguments.Add("copy");
            }
        }

        arguments.Add("-shortest");
        arguments.Add(outputPath);
        return arguments;
    }

    private static void WriteImageToRgb24Bytes(Image<Rgba32> image, byte[] destination, int destinationLength)
    {
        image.ProcessPixelRows(accessor =>
        {
            var destinationSpan = destination.AsSpan(0, destinationLength);
            for (int y = 0; y < image.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                int rowByteOffset = y * image.Width * 3;
                for (int x = 0; x < image.Width; x++)
                {
                    int byteOffset = rowByteOffset + (x * 3);
                    var pixel = row[x];
                    destinationSpan[byteOffset] = pixel.R;
                    destinationSpan[byteOffset + 1] = pixel.G;
                    destinationSpan[byteOffset + 2] = pixel.B;
                }
            }
        });
    }

    private static async Task<bool> TryReadFrameAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return offset == 0 ? false : throw new InvalidOperationException("Rawvideo frame stream ended mid-frame.");
            offset += read;
        }

        return true;
    }

    private static async Task ReadErrorAsync(StreamReader reader, StringBuilder sink, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        while (!cancellationToken.IsCancellationRequested)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                break;

            sink.Append(buffer, 0, read);
        }
    }

    private static async Task SuppressCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void TryKill(Process process, string reason)
    {
        try
        {
            if (!process.HasExited)
            {
                _logger.Log($"UpscaleRawVideoPipeline: killing process pid={process.Id} reason={reason}.");
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"UpscaleRawVideoPipeline: failed to kill process for {reason}. {ex.Message}");
        }
    }
}
