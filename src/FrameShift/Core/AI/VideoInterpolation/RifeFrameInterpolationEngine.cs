using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using FrameShift.Core.AI;
using FrameShift.Core.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.PixelFormats;

namespace FrameShift.Core.AI.VideoInterpolation;

internal readonly record struct RifeInterpolationProgress(
    int PassIndex,
    int PassCount,
    int ProcessedPairs,
    int TotalPairs,
    string Status);

internal sealed class RifeFrameInterpolationEngine : IDisposable
{
    private const string TempFrameExtension = ".bmp";

    private static int s_sessionCreationCount;
    private static readonly BmpEncoder s_bmpEncoder = new();

    private readonly string _modelPath;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly RifePerformanceMetrics? _metrics;

    private InferenceSession? _session;
    private bool _sessionReady;
    private string _input0Name = "img0";
    private string _input1Name = "img1";
    private string _outputName = "middle";
    private DenseTensor<float>? _firstTensor;
    private DenseTensor<float>? _secondTensor;
    private IReadOnlyList<NamedOnnxValue>? _reusableInputs;
    private int _bufferedWidth;
    private int _bufferedHeight;
    private SixLabors.ImageSharp.Image<Rgb24>? _outputImageBuffer;

    public RifeFrameInterpolationEngine(string modelPath, RifePerformanceMetrics? metrics = null)
    {
        _modelPath = modelPath;
        _metrics = metrics;
    }

    public string Provider { get; private set; } = "—";

