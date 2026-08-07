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
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI.VideoInterpolation;

internal sealed record RifeRawVideoPipelineResult(
    string Provider,
    int SourceFrameCount,
    int OutputFrameCount,
    bool AudioExpected,
    bool AudioPreserved,
    TimeSpan? SourceDuration,
    TimeSpan? OutputDuration,
    string Report);

internal sealed class RifeRawVideoPipeline
{
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly AppLogger _logger;

    public RifeRawVideoPipeline(FfmpegRunner ffmpegRunner, AppLogger logger)
    {
        _ffmpegRunner = ffmpegRunner;
        _logger = logger;
    }

    public async Task<RifeRawVideoPipelineResult> RunAsync(
        string ffmpegPath,
        string ffprobePath,
        string sourceVideoPath,
        string outputPath,
        string modelPath,
        MediaProbeResult probe,
        RifeInterpolateVideoSettings settings,
        bool hasAudio,
        int sampleRate,
        string modeLabel,
        string videoCodec,
        IReadOnlyList<string> videoArgs,
        IProgress<(int Processed, int Total)>? progress,
        CancellationToken cancellationToken)
    {
        if (settings.TargetMultiplier != 2)
        {
            throw new InvalidOperationException("The rawvideo RIFE pipeline prototype currently supports x2 only.");
        }

        if (probe.VideoFrameRate is null || probe.VideoFrameRate <= 0 || !probe.Duration.HasValue)
        {
            throw new InvalidOperationException("Rawvideo RIFE pipeline requires a probed source FPS and duration.");
        }

        var width = probe.DisplayVideoWidth;
        var height = probe.DisplayVideoHeight;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("Rawvideo RIFE pipeline requires a valid source resolution.");
        }

        var frameBytes = checked(width * height * 3);
        var estimatedTotal = probe.EstimatedVideoFrameCount > 1
            ? (int)Math.Min(int.MaxValue, probe.EstimatedVideoFrameCount!.Value - 1)
            : Math.Max(1, (int)Math.Round(probe.Duration!.Value.TotalSeconds * probe.VideoFrameRate!.Value) - 1);
        var targetFps = settings.GetOutputFrameRate(probe.VideoFrameRate.Value);
        var targetFpsText = targetFps.ToString("0.###", CultureInfo.InvariantCulture);
        var metrics = new RifePerformanceMetrics
        {
            TempFrameFormat = "rawvideo-memory",
            CropMode = "Implicit crop during tensor -> rawvideo conversion",
            TempFrameExtension = ".raw",
            PipelineDescription = "Video -> FFmpeg rawvideo stdout -> memory frames -> float tensors -> ONNX -> rawvideo stdin -> FFmpeg encode"
        };

        var decodeArguments = BuildDecodeArguments(sourceVideoPath);
        var encodeArguments = BuildEncodeArguments(sourceVideoPath, outputPath, targetFpsText, width, height, hasAudio, settings, sampleRate, videoCodec, videoArgs);

        var totalStopwatch = Stopwatch.StartNew();

        FfmpegRunner.RawVideoProcess? decoder = null;
        FfmpegRunner.RawVideoProcess? encoder = null;
        byte[]? firstFrame = null;
        byte[]? currentFrame = null;
        byte[]? middleFrame = null;
        var completed = false;

