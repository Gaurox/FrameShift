using FrameShift.Core.FFmpeg;
using Xunit;

namespace FrameShift.Tests;

public sealed class FfmpegProgressStateTests
{
    [Fact]
    public void FreshState_PhaseIsPreparing()
    {
        var state = new FfmpegProgressState();

        var snapshot = state.AdvanceSnapshot();

        Assert.Equal(FfmpegProgressPhase.Preparing, snapshot.Phase);
    }

    [Fact]
    public void FreshState_FinishedIsFalse()
    {
        var state = new FfmpegProgressState();

        Assert.False(state.Finished);
    }

    [Fact]
    public void AfterUpdateRealProgress_PhaseIsProcessing()
    {
        var state = new FfmpegProgressState();
        state.UpdateRealProgress(1.0, System.TimeSpan.FromSeconds(10));

        var snapshot = state.AdvanceSnapshot();

        Assert.Equal(FfmpegProgressPhase.Processing, snapshot.Phase);
    }

    [Fact]
    public void AfterUpdateRealProgress_DisplayedPermilleIsPositive()
    {
        var state = new FfmpegProgressState();
        state.UpdateRealProgress(5.0, System.TimeSpan.FromSeconds(10));

        var snapshot = state.AdvanceSnapshot();

        Assert.True(snapshot.DisplayedPermille > 0);
    }

    [Fact]
    public void AfterMarkFinalizing_PhaseIsFinalizing()
    {
        var state = new FfmpegProgressState();
        state.MarkFinalizing();

        var snapshot = state.AdvanceSnapshot();

        Assert.Equal(FfmpegProgressPhase.Finalizing, snapshot.Phase);
    }

    [Fact]
    public void AfterMarkFinished_FinishedIsTrue()
    {
        var state = new FfmpegProgressState();
        state.MarkFinished();

        Assert.True(state.Finished);
    }

    [Fact]
    public void AfterMarkFinished_DisplayedPermilleIs1000()
    {
        var state = new FfmpegProgressState();
        state.MarkFinished();

        var snapshot = state.AdvanceSnapshot();

        Assert.Equal(1000, snapshot.DisplayedPermille);
    }

    [Fact]
    public void ProgressNeverExceeds1000()
    {
        var state = new FfmpegProgressState();
        state.UpdateRealProgress(999.0, System.TimeSpan.FromSeconds(1));

        var snapshot = state.AdvanceSnapshot();

        Assert.True(snapshot.DisplayedPermille <= 1000);
    }

    [Fact]
    public void UpdateFrameProgress_ValidFrames_IncreasesPermille()
    {
        var state = new FfmpegProgressState();
        state.UpdateFrameProgress(500, 1000);

        var snapshot = state.AdvanceSnapshot();

        Assert.True(snapshot.DisplayedPermille > 0);
        Assert.Equal(FfmpegProgressPhase.Processing, snapshot.Phase);
    }
}
