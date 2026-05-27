using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI.VideoInterpolation;

/// <summary>
/// Three-stage producer/consumer pipeline:
///   Decode task  → Channel&lt;byte[]&gt; → Inference task → Channel&lt;byte[]&gt; → Encode task
///
/// Decode and encode tasks overlap with ONNX inference, so GPU and CPU run simultaneously.
/// Buffer ownership protocol:
///   1. Decode rents a buffer, fills it, writes to decodeChannel  (ownership → Inference)
///   2. Inference reads i1, copies i1→i0 BEFORE transfer, then writes (middle, i1) to encodeChannel (ownership → Encode)
///   3. Encode reads each buffer, writes bytes to encoder stdin, returns buffer to ArrayPool
/// The copy of i1→i0 (step 2) happens on the inference thread before i1 is transferred,
/// which ensures no concurrent write/read race on i1 between inference and encode.
/// </summary>
internal sealed class RifeRawVideoParallelPipeline
{
    private const int DecodeChannelCapacity = 4;
    private const int EncodeChannelCapacity = 8;

    private readonly FfmpegRunner _ffmpegRunner;
    private readonly AppLogger _logger;

    public RifeRawVideoParallelPipeline(FfmpegRunner ffmpegRunner, AppLogger logger)
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
        CancellationToken cancellationToken)
    {
        if (settings.TargetMultiplier != 2)
        {
            throw new InvalidOperationException("The parallel rawvideo RIFE pipeline prototype currently supports x2 only.");
        }

        if (probe.VideoFrameRate is null || probe.VideoFrameRate <= 0 || !probe.Duration.HasValue)
        {
            throw new InvalidOperationException("Parallel rawvideo RIFE pipeline requires a probed source FPS and duration.");
        }

        var width = probe.VideoWidth;
        var height = probe.VideoHeight;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("Parallel rawvideo RIFE pipeline requires a valid source resolution.");
        }

        var frameBytes = checked(width * height * 3);
        var targetFps = settings.GetOutputFrameRate(probe.VideoFrameRate.Value);
        var targetFpsText = targetFps.ToString("0.###", CultureInfo.InvariantCulture);

        var metrics = new RifePerformanceMetrics
        {
            TempFrameFormat = "rawvideo-memory",
            CropMode = "Implicit crop during tensor → rawvideo conversion",
            TempFrameExtension = ".raw",
            PipelineDescription =
                $"Parallel 3-stage: decode→Channel(cap={DecodeChannelCapacity})→inference→Channel(cap={EncodeChannelCapacity})→encode"
        };

        var decodeArguments = BuildDecodeArguments(sourceVideoPath);
        var encodeArguments = BuildEncodeArguments(
            sourceVideoPath, outputPath, targetFpsText, width, height,
            hasAudio, settings, sampleRate, videoCodec, videoArgs);

        using var decoder = new Process { StartInfo = _ffmpegRunner.CreateStartInfo(ffmpegPath, decodeArguments) };
        using var encoder = new Process { StartInfo = _ffmpegRunner.CreateStartInfo(ffmpegPath, encodeArguments) };
        encoder.StartInfo.RedirectStandardInput = true;

        var decoderError = new StringBuilder();
        var encoderError = new StringBuilder();
        var totalStopwatch = Stopwatch.StartNew();

        decoder.Start();
        encoder.Start();
        _logger.Log($"RifeRawVideoParallelPipeline: decoder pid={decoder.Id}, encoder pid={encoder.Id}");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pipelineCt = linkedCts.Token;

        using var stderrCts = new CancellationTokenSource();
        var decoderErrorTask = ReadErrorAsync(decoder.StandardError, decoderError, stderrCts.Token);
        var encoderErrorTask = ReadErrorAsync(encoder.StandardError, encoderError, stderrCts.Token);

        var decodeChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(DecodeChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
        var encodeChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(EncodeChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        int sourceFrameCount = 0;
        int outputFrameCount = 0;
        var decodeWallclock = new Stopwatch();
        var encodeWallclock = new Stopwatch();

        using var engine = new RifeFrameInterpolationEngine(modelPath, metrics);

        Task decodeTask;
        Task inferenceTask;
        Task encodeTask;

        try
        {
            decodeTask = Task.Run(async () =>
            {
                decodeWallclock.Start();
                try
                {
                    var frameCount = 0;
                    while (true)
                    {
                        var buf = ArrayPool<byte>.Shared.Rent(frameBytes);
                        var hasFrame = await TryReadFrameAsync(
                            decoder.StandardOutput.BaseStream,
                            buf.AsMemory(0, frameBytes),
                            pipelineCt).ConfigureAwait(false);

                        if (!hasFrame)
                        {
                            ArrayPool<byte>.Shared.Return(buf);
                            break;
                        }

                        frameCount++;
                        await decodeChannel.Writer.WriteAsync(buf, pipelineCt).ConfigureAwait(false);
                    }

                    Interlocked.Exchange(ref sourceFrameCount, frameCount);
                    decodeChannel.Writer.TryComplete();
                }
                catch (OperationCanceledException)
                {
                    DrainAndReturnChannel(decodeChannel.Reader, frameBytes);
                    decodeChannel.Writer.TryComplete();
                    throw;
                }
                catch (Exception ex)
                {
                    DrainAndReturnChannel(decodeChannel.Reader, frameBytes);
                    decodeChannel.Writer.TryComplete(ex);
                    linkedCts.Cancel();
                    throw;
                }
                finally
                {
                    decodeWallclock.Stop();
                }
            }, pipelineCt);

            inferenceTask = Task.Run(async () =>
            {
                var i0 = ArrayPool<byte>.Shared.Rent(frameBytes);
                try
                {
                    var firstBuf = await decodeChannel.Reader.ReadAsync(pipelineCt).ConfigureAwait(false);
                    firstBuf.AsSpan(0, frameBytes).CopyTo(i0.AsSpan(0, frameBytes));
                    await encodeChannel.Writer.WriteAsync(firstBuf, pipelineCt).ConfigureAwait(false);

                    await foreach (var i1Buf in decodeChannel.Reader.ReadAllAsync(pipelineCt).ConfigureAwait(false))
                    {
                        var middleBuf = ArrayPool<byte>.Shared.Rent(frameBytes);

                        engine.InterpolateMiddleFrameRaw(
                            i0.AsSpan(0, frameBytes),
                            i1Buf.AsSpan(0, frameBytes),
                            width,
                            height,
                            middleBuf.AsSpan(0, frameBytes),
                            pipelineCt);

                        // Copy i1→i0 BEFORE transferring i1 to encode, to prevent a
                        // write/read race if decode reuses the same array after pool return.
                        i1Buf.AsSpan(0, frameBytes).CopyTo(i0.AsSpan(0, frameBytes));

                        await encodeChannel.Writer.WriteAsync(middleBuf, pipelineCt).ConfigureAwait(false);
                        await encodeChannel.Writer.WriteAsync(i1Buf, pipelineCt).ConfigureAwait(false);

                        metrics.IncrementPairCount();
                    }

                    encodeChannel.Writer.TryComplete();
                }
                catch (OperationCanceledException)
                {
                    DrainAndReturnChannel(decodeChannel.Reader, frameBytes);
                    DrainAndReturnChannel(encodeChannel.Reader, frameBytes);
                    encodeChannel.Writer.TryComplete();
                    throw;
                }
                catch (Exception ex)
                {
                    DrainAndReturnChannel(decodeChannel.Reader, frameBytes);
                    DrainAndReturnChannel(encodeChannel.Reader, frameBytes);
                    encodeChannel.Writer.TryComplete(ex);
                    linkedCts.Cancel();
                    throw;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(i0);
                }
            }, pipelineCt);

            encodeTask = Task.Run(async () =>
            {
                encodeWallclock.Start();
                try
                {
                    var frameCount = 0;
                    await foreach (var buf in encodeChannel.Reader.ReadAllAsync(pipelineCt).ConfigureAwait(false))
                    {
                        await encoder.StandardInput.BaseStream
                            .WriteAsync(buf.AsMemory(0, frameBytes), pipelineCt)
                            .ConfigureAwait(false);
                        ArrayPool<byte>.Shared.Return(buf);
                        frameCount++;
                    }

                    Interlocked.Exchange(ref outputFrameCount, frameCount);
                }
                catch
                {
                    DrainAndReturnChannel(encodeChannel.Reader, frameBytes);
                    if (pipelineCt.IsCancellationRequested is false)
                    {
                        linkedCts.Cancel();
                    }
                    throw;
                }
                finally
                {
                    encodeWallclock.Stop();
                }
            }, pipelineCt);

            try
            {
                await Task.WhenAll(decodeTask, inferenceTask, encodeTask).ConfigureAwait(false);
            }
            catch
            {
                linkedCts.Cancel();
                await Task.WhenAll(
                    SuppressAsync(decodeTask),
                    SuppressAsync(inferenceTask),
                    SuppressAsync(encodeTask)).ConfigureAwait(false);

                // Re-throw the first real (non-cancellation) exception from any task.
                foreach (var t in new[] { decodeTask, inferenceTask, encodeTask })
                {
                    if (t.IsFaulted &&
                        t.Exception?.InnerException is { } taskEx &&
                        taskEx is not OperationCanceledException)
                    {
                        ExceptionDispatchInfo.Capture(taskEx).Throw();
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }

            var reconstructionStopwatch = Stopwatch.StartNew();
            await encoder.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            encoder.StandardInput.Close();
            await encoder.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            reconstructionStopwatch.Stop();
            metrics.AddReconstruction(reconstructionStopwatch.Elapsed);

            await decoder.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            stderrCts.Cancel();
            await SuppressAsync(decoderErrorTask).ConfigureAwait(false);
            await SuppressAsync(encoderErrorTask).ConfigureAwait(false);

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

            var outputProbe = await new FfprobeRunner(_logger)
                .TryProbeMediaAsync(ffprobePath, outputPath, cancellationToken)
                .ConfigureAwait(false);
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
                outputDuration,
                decodeWallclock.Elapsed,
                encodeWallclock.Elapsed,
                frameBytes);

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
            if (!decoder.HasExited) TryKill(decoder, "parallel-finally-decoder");
            if (!encoder.HasExited) TryKill(encoder, "parallel-finally-encoder");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void DrainAndReturnChannel(ChannelReader<byte[]> reader, int frameBytes)
    {
        while (reader.TryRead(out var abandoned))
        {
            ArrayPool<byte>.Shared.Return(abandoned);
        }
    }

    private static async Task<bool> TryReadFrameAsync(
        Stream stream,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < destination.Length)
        {
            var bytesRead = await stream.ReadAsync(destination[totalRead..], cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                if (totalRead == 0) return false;
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
                if (line is null) return;
                if (!string.IsNullOrWhiteSpace(line))
                {
                    errorBuilder.AppendLine(line);
                    _logger.Log(line);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static Task SuppressAsync(Task task) =>
        task.ContinueWith(_ => { }, TaskContinuationOptions.None);

    private void TryKill(Process process, string reason)
    {
        try
        {
            _logger.Log($"RifeRawVideoParallelPipeline: killing pid={process.Id}, reason={reason}");
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _logger.Log($"RifeRawVideoParallelPipeline: kill failed, reason={reason}, error={ex.Message}");
        }
    }

    // ── Argument builders (identical to RifeRawVideoPipeline) ─────────────

    private static IReadOnlyList<string> BuildDecodeArguments(string sourceVideoPath) =>
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

    private static IReadOnlyList<string> BuildEncodeArguments(
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

    // ── Report ────────────────────────────────────────────────────────────────

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
        TimeSpan? outputDuration,
        TimeSpan decodeWallclock,
        TimeSpan encodeWallclock,
        int frameBytes)
    {
        var b = new StringBuilder();
        b.AppendLine("RIFE PARALLEL PIPELINE REPORT");
        b.AppendLine($"Provider:         {metrics.Provider}");
        b.AppendLine($"Mode:             {modeLabel}");
        b.AppendLine($"Resolution:       {width}x{height}");
        b.AppendLine($"Target FPS:       {targetFps.ToString("0.###", CultureInfo.InvariantCulture)}");
        b.AppendLine($"Source frames:    {sourceFrameCount}");
        b.AppendLine($"Output frames:    {outputFrameCount}");
        b.AppendLine($"Pairs processed:  {metrics.PairCount}");
        b.AppendLine($"Audio expected:   {audioExpected}");
        b.AppendLine($"Audio preserved:  {audioPreserved}");
        b.AppendLine($"Source duration:  {FormatDuration(sourceDuration)}");
        b.AppendLine($"Output duration:  {FormatDuration(outputDuration)}");
        b.AppendLine();
        b.AppendLine("Stage timings (wallclock — overlap means sum > total):");
        b.AppendLine($"  Session creation:     {FormatMs(metrics.SessionCreationDuration)}");
        b.AppendLine($"  Decode thread:        {FormatMs(decodeWallclock)}");
        b.AppendLine($"  Image → tensor:       {FormatMs(metrics.ImageToTensorDuration)}  (inference thread, CPU)");
        b.AppendLine($"  ONNX inference:       {FormatMs(metrics.InferenceDuration)}  (inference thread, GPU)");
        b.AppendLine($"  Tensor → rawvideo:    {FormatMs(metrics.TensorToImageDuration)}  (inference thread, CPU)");
        b.AppendLine($"  Encode thread:        {FormatMs(encodeWallclock)}");
        b.AppendLine($"  Encoder close/flush:  {FormatMs(metrics.ReconstructionDuration)}");
        b.AppendLine($"  Total:                {FormatMs(metrics.TotalDuration)}");
        b.AppendLine();

        if (metrics.PairCount > 0)
        {
            var inferenceMs = metrics.InferenceDuration.TotalMilliseconds;
            var tensorCpuMs = metrics.ImageToTensorDuration.TotalMilliseconds + metrics.TensorToImageDuration.TotalMilliseconds;
            b.AppendLine("Per-pair averages (inference thread only, not wallclock):");
            b.AppendLine($"  Image → tensor:    {(tensorCpuMs / metrics.PairCount).ToString("0.0", CultureInfo.InvariantCulture)} ms  (×2 frames/pair)");
            b.AppendLine($"  ONNX inference:    {(inferenceMs / metrics.PairCount).ToString("0.0", CultureInfo.InvariantCulture)} ms");
            b.AppendLine($"  Total inference:   {((metrics.ImageToTensorDuration + metrics.InferenceDuration + metrics.TensorToImageDuration).TotalMilliseconds / metrics.PairCount).ToString("0.0", CultureInfo.InvariantCulture)} ms");
        }

        if (metrics.TotalDuration.TotalSeconds > 0 && outputFrameCount > 0)
        {
            b.AppendLine();
            b.AppendLine($"Real output FPS:  {(outputFrameCount / metrics.TotalDuration.TotalSeconds).ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        b.AppendLine();
        b.AppendLine("Memory (peak in-flight estimate):");
        var peakBuffers = DecodeChannelCapacity + EncodeChannelCapacity + 3;
        var peakMb = (long)peakBuffers * frameBytes / (1024 * 1024);
        b.AppendLine($"  Frame buffers: {peakBuffers} × {frameBytes / (1024 * 1024.0):0.0} MB ≈ {peakMb} MB");
        b.AppendLine($"  Decode channel capacity:  {DecodeChannelCapacity} frames");
        b.AppendLine($"  Encode channel capacity:  {EncodeChannelCapacity} frames");

        return b.ToString();
    }

    private static string FormatMs(TimeSpan t) =>
        $"{t.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms";

    private static string FormatDuration(TimeSpan? t) =>
        t.HasValue
            ? t.Value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " s"
            : "unavailable";
}