        try
        {
            _logger.Log("RifeRawVideoPipeline: starting protected rawvideo decoder and encoder scopes.");
            decoder = _ffmpegRunner.StartRawVideoProcess(
                ffmpegPath,
                decodeArguments,
                redirectStandardInput: false,
                drainStandardOutput: false,
                role: "rife-decoder",
                cancellationToken: cancellationToken);
            encoder = _ffmpegRunner.StartRawVideoProcess(
                ffmpegPath,
                encodeArguments,
                redirectStandardInput: true,
                drainStandardOutput: true,
                role: "rife-encoder",
                cancellationToken: cancellationToken);

            firstFrame = ArrayPool<byte>.Shared.Rent(frameBytes);
            currentFrame = ArrayPool<byte>.Shared.Rent(frameBytes);
            middleFrame = ArrayPool<byte>.Shared.Rent(frameBytes);

            using var engine = new RifeFrameInterpolationEngine(modelPath, metrics);

            var decodeFirstFrameStopwatch = Stopwatch.StartNew();
            if (!await TryReadFrameAsync(decoder.StandardOutput, firstFrame.AsMemory(0, frameBytes), cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Rawvideo decoder did not produce any frame.");
            }
            decodeFirstFrameStopwatch.Stop();
            metrics.AddExtraction(decodeFirstFrameStopwatch.Elapsed);

            var sourceFrameCount = 1;
            var outputFrameCount = 0;

            var writeFirstFrameStopwatch = Stopwatch.StartNew();
            await encoder.StandardInput.WriteAsync(firstFrame.AsMemory(0, frameBytes), cancellationToken).ConfigureAwait(false);
            writeFirstFrameStopwatch.Stop();
            metrics.AddWriteFrame(writeFirstFrameStopwatch.Elapsed);
            outputFrameCount++;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool hasFrame;
                var readFrameStopwatch = Stopwatch.StartNew();
                hasFrame = await TryReadFrameAsync(decoder.StandardOutput, currentFrame.AsMemory(0, frameBytes), cancellationToken).ConfigureAwait(false);
                readFrameStopwatch.Stop();
                metrics.AddReadFrame(readFrameStopwatch.Elapsed);

                if (!hasFrame)
                {
                    break;
                }

                sourceFrameCount++;
                engine.InterpolateMiddleFrameRaw(
                    firstFrame.AsSpan(0, frameBytes),
                    currentFrame.AsSpan(0, frameBytes),
                    width,
                    height,
                    middleFrame.AsSpan(0, frameBytes),
                    cancellationToken);

                var writeFrameStopwatch = Stopwatch.StartNew();
                await encoder.StandardInput.WriteAsync(middleFrame.AsMemory(0, frameBytes), cancellationToken).ConfigureAwait(false);
                await encoder.StandardInput.WriteAsync(currentFrame.AsMemory(0, frameBytes), cancellationToken).ConfigureAwait(false);
                writeFrameStopwatch.Stop();
                metrics.AddWriteFrame(writeFrameStopwatch.Elapsed);
                outputFrameCount += 2;
                metrics.IncrementPairCount();

                (firstFrame, currentFrame) = (currentFrame, firstFrame);
                progress?.Report((sourceFrameCount - 1, estimatedTotal));
            }

            var reconstructionStopwatch = Stopwatch.StartNew();
            await encoder.CompleteStandardInputAsync(cancellationToken).ConfigureAwait(false);
            await encoder.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            reconstructionStopwatch.Stop();
            metrics.AddReconstruction(reconstructionStopwatch.Elapsed);

            await decoder.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (decoder.ExitCode != 0)
            {
                throw new InvalidOperationException($"Rawvideo decoder failed: {decoder.StandardError}");
            }

            if (encoder.ExitCode != 0 || !File.Exists(outputPath))
            {
                throw new InvalidOperationException($"Rawvideo encoder failed: {encoder.StandardError}");
            }

            totalStopwatch.Stop();
            metrics.CompleteTotal(totalStopwatch);

            var outputProbe = await new FfprobeRunner(_logger).TryProbeMediaAsync(ffprobePath, outputPath, cancellationToken).ConfigureAwait(false);
            var outputMedia = outputProbe.Probe;
            var sourceDuration = probe.Duration;
            var outputDuration = outputMedia?.Duration;
            var audioExpected = settings.PreservesAudio && probe.HasAudio;
            var audioPreserved = outputMedia?.HasAudio ?? false;

            metrics.Provider = engine.Provider;
            var report = BuildReport(
                metrics,
                modeLabel,
                width,
                height,
                targetFps,
                sourceFrameCount,
                outputFrameCount,
                audioExpected,
                audioPreserved,
                sourceDuration,
                outputDuration);

            completed = true;
            return new RifeRawVideoPipelineResult(
                engine.Provider,
                sourceFrameCount,
                outputFrameCount,
                audioExpected,
                audioPreserved,
                sourceDuration,
                outputDuration,
                report);
        }
        finally
        {
            if (encoder is not null)
            {
                encoder.RequestStop("rife-finally-encoder");
            }

            if (decoder is not null)
            {
                decoder.RequestStop("rife-finally-decoder");
            }

            try
            {
                if (encoder is not null)
                {
                    await encoder.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    if (decoder is not null)
                    {
                        await decoder.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (firstFrame is not null)
                    {
                        ArrayPool<byte>.Shared.Return(firstFrame);
                    }

                    if (currentFrame is not null)
                    {
                        ArrayPool<byte>.Shared.Return(currentFrame);
                    }

                    if (middleFrame is not null)
                    {
                        ArrayPool<byte>.Shared.Return(middleFrame);
                    }

                    if (!completed)
                    {
                        ConversionActionHelper.DeleteIfExists(outputPath);
                    }
                }
            }
        }
    }

    internal static IReadOnlyList<string> BuildDecodeArguments(string sourceVideoPath)
    {
        return
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
    }

