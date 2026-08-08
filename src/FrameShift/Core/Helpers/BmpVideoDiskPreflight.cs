using System;
using System.IO;

namespace FrameShift.Core.Helpers;

internal readonly record struct BmpVideoDiskRequirement(
    long PeakTemporaryBytes,
    long EstimatedOutputBytes,
    long RequiredTemporaryBytes,
    long RequiredOutputBytes);

internal static class BmpVideoDiskPreflight
{
    private const long SafetyNumerator = 6;
    private const long SafetyDenominator = 5;

    public static bool TryResolveFrameCount(
        long? reportedFrameCount,
        TimeSpan duration,
        double frameRate,
        out long frameCount,
        out string failureMessage)
    {
        frameCount = reportedFrameCount.GetValueOrDefault();
        failureMessage = string.Empty;
        if (frameCount > 0)
            return true;

        double estimated = Math.Ceiling(duration.TotalSeconds * frameRate);
        if (duration <= TimeSpan.Zero || frameRate <= 0 || double.IsNaN(estimated) || double.IsInfinity(estimated) || estimated > long.MaxValue)
        {
            failureMessage = "FrameShift could not determine the video frame count for the BMP disk space preflight.";
            return false;
        }

        frameCount = (long)estimated;
        if (frameCount > 0)
            return true;

        failureMessage = "FrameShift could not determine the video frame count for the BMP disk space preflight.";
        return false;
    }

    public static bool TryEstimateRife(
        long sourceFrameCount,
        int width,
        int height,
        int targetMultiplier,
        long sourceFileBytes,
        out BmpVideoDiskRequirement requirement,
        out string failureMessage)
    {
        requirement = default;
        failureMessage = string.Empty;
        if (!AreValidDimensions(sourceFrameCount, width, height) || targetMultiplier < 2 || (targetMultiplier & (targetMultiplier - 1)) != 0)
        {
            failureMessage = "FrameShift could not estimate the RIFE BMP disk space requirement.";
            return false;
        }

        try
        {
            long frameBytes = EstimateBmp24Bytes(width, height);
            long currentFrames = sourceFrameCount;
            long peakFramesBytes = checked(currentFrames * frameBytes);
            int passCount = (int)Math.Round(Math.Log2(targetMultiplier));
            for (int pass = 0; pass < passCount; pass++)
            {
                long outputFrames = checked((currentFrames - 1) * 2 + 1);
                long passPeak = checked((currentFrames + outputFrames) * frameBytes);
                peakFramesBytes = Math.Max(peakFramesBytes, passPeak);
                currentFrames = outputFrames;
            }

            long outputBytes = EstimateEncodedOutputBytes(sourceFileBytes, targetMultiplier);
            requirement = CreateRequirement(peakFramesBytes, outputBytes);
            return true;
        }
        catch (OverflowException)
        {
            failureMessage = "The RIFE BMP disk space estimate is too large for this video.";
            return false;
        }
    }

    public static bool TryEstimateUpscale(
        long sourceFrameCount,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        long sourceFileBytes,
        out BmpVideoDiskRequirement requirement,
        out string failureMessage)
    {
        requirement = default;
        failureMessage = string.Empty;
        if (!AreValidDimensions(sourceFrameCount, sourceWidth, sourceHeight) || targetWidth <= 0 || targetHeight <= 0)
        {
            failureMessage = "FrameShift could not estimate the Upscale Video BMP disk space requirement.";
            return false;
        }

        try
        {
            long sourceFrameBytes = EstimateBmp24Bytes(sourceWidth, sourceHeight);
            long targetFrameBytes = EstimateBmp32Bytes(targetWidth, targetHeight);
            long inputTotal = checked(sourceFrameCount * sourceFrameBytes);
            long outputTotal = checked(sourceFrameCount * targetFrameBytes);

            // Each input image is removed once its corresponding output BMP was saved.
            // The short overlap is one input frame plus the accumulated output set.
            long peakTemporaryBytes = Math.Max(inputTotal, checked(outputTotal + sourceFrameBytes));
            long outputBytes = EstimateEncodedOutputBytes(
                sourceFileBytes,
                checked((long)targetWidth * targetHeight),
                checked((long)sourceWidth * sourceHeight));
            requirement = CreateRequirement(peakTemporaryBytes, outputBytes);
            return true;
        }
        catch (OverflowException)
        {
            failureMessage = "The Upscale Video BMP disk space estimate is too large for this video.";
            return false;
        }
    }

