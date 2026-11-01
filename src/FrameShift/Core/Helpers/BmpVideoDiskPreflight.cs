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
            failureMessage = L.Get("BmpVideoDiskPreflight_FrameShift_could_not_determine_the_v_982194b8");
            return false;
        }

        frameCount = (long)estimated;
        if (frameCount > 0)
            return true;

        failureMessage = L.Get("BmpVideoDiskPreflight_FrameShift_could_not_determine_the_v_982194b8");
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
            failureMessage = L.Get("BmpVideoDiskPreflight_FrameShift_could_not_estimate_the_RI_f3688104");
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
            failureMessage = L.Get("BmpVideoDiskPreflight_The_RIFE_BMP_disk_space_estimate_is__678e6882");
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
            failureMessage = L.Get("BmpVideoDiskPreflight_FrameShift_could_not_estimate_the_Up_8fdcaa66");
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
            failureMessage = L.Get("BmpVideoDiskPreflight_The_Upscale_Video_BMP_disk_space_est_fd9e5922");
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

            failureMessage = L.Format("BmpVideoDiskPreflight_Insufficient_free_space_on_0_This_BM_c484e84a",tempRoot,FormatBytes(required)) +
                             L.Format("BmpVideoDiskPreflight_including_temporary_frames_and_the_f_ba0d34e7");
            return false;
        }

        if (availableTemporaryBytes < requirement.RequiredTemporaryBytes)
        {
            failureMessage = L.Format("BmpVideoDiskPreflight_Insufficient_free_space_on_0_Tempora_3d76d77a",tempRoot,FormatBytes(requirement.RequiredTemporaryBytes));
            return false;
        }

        if (availableOutputBytes < requirement.RequiredOutputBytes)
        {
            failureMessage = L.Format("BmpVideoDiskPreflight_Insufficient_free_space_on_0_The_fin_1db62f92",outputRoot,FormatBytes(requirement.RequiredOutputBytes));
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
            failureMessage = L.Get("BmpVideoDiskPreflight_FrameShift_could_not_verify_the_avai_c97bc83b");
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