    internal static IReadOnlyList<string> BuildEncodeArguments(
        string sourceVideoPath,
        string outputPath,
        string targetFpsText,
        int width,
        int height,
        bool hasAudio,
        RifeInterpolateVideoSettings settings,
        int sampleRate,
        string videoCodec,
        IReadOnlyList<string> videoArgs)
    {
        var sizeText = $"{width}x{height}";
        if (settings.PreservesAudio && hasAudio)
        {
            if (settings.IsSlowMotion)
            {
                var audioFilter = RifeInterpolateVideoAction.BuildSlowMotionAudioFilter(settings, sampleRate);
                return
                [
                    "-hide_banner",
                    "-loglevel", "error",
                    "-nostats",
                    "-y",
                    "-f", "rawvideo",
                    "-pix_fmt", "rgb24",
                    "-video_size", sizeText,
                    "-framerate", targetFpsText,
                    "-i", "-",
                    "-i", sourceVideoPath,
                    "-filter_complex", $"[1:a]{audioFilter}[a]",
                    "-map", "0:v:0",
                    "-map", "[a]",
                    "-c:v", videoCodec,
                    .. videoArgs,
                    .. RifeInterpolateVideoAction.GetAudioCodecArgs(Path.GetExtension(outputPath)),
                    "-shortest",
                    outputPath
                ];
            }

            return
            [
                "-hide_banner",
                "-loglevel", "error",
                "-nostats",
                "-y",
                "-f", "rawvideo",
                "-pix_fmt", "rgb24",
                "-video_size", sizeText,
                "-framerate", targetFpsText,
                "-i", "-",
                "-i", sourceVideoPath,
                "-map", "0:v:0",
                "-map", "1:a?",
                "-c:v", videoCodec,
                .. videoArgs,
                "-c:a", "copy",
                "-shortest",
                outputPath
            ];
        }

        return
        [
            "-hide_banner",
            "-loglevel", "error",
            "-nostats",
            "-y",
            "-f", "rawvideo",
            "-pix_fmt", "rgb24",
            "-video_size", sizeText,
            "-framerate", targetFpsText,
            "-i", "-",
            "-map", "0:v:0",
            "-c:v", videoCodec,
            .. videoArgs,
            outputPath
        ];
    }

    private static async Task<bool> TryReadFrameAsync(Stream stream, Memory<byte> destination, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < destination.Length)
        {
            var bytesRead = await stream.ReadAsync(destination[totalRead..], cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                if (totalRead == 0)
                {
                    return false;
                }

                throw new EndOfStreamException("Rawvideo decoder ended mid-frame.");
            }

            totalRead += bytesRead;
        }

        return true;
    }

    private static string BuildReport(
        RifePerformanceMetrics metrics,
        string modeLabel,
        int width,
        int height,
        double targetFps,
        int sourceFrameCount,
        int outputFrameCount,
        bool audioExpected,
        bool audioPreserved,
        TimeSpan? sourceDuration,
        TimeSpan? outputDuration)
    {
        var builder = new StringBuilder();
        builder.AppendLine("RIFE RAWVIDEO PIPE REPORT");
        builder.AppendLine($"Provider: {metrics.Provider}");
        builder.AppendLine($"Mode: {modeLabel}");
        builder.AppendLine($"Resolution: {width}x{height}");
        builder.AppendLine($"Target FPS: {targetFps.ToString("0.###", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"Source frames: {sourceFrameCount}");
        builder.AppendLine($"Output frames: {outputFrameCount}");
        builder.AppendLine($"Audio expected: {audioExpected}");
        builder.AppendLine($"Audio preserved: {audioPreserved}");
        builder.AppendLine($"Source duration: {FormatDuration(sourceDuration)}");
        builder.AppendLine($"Output duration: {FormatDuration(outputDuration)}");
        builder.AppendLine($"Session creation: {FormatMilliseconds(metrics.SessionCreationDuration)}");
        builder.AppendLine($"Decode / rawvideo input: {FormatMilliseconds(metrics.ExtractionDuration + metrics.ReadFrameDuration)}");
        builder.AppendLine($"Image -> tensor: {FormatMilliseconds(metrics.ImageToTensorDuration)}");
        builder.AppendLine($"ONNX inference: {FormatMilliseconds(metrics.InferenceDuration)}");
        builder.AppendLine($"Tensor -> rawvideo: {FormatMilliseconds(metrics.TensorToImageDuration)}");
        builder.AppendLine($"Encode / rawvideo output: {FormatMilliseconds(metrics.WriteFrameDuration + metrics.ReconstructionDuration)}");
        builder.AppendLine($"Total time: {FormatMilliseconds(metrics.TotalDuration)}");
        if (metrics.TotalDuration.TotalSeconds > 0)
        {
            builder.AppendLine($"Real FPS: {(outputFrameCount / metrics.TotalDuration.TotalSeconds).ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        return builder.ToString();
    }

    private static string FormatMilliseconds(TimeSpan duration)
    {
        return $"{duration.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms";
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        if (!duration.HasValue)
        {
            return "Unavailable";
        }

        return duration.Value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " s";
    }
}
