using System;
using FrameShift.Core.AI.RemoveNoise;
using Xunit;

namespace FrameShift.Tests;

public sealed class RemoveNoiseMemoryEstimatorTests
{
    [Fact]
    public void Estimate_GrowsWithDuration_AndAccountsForRemovedBuffers()
    {
        Assert.True(RemoveNoiseMemoryEstimator.TryEstimate(TimeSpan.FromMinutes(5), out var shortEstimate, out var shortFailure), shortFailure);
        Assert.True(RemoveNoiseMemoryEstimator.TryEstimate(TimeSpan.FromHours(1), out var longEstimate, out var longFailure), longFailure);

        Assert.True(longEstimate.SampleCount > shortEstimate.SampleCount);
        Assert.True(longEstimate.FrameCount > shortEstimate.FrameCount);
        Assert.True(longEstimate.EstimatedWorkingSetBytes > shortEstimate.EstimatedWorkingSetBytes);
        Assert.True(longEstimate.LegacyEstimatedWorkingSetBytes > longEstimate.EstimatedWorkingSetBytes);
        Assert.True(longEstimate.RequiredAvailableBytes > longEstimate.EstimatedWorkingSetBytes);
    }

    [Fact]
    public void ResourceDecision_UsesAvailableRamRatherThanADurationCap()
    {
        Assert.True(RemoveNoiseMemoryEstimator.TryEstimate(TimeSpan.FromHours(3), out var estimate, out var failure), failure);

        Assert.True(RemoveNoiseMemoryEstimator.HasEnoughAvailableMemory(estimate, estimate.RequiredAvailableBytes));
        Assert.False(RemoveNoiseMemoryEstimator.HasEnoughAvailableMemory(estimate, estimate.RequiredAvailableBytes - 1));
    }
}
