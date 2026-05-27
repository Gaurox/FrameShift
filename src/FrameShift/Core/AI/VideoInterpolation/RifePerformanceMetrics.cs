using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace FrameShift.Core.AI.VideoInterpolation;

internal sealed class RifePerformanceMetrics
{
    private long _totalTicks;
    private long _extractionTicks;
    private long _reconstructionTicks;
    private long _readFrameTicks;
    private long _bmpLoadTicks;
    private long _imageToTensorTicks;
    private long _tensorAllocationTicks;
    private long _tensorFillTicks;
    private long _tensorNormalizationTicks;
    private long _padTicks;
    private long _inferenceTicks;
    private long _onnxOutputAccessTicks;
    private long _cropTicks;
    private long _tensorToImageTicks;
    private long _outputImageAllocationTicks;
    private long _floatToByteTicks;
    private long _imagePixelWriteTicks;
    private long _writeFrameTicks;
    private long _bmpWriteTicks;
    private long _sessionCreationTicks;

    public int PairCount { get; private set; }
    public int SessionCreationCount { get; private set; }
    public int PassCount { get; private set; }
    public int InputFrameLoadCount { get; private set; }
    public int TensorAllocationCount { get; private set; }
    public int OutputImageAllocationCount { get; private set; }
    public int NamedOnnxValueListCreationCount { get; private set; }
    public int FileStreamCreationCount { get; private set; }
    public int SourceWidth { get; private set; }
    public int SourceHeight { get; private set; }
    public int PaddedWidth { get; private set; }
    public int PaddedHeight { get; private set; }
    public int ExtractedFrameCount { get; private set; }
    public int EncodedFrameCount { get; private set; }
    public long ExtractedFrameBytes { get; private set; }
    public long EncodedFrameBytes { get; private set; }
    public string Provider { get; set; } = "Unknown";
    public string PipelineDescription { get; set; } = string.Empty;
    public string VramApproximationText { get; set; } = "Unavailable";
    public string TempFrameFormat { get; set; } = "PNG";
    public string TempFrameExtension { get; set; } = ".png";
    public string CropMode { get; set; } = "Implicit";
    internal TimeSpan TotalDuration => TimeSpanFromTicks(_totalTicks);
    internal TimeSpan ExtractionDuration => TimeSpanFromTicks(_extractionTicks);
    internal TimeSpan ReconstructionDuration => TimeSpanFromTicks(_reconstructionTicks);
    internal TimeSpan ReadFrameDuration => TimeSpanFromTicks(_readFrameTicks);
    internal TimeSpan ImageToTensorDuration => TimeSpanFromTicks(_imageToTensorTicks);
    internal TimeSpan InferenceDuration => TimeSpanFromTicks(_inferenceTicks);
    internal TimeSpan TensorToImageDuration => TimeSpanFromTicks(_tensorToImageTicks);
    internal TimeSpan WriteFrameDuration => TimeSpanFromTicks(_writeFrameTicks);
    internal TimeSpan SessionCreationDuration => TimeSpanFromTicks(_sessionCreationTicks);

    public void CompleteTotal(Stopwatch stopwatch) => _totalTicks = stopwatch.ElapsedTicks;
    public void AddExtraction(TimeSpan duration) => _extractionTicks += duration.Ticks;
    public void AddReconstruction(TimeSpan duration) => _reconstructionTicks += duration.Ticks;
    public void AddReadFrame(TimeSpan duration) => _readFrameTicks += duration.Ticks;
    public void AddBmpLoad(TimeSpan duration)
    {
        _bmpLoadTicks += duration.Ticks;
        InputFrameLoadCount++;
    }
    public void AddImageToTensor(TimeSpan duration) => _imageToTensorTicks += duration.Ticks;
    public void AddTensorAllocation(TimeSpan duration)
    {
        _tensorAllocationTicks += duration.Ticks;
        TensorAllocationCount += 2;
    }
    public void AddTensorFill(TimeSpan duration) => _tensorFillTicks += duration.Ticks;
    public void AddTensorNormalization(TimeSpan duration) => _tensorNormalizationTicks += duration.Ticks;
    public void AddPad(TimeSpan duration) => _padTicks += duration.Ticks;
    public void AddInference(TimeSpan duration) => _inferenceTicks += duration.Ticks;
    public void AddOnnxOutputAccess(TimeSpan duration) => _onnxOutputAccessTicks += duration.Ticks;
    public void AddCrop(TimeSpan duration) => _cropTicks += duration.Ticks;
    public void AddTensorToImage(TimeSpan duration) => _tensorToImageTicks += duration.Ticks;
    public void AddOutputImageAllocation(TimeSpan duration)
    {
        _outputImageAllocationTicks += duration.Ticks;
        OutputImageAllocationCount++;
    }
    public void AddFloatToByte(TimeSpan duration) => _floatToByteTicks += duration.Ticks;
    public void AddImagePixelWrite(TimeSpan duration) => _imagePixelWriteTicks += duration.Ticks;
    public void AddWriteFrame(TimeSpan duration) => _writeFrameTicks += duration.Ticks;
    public void AddBmpWrite(TimeSpan duration) => _bmpWriteTicks += duration.Ticks;
    public void AddSessionCreation(TimeSpan duration)
    {
        _sessionCreationTicks += duration.Ticks;
        SessionCreationCount++;
    }
    public void IncrementNamedOnnxValueListCreationCount() => NamedOnnxValueListCreationCount++;
    public void IncrementFileStreamCreationCount() => FileStreamCreationCount++;

