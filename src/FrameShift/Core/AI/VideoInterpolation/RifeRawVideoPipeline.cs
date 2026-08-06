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

        using var decoder = new Process { StartInfo = _ffmpegRunner.CreateStartInfo(ffmpegPath, decodeArguments) };
        using var encoder = new Process { StartInfo = _ffmpegRunner.CreateStartInfo(ffmpegPath, encodeArguments) };
        encoder.StartInfo.RedirectStandardInput = true;

        var decoderError = new StringBuilder();
        var encoderError = new StringBuilder();
        var totalStopwatch = Stopwatch.StartNew();

        decoder.Start();
        encoder.Start();
        _logger.Log($"RifeRawVideoPipeline: decoder started pid={decoder.Id}");
        _logger.Log($"RifeRawVideoPipeline: encoder started pid={encoder.Id}");

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            TryKill(decoder, "rawvideo-cancel-decoder");
            TryKill(encoder, "rawvideo-cancel-encoder");
        });

        using var stderrCancellationSource = new CancellationTokenSource();
        var decoderErrorTask = ReadErrorAsync(decoder.StandardError, decoderError, stderrCancellationSource.Token);
        var encoderErrorTask = ReadErrorAsync(encoder.StandardError, encoderError, stderrCancellationSource.Token);

        var firstFrame = ArrayPool<byte>.Shared.Rent(frameBytes);
        var currentFrame = ArrayPool<byte>.Shared.Rent(frameBytes);
        var middleFrame = ArrayPool<byte>.Shared.Rent(frameBytes);

        try
        {
            using var engine = new RifeFrameInterpolationEngine(modelPath, metrics);

            var decodeFirstFrameStopwatch = Stopwatch.StartNew();
            if (!await TryReadFrameAsync(decoder.StandardOutput.BaseStream, firstFrame.AsMemory(0, frameBytes), cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Rawvideo decoder did not produce any frame.");
            }
            decodeFirstFrameStopwatch.Stop();
            metrics.AddExtraction(decodeFirstFrameStopwatch.Elapsed);

            var sourceFrameCount = 1;
            var outputFrameCount = 0;

            var writeFirstFrameStopwatch = Stopwatch.StartNew();
            await encoder.StandardInput.BaseStream.WriteAsync(firstFrame.AsMemory(0, frameBytes), cancellationToken).ConfigureAwait(false);
            writeFirstFrameStopwatch.Stop();
            metrics.AddWriteFrame(writeFirstFrameStopwatch.Elapsed);
            outputFrameCount++;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool hasFrame;
                var readFrameStopwatch = Stopwatch.StartNew();
                hasFrame = await TryReadFrameAsync(decoder.StandardOutput.BaseStream, currentFrame.AsMemory(0, frameBytes), cancellationToken).ConfigureAwait(false);
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
                await encoder.StandardInput.BaseStream.WriteAsync(middleFrame.AsMemory(0, frameBytes), cancellationToken).ConfigureAwait(false);
                await encoder.StandardInput.BaseStream.WriteAsync(currentFrame.AsMemory(0, frameBytes), cancellationToken).ConfigureAwait(false);
                writeFrameStopwatch.Stop();
                metrics.AddWriteFrame(writeFrameStopwatch.Elapsed);
                outputFrameCount += 2;
                metrics.IncrementPairCount();

                (firstFrame, currentFrame) = (currentFrame, firstFrame);
                progress?.Report((sourceFrameCount - 1, estimatedTotal));
            }

            var reconstructionStopwatch = Stopwatch.StartNew();
            await encoder.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            encoder.StandardInput.Close();
            await encoder.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            reconstructionStopwatch.Stop();
            metrics.AddReconstruction(reconstructionStopwatch.Elapsed);

            await decoder.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            stderrCancellationSource.Cancel();
            await SuppressCancellationAsync(decoderErrorTask).ConfigureAwait(false);
            await SuppressCancellationAsync(encoderErrorTask).ConfigureAwait(false);

            if (decoder.ExitCode != 0)
            {
                throw new InvalidOperationException($"Rawvideo decoder failed: {decoderError}");
            }

            if (encoder.ExitCode != 0 || !File.Exists(outputPath))
            {
                throw new InvalidOperationException($"Rawvideo encoder failed: {encoderError}");
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
            ArrayPool<byte>.Shared.Return(firstFrame);
            ArrayPool<byte>.Shared.Return(currentFrame);
            ArrayPool<byte>.Shared.Return(middleFrame);

            if (!decoder.HasExited)
            {
                TryKill(decoder, "rawvideo-finally-decoder");
            }

            if (!encoder.HasExited)
            {
                TryKill(encoder, "rawvideo-finally-encoder");
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

    private async Task ReadErrorAsync(StreamReader reader, StringBuilder errorBuilder, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                errorBuilder.AppendLine(line);
                _logger.Log(line);
            }
        }
        catch (OperationCanceledException)
        {
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
            _logger.Log($"RifeRawVideoPipeline: killing process pid={process.Id}, reason={reason}");
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _logger.Log($"RifeRawVideoPipeline: kill failed pid={TryGetProcessId(process)}, reason={reason}, error={ex.Message}");
        }
    }

    private static int TryGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return -1;
        }
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