    public int InterpolateAsync(
        string inputFramesDirectory,
        string outputFramesDirectory,
        IProgress<RifeInterpolationProgress>? progress,
        CancellationToken cancellationToken)
    {
        EnsureSession(cancellationToken);

        var inputFrames = Directory.GetFiles(inputFramesDirectory, $"*{TempFrameExtension}", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (inputFrames.Length == 0)
        {
            throw new InvalidOperationException("FrameShift could not find extracted video frames for interpolation.");
        }

        Directory.CreateDirectory(outputFramesDirectory);

        var totalPairs = Math.Max(0, inputFrames.Length - 1);
        var outputIndex = 1;
        SixLabors.ImageSharp.Image<Rgb24>? previous = null;

        try
        {
            using (new RifePerfTimer(duration =>
            {
                _metrics?.AddReadFrame(duration);
                _metrics?.AddBmpLoad(duration);
            }))
            {
                previous = SixLabors.ImageSharp.Image.Load<Rgb24>(inputFrames[0]);
            }
            SaveFrame(previous, outputFramesDirectory, outputIndex++);

            if (inputFrames.Length == 1)
            {
                progress?.Report(new RifeInterpolationProgress(1, 1, 1, 1, $"Running AI inference on {Provider}..."));
                return outputIndex - 1;
            }

            for (var pairIndex = 1; pairIndex < inputFrames.Length; pairIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                SixLabors.ImageSharp.Image<Rgb24> current;
                using (new RifePerfTimer(duration =>
                {
                    _metrics?.AddReadFrame(duration);
                    _metrics?.AddBmpLoad(duration);
                }))
                {
                    current = SixLabors.ImageSharp.Image.Load<Rgb24>(inputFrames[pairIndex]);
                }
                try
                {
                    var middle = InterpolateMiddleFrame(previous, current, cancellationToken);
                    SaveFrame(middle, outputFramesDirectory, outputIndex++);
                    SaveFrame(current, outputFramesDirectory, outputIndex++);
                }
                finally
                {
                    previous.Dispose();
                    previous = current;
                }

                progress?.Report(new RifeInterpolationProgress(
                    1,
                    1,
                    pairIndex,
                    totalPairs,
                    $"Running AI inference on {Provider}..."));
                _metrics?.IncrementPairCount();
            }
            return outputIndex - 1;
        }
        finally
        {
            previous?.Dispose();
        }
    }

    public int InterpolateMultiplePassesAsync(
        string inputFramesDirectory,
        string workingRootDirectory,
        int targetMultiplier,
        IProgress<RifeInterpolationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (targetMultiplier < 2 || (targetMultiplier & (targetMultiplier - 1)) != 0)
        {
            throw new InvalidOperationException("RIFE interpolation currently supports powers of two only.");
        }

        var passCount = (int)Math.Round(Math.Log2(targetMultiplier));
        _metrics?.SetPassCount(passCount);
        var currentInputDirectory = inputFramesDirectory;

        for (var passIndex = 1; passIndex <= passCount; passIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentOutputDirectory = Path.Combine(workingRootDirectory, $"pass_{passIndex:00}");
            if (Directory.Exists(currentOutputDirectory))
            {
                Directory.Delete(currentOutputDirectory, recursive: true);
            }

            Directory.CreateDirectory(currentOutputDirectory);

            var localPassIndex = passIndex;
            var passProgress = progress is null
                ? null
                : new Progress<RifeInterpolationProgress>(inner =>
                {
                    progress.Report(new RifeInterpolationProgress(
                        localPassIndex,
                        passCount,
                        inner.ProcessedPairs,
                        inner.TotalPairs,
                        $"Pass {localPassIndex}/{passCount} - {inner.Status}"));
                });

            InterpolateAsync(
                currentInputDirectory,
                currentOutputDirectory,
                passProgress,
                cancellationToken);

            currentInputDirectory = currentOutputDirectory;
        }

        return Directory.GetFiles(currentInputDirectory, $"*{TempFrameExtension}", SearchOption.TopDirectoryOnly).Length;
    }

    public void InterpolateMiddleFrameRaw(
        ReadOnlySpan<byte> firstFrameBytes,
        ReadOnlySpan<byte> secondFrameBytes,
        int width,
        int height,
        Span<byte> outputFrameBytes,
        CancellationToken cancellationToken)
    {
        EnsureSession(cancellationToken);

        var expectedFrameBytes = checked(width * height * 3);
        if (firstFrameBytes.Length < expectedFrameBytes ||
            secondFrameBytes.Length < expectedFrameBytes ||
            outputFrameBytes.Length < expectedFrameBytes)
        {
            throw new InvalidOperationException("Rawvideo frame buffers do not match the expected RGB24 frame size.");
        }

        int paddedWidth;
        int paddedHeight;
        using (new RifePerfTimer(duration => _metrics?.AddPad(duration)))
        {
            paddedWidth = AlignTo64(width);
            paddedHeight = AlignTo64(height);
        }
        _metrics?.SetResolution(width, height, paddedWidth, paddedHeight);

        EnsureReusableBuffers(paddedWidth, paddedHeight);

        using (new RifePerfTimer(duration => _metrics?.AddImageToTensor(duration)))
        {
            FillTensorFromRgb24Bytes(firstFrameBytes, _firstTensor!, width, height, paddedWidth, paddedHeight, _metrics);
            FillTensorFromRgb24Bytes(secondFrameBytes, _secondTensor!, width, height, paddedWidth, paddedHeight, _metrics);
        }

        cancellationToken.ThrowIfCancellationRequested();

        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results;
        using (new RifePerfTimer(duration => _metrics?.AddInference(duration)))
        {
            results = _session!.Run(_reusableInputs!);
        }
        using var disposableResults = results;

        cancellationToken.ThrowIfCancellationRequested();

        Tensor<float> outputTensor;
        using (new RifePerfTimer(duration => _metrics?.AddOnnxOutputAccess(duration)))
        {
            outputTensor = disposableResults.First(value => string.Equals(value.Name, _outputName, StringComparison.OrdinalIgnoreCase))
                .AsTensor<float>();
        }

        using (new RifePerfTimer(duration =>
        {
            _metrics?.AddTensorToImage(duration);
            _metrics?.AddFloatToByte(duration);
        }))
        {
            if (outputTensor is DenseTensor<float> outputDenseTensor)
            {
                WriteTensorToRgb24Bytes(outputDenseTensor.Buffer, width, height, paddedWidth, paddedHeight, outputFrameBytes);
            }
            else
            {
                WriteTensorToRgb24Bytes(outputTensor, width, height, outputFrameBytes);
            }
        }
    }

    private void EnsureSession(CancellationToken cancellationToken)
    {
        if (_sessionReady)
        {
            return;
        }

        _initGate.Wait(cancellationToken);
        try
        {
            if (_sessionReady)
            {
                return;
            }

            var sessionCreationStopwatch = Stopwatch.StartNew();
            (_session, Provider) = CreateSession(_modelPath);
            sessionCreationStopwatch.Stop();
            _metrics?.AddSessionCreation(sessionCreationStopwatch.Elapsed);
            if (_metrics is not null)
            {
                _metrics.Provider = Provider;
            }

            var inputNames = _session.InputMetadata.Keys.ToArray();
            if (inputNames.Length < 2)
            {
                throw new InvalidOperationException("The selected RIFE model does not expose the expected two image inputs.");
            }

            _input0Name = inputNames[0];
            _input1Name = inputNames[1];
            _outputName = _session.OutputMetadata.Keys.FirstOrDefault() ?? "middle";

            Interlocked.Increment(ref s_sessionCreationCount);
            _sessionReady = true;
            AppLogger.LogStatic(
                $"RifeFrameInterpolationEngine: session ready. provider={Provider}, input0={_input0Name}, input1={_input1Name}, output={_outputName}, sessionCreationCount={s_sessionCreationCount}");
        }
        finally
        {
            _initGate.Release();
        }
    }

    private SixLabors.ImageSharp.Image<Rgb24> InterpolateMiddleFrame(
        SixLabors.ImageSharp.Image<Rgb24> first,
        SixLabors.ImageSharp.Image<Rgb24> second,
        CancellationToken cancellationToken)
    {
        if (first.Width != second.Width || first.Height != second.Height)
        {
            throw new InvalidOperationException("Extracted video frames do not all share the same resolution.");
        }

        var width = first.Width;
        var height = first.Height;

        int paddedWidth;
        int paddedHeight;
        using (new RifePerfTimer(duration => _metrics?.AddPad(duration)))
        {
            paddedWidth = AlignTo64(width);
            paddedHeight = AlignTo64(height);
        }
        _metrics?.SetResolution(width, height, paddedWidth, paddedHeight);

        EnsureReusableBuffers(paddedWidth, paddedHeight);

        using (new RifePerfTimer(duration => _metrics?.AddImageToTensor(duration)))
        {
            FillTensor(first, _firstTensor!, paddedWidth, paddedHeight, _metrics);
            FillTensor(second, _secondTensor!, paddedWidth, paddedHeight, _metrics);
        }

        cancellationToken.ThrowIfCancellationRequested();

        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results;
        using (new RifePerfTimer(duration => _metrics?.AddInference(duration)))
        {
            results = _session!.Run(_reusableInputs!);
        }
        using var disposableResults = results;

        cancellationToken.ThrowIfCancellationRequested();

        Tensor<float> outputTensor;
        using (new RifePerfTimer(duration => _metrics?.AddOnnxOutputAccess(duration)))
        {
            outputTensor = disposableResults.First(value => string.Equals(value.Name, _outputName, StringComparison.OrdinalIgnoreCase))
                .AsTensor<float>();
        }

        using (new RifePerfTimer(duration => _metrics?.AddOutputImageAllocation(duration)))
        {
            EnsureOutputImageBuffer(width, height);
        }

        var result = _outputImageBuffer!;
        using (new RifePerfTimer(duration =>
        {
            _metrics?.AddTensorToImage(duration);
            _metrics?.AddFloatToByte(duration);
        }))
        {
            if (outputTensor is DenseTensor<float> outputDenseTensor)
            {
                var outputBuffer = outputDenseTensor.Buffer;
                result.ProcessPixelRows(accessor =>
                {
                    WriteTensorRowsToImage(accessor, outputBuffer, width, height, paddedWidth, paddedHeight);
                });
            }
            else
            {
                result.ProcessPixelRows(accessor =>
                {
                    for (var y = 0; y < height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (var x = 0; x < width; x++)
                        {
                            row[x] = new Rgb24(
                                ToByte(outputTensor[0, 0, y, x]),
                                ToByte(outputTensor[0, 1, y, x]),
                                ToByte(outputTensor[0, 2, y, x]));
                        }
                    }
                });
            }
        }

        // Crop is implicit: only the original bounds are materialized from the padded tensor.
        _metrics?.AddCrop(TimeSpan.Zero);

        return result;
    }

    private void EnsureReusableBuffers(int paddedWidth, int paddedHeight)
    {
        if (_firstTensor is not null &&
            _secondTensor is not null &&
            _reusableInputs is not null &&
            _bufferedWidth == paddedWidth &&
            _bufferedHeight == paddedHeight)
        {
            return;
        }

        using (new RifePerfTimer(duration => _metrics?.AddTensorAllocation(duration)))
        {
            _firstTensor = new DenseTensor<float>(new[] { 1, 3, paddedHeight, paddedWidth });
            _secondTensor = new DenseTensor<float>(new[] { 1, 3, paddedHeight, paddedWidth });
        }

        _reusableInputs =
        [
            NamedOnnxValue.CreateFromTensor(_input0Name, _firstTensor),
            NamedOnnxValue.CreateFromTensor(_input1Name, _secondTensor)
        ];
        _metrics?.IncrementNamedOnnxValueListCreationCount();
        _bufferedWidth = paddedWidth;
        _bufferedHeight = paddedHeight;
    }

    private void EnsureOutputImageBuffer(int width, int height)
    {
        if (_outputImageBuffer is not null &&
            _outputImageBuffer.Width == width &&
            _outputImageBuffer.Height == height)
        {
            return;
        }

        _outputImageBuffer?.Dispose();
        _outputImageBuffer = new SixLabors.ImageSharp.Image<Rgb24>(width, height);
    }

    private static void FillTensor(
        SixLabors.ImageSharp.Image<Rgb24> image,
        DenseTensor<float> tensor,
        int paddedWidth,
        int paddedHeight,
        RifePerformanceMetrics? metrics)
    {
        var fillStopwatch = Stopwatch.StartNew();
        var tensorBuffer = tensor.Buffer;
        const float normalizationScale = 1f / 255f;

        image.ProcessPixelRows(accessor =>
        {
            FillTensorRowsFromImage(accessor, tensorBuffer, image.Width, image.Height, paddedWidth, paddedHeight, normalizationScale);
        });
        fillStopwatch.Stop();
        metrics?.AddTensorFill(fillStopwatch.Elapsed);
        metrics?.AddTensorNormalization(fillStopwatch.Elapsed);
    }

    private static void FillTensorFromRgb24Bytes(
        ReadOnlySpan<byte> frameBytes,
        DenseTensor<float> tensor,
        int width,
        int height,
        int paddedWidth,
        int paddedHeight,
        RifePerformanceMetrics? metrics)
    {
        var fillStopwatch = Stopwatch.StartNew();
        RifeTensorConversionSimd.Fill(frameBytes, tensor.Buffer.Span, width, height, paddedWidth, paddedHeight);
        fillStopwatch.Stop();
        metrics?.AddTensorFill(fillStopwatch.Elapsed);
        metrics?.AddTensorNormalization(fillStopwatch.Elapsed);
    }

    private static void FillTensorRowsFromImage(
        SixLabors.ImageSharp.PixelAccessor<Rgb24> accessor,
        Memory<float> tensorBuffer,
        int width,
        int height,
        int paddedWidth,
        int paddedHeight,
        float normalizationScale)
    {
        var tensorSpan = tensorBuffer.Span;
        var planeSize = paddedWidth * paddedHeight;
        var sourcePlaneLength = paddedWidth * height;
        var redBase = 0;
        var greenBase = planeSize;
        var blueBase = planeSize * 2;

        for (var y = 0; y < height; y++)
        {
            var sourceRow = accessor.GetRowSpan(y);
            var sourceRowBytes = MemoryMarshal.AsBytes(sourceRow);
            var rowOffset = y * paddedWidth;
            var redOffset = redBase + rowOffset;
            var greenOffset = greenBase + rowOffset;
            var blueOffset = blueBase + rowOffset;

            var sourceByteIndex = 0;
            for (var x = 0; x < width; x++)
            {
                tensorSpan[redOffset + x] = sourceRowBytes[sourceByteIndex] * normalizationScale;
                tensorSpan[greenOffset + x] = sourceRowBytes[sourceByteIndex + 1] * normalizationScale;
                tensorSpan[blueOffset + x] = sourceRowBytes[sourceByteIndex + 2] * normalizationScale;
                sourceByteIndex += 3;
            }

            var lastPixelByteIndex = (width - 1) * 3;
            var edgeRed = sourceRowBytes[lastPixelByteIndex] * normalizationScale;
            var edgeGreen = sourceRowBytes[lastPixelByteIndex + 1] * normalizationScale;
            var edgeBlue = sourceRowBytes[lastPixelByteIndex + 2] * normalizationScale;

            for (var x = width; x < paddedWidth; x++)
            {
                tensorSpan[redOffset + x] = edgeRed;
                tensorSpan[greenOffset + x] = edgeGreen;
                tensorSpan[blueOffset + x] = edgeBlue;
            }
        }

        if (height >= paddedHeight)
        {
            return;
        }

        var lastRowOffset = (height - 1) * paddedWidth;
        var redLastRow = tensorSpan.Slice(redBase + lastRowOffset, paddedWidth);
        var greenLastRow = tensorSpan.Slice(greenBase + lastRowOffset, paddedWidth);
        var blueLastRow = tensorSpan.Slice(blueBase + lastRowOffset, paddedWidth);

        for (var y = height; y < paddedHeight; y++)
        {
            var rowOffset = y * paddedWidth;
            redLastRow.CopyTo(tensorSpan.Slice(redBase + rowOffset, paddedWidth));
            greenLastRow.CopyTo(tensorSpan.Slice(greenBase + rowOffset, paddedWidth));
            blueLastRow.CopyTo(tensorSpan.Slice(blueBase + rowOffset, paddedWidth));
        }
    }

    private static void WriteTensorRowsToImage(
        SixLabors.ImageSharp.PixelAccessor<Rgb24> accessor,
        Memory<float> outputBuffer,
        int width,
        int height,
        int paddedWidth,
        int paddedHeight)
    {
        var outputSpan = outputBuffer.Span;
        var planeSize = paddedWidth * paddedHeight;
        var vlen = Vector<float>.Count;
        var vscale = new Vector<float>(255f);
        var vhalf  = new Vector<float>(0.5f);
        var vzero  = Vector<float>.Zero;
        var vones  = new Vector<float>(1f);

        for (var y = 0; y < height; y++)
        {
            var row = accessor.GetRowSpan(y);
            var rowBytes = MemoryMarshal.AsBytes(row);
            var rowOffset = y * paddedWidth;
            var rSlice = outputSpan.Slice(rowOffset, paddedWidth);
            var gSlice = outputSpan.Slice(planeSize + rowOffset, paddedWidth);
            var bSlice = outputSpan.Slice(planeSize * 2 + rowOffset, paddedWidth);

            var x = 0;
            for (; x + vlen <= width; x += vlen)
            {
                var vr = Vector.Min(Vector.Max(new Vector<float>(rSlice.Slice(x)), vzero), vones) * vscale + vhalf;
                var vg = Vector.Min(Vector.Max(new Vector<float>(gSlice.Slice(x)), vzero), vones) * vscale + vhalf;
                var vb = Vector.Min(Vector.Max(new Vector<float>(bSlice.Slice(x)), vzero), vones) * vscale + vhalf;

                var byteOffset = x * 3;
                for (var i = 0; i < vlen; i++)
                {
                    rowBytes[byteOffset] = (byte)vr[i];
                    rowBytes[byteOffset + 1] = (byte)vg[i];
                    rowBytes[byteOffset + 2] = (byte)vb[i];
                    byteOffset += 3;
                }
            }

            for (; x < width; x++)
            {
                var byteOffset = x * 3;
                rowBytes[byteOffset] = ToByte(rSlice[x]);
                rowBytes[byteOffset + 1] = ToByte(gSlice[x]);
                rowBytes[byteOffset + 2] = ToByte(bSlice[x]);
            }
        }
    }

    private static void WriteTensorToRgb24Bytes(
        Memory<float> outputBuffer,
        int width,
        int height,
        int paddedWidth,
        int paddedHeight,
        Span<byte> outputFrameBytes)
    {
        RifeTensorConversionSimd.Write(outputBuffer.Span, width, height, paddedWidth, paddedHeight, outputFrameBytes);
    }

    private static void WriteTensorToRgb24Bytes(
        Tensor<float> outputTensor,
        int width,
        int height,
        Span<byte> outputFrameBytes)
    {
        for (var y = 0; y < height; y++)
        {
            var rowByteOffset = y * width * 3;
            for (var x = 0; x < width; x++)
            {
                var byteOffset = rowByteOffset + (x * 3);
                outputFrameBytes[byteOffset] = ToByte(outputTensor[0, 0, y, x]);
                outputFrameBytes[byteOffset + 1] = ToByte(outputTensor[0, 1, y, x]);
                outputFrameBytes[byteOffset + 2] = ToByte(outputTensor[0, 2, y, x]);
            }
        }
    }

    private void SaveFrame(SixLabors.ImageSharp.Image<Rgb24> image, string outputFramesDirectory, int outputIndex)
    {
        using (new RifePerfTimer(duration =>
        {
            _metrics?.AddWriteFrame(duration);
            _metrics?.AddBmpWrite(duration);
        }))
        {
            var framePath = Path.Combine(outputFramesDirectory, $"{outputIndex:00000000}{TempFrameExtension}");
            _metrics?.IncrementFileStreamCreationCount();
            using var stream = File.Create(framePath);
            image.Save(stream, s_bmpEncoder);
        }
    }

    private static (InferenceSession session, string provider) CreateSession(string modelPath) =>
        OnnxProviderHelper.CreateSessionPreferred(modelPath, "RifeFrameInterpolationEngine");

    private static int AlignTo64(int value)
    {
        return ((value + 63) / 64) * 64;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ToByte(float value) =>
        (byte)(uint)Math.Clamp(value * 255f + 0.5f, 0f, 255f);

    public void Dispose()
    {
        _session?.Dispose();
        _initGate.Dispose();
        _outputImageBuffer?.Dispose();
    }
}