    public static bool HasEnoughDiskSpace(
        BmpVideoDiskRequirement requirement,
        string temporaryPath,
        string outputPath,
        long availableTemporaryBytes,
        long availableOutputBytes,
        out string failureMessage)
    {
        failureMessage = string.Empty;
        string tempRoot = Path.GetPathRoot(Path.GetFullPath(temporaryPath)) ?? string.Empty;
        string outputRoot = Path.GetPathRoot(Path.GetFullPath(outputPath)) ?? string.Empty;
        bool sameVolume = string.Equals(tempRoot, outputRoot, StringComparison.OrdinalIgnoreCase);

        if (sameVolume)
        {
            long required = checked(requirement.RequiredTemporaryBytes + requirement.RequiredOutputBytes);
            if (availableTemporaryBytes >= required)
                return true;

            failureMessage = $"Insufficient free space on {tempRoot}. This BMP pipeline needs about {FormatBytes(required)} free, " +
                             $"including temporary frames and the final output.";
            return false;
        }

        if (availableTemporaryBytes < requirement.RequiredTemporaryBytes)
        {
            failureMessage = $"Insufficient free space on {tempRoot}. Temporary BMP frames need about {FormatBytes(requirement.RequiredTemporaryBytes)} free.";
            return false;
        }

        if (availableOutputBytes < requirement.RequiredOutputBytes)
        {
            failureMessage = $"Insufficient free space on {outputRoot}. The final video needs about {FormatBytes(requirement.RequiredOutputBytes)} free.";
            return false;
        }

        return true;
    }

    public static bool TryValidate(BmpVideoDiskRequirement requirement, string temporaryPath, string outputPath, out string failureMessage)
    {
        failureMessage = string.Empty;
        try
        {
            long tempAvailable = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(temporaryPath))!).AvailableFreeSpace;
            long outputAvailable = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(outputPath))!).AvailableFreeSpace;
            return HasEnoughDiskSpace(requirement, temporaryPath, outputPath, tempAvailable, outputAvailable, out failureMessage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            failureMessage = "FrameShift could not verify the available disk space for the BMP frame pipeline.";
            return false;
        }
    }

    private static BmpVideoDiskRequirement CreateRequirement(long peakTemporaryBytes, long outputBytes) => new(
        peakTemporaryBytes,
        outputBytes,
        WithSafetyMargin(peakTemporaryBytes),
        WithSafetyMargin(outputBytes));

    private static bool AreValidDimensions(long frameCount, int width, int height) => frameCount > 0 && width > 0 && height > 0;

    private static long EstimateBmp24Bytes(int width, int height)
    {
        long rowBytes = checked((((long)width * 3 + 3) / 4) * 4);
        return checked(54 + rowBytes * height);
    }

    private static long EstimateBmp32Bytes(int width, int height) => checked(54 + (long)width * height * 4);

    private static long EstimateEncodedOutputBytes(long sourceFileBytes, int multiplier) =>
        EstimateEncodedOutputBytes(sourceFileBytes, multiplier, 1);

    private static long EstimateEncodedOutputBytes(long sourceFileBytes, long numerator, long denominator)
    {
        long sourceBytes = Math.Max(1, sourceFileBytes);
        long scaled = checked((sourceBytes * numerator + denominator - 1) / denominator);
        return Math.Max(sourceBytes, scaled);
    }

    private static long WithSafetyMargin(long bytes) => checked((bytes * SafetyNumerator + SafetyDenominator - 1) / SafetyDenominator);

    internal static string FormatBytes(long bytes) => $"{bytes / (1024d * 1024d):0.0} MB";
}