    public void SetResolution(int sourceWidth, int sourceHeight, int paddedWidth, int paddedHeight)
    {
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        PaddedWidth = paddedWidth;
        PaddedHeight = paddedHeight;
    }

    public void IncrementPairCount() => PairCount++;
    public void SetPassCount(int passCount) => PassCount = passCount;

    public void SetExtractedFrames(string directoryPath)
    {
        ExtractedFrameCount = CountFrameFiles(directoryPath, TempFrameExtension);
        ExtractedFrameBytes = SumFileSizes(directoryPath, TempFrameExtension);
    }

    public void SetEncodedFrames(string directoryPath)
    {
        EncodedFrameCount = CountFrameFiles(directoryPath, TempFrameExtension);
        EncodedFrameBytes = SumFileSizes(directoryPath, TempFrameExtension);
    }

    public string BuildReport()
    {
        var builder = new StringBuilder();
        var total = TimeSpanFromTicks(_totalTicks);
        var onnxPipelineTicks = _readFrameTicks + _imageToTensorTicks + _padTicks + _inferenceTicks + _cropTicks + _tensorToImageTicks + _writeFrameTicks;
        var otherTicks = Math.Max(0, _totalTicks - (_extractionTicks + _reconstructionTicks + onnxPipelineTicks + _sessionCreationTicks));

        builder.AppendLine("RIFE PERFORMANCE REPORT");
        builder.AppendLine($"Provider: {Provider}");
        builder.AppendLine($"Session creations: {SessionCreationCount}");
        builder.AppendLine($"Session creation time: {FormatDuration(_sessionCreationTicks)}");
        builder.AppendLine($"Pairs processed: {PairCount}");
        builder.AppendLine($"Passes: {PassCount}");
        builder.AppendLine($"Resolution: {SourceWidth}x{SourceHeight}");
        builder.AppendLine($"Padded resolution: {PaddedWidth}x{PaddedHeight}");
        builder.AppendLine($"Approx VRAM lower bound: {VramApproximationText}");
        builder.AppendLine($"Input frame loads: {InputFrameLoadCount}");
        builder.AppendLine($"Tensor allocations: {TensorAllocationCount}");
        builder.AppendLine($"Output image allocations: {OutputImageAllocationCount}");
        builder.AppendLine($"Reusable ONNX input set creations: {NamedOnnxValueListCreationCount}");
        builder.AppendLine($"File stream creations: {FileStreamCreationCount}");
        builder.AppendLine($"Temporary frame format: {TempFrameFormat}");
        builder.AppendLine($"Crop mode: {CropMode}");
        builder.AppendLine($"Pipeline: {PipelineDescription}");
        builder.AppendLine($"Extracted temporary frames: {ExtractedFrameCount} files / {FormatBytes(ExtractedFrameBytes)}");
        builder.AppendLine($"Encoded temporary frames: {EncodedFrameCount} files / {FormatBytes(EncodedFrameBytes)}");
        builder.AppendLine();
        builder.AppendLine("Detailed conversion timings:");
        builder.AppendLine($"BMP load + ImageSharp materialization: {FormatDuration(_bmpLoadTicks)} ({FormatPercent(_bmpLoadTicks, _totalTicks)})");
        builder.AppendLine($"Tensor allocation: {FormatDuration(_tensorAllocationTicks)} ({FormatPercent(_tensorAllocationTicks, _totalTicks)})");
        builder.AppendLine($"Tensor fill from ImageSharp pixels: {FormatDuration(_tensorFillTicks)} ({FormatPercent(_tensorFillTicks, _totalTicks)})");
        builder.AppendLine($"Float normalization during tensor fill: {FormatDuration(_tensorNormalizationTicks)} ({FormatPercent(_tensorNormalizationTicks, _totalTicks)})");
        builder.AppendLine($"ONNX output access: {FormatDuration(_onnxOutputAccessTicks)} ({FormatPercent(_onnxOutputAccessTicks, _totalTicks)})");
        builder.AppendLine($"Output image allocation: {FormatDuration(_outputImageAllocationTicks)} ({FormatPercent(_outputImageAllocationTicks, _totalTicks)})");
        builder.AppendLine($"Float -> byte conversion: {FormatDuration(_floatToByteTicks)} ({FormatPercent(_floatToByteTicks, _totalTicks)})");
        builder.AppendLine($"Output image pixel writes: {FormatDuration(_imagePixelWriteTicks)} ({FormatPercent(_imagePixelWriteTicks, _totalTicks)})");
        builder.AppendLine($"BMP encode + write: {FormatDuration(_bmpWriteTicks)} ({FormatPercent(_bmpWriteTicks, _totalTicks)})");
        builder.AppendLine();
        builder.AppendLine($"Total time: {FormatDuration(_totalTicks)}");
        builder.AppendLine($"Extraction / FFmpeg decode: {FormatDuration(_extractionTicks)} ({FormatPercent(_extractionTicks, _totalTicks)})");
        builder.AppendLine($"Session creation: {FormatDuration(_sessionCreationTicks)} ({FormatPercent(_sessionCreationTicks, _totalTicks)})");
        builder.AppendLine($"Frame read: {FormatDuration(_readFrameTicks)} ({FormatPercent(_readFrameTicks, _totalTicks)})");
        builder.AppendLine($"Image -> tensor: {FormatDuration(_imageToTensorTicks)} ({FormatPercent(_imageToTensorTicks, _totalTicks)})");
        builder.AppendLine($"Pad mod64 setup: {FormatDuration(_padTicks)} ({FormatPercent(_padTicks, _totalTicks)})");
        builder.AppendLine($"ONNX inference: {FormatDuration(_inferenceTicks)} ({FormatPercent(_inferenceTicks, _totalTicks)})");
        builder.AppendLine($"Crop return: {FormatDuration(_cropTicks)} ({FormatPercent(_cropTicks, _totalTicks)})");
        builder.AppendLine($"Tensor -> image: {FormatDuration(_tensorToImageTicks)} ({FormatPercent(_tensorToImageTicks, _totalTicks)})");
        builder.AppendLine($"Temporary frame write: {FormatDuration(_writeFrameTicks)} ({FormatPercent(_writeFrameTicks, _totalTicks)})");
        builder.AppendLine($"Reconstruction / FFmpeg encode: {FormatDuration(_reconstructionTicks)} ({FormatPercent(_reconstructionTicks, _totalTicks)})");
        builder.AppendLine($"Unattributed / other: {FormatDuration(otherTicks)} ({FormatPercent(otherTicks, _totalTicks)})");

        if (PairCount > 0)
        {
            builder.AppendLine($"Average time per pair: {FormatDuration(onnxPipelineTicks / PairCount)}");
        }

        builder.AppendLine();
        builder.AppendLine("Allocation and copy observations:");
        builder.AppendLine("- Input tensors are reused while the padded resolution stays unchanged.");
        builder.AppendLine("- ONNX input wrappers are reused while the padded resolution stays unchanged.");
        builder.AppendLine("- Output ImageSharp images are still allocated per interpolated frame.");
        builder.AppendLine("- Each saved frame still creates a new FileStream.");
        builder.AppendLine("- Input BMP files are decoded directly into ImageSharp Image<Rgb24>; there is no separate GDI Bitmap conversion step.");
        builder.AppendLine("- Tensor fill performs per-pixel reads from ImageSharp and per-channel writes into tensor buffers; this is the main explicit copy path before ONNX.");
        builder.AppendLine("- Float normalization is fused into the tensor fill loop; there is no separate normalization buffer or extra standalone normalization pass.");
        builder.AppendLine("- Tensor -> image performs per-pixel float reads from ONNX output and per-pixel writes into a new ImageSharp image; this is the main explicit copy path after ONNX.");
        builder.AppendLine("- Float -> byte conversion is fused into the tensor -> image population loop; there is no separate post-processing buffer.");

        return builder.ToString();
    }

