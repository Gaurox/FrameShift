using System;
using System.IO;
using System.Threading;
using FrameShift.Core.AI.VideoInterpolation;
using FrameShift.Core.AI.VideoUpscale;
using FrameShift.Core.Helpers;
using Xunit;

namespace FrameShift.Tests;

public sealed class BmpVideoDiskPreflightTests
{
    [Fact]
    public void FrameCount_UsesProbeValueOrDurationFallback()
    {
        Assert.True(BmpVideoDiskPreflight.TryResolveFrameCount(120, TimeSpan.FromSeconds(1), 60, out var reported, out var reportedFailure), reportedFailure);
        Assert.Equal(120, reported);

        Assert.True(BmpVideoDiskPreflight.TryResolveFrameCount(0, TimeSpan.FromSeconds(2), 29.97, out var derived, out var derivedFailure), derivedFailure);
        Assert.Equal(60, derived);
    }

    [Fact]
    public void RifeEstimate_CoversTwoGenerationsAndOutputMargin()
    {
        Assert.True(BmpVideoDiskPreflight.TryEstimateRife(
            sourceFrameCount: 100,
            width: 1920,
            height: 1080,
            targetMultiplier: 4,
            sourceFileBytes: 500L * 1024 * 1024,
            out var requirement,
            out var failure), failure);

        Assert.True(requirement.PeakTemporaryBytes > 100L * 1920 * 1080 * 3);
        Assert.True(requirement.EstimatedOutputBytes >= 2_000L * 1024 * 1024);
        Assert.True(requirement.RequiredTemporaryBytes > requirement.PeakTemporaryBytes);
        Assert.True(requirement.RequiredOutputBytes > requirement.EstimatedOutputBytes);
    }

    [Fact]
    public void UpscaleEstimate_AccountsForProgressiveInputCleanup()
    {
        Assert.True(BmpVideoDiskPreflight.TryEstimateUpscale(
            sourceFrameCount: 10,
            sourceWidth: 640,
            sourceHeight: 360,
            targetWidth: 1280,
            targetHeight: 720,
            sourceFileBytes: 1L * 1024 * 1024,
            out var requirement,
            out var failure), failure);

        Assert.True(requirement.PeakTemporaryBytes > requirement.EstimatedOutputBytes);
        Assert.True(requirement.RequiredTemporaryBytes > requirement.PeakTemporaryBytes);
    }

    [Fact]
    public void DiskDecision_RejectsBeforeFrameExtractionWhenFreeSpaceIsInsufficient()
    {
        var requirement = new BmpVideoDiskRequirement(
            PeakTemporaryBytes: 1_000,
            EstimatedOutputBytes: 500,
            RequiredTemporaryBytes: 1_200,
            RequiredOutputBytes: 600);

        Assert.True(BmpVideoDiskPreflight.HasEnoughDiskSpace(
            requirement,
            @"C:\Temp\FrameShift",
            @"D:\Videos\output.mp4",
            availableTemporaryBytes: 1_200,
            availableOutputBytes: 600,
            out var allowedFailure), allowedFailure);

        Assert.False(BmpVideoDiskPreflight.HasEnoughDiskSpace(
            requirement,
            @"C:\Temp\FrameShift",
            @"D:\Videos\output.mp4",
            availableTemporaryBytes: 1_199,
            availableOutputBytes: 10_000,
            out var failure));
        Assert.Contains("Temporary BMP", failure, StringComparison.OrdinalIgnoreCase);

        Assert.True(BmpVideoDiskPreflight.HasEnoughDiskSpace(
            requirement,
            @"C:\Temp\FrameShift",
            @"C:\Videos\output.mp4",
            availableTemporaryBytes: 1_800,
            availableOutputBytes: 999_999,
            out var sameVolumeFailure), sameVolumeFailure);

        Assert.False(BmpVideoDiskPreflight.HasEnoughDiskSpace(
            requirement,
            @"C:\Temp\FrameShift",
            @"C:\Videos\output.mp4",
            availableTemporaryBytes: 1_799,
            availableOutputBytes: 999_999,
            out _));
    }

    [Fact]
    public void ProgressiveCleanup_DeletesOnlyCompletedInputs_AndHonorsCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"frameshift_bmp_cleanup_{Guid.NewGuid():N}");
        var rifeInput = Path.Combine(root, "rife-input");
        var retained = Path.Combine(root, "retained");
        var upscaleInput = Path.Combine(root, "upscale-input.bmp");
        Directory.CreateDirectory(rifeInput);
        Directory.CreateDirectory(retained);
        File.WriteAllText(Path.Combine(rifeInput, "00000001.bmp"), "frame");
        File.WriteAllText(Path.Combine(retained, "keep.bmp"), "frame");
        File.WriteAllText(upscaleInput, "frame");

        try
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                RifeFrameInterpolationEngine.DeleteCompletedInputDirectory(rifeInput, canceled.Token));
            Assert.True(Directory.Exists(rifeInput));

            RifeFrameInterpolationEngine.DeleteCompletedInputDirectory(rifeInput, CancellationToken.None);
            Assert.False(Directory.Exists(rifeInput));
            Assert.True(File.Exists(Path.Combine(retained, "keep.bmp")));

            Assert.Throws<OperationCanceledException>(() =>
                VideoUpscaleEngine.DeleteProcessedInputFrame(upscaleInput, canceled.Token));
            Assert.True(File.Exists(upscaleInput));

            VideoUpscaleEngine.DeleteProcessedInputFrame(upscaleInput, CancellationToken.None);
            Assert.False(File.Exists(upscaleInput));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