    private static int CountFrameFiles(string directoryPath, string extension)
    {
        return Directory.Exists(directoryPath)
            ? Directory.GetFiles(directoryPath, $"*{extension}", SearchOption.TopDirectoryOnly).Length
            : 0;
    }

    private static long SumFileSizes(string directoryPath, string extension)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        long total = 0;
        foreach (var filePath in Directory.GetFiles(directoryPath, $"*{extension}", SearchOption.TopDirectoryOnly))
        {
            total += new FileInfo(filePath).Length;
        }

        return total;
    }

    private static TimeSpan TimeSpanFromTicks(long ticks)
    {
        if (ticks <= 0)
        {
            return TimeSpan.Zero;
        }

        var seconds = ticks / (double)Stopwatch.Frequency;
        return TimeSpan.FromSeconds(seconds);
    }

    private static string FormatDuration(long ticks)
    {
        var timeSpan = TimeSpanFromTicks(ticks);
        return $"{timeSpan.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms";
    }

    private static string FormatPercent(long partTicks, long totalTicks)
    {
        if (partTicks <= 0 || totalTicks <= 0)
        {
            return "0.0%";
        }

        var ratio = partTicks * 100d / totalTicks;
        return $"{ratio.ToString("0.0", CultureInfo.InvariantCulture)}%";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        var mb = bytes / (1024d * 1024d);
        return $"{mb.ToString("0.00", CultureInfo.InvariantCulture)} MB";
    }
}
